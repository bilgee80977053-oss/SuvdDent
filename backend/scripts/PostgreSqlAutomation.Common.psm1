Set-StrictMode -Version Latest

$script:PgHost = "127.0.0.1"
$script:PgPort = 5432
$script:PgRole = "nexora_dev"
$script:AllowedDatabases = @("nexora_dental_test", "nexora_dental_rehearsal")

function Assert-PgAutomationEnvironmentClear {
    if (Test-Path Env:\PGPASSWORD) {
        throw "Remove inherited PGPASSWORD before starting this workflow."
    }
    if (Test-Path Env:\NEXORA_PG_TEST_CONNECTION) {
        throw "Remove inherited NEXORA_PG_TEST_CONNECTION before starting this workflow."
    }
}

function Read-PgSecurePassword {
    $securePassword = Read-Host "PostgreSQL password for the approved isolated target" -AsSecureString
    if ($null -eq $securePassword -or $securePassword.Length -eq 0) {
        if ($null -ne $securePassword) { $securePassword.Dispose() }
        throw "A PostgreSQL password is required."
    }

    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        if ($pointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        }
        $securePassword.Dispose()
    }
}

function Assert-PgDatabaseAllowlisted {
    param([Parameter(Mandatory)][string]$Database)

    if ($script:AllowedDatabases -notcontains $Database) {
        throw "Database target rejected. Only the isolated test and rehearsal databases are allowed."
    }
}

function Test-PgApproval {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Response,
        [Parameter(Mandatory)][string]$Token
    )

    return $Response.Trim() -ceq $Token
}

function ConvertTo-PgSanitizedDiagnostic {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [ValidateRange(1, 10000)][int]$MaxLength = 1000
    )

    $safeText = $Text
    foreach ($secret in @($env:PGPASSWORD, $env:NEXORA_PG_TEST_CONNECTION)) {
        if (-not [string]::IsNullOrEmpty($secret)) {
            $safeText = $safeText.Replace($secret, "[REDACTED]")
        }
    }
    $safeText = [regex]::Replace($safeText, '(?i)(NEXORA_PG_TEST_CONNECTION|PGPASSWORD)\s*[:=]\s*[^\r\n]+', '$1=[REDACTED]')
    $safeText = [regex]::Replace($safeText,
        '(?i)(password\s*=\s*)(?:"[^"]*"|''[^'']*''|[^;\s]+)', '${1}[REDACTED]')
    $safeText = [regex]::Replace($safeText, '[\x00-\x08\x0B\x0C\x0E-\x1F]', '')
    if ($safeText.Length -gt $MaxLength) {
        $safeText = $safeText.Substring(0, $MaxLength) + "..."
    }
    return $safeText
}

function ConvertTo-PgSanitizedTestOutput {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Lines)

    $text = @($Lines | ForEach-Object { [string]$_ } | Select-Object -Last 20) -join "`n"
    return ConvertTo-PgSanitizedDiagnostic -Text $text -MaxLength 3000
}

function Get-PgExecutable {
    param([Parameter(Mandatory)][ValidateSet("psql", "pg_dump", "pg_restore")][string]$Name)

    $candidates = @()
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command -and $command.CommandType -eq "Application") {
        $candidates += $command.Source
    }

    foreach ($programFiles in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432)) {
        if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
            $candidates += Join-Path $programFiles "PostgreSQL\18\bin\$Name.exe"
        }
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }
        $versionOutput = @(& $candidate --version 2>$null)
        if ($LASTEXITCODE -eq 0 -and
            (($versionOutput -join " ") -match '\bPostgreSQL\)?\s+18(?:\.|$)')) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "A PostgreSQL 18 '$Name' executable could not be found on PATH or under Program Files."
}

function New-PgSqlTemporaryFile {
    param([Parameter(Mandatory)][string]$Sql)

    $path = Join-Path ([IO.Path]::GetTempPath()) ("SuvdDent-" + [Guid]::NewGuid().ToString("N") + ".sql")
    $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::WriteAllText($path, $Sql, $utf8NoBom)
    return $path
}

