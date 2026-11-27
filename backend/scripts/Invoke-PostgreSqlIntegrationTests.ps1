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
    testDiagnostics = $null
    failureStage = $null
}
$password = $null
$connectionString = $null
$ownsPgPassword = $false
$ownsTestConnection = $false
$failed = $false
$stage = "initialization"
$testResultsDirectory = $null

try {
    Assert-PgAutomationEnvironmentClear
    $expectedMigrations = @(Get-PgExpectedMigrationIds -RepositoryRoot $root)
    if ($expectedMigrations.Count -ne 7) {
        throw "The reviewed PostgreSQL migration chain must contain exactly seven migrations."
    }

    $stage = "offline-release-build"
    $null = & dotnet build (Join-Path $root "backend\NexoraDental.sln") --configuration Release --no-restore 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "The Release build failed."
    }

    $stage = "test-database-preflight-approval"
    $preflightApproval = Read-Host "To connect read-only to the fixed isolated test database, type CONNECT_TO_TEST_PREFLIGHT"
    if (-not (Test-PgApproval -Response $preflightApproval -Token "CONNECT_TO_TEST_PREFLIGHT")) {
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
        if ($appliedCount -notin @(4, 5, 6, 7)) {
            throw "The isolated test database must have the reviewed four- through seven-migration history."
        }
        $report.appliedMigrationCount = $appliedCount

        $stage = "tenant-root-preflight"
        $orphanCount = Get-PgTenantRootOrphanCount -Database "nexora_dental_test"
        if ($orphanCount -ne 0) {
            throw "Tenant-root orphan preflight found records; migration and tests are blocked."
        }

        if ($appliedCount -lt $expectedMigrations.Count) {
            $stage = "migration-sql-preparation"
            $lastApplied = $history.Applied[$appliedCount - 1].Id
            $newMigration = $history.Expected[$appliedCount]
            $approvalToken = switch ($newMigration) {
                "20261125115303_AddPostgreSqlClinicTenantRootForeignKeys" { "APPLY_TENANT_ROOT_FK_MIGRATION" }
                "20261126070352_EnforceAppointmentIntegrity" { "APPLY_APPOINTMENT_INTEGRITY_MIGRATION" }
                "20261127122656_AddPostgreSqlAppointmentReschedules" { "APPLY_APPOINTMENT_RESCHEDULE_MIGRATION" }
                default { throw "No explicit write approval is defined for the next migration." }
            }
            $sqlDirectory = Join-Path $root "artifacts\sql\PostgreSql"
            [void][IO.Directory]::CreateDirectory($sqlDirectory)
            $migrationSqlPath = Join-Path $sqlDirectory "Migration-$newMigration-Test-$runId.sql"
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
                throw "Migration SQL generation failed."
            }
            $scriptText = [IO.File]::ReadAllText($migrationSqlPath)
            if (-not $scriptText.Contains($newMigration)) {
                throw "Generated migration SQL does not identify the next required migration."
            }
            $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
            [IO.File]::WriteAllText($migrationSqlPath, $scriptText, $utf8NoBom)
            $bytes = [IO.File]::ReadAllBytes($migrationSqlPath)
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
                throw "Generated migration SQL must be UTF-8 without a byte-order mark."
            }
            $report.migrationPrepared = $true
            $report.migrationIdPrepared = $newMigration
            $report.migrationSqlPath = $migrationSqlPath

            $stage = "migration-write-approval"
            $migrationApproval = Read-Host "Migration $newMigration is prepared for nexora_dental_test. Type $approvalToken to apply it"
            if (-not (Test-PgApproval -Response $migrationApproval -Token $approvalToken)) {
                $report.status = "approval-required"
            }
            else {
                $stage = "migration-application"
                Invoke-PgSqlFile -Database "nexora_dental_test" -Path $migrationSqlPath `
                    -RepositoryRoot $root -ExpectedMigrationIds @($expectedMigrations[0..($appliedCount - 1)])
                $report.migrationApplied = $true
                $history = Assert-PgHistoryPrefix -Database "nexora_dental_test" -RepositoryRoot $root `
                    -ExpectedCount ($appliedCount + 1)
                $report.appliedMigrationCount = $history.Applied.Count
                if ($history.Applied.Count -lt $expectedMigrations.Count) {
                    $report.status = "migration-applied-awaiting-next-approval"
                }
            }
        }

        if ($report.status -eq "started") {
            $stage = "required-constraint-verification"
            $null = Assert-PgTenantRootConstraints -Database "nexora_dental_test"
            $null = Assert-PgAppointmentIntegrityConstraints -Database "nexora_dental_test"

            $stage = "test-execution-approval"
            $testApproval = Read-Host "Type RUN_13_POSTGRESQL_TESTS to execute the write-capable integration tests"
            if (-not (Test-PgApproval -Response $testApproval -Token "RUN_13_POSTGRESQL_TESTS")) {
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
                $testResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) "SuvdDent-TestResults-$runId"
                [void][IO.Directory]::CreateDirectory($testResultsDirectory)
                $trxFileName = "PostgreSqlIntegration-$runId.trx"
                $testOutput = @(& dotnet test (Join-Path $root "backend\DentalApi.IntegrationTests\DentalApi.IntegrationTests.csproj") `
                    --configuration Release --no-build --no-restore --filter $filter `
                    --logger "trx;LogFileName=$trxFileName" `
                    --results-directory $testResultsDirectory 2>&1)
                $testExitCode = $LASTEXITCODE
                $trxFiles = @(Get-ChildItem -LiteralPath $testResultsDirectory -Filter $trxFileName -File -Recurse)
                if ($trxFiles.Count -ne 1) {
                    $report.testDiagnostics = ConvertTo-PgSanitizedTestOutput -Lines $testOutput
                    throw "The .NET test runner did not produce exactly one structured TRX result file."
                }
                try {
                    $results = Get-PgTrxTestResults -Path $trxFiles[0].FullName
                }
                catch {
                    $report.testDiagnostics = ConvertTo-PgSanitizedTestOutput -Lines $testOutput
                    throw "The .NET test runner produced an unreadable structured TRX result file."
                }
                $report.testResults = $results
                $report.testDiagnostics = $results.Diagnostics
                if ($testExitCode -ne 0 -or -not (Test-PgIntegrationTestResults -Results $results)) {
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
    if ($null -ne $testResultsDirectory -and (Test-Path -LiteralPath $testResultsDirectory -PathType Container)) {
        try {
            Remove-Item -LiteralPath $testResultsDirectory -Recurse -Force -ErrorAction Stop
        }
        catch {
            $failed = $true
            if ($report.status -ne "failed") {
                $report.status = "failed"
                $report.failureStage = "test-results-cleanup"
            }
        }
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
if ($null -ne $report.testDiagnostics -and @($report.testDiagnostics).Count -gt 0) {
    Write-Output ("Sanitized test diagnostics: " + (ConvertTo-Json -InputObject $report.testDiagnostics -Depth 4 -Compress))
}
Write-Output "Secret-free report: $reportPath"
