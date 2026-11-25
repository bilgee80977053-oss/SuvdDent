#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet("Prepare", "Rehearse")]
    [string]$Mode = "Prepare",
    [Parameter(Mandatory)][string]$FromMigration,
    [Parameter(Mandatory)][string]$ToMigration,
    [Parameter(Mandatory)][string]$BackupDirectory,
    [string[]]$RequiredConstraint = @(
        "FK_Users_Clinics_ClinicId",
        "FK_Services_Clinics_ClinicId",
        "FK_Patients_Clinics_ClinicId"
    ),
    [switch]$AllowDataFingerprintChange
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Import-Module (Join-Path $PSScriptRoot "PostgreSqlAutomation.Common.psm1") -Force

$runId = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$reportPath = Join-Path $root "artifacts\reports\PostgreSqlMigration-$runId.json"
$report = [ordered]@{
    workflow = "isolated-postgresql-migration"
    mode = $Mode
    targetDatabase = "nexora_dental_rehearsal"
    runId = $runId
    fromMigration = $FromMigration
    toMigration = $ToMigration
    status = "started"
    targetIdentity = $null
    backupPath = $null
    backupSha256 = $null
    backupArchiveVerified = $false
    migrationSqlPath = $null
    beforeDataSha256 = $null
    afterDataSha256 = $null
    migrationApplied = $false
    dataFingerprintChangeApproved = [bool]$AllowDataFingerprintChange
    failureStage = $null
}
$password = $null
$ownsPgPassword = $false
$failed = $false
$stage = "initialization"
$temporaryDataDump = $null