function New-PgReadOnlySqlText {
    param([Parameter(Mandatory)][string]$Sql)

    $query = $Sql.Trim().TrimEnd(";").Trim()
    if ($query -notmatch '\ASELECT\b' -or $query.Contains(";") -or $query.Contains("\")) {
        throw "Inspection SQL must be one SELECT statement without transaction or psql commands."
    }
    return "BEGIN TRANSACTION READ ONLY;`n$query;`nROLLBACK;`n"
}

function Invoke-PgSql {
    param(
        [Parameter(Mandatory)][ValidateSet("nexora_dental_test", "nexora_dental_rehearsal")]
        [string]$Database,
        [Parameter(Mandatory)][string]$Sql
    )

    Assert-PgDatabaseAllowlisted $Database
    if ([string]::IsNullOrEmpty($env:PGPASSWORD)) {
        throw "The temporary PostgreSQL credential is not available."
    }
    $psql = Get-PgExecutable -Name psql
    $readOnlySql = New-PgReadOnlySqlText -Sql $Sql
    $sqlFile = New-PgSqlTemporaryFile -Sql $readOnlySql
    try {
        $output = @(& $psql -X --no-password --set ON_ERROR_STOP=1 --no-align --tuples-only --quiet `
            --field-separator "|" --host $script:PgHost --port $script:PgPort --username $script:PgRole `
            --dbname $Database --file $sqlFile 2>$null)
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL read-only query failed; details were suppressed."
        }
        return @($output | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_.Length -gt 0 })
    }
    finally {
        if (Test-Path -LiteralPath $sqlFile -PathType Leaf) {
            Remove-Item -LiteralPath $sqlFile -Force
        }
    }
}

function Invoke-PgSqlFile {
    param(
        [Parameter(Mandatory)][ValidateSet("nexora_dental_test", "nexora_dental_rehearsal")]
        [string]$Database,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string[]]$ExpectedMigrationIds
    )

    Assert-PgDatabaseAllowlisted $Database
    if ([string]::IsNullOrEmpty($env:PGPASSWORD)) {
        throw "The temporary PostgreSQL credential is not available."
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The approved SQL file was not found."
    }
    $guard = New-PgWriteGuardSql -Database $Database -RepositoryRoot $RepositoryRoot `
        -ExpectedMigrationIds $ExpectedMigrationIds
    $migrationSql = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).Path)
    $guardedSqlFile = New-PgSqlTemporaryFile -Sql ($guard + "`n" + $migrationSql)
    try {
        $psql = Get-PgExecutable -Name psql
        $null = & $psql -X --no-password --set ON_ERROR_STOP=1 --quiet `
            --host $script:PgHost --port $script:PgPort --username $script:PgRole `
            --dbname $Database --file $guardedSqlFile 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL SQL execution failed; provider details were suppressed."
        }
    }
    finally {
        if (Test-Path -LiteralPath $guardedSqlFile -PathType Leaf) {
            Remove-Item -LiteralPath $guardedSqlFile -Force
        }
    }
}

