#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Import-Module (Join-Path $PSScriptRoot "PostgreSqlAutomation.Common.psm1") -Force

$runId = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$reportPath = Join-Path $root "artifacts\reports\PostgreSqlIntegration-$runId.json"
$report = [ordered]@{
    workflow = "isolated-postgresql-integration-tests"
    targetDatabase = "nexora_dental_test"
    runId = $runId
    status = "started"
    targetIdentity = $null
    appliedMigrationCount = $null
    migrationPrepared = $false
    migrationApplied = $false
    testResults = $null
    failureStage = $null
}
$password = $null
$connectionString = $null
$ownsPgPassword = $false
$ownsTestConnection = $false
$failed = $false
$stage = "initialization"

try {
    Assert-PgAutomationEnvironmentClear
    $expectedMigrations = @(Get-PgExpectedMigrationIds -RepositoryRoot $root)
    if ($expectedMigrations.Count -ne 5) {
        throw "The reviewed PostgreSQL migration chain must contain exactly five migrations."
    }

    $stage = "offline-release-build"
    $null = & dotnet build (Join-Path $root "backend\NexoraDental.sln") --configuration Release --no-restore 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "The Release build failed."
    }

    $stage = "test-database-preflight-approval"
    $preflightApproval = Read-Host "To connect read-only to the fixed isolated test database, type CONNECT_TO_TEST_PREFLIGHT"
    if ($preflightApproval -cne "CONNECT_TO_TEST_PREFLIGHT") {
        $report.status = "approval-required"
    }
    else {
        $stage = "private-credential-entry"
        $password = Read-PgSecurePassword
        $env:PGPASSWORD = $password
        $ownsPgPassword = $true
        $password = $null

        $stage = "target-identity"
        $identity = Assert-PgTargetIdentity -Database "nexora_dental_test" -ExpectedDatabase "nexora_dental_test"
        $report.targetIdentity = $identity

        $stage = "migration-history"
        $history = Assert-PgHistoryPrefix -Database "nexora_dental_test" -RepositoryRoot $root
        $appliedCount = $history.Applied.Count
        if ($appliedCount -notin @(4, 5)) {
            throw "The isolated test database must have the reviewed four- or five-migration history."
        }
        $report.appliedMigrationCount = $appliedCount

        $stage = "tenant-root-preflight"
        $orphanCount = Get-PgTenantRootOrphanCount -Database "nexora_dental_test"
        if ($orphanCount -ne 0) {
            throw "Tenant-root orphan preflight found records; migration and tests are blocked."
        }

        if ($appliedCount -eq 4) {
            $stage = "migration-sql-preparation"
            $lastApplied = $history.Applied[3].Id
            $newMigration = $history.Expected[4]
            $sqlDirectory = Join-Path $root "artifacts\sql\PostgreSql"
            [void][IO.Directory]::CreateDirectory($sqlDirectory)
            $migrationSqlPath = Join-Path $sqlDirectory "TenantRootForeignKeys-Test-$runId.sql"
            $dotnetArgs = @(
                "ef", "migrations", "script", $lastApplied, $newMigration,
                "--context", "PostgreSqlAppDbContext",
                "--project", (Join-Path $root "backend\DentalApi"),
                "--startup-project", (Join-Path $root "backend\DentalApi"),
                "--configuration", "Release",
                "--output", $migrationSqlPath
            )
            $null = & dotnet @dotnetArgs 2>&1
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $migrationSqlPath -PathType Leaf)) {
                throw "Tenant-root migration SQL generation failed."
            }
            $scriptText = [IO.File]::ReadAllText($migrationSqlPath)
            if (-not $scriptText.Contains($newMigration)) {
                throw "Generated migration SQL does not identify the required fifth migration."
            }
            $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
            [IO.File]::WriteAllText($migrationSqlPath, $scriptText, $utf8NoBom)
            $bytes = [IO.File]::ReadAllBytes($migrationSqlPath)
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
                throw "Generated migration SQL must be UTF-8 without a byte-order mark."
            }
            $report.migrationPrepared = $true
            $report.migrationSqlPath = $migrationSqlPath

            $stage = "migration-write-approval"
            $migrationApproval = Read-Host "Migration SQL is prepared for nexora_dental_test. Type APPLY_TENANT_ROOT_FK_MIGRATION to apply it"
            if ($migrationApproval -cne "APPLY_TENANT_ROOT_FK_MIGRATION") {
                $report.status = "approval-required"
            }
            else {
                $stage = "migration-application"
                Invoke-PgSqlFile -Database "nexora_dental_test" -Path $migrationSqlPath
                $report.migrationApplied = $true
                $history = Assert-PgHistoryPrefix -Database "nexora_dental_test" -RepositoryRoot $root -ExpectedCount 5
                $report.appliedMigrationCount = $history.Applied.Count
            }
        }

        if ($report.status -ne "approval-required") {
            $stage = "required-constraint-verification"
            $null = Assert-PgTenantRootConstraints -Database "nexora_dental_test"

            $stage = "test-execution-approval"
            $testApproval = Read-Host "Type RUN_13_POSTGRESQL_TESTS to execute the write-capable integration tests"
            if ($testApproval -cne "RUN_13_POSTGRESQL_TESTS") {
                $report.status = "approval-required"
            }
            else {
                $stage = "test-connection-setup"
                $connectionString = New-PgTestConnectionString -Password $env:PGPASSWORD
                $env:NEXORA_PG_TEST_CONNECTION = $connectionString
                $ownsTestConnection = $true
                $connectionString = $null

                $stage = "postgresql-integration-tests"
                $filter = "Category=PostgreSqlIntegration|Category=PostgreSqlConcurrency"
                $testOutput = @(& dotnet test (Join-Path $root "backend\NexoraDental.sln") `
                    --configuration Release --no-build --filter $filter 2>&1)
                $testExitCode = $LASTEXITCODE
                $testText = $testOutput -join "`n"
                $summaryMatch = [regex]::Match($testText, 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')
                if (-not $summaryMatch.Success) {
                    throw "PostgreSQL integration test summary could not be parsed."
                }
                $results = [PSCustomObject]@{
                    Failed = [int]$summaryMatch.Groups[1].Value
                    Passed = [int]$summaryMatch.Groups[2].Value
                    Skipped = [int]$summaryMatch.Groups[3].Value
                    Total = [int]$summaryMatch.Groups[4].Value
                }
                $report.testResults = $results
                if ($testExitCode -ne 0 -or $results.Failed -ne 0 -or $results.Skipped -ne 0 -or $results.Total -ne 13) {
                    throw "The PostgreSQL run must finish with exactly 13 passing tests and no skipped or failed tests."
                }
                $report.status = "passed"
            }
        }
    }
}
catch {
    $report.status = "failed"
    $report.failureStage = $stage
    $failed = $true
}
finally {
    if ($ownsTestConnection) {
        Remove-Item Env:\NEXORA_PG_TEST_CONNECTION -ErrorAction SilentlyContinue
    }
    if ($ownsPgPassword) {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
    $password = $null
    $connectionString = $null
    try {
        Write-PgJsonReport -Path $reportPath -Report $report
    }
    catch {
        $failed = $true
    }
}

if ($failed) {
    Write-Error "PostgreSQL integration workflow failed at stage '$($report.failureStage)'. Provider details were suppressed. Report: $reportPath"
    exit 1
}

Write-Output "PostgreSQL integration workflow status: $($report.status)"
if ($null -ne $report.testResults) {
    Write-Output "Tests: $($report.testResults.Passed) passed, $($report.testResults.Failed) failed, $($report.testResults.Skipped) skipped, $($report.testResults.Total) total."
}
Write-Output "Secret-free report: $reportPath"