try {
    Assert-PgAutomationEnvironmentClear
    $targetDatabase = "nexora_dental_rehearsal"
    $expectedMigrations = @(Get-PgExpectedMigrationIds -RepositoryRoot $root)
    $fromIndex = [Array]::IndexOf($expectedMigrations, $FromMigration)
    $toIndex = [Array]::IndexOf($expectedMigrations, $ToMigration)
    if ($fromIndex -lt 0 -or $toIndex -le $fromIndex) {
        throw "The requested migration range is not an ordered range in the reviewed PostgreSQL migration chain."
    }

    $stage = "backup-directory-validation"
    if (-not (Test-Path -LiteralPath $BackupDirectory -PathType Container)) {
        throw "The backup directory must already exist and must be outside the repository."
    }
    $resolvedBackupDirectory = (Resolve-Path -LiteralPath $BackupDirectory).Path
    $repositoryPrefix = $root.TrimEnd("\") + "\"
    if ($resolvedBackupDirectory.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Database backups must be stored outside the repository."
    }

    $stage = "private-credential-entry"
    $password = Read-PgSecurePassword
    $env:PGPASSWORD = $password
    $ownsPgPassword = $true
    $password = $null

    $stage = "target-identity"
    $identity = Assert-PgTargetIdentity -Database $targetDatabase -ExpectedDatabase $targetDatabase
    $report.targetIdentity = $identity

    $stage = "migration-history"
    $history = Assert-PgHistoryPrefix -Database $targetDatabase -RepositoryRoot $root
    if ($history.Applied.Count -ne ($fromIndex + 1) -or
        $history.Applied[$fromIndex].Id -cne $FromMigration) {
        throw "The rehearsal database history must end exactly at the requested FromMigration."
    }
    for ($index = 0; $index -le $fromIndex; $index++) {
        if ($history.Applied[$index].Id -cne $expectedMigrations[$index]) {
            throw "The rehearsal database history is not the expected migration prefix."
        }
    }

    $stage = "tenant-root-read-only-preflight"
    $orphanCount = Get-PgTenantRootOrphanCount -Database $targetDatabase
    if ($orphanCount -ne 0) {
        throw "Tenant-root preflight found orphan records; the workflow is blocked."
    }

    $stage = "backup"
    $pgDump = Get-Command pg_dump -ErrorAction Stop
    $backupPath = Join-Path $resolvedBackupDirectory "nexora_dental_rehearsal-$runId.backup"
    if (Test-Path -LiteralPath $backupPath) {
        throw "The backup output path already exists."
    }
    $null = & $pgDump.Source --no-password --format=custom --no-owner --no-privileges `
        --host 127.0.0.1 --port 5432 --username nexora_dev --dbname $targetDatabase `
        --file $backupPath 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath -Force
        }
        throw "PostgreSQL backup creation failed; provider details were suppressed."
    }
    $backupItem = Get-Item -LiteralPath $backupPath
    if ($backupItem.Length -le 0) {
        throw "PostgreSQL backup output is empty."
    }
    $backupHash = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash
    $report.backupPath = $backupPath
    $report.backupSha256 = $backupHash
    $pgRestore = Get-Command pg_restore -ErrorAction Stop
    $null = & $pgRestore.Source --list $backupPath 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "The generated PostgreSQL backup archive did not pass its format check."
    }
    $report.backupArchiveVerified = $true

    $stage = "migration-sql-generation"
    $sqlDirectory = Join-Path $root "artifacts\sql\PostgreSql"
    [void][IO.Directory]::CreateDirectory($sqlDirectory)
    $migrationSqlPath = Join-Path $sqlDirectory "Migration-$FromMigration-to-$ToMigration-$runId.sql"
    $dotnetArgs = @(
        "ef", "migrations", "script", $FromMigration, $ToMigration,
        "--context", "PostgreSqlAppDbContext",
        "--project", (Join-Path $root "backend\DentalApi"),
        "--startup-project", (Join-Path $root "backend\DentalApi"),
        "--configuration", "Release",
        "--output", $migrationSqlPath
    )
    $null = & dotnet @dotnetArgs 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $migrationSqlPath -PathType Leaf)) {
        throw "EF migration SQL generation failed."
    }
    $scriptText = [IO.File]::ReadAllText($migrationSqlPath)
    if (-not $scriptText.Contains($ToMigration)) {
        throw "Generated migration SQL does not identify the requested target migration."
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::WriteAllText($migrationSqlPath, $scriptText, $utf8NoBom)
    $bytes = [IO.File]::ReadAllBytes($migrationSqlPath)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
        throw "Generated migration SQL must be UTF-8 without a byte-order mark."
    }
    $report.migrationSqlPath = $migrationSqlPath

    $stage = "before-data-fingerprint"
    $pgDump = Get-Command pg_dump -ErrorAction Stop
    $temporaryDataDump = Join-Path ([IO.Path]::GetTempPath()) "SuvdDent-DataBefore-$runId.sql"
    $null = & $pgDump.Source --no-password --data-only --column-inserts --no-owner --no-privileges `
        '--exclude-table-data=public."__EFMigrationsHistory"' `
        --host 127.0.0.1 --port 5432 --username nexora_dev --dbname $targetDatabase `
        --file $temporaryDataDump 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporaryDataDump -PathType Leaf)) {
        throw "Before-migration data fingerprint generation failed."
    }
    $beforeHash = (Get-FileHash -LiteralPath $temporaryDataDump -Algorithm SHA256).Hash
    $report.beforeDataSha256 = $beforeHash
    Remove-Item -LiteralPath $temporaryDataDump -Force
    $temporaryDataDump = $null

    if ($Mode -eq "Prepare") {
        $report.status = "prepared-no-schema-write"
    }
    else {
        $stage = "migration-write-approval"
        $approval = Read-Host "Target is fixed to nexora_dental_rehearsal. Type APPLY_MIGRATION_TO_REHEARSAL to execute the generated SQL"
        if ($approval -cne "APPLY_MIGRATION_TO_REHEARSAL") {
            $report.status = "approval-required"
        }
        elseif ($AllowDataFingerprintChange) {
            $stage = "data-change-approval"
            $dataApproval = Read-Host "The migration is authorized to change application data. Type APPROVE_DATA_FINGERPRINT_CHANGE"
            if ($dataApproval -cne "APPROVE_DATA_FINGERPRINT_CHANGE") {
                $report.status = "approval-required"
            }
        }

        if ($report.status -ne "approval-required") {
            $stage = "migration-application"
            Invoke-PgSqlFile -Database $targetDatabase -Path $migrationSqlPath
            $report.migrationApplied = $true

            $stage = "post-migration-history"
            $postHistory = Assert-PgHistoryPrefix -Database $targetDatabase -RepositoryRoot $root -ExpectedCount ($toIndex + 1)
            if ($postHistory.Applied[$toIndex].Id -cne $ToMigration) {
                throw "Post-migration history does not end at the requested target migration."
            }

            $stage = "post-migration-constraints"
            if ($RequiredConstraint.Count -eq 0) {
                throw "At least one required constraint must be supplied for a migration rehearsal."
            }
            $constraintRows = @(Get-PgConstraintRows -Database $targetDatabase -ConstraintNames $RequiredConstraint)
            if ($constraintRows.Count -ne $RequiredConstraint.Count) {
                throw "One or more required constraints are missing or duplicated."
            }
            $knownTenantConstraints = @(
                "FK_Patients_Clinics_ClinicId",
                "FK_Services_Clinics_ClinicId",
                "FK_Users_Clinics_ClinicId"
            )
            $providedSorted = @($RequiredConstraint | Sort-Object)
            $knownSorted = @($knownTenantConstraints | Sort-Object)
            $constraintDifference = @(Compare-Object -ReferenceObject $knownSorted -DifferenceObject $providedSorted)
            if ($constraintDifference.Count -eq 0) {
                $null = Assert-PgTenantRootConstraints -Database $targetDatabase
            }
            else {
                foreach ($constraintName in $RequiredConstraint) {
                    $matches = @($constraintRows | Where-Object { $_ -match "\|$([regex]::Escape($constraintName))\|true\|" })
                    if ($matches.Count -ne 1) {
                        throw "A required constraint was not confirmed as present and validated."
                    }
                }
            }

            $stage = "after-data-fingerprint"
            $temporaryDataDump = Join-Path ([IO.Path]::GetTempPath()) "SuvdDent-DataAfter-$runId.sql"
            $null = & $pgDump.Source --no-password --data-only --column-inserts --no-owner --no-privileges `
                '--exclude-table-data=public."__EFMigrationsHistory"' `
                --host 127.0.0.1 --port 5432 --username nexora_dev --dbname $targetDatabase `
                --file $temporaryDataDump 2>$null
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporaryDataDump -PathType Leaf)) {
                throw "After-migration data fingerprint generation failed."
            }
            $afterHash = (Get-FileHash -LiteralPath $temporaryDataDump -Algorithm SHA256).Hash
            $report.afterDataSha256 = $afterHash
            Remove-Item -LiteralPath $temporaryDataDump -Force
            $temporaryDataDump = $null

            if ($beforeHash -cne $afterHash -and -not $AllowDataFingerprintChange) {
                throw "Application-data fingerprint changed without prior explicit authorization."
            }
            $report.status = "rehearsed"
        }
    }
}
catch {
    $report.status = "failed"
    $report.failureStage = $stage
    $failed = $true
}
finally {
    if ($ownsPgPassword) {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
    $password = $null
    if ($null -ne $temporaryDataDump -and (Test-Path -LiteralPath $temporaryDataDump -PathType Leaf)) {
        Remove-Item -LiteralPath $temporaryDataDump -Force -ErrorAction SilentlyContinue
    }
    try {
        Write-PgJsonReport -Path $reportPath -Report $report
    }
    catch {
        $failed = $true
    }
}

if ($failed) {
    Write-Error "PostgreSQL migration workflow failed at stage '$($report.failureStage)'. Provider details were suppressed. Report: $reportPath"
    exit 1
}

Write-Output "PostgreSQL migration workflow status: $($report.status)"
if ($null -ne $report.backupSha256) {
    Write-Output "Backup SHA256: $($report.backupSha256)"
}
Write-Output "Secret-free report: $reportPath"