function ConvertTo-PgSqlLiteral {
    param([Parameter(Mandatory)][string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

function New-PgWriteGuardSql {
    param(
        [Parameter(Mandatory)][ValidateSet("nexora_dental_test", "nexora_dental_rehearsal")][string]$Database,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string[]]$ExpectedMigrationIds
    )

    Assert-PgDatabaseAllowlisted $Database
    if ($ExpectedMigrationIds.Count -eq 0 -or
        @($ExpectedMigrationIds | Where-Object { $_ -notmatch '\A\d{14}_[A-Za-z0-9_]+\z' }).Count -gt 0) {
        throw "The approved write requires a non-empty ordered migration history."
    }
    $repositoryIds = @(Get-PgExpectedMigrationIds -RepositoryRoot $RepositoryRoot)
    if ($ExpectedMigrationIds.Count -gt $repositoryIds.Count) {
        throw "The approved write history is not a repository migration prefix."
    }
    for ($index = 0; $index -lt $ExpectedMigrationIds.Count; $index++) {
        if ($ExpectedMigrationIds[$index] -cne $repositoryIds[$index]) {
            throw "The approved write history is not a repository migration prefix."
        }
    }

    $productVersion = ConvertTo-PgSqlLiteral (Get-PgEfProductVersion -RepositoryRoot $RepositoryRoot)
    $migrationArray = @($ExpectedMigrationIds | ForEach-Object { ConvertTo-PgSqlLiteral $_ }) -join ", "
    $expectedDatabase = ConvertTo-PgSqlLiteral $Database
    $expectedRole = ConvertTo-PgSqlLiteral $script:PgRole
    $expectedHost = ConvertTo-PgSqlLiteral $script:PgHost
    $expectedPort = ConvertTo-PgSqlLiteral "$($script:PgPort)"
    return @"
DO `$pg_guard`$
DECLARE
    actual_ids text[];
    actual_count integer;
BEGIN
    IF current_database() <> $expectedDatabase OR current_user <> $expectedRole OR
       COALESCE(host(inet_server_addr()), '') <> $expectedHost OR
       COALESCE(inet_server_port()::text, '') <> $expectedPort OR
       COALESCE(current_schema(), '') <> 'public' OR
       current_setting('server_version_num')::integer / 10000 <> 18 THEN
        RAISE EXCEPTION 'PostgreSQL write target identity changed';
    END IF;

    SELECT COALESCE(array_agg("MigrationId" ORDER BY "MigrationId"), ARRAY[]::text[]),
           count(*)
    INTO actual_ids, actual_count
    FROM public."__EFMigrationsHistory";

    IF actual_ids IS DISTINCT FROM ARRAY[$migrationArray]::text[] OR
       actual_count <> $($ExpectedMigrationIds.Count) OR
       EXISTS (
           SELECT 1
           FROM public."__EFMigrationsHistory"
           WHERE "ProductVersion" <> $productVersion
       ) THEN
        RAISE EXCEPTION 'PostgreSQL migration history changed before approved write';
    END IF;
END
`$pg_guard`$;
"@
}

function Get-PgTargetIdentity {
    param([Parameter(Mandatory)][string]$Database)

    $query = @"
SELECT current_database() || '|' || current_user || '|' ||
       COALESCE(host(inet_server_addr()), '') || '|' ||
       COALESCE(inet_server_port()::text, '') || '|' ||
       COALESCE(current_schema(), '') || '|' ||
       (current_setting('server_version_num')::integer / 10000)::text;
"@
    $rows = @(Invoke-PgSql -Database $Database -Sql $query)
    if ($rows.Count -ne 1) {
        throw "PostgreSQL target identity could not be verified."
    }
    $fields = $rows[0] -split "\|", 6
    if ($fields.Count -ne 6) {
        throw "PostgreSQL target identity could not be verified."
    }
    return [PSCustomObject]@{
        Database = $fields[0]
        Role = $fields[1]
        Host = $fields[2]
        Port = $fields[3]
        Schema = $fields[4]
        MajorVersion = $fields[5]
    }
}

function Assert-PgTargetIdentity {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$ExpectedDatabase
    )

    Assert-PgDatabaseAllowlisted $ExpectedDatabase
    if ($Database -cne $ExpectedDatabase) {
        throw "Connected database does not match the fixed isolated target."
    }
    $identity = Get-PgTargetIdentity -Database $ExpectedDatabase
    if ($identity.Database -cne $ExpectedDatabase -or $identity.Role -cne $script:PgRole -or
        $identity.Host -cne $script:PgHost -or $identity.Port -cne "$($script:PgPort)" -or
        $identity.Schema -cne "public" -or $identity.MajorVersion -cne "18") {
        throw "PostgreSQL target rejected. Verify the isolated database, local endpoint, role, schema, and server version."
    }
    return $identity
}

function Get-PgExpectedMigrationIds {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $directory = Join-Path $RepositoryRoot "backend\DentalApi\Migrations\PostgreSql"
    $ids = @()
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter "*.Designer.cs" -File) {
        $content = [IO.File]::ReadAllText($file.FullName)
        $match = [regex]::Match($content, '\[Migration\("([^"]+)"\)\]')
        if ($match.Success) {
            $ids += $match.Groups[1].Value
        }
    }
    if ($ids.Count -eq 0) {
        throw "PostgreSQL EF migration metadata could not be discovered."
    }
    [Array]::Sort([string[]]$ids, [StringComparer]::Ordinal)
    for ($index = 1; $index -lt $ids.Count; $index++) {
        if ($ids[$index] -ceq $ids[$index - 1]) {
            throw "PostgreSQL EF migration metadata contains a duplicate migration ID."
        }
    }
    return [string[]]$ids
}

