#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
Import-Module (Join-Path $PSScriptRoot "..\PostgreSqlAutomation.Common.psm1") -Force

function Assert-OfflineTest {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)

    if (-not $Condition) {
        throw "Offline automation test failed: $Message"
    }
}

$temporaryFiles = @()
try {
    $migrationIds = @(Get-PgExpectedMigrationIds -RepositoryRoot $root)
    $expectedIds = @(
        "20261027132306_PostgreSqlBaseline",
        "20261027180725_AddPostgreSqlAppointmentFoundation",
        "20261102165221_AddPostgreSqlNotificationOutbox",
        "20261102180315_AddPostgreSqlPhoneVerificationChallenges",
        "20261125115303_AddPostgreSqlClinicTenantRootForeignKeys",
        "20261126070352_EnforceAppointmentIntegrity",
        "20261127122656_AddPostgreSqlAppointmentReschedules"
    )
    Assert-OfflineTest ($migrationIds.Count -eq 7) "migration IDs are a flat seven-element array"
    Assert-OfflineTest (@($migrationIds | Where-Object { $_ -is [array] }).Count -eq 0) "migration IDs are not nested"
    Assert-OfflineTest ((Compare-Object -ReferenceObject $expectedIds -DifferenceObject $migrationIds).Count -eq 0) `
        "migration IDs have the exact expected chronological order"
    Assert-OfflineTest (Test-PgConstraintName -Name "FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_Servic~") `
        "EF-generated tilde-suffixed constraint names are accepted"
    Assert-OfflineTest (-not (Test-PgConstraintName -Name "FK_Appointments_Service~~")) `
        "constraint name validation rejects multiple terminal tildes"
    Assert-OfflineTest (-not (Test-PgConstraintName -Name "FK_Appointments_Service~'; DROP TABLE Clinics;--")) `
        "constraint name validation rejects SQL injection syntax"

    $validConstraints = @(
        [PSCustomObject]@{
            Table = "DoctorServices"; Name = "AK_DoctorServices_ClinicId_DoctorProfileId_ServiceId"
            Validated = "true"; Definition = 'UNIQUE ("ClinicId", "DoctorProfileId", "ServiceId")'
        },
        [PSCustomObject]@{
            Table = "Appointments"; Name = "FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_Servic~"
            Validated = "true"; Definition = 'FOREIGN KEY ("ClinicId", "DoctorProfileId", "ServiceId") REFERENCES "DoctorServices" ("ClinicId", "DoctorProfileId", "ServiceId") ON DELETE RESTRICT'
        },
        [PSCustomObject]@{
            Table = "Appointments"; Name = "EX_Appointments_ConfirmedDoctorOccupied_NoOverlap"
            Validated = "true"; Definition = 'EXCLUDE USING gist ("ClinicId" WITH =, "DoctorProfileId" WITH =, tstzrange("OccupiedStartUtc", "OccupiedEndUtc", ''[)''::text) WITH &&) WHERE ("Status" = ''Confirmed''::text)'
        },
        [PSCustomObject]@{
            Table = "Appointments"; Name = "EX_Appointments_ConfirmedPatientTreatment_NoOverlap"
            Validated = "true"; Definition = 'EXCLUDE USING gist ("ClinicId" WITH =, "PatientId" WITH =, tstzrange("StartUtc", "EndUtc", ''[)''::text) WITH &&) WHERE ("Status" = ''Confirmed''::text)'
        }
    )
    foreach ($constraint in $validConstraints) {
        Assert-OfflineTest (Test-PgAppointmentIntegrityConstraintRow -Table $constraint.Table `
            -Name $constraint.Name -Validated $constraint.Validated -Definition $constraint.Definition) `
            "appointment constraint definition matches its exact validated type, table and columns"
    }
    Assert-OfflineTest (-not (Test-PgAppointmentIntegrityConstraintRow -Table "OtherTable" `
        -Name $validConstraints[1].Name -Validated "true" -Definition $validConstraints[1].Definition)) `
        "appointment FK verification checks table ownership"
    Assert-OfflineTest (-not (Test-PgAppointmentIntegrityConstraintRow -Table "Appointments" `
        -Name $validConstraints[1].Name -Validated "true" `
        -Definition ($validConstraints[1].Definition.Replace("ON DELETE RESTRICT", "ON DELETE NO ACTION")))) `
        "appointment FK verification requires restrictive delete behavior"
    Assert-OfflineTest (-not (Test-PgAppointmentIntegrityConstraintRow -Table "Appointments" `
        -Name $validConstraints[2].Name -Validated "true" `
        -Definition ($validConstraints[2].Definition.Replace("'[)", "'[]")))) `
        "appointment exclusion verification requires half-open interval boundaries"

    # PostgreSQL 18 may print the varchar Status comparison with a text cast.
    $catalogPredicates = @(
        '(("Status")::text = ''Confirmed''::text)',
        '((("Status")::text = ''Confirmed''::text))'
    )
    foreach ($constraint in $validConstraints[2..3]) {
        $definitionPrefix = ($constraint.Definition -split ' WHERE ', 2)[0]
        foreach ($catalogPredicate in $catalogPredicates) {
            Assert-OfflineTest (Test-PgAppointmentIntegrityConstraintRow -Table $constraint.Table `
                -Name $constraint.Name -Validated $constraint.Validated `
                -Definition "$definitionPrefix WHERE $catalogPredicate") `
                "accepts the PostgreSQL 18 catalog form of the confirmed-only predicate"
        }
        $catalogDefinition = "$definitionPrefix WHERE $($catalogPredicates[1])"
        Assert-OfflineTest (-not (Test-PgAppointmentIntegrityConstraintRow -Table $constraint.Table `
            -Name $constraint.Name -Validated $constraint.Validated `
            -Definition ($catalogDefinition.Replace("Confirmed", "Cancelled")))) `
            "rejects a different booking status"
        Assert-OfflineTest (-not (Test-PgAppointmentIntegrityConstraintRow -Table $constraint.Table `
            -Name $constraint.Name -Validated $constraint.Validated `
            -Definition ($catalogDefinition + ' OR true'))) `
            "rejects an extra predicate"
        Assert-OfflineTest (-not (Test-PgAppointmentIntegrityConstraintRow -Table $constraint.Table `
            -Name $constraint.Name -Validated $constraint.Validated `
            -Definition ($catalogDefinition.Substring(0, $catalogDefinition.Length - 1)))) `
            "rejects unbalanced predicate parentheses"
    }
    Assert-OfflineTest (Test-PgAppointmentIntegrityConstraintRow -Table "Appointments" `
        -Name $validConstraints[1].Name -Validated "true" `
        -Definition ($validConstraints[1].Definition.Replace('"DoctorServices" (', '"DoctorServices"('))) `
        "catalog FK accepts optional spacing before referenced columns"

    $readOnlySql = New-PgReadOnlySqlText -Sql "SELECT current_database();"
    Assert-OfflineTest ($readOnlySql -match '\ABEGIN TRANSACTION READ ONLY;') "inspection SQL starts a read-only transaction"
    Assert-OfflineTest ($readOnlySql -match 'SELECT current_database\(\);' -and $readOnlySql -match 'ROLLBACK;\s*\z') `
        "inspection SQL ends by rolling back its read-only transaction"
    $rejectedInspectionSql = $false
    try { $null = New-PgReadOnlySqlText -Sql "SELECT 1; DELETE FROM public.""Clinics""" }
    catch { $rejectedInspectionSql = $true }
    Assert-OfflineTest $rejectedInspectionSql "inspection SQL rejects multiple statements"
    Assert-OfflineTest (Test-PgApproval -Response " RUN_13_POSTGRESQL_TESTS `t" -Token "RUN_13_POSTGRESQL_TESTS") `
        "approval input trims surrounding whitespace while preserving exact tokens"
    Assert-OfflineTest (-not (Test-PgApproval -Response "run_13_postgresql_tests" -Token "RUN_13_POSTGRESQL_TESTS")) `
        "approval tokens remain case-sensitive"
    $safeDiagnostic = ConvertTo-PgSanitizedDiagnostic -Text "Failure: Password=synthetic-secret; host=local"
    Assert-OfflineTest (-not $safeDiagnostic.Contains("synthetic-secret") -and $safeDiagnostic.Contains("[REDACTED]")) `
        "diagnostics redact credentials"

    $sqlFile = New-PgSqlTemporaryFile -Sql $readOnlySql
    $temporaryFiles += $sqlFile
    $bytes = [IO.File]::ReadAllBytes($sqlFile)
    Assert-OfflineTest (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)) `
        "temporary SQL is UTF-8 without a BOM"
    Assert-OfflineTest ([IO.File]::ReadAllText($sqlFile).Contains("BEGIN TRANSACTION READ ONLY")) `
        "temporary SQL file retains the read-only transaction"

    $guard = New-PgWriteGuardSql -Database "nexora_dental_test" -RepositoryRoot $root `
        -ExpectedMigrationIds @($migrationIds[0..4])
    Assert-OfflineTest ($guard.Contains("current_database()") -and $guard.Contains("current_user")) `
        "approved write guard verifies same-session target identity"
    Assert-OfflineTest ($guard.Contains("actual_ids IS DISTINCT FROM ARRAY['20261027132306_PostgreSqlBaseline', '20261027180725_AddPostgreSqlAppointmentFoundation', '20261102165221_AddPostgreSqlNotificationOutbox', '20261102180315_AddPostgreSqlPhoneVerificationChallenges', '20261125115303_AddPostgreSqlClinicTenantRootForeignKeys']::text[]")) `
        "approved write guard checks the exact ordered current migration history"
    Assert-OfflineTest ($guard.Contains("ProductVersion")) "approved write guard checks EF product versions"
    $priorGuard = New-PgWriteGuardSql -Database "nexora_dental_test" -RepositoryRoot $root `
        -ExpectedMigrationIds @($migrationIds[0..5])
    Assert-OfflineTest ($priorGuard.Contains("actual_count <> 6") -and
        -not $priorGuard.Contains("AddPostgreSqlAppointmentReschedules")) "six-migration prefix remains independently fenced"
    $completeGuard = New-PgWriteGuardSql -Database "nexora_dental_test" -RepositoryRoot $root `
        -ExpectedMigrationIds $migrationIds
    Assert-OfflineTest ($completeGuard.Contains("'20261126070352_EnforceAppointmentIntegrity'") -and
        $completeGuard.Contains("'20261127122656_AddPostgreSqlAppointmentReschedules'") -and
        $completeGuard.Contains("actual_count <> 7")) `
        "approved write guard verifies the exact seven-migration history, including the reschedule ledger"

    $rows = @(
        [PSCustomObject]@{ Table = "Clinics"; Json = '{"Id":2,"Name":"B"}' },
        [PSCustomObject]@{ Table = "Clinics"; Json = '{"Id":1,"Name":"A"}' },
        [PSCustomObject]@{ Table = "Users"; Json = '{"Id":3,"Name":"C"}' }
    )
    $fingerprint = ConvertTo-PgApplicationDataFingerprint -Rows $rows
    $reorderedFingerprint = ConvertTo-PgApplicationDataFingerprint -Rows @($rows[2], $rows[0], $rows[1])
    $changedFingerprint = ConvertTo-PgApplicationDataFingerprint -Rows @(
        [PSCustomObject]@{ Table = "Clinics"; Json = '{"Id":2,"Name":"Changed"}' },
        $rows[1],
        $rows[2]
    )
    Assert-OfflineTest ($fingerprint -ceq $reorderedFingerprint) "application-data fingerprint is order-independent"
    Assert-OfflineTest ($fingerprint -cne $changedFingerprint) "application-data fingerprint detects changed rows"

    $trxPath = Join-Path ([IO.Path]::GetTempPath()) ("SuvdDent-Offline-" + [Guid]::NewGuid().ToString("N") + ".trx")
    $temporaryFiles += $trxPath
    $trx = @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="pass-1" outcome="Passed" />
    <UnitTestResult testName="pass-2" outcome="Passed" />
    <UnitTestResult testName="failed" outcome="Failed">
      <Output><ErrorInfo><Message>Failure: Password=synthetic-secret; sanitized sample.</Message></ErrorInfo></Output>
    </UnitTestResult>
    <UnitTestResult testName="skipped" outcome="NotExecuted" />
  </Results>
</TestRun>
'@
    [IO.File]::WriteAllText($trxPath, $trx, (New-Object System.Text.UTF8Encoding -ArgumentList $false))
    $testResults = Get-PgTrxTestResults -Path $trxPath
    Assert-OfflineTest ($testResults.Total -eq 4 -and $testResults.Passed -eq 2 -and
        $testResults.Failed -eq 1 -and $testResults.Skipped -eq 1) "TRX results classify pass, failure, and skip outcomes"
    Assert-OfflineTest ($testResults.Diagnostics.Count -eq 1 -and
        -not $testResults.Diagnostics[0].Message.Contains("synthetic-secret")) "TRX failure diagnostics are sanitized"
    Assert-OfflineTest (Test-PgIntegrationTestResults -Results ([PSCustomObject]@{
        Total = 13; Passed = 13; Failed = 0; Skipped = 0
    })) "integration runner accepts exactly thirteen passing structured TRX results"
    Assert-OfflineTest (-not (Test-PgIntegrationTestResults -Results ([PSCustomObject]@{
        Total = 13; Passed = 12; Failed = 1; Skipped = 0
    }))) "integration runner rejects a failed structured TRX result"

    Write-Output "Offline PostgreSQL automation helper tests passed."
}
finally {
    foreach ($path in $temporaryFiles) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    Remove-Module PostgreSqlAutomation.Common -ErrorAction SilentlyContinue
}