function Get-PgEfProductVersion {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $projectPath = Join-Path $RepositoryRoot "backend\DentalApi\DentalApi.csproj"
    [xml]$project = [IO.File]::ReadAllText($projectPath)
    $node = $project.SelectSingleNode("//PackageReference[@Include='Microsoft.EntityFrameworkCore.Design']/@Version")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.Value)) {
        throw "EF Core product version could not be read from the project."
    }
    return $node.Value
}

function Get-PgMigrationHistory {
    param([Parameter(Mandatory)][string]$Database)

    $rows = @(Invoke-PgSql -Database $Database -Sql `
        'SELECT "MigrationId" || ''|'' || "ProductVersion" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";')
    return @($rows | ForEach-Object {
        $fields = $_ -split "\|", 2
        if ($fields.Count -ne 2) {
            throw "PostgreSQL migration history returned an invalid row."
        }
        [PSCustomObject]@{ Id = $fields[0]; ProductVersion = $fields[1] }
    })
}

function Assert-PgHistoryPrefix {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [int]$ExpectedCount = -1
    )

    $expected = @(Get-PgExpectedMigrationIds -RepositoryRoot $RepositoryRoot)
    $history = @(Get-PgMigrationHistory -Database $Database)
    $productVersion = Get-PgEfProductVersion -RepositoryRoot $RepositoryRoot
    if ($history.Count -gt $expected.Count -or
        ($ExpectedCount -ge 0 -and $history.Count -ne $ExpectedCount)) {
        throw "PostgreSQL migration history is not the expected ordered prefix."
    }
    for ($index = 0; $index -lt $history.Count; $index++) {
        if ($history[$index].Id -cne $expected[$index] -or
            $history[$index].ProductVersion -cne $productVersion) {
            throw "PostgreSQL migration history is not the expected ordered prefix."
        }
    }
    return [PSCustomObject]@{ Expected = $expected; Applied = $history; ProductVersion = $productVersion }
}

function Get-PgConstraintRows {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string[]]$ConstraintNames
    )

    if ($ConstraintNames.Count -eq 0 -or
        @($ConstraintNames | Where-Object { -not (Test-PgConstraintName -Name $_) }).Count -gt 0) {
        throw "Constraint names must be a non-empty list of SQL identifiers."
    }
    $quotedNames = @($ConstraintNames | ForEach-Object { "'" + $_ + "'" }) -join ","
    $query = @"
SELECT tableClass.relname || '|' || con.conname || '|' || con.convalidated::text || '|' ||
       replace(pg_get_constraintdef(con.oid, true), chr(10), ' ')
FROM pg_catalog.pg_constraint AS con
JOIN pg_catalog.pg_class AS tableClass ON tableClass.oid = con.conrelid
JOIN pg_catalog.pg_namespace AS schemaInfo ON schemaInfo.oid = tableClass.relnamespace
WHERE schemaInfo.nspname = 'public' AND con.conname IN ($quotedNames)
ORDER BY tableClass.relname, con.conname;
"@
    return @(Invoke-PgSql -Database $Database -Sql $query)
}

function Test-PgConstraintName {
    param([Parameter(Mandatory)][string]$Name)

    return $Name -cmatch '\A[A-Za-z0-9_]+~?\z'
}

function Assert-PgTenantRootConstraints {
    param([Parameter(Mandatory)][string]$Database)

    $expected = [ordered]@{
        "FK_Patients_Clinics_ClinicId" = "Patients"
        "FK_Services_Clinics_ClinicId" = "Services"
        "FK_Users_Clinics_ClinicId" = "Users"
    }
    $rows = @(Get-PgConstraintRows -Database $Database -ConstraintNames @($expected.Keys))
    if ($rows.Count -ne $expected.Count) {
        throw "Required tenant-root constraints are missing or duplicated."
    }
    foreach ($row in $rows) {
        $fields = $row -split "\|", 4
        if ($fields.Count -ne 4 -or -not $expected.Contains($fields[1]) -or
            $fields[0] -cne $expected[$fields[1]] -or $fields[2] -cne "true" -or
            $fields[3] -notmatch 'FOREIGN KEY\s+\("ClinicId"\)\s+REFERENCES\s+(?:public\.)?"Clinics"\s*\("Id"\)\s+ON DELETE RESTRICT') {
            throw "A required validated restrictive clinic foreign key does not match the reviewed definition."
        }
    }
    return $rows
}

function Assert-PgAppointmentIntegrityConstraints {
    param([Parameter(Mandatory)][string]$Database)

    $expected = [ordered]@{
        "AK_DoctorServices_ClinicId_DoctorProfileId_ServiceId" = "DoctorServices"
        "FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_Servic~" = "Appointments"
        "EX_Appointments_ConfirmedDoctorOccupied_NoOverlap" = "Appointments"
        "EX_Appointments_ConfirmedPatientTreatment_NoOverlap" = "Appointments"
    }
    $rows = @(Get-PgConstraintRows -Database $Database -ConstraintNames @($expected.Keys))
    if ($rows.Count -ne $expected.Count) {
        throw "Required appointment-integrity constraints are missing or duplicated."
    }
    foreach ($row in $rows) {
        $fields = $row -split "\|", 4
        if ($fields.Count -ne 4 -or -not $expected.Contains($fields[1]) -or
            -not (Test-PgAppointmentIntegrityConstraintRow -Table $fields[0] -Name $fields[1] `
                -Validated $fields[2] -Definition $fields[3])) {
            throw "An appointment-integrity constraint is missing, unvalidated, or attached to the wrong table."
        }
    }
    return $rows
}

function Test-PgAppointmentIntegrityConstraintRow {
    param(
        [Parameter(Mandatory)][string]$Table,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Validated,
        [Parameter(Mandatory)][string]$Definition
    )

    if ($Validated -cne "true") {
        return $false
    }

    switch ($Name) {
        "AK_DoctorServices_ClinicId_DoctorProfileId_ServiceId" {
            return $Table -ceq "DoctorServices" -and
                $Definition -cmatch '\AUNIQUE\s+\("ClinicId",\s*"DoctorProfileId",\s*"ServiceId"\)\z'
        }
        "FK_Appointments_DoctorServices_ClinicId_DoctorProfileId_Servic~" {
            return $Table -ceq "Appointments" -and
                $Definition -cmatch '\AFOREIGN KEY\s+\("ClinicId",\s*"DoctorProfileId",\s*"ServiceId"\)\s+REFERENCES\s+(?:public\.)?"DoctorServices"\s+\("ClinicId",\s*"DoctorProfileId",\s*"ServiceId"\)\s+ON DELETE RESTRICT\z'
        }
        "EX_Appointments_ConfirmedDoctorOccupied_NoOverlap" {
            return $Table -ceq "Appointments" -and
                $Definition -cmatch '\AEXCLUDE USING gist\s+\("ClinicId"\s+WITH =,\s*"DoctorProfileId"\s+WITH =,\s*tstzrange\("OccupiedStartUtc",\s*"OccupiedEndUtc",\s*''\[\)''(?:::text)?\)\s+WITH &&\)\s+WHERE \("Status"\s*=\s*''Confirmed''(?:::text)?\)\z'
        }
        "EX_Appointments_ConfirmedPatientTreatment_NoOverlap" {
            return $Table -ceq "Appointments" -and
                $Definition -cmatch '\AEXCLUDE USING gist\s+\("ClinicId"\s+WITH =,\s*"PatientId"\s+WITH =,\s*tstzrange\("StartUtc",\s*"EndUtc",\s*''\[\)''(?:::text)?\)\s+WITH &&\)\s+WHERE \("Status"\s*=\s*''Confirmed''(?:::text)?\)\z'
        }
        default { return $false }
    }
}

function Test-PgIntegrationTestResults {
    param([Parameter(Mandatory)][object]$Results)

    return $Results.Total -eq 13 -and $Results.Passed -eq 13 -and
        $Results.Failed -eq 0 -and $Results.Skipped -eq 0
}

function Get-PgTenantRootOrphanCount {
    param([Parameter(Mandatory)][string]$Database)

    $query = @"
SELECT count(*)
FROM (
    SELECT u."Id"
    FROM public."Users" AS u
    LEFT JOIN public."Clinics" AS c ON c."Id" = u."ClinicId"
    WHERE c."Id" IS NULL
    UNION ALL
    SELECT s."Id"
    FROM public."Services" AS s
    LEFT JOIN public."Clinics" AS c ON c."Id" = s."ClinicId"
    WHERE c."Id" IS NULL
    UNION ALL
    SELECT p."Id"
    FROM public."Patients" AS p
    LEFT JOIN public."Clinics" AS c ON c."Id" = p."ClinicId"
    WHERE c."Id" IS NULL
) AS missingClinic;
"@
    $rows = @(Invoke-PgSql -Database $Database -Sql $query)
    if ($rows.Count -ne 1) {
        throw "Tenant-root preflight returned an unexpected result."
    }
    return [long]$rows[0]
}

function ConvertTo-PgApplicationDataFingerprint {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Rows)

    $canonicalRows = New-Object 'System.Collections.Generic.List[string]'
    foreach ($row in $Rows) {
        if ($null -eq $row -or [string]::IsNullOrWhiteSpace([string]$row.Table) -or $null -eq $row.Json) {
            throw "Application-data fingerprint input contains an invalid row."
        }
        $canonicalRows.Add([string]$row.Table + [char]0 + [string]$row.Json)
    }
    $sortedRows = $canonicalRows.ToArray()
    [Array]::Sort($sortedRows, [StringComparer]::Ordinal)

    $builder = New-Object System.Text.StringBuilder
    foreach ($canonicalRow in $sortedRows) {
        $separator = $canonicalRow.IndexOf([char]0)
        $table = $canonicalRow.Substring(0, $separator)
        $json = $canonicalRow.Substring($separator + 1)
        [void]$builder.Append($table.Length.ToString([Globalization.CultureInfo]::InvariantCulture))
        [void]$builder.Append(":").Append($table)
        [void]$builder.Append($json.Length.ToString([Globalization.CultureInfo]::InvariantCulture))
        [void]$builder.Append(":").Append($json).Append("`n")
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    $bytes = $utf8NoBom.GetBytes($builder.ToString())
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-PgApplicationDataFingerprint {
    param([Parameter(Mandatory)][ValidateSet("nexora_dental_test", "nexora_dental_rehearsal")][string]$Database)

    $tableQuery = @"
SELECT relname
FROM pg_catalog.pg_class AS relation
JOIN pg_catalog.pg_namespace AS schemaInfo ON schemaInfo.oid = relation.relnamespace
WHERE schemaInfo.nspname = 'public'
  AND relation.relkind IN ('r', 'p')
  AND relation.relname <> '__EFMigrationsHistory'
ORDER BY relname;
"@
    $tableNames = @(Invoke-PgSql -Database $Database -Sql $tableQuery)
    $tableQueries = New-Object 'System.Collections.Generic.List[string]'
    foreach ($tableName in $tableNames) {
        if ($tableName -notmatch '\A[A-Za-z0-9_]+\z') {
            throw "An application table name could not be safely quoted for fingerprinting."
        }
        $quotedTable = '"' + $tableName.Replace('"', '""') + '"'
        $tableLiteral = ConvertTo-PgSqlLiteral $tableName
        $tableQueries.Add("SELECT $tableLiteral || '|' || to_jsonb(applicationRow)::text FROM public.$quotedTable AS applicationRow")
    }

    $rows = New-Object 'System.Collections.Generic.List[object]'
    if ($tableQueries.Count -gt 0) {
        $query = ($tableQueries -join "`nUNION ALL`n") + "`nORDER BY 1"
        foreach ($line in @(Invoke-PgSql -Database $Database -Sql $query)) {
            $separator = $line.IndexOf("|")
            if ($separator -le 0) {
                throw "Application-data fingerprint query returned an invalid row."
            }
            $rows.Add([PSCustomObject]@{
                Table = $line.Substring(0, $separator)
                Json = $line.Substring($separator + 1)
            })
        }
    }
    return ConvertTo-PgApplicationDataFingerprint -Rows ($rows.ToArray())
}

function Get-PgTrxTestResults {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The .NET test result file was not found."
    }
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create((Resolve-Path -LiteralPath $Path).Path, $settings)
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    $results = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($results.Count -eq 0) {
        throw "The .NET test result file contains no structured test results."
    }
    $passed = @($results | Where-Object { $_.GetAttribute("outcome") -ceq "Passed" }).Count
    $failedResults = @($results | Where-Object {
        $_.GetAttribute("outcome") -cin @("Failed", "Error", "Timeout", "Aborted")
    })
    $failed = $failedResults.Count
    $skipped = $results.Count - $passed - $failed
    $diagnostics = @($failedResults | Select-Object -First 5 | ForEach-Object {
        $messageNode = $_.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
        $message = if ($null -eq $messageNode) { "" } else { $messageNode.InnerText }
        [PSCustomObject]@{
            Test = ConvertTo-PgSanitizedDiagnostic -Text $_.GetAttribute("testName") -MaxLength 200
            Outcome = $_.GetAttribute("outcome")
            Message = ConvertTo-PgSanitizedDiagnostic -Text $message -MaxLength 1000
        }
    })
    return [PSCustomObject]@{
        Failed = $failed
        Passed = $passed
        Skipped = $skipped
        Total = $results.Count
        Diagnostics = $diagnostics
    }
}

function New-PgTestConnectionString {
    param([Parameter(Mandatory)][string]$Password)

    $escapedPassword = $Password.Replace('"', '""')
    return 'Host=127.0.0.1;Port=5432;Database=nexora_dental_test;Username=nexora_dev;Password="' +
        $escapedPassword + '";Search Path=public;Pooling=false;Enlist=false;Include Error Detail=false;' +
        'Log Parameters=false;Timeout=5;Command Timeout=15;Application Name="Nexora isolated integration tests"'
}

function Write-PgJsonReport {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Report
    )

    $directory = Split-Path -Parent $Path
    [void][IO.Directory]::CreateDirectory($directory)
    $json = ConvertTo-Json -InputObject $Report -Depth 8
    $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

Export-ModuleMember -Function Assert-PgAutomationEnvironmentClear, Assert-PgTargetIdentity, Assert-PgHistoryPrefix, `
    Get-PgExpectedMigrationIds, Get-PgEfProductVersion, Get-PgMigrationHistory, Get-PgConstraintRows, `
    Test-PgConstraintName, Assert-PgTenantRootConstraints, Assert-PgAppointmentIntegrityConstraints, `
    Test-PgAppointmentIntegrityConstraintRow, Test-PgIntegrationTestResults, Test-PgApproval, `
    ConvertTo-PgSanitizedDiagnostic, ConvertTo-PgSanitizedTestOutput, `
    Get-PgExecutable, New-PgSqlTemporaryFile, New-PgReadOnlySqlText, New-PgWriteGuardSql, `
    Get-PgTenantRootOrphanCount, Invoke-PgSql, Invoke-PgSqlFile, New-PgTestConnectionString, `
    Read-PgSecurePassword, Write-PgJsonReport, ConvertTo-PgApplicationDataFingerprint, `
    Get-PgApplicationDataFingerprint, Get-PgTrxTestResults
