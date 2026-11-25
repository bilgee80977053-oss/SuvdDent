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
    $psql = Get-Command psql -ErrorAction Stop
    $output = @(& $psql.Source -X --no-password --set ON_ERROR_STOP=1 --no-align --tuples-only --quiet `
        --field-separator "|" --host $script:PgHost --port $script:PgPort --username $script:PgRole `
        --dbname $Database --command $Sql 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL read-only query failed; details were suppressed."
    }
    return @($output | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_.Length -gt 0 })
}

function Invoke-PgSqlFile {
    param(
        [Parameter(Mandatory)][ValidateSet("nexora_dental_test", "nexora_dental_rehearsal")]
        [string]$Database,
        [Parameter(Mandatory)][string]$Path
    )

    Assert-PgDatabaseAllowlisted $Database
    if ([string]::IsNullOrEmpty($env:PGPASSWORD)) {
        throw "The temporary PostgreSQL credential is not available."
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The approved SQL file was not found."
    }
    $psql = Get-Command psql -ErrorAction Stop
    $null = & $psql.Source -X --no-password --set ON_ERROR_STOP=1 --quiet `
        --host $script:PgHost --port $script:PgPort --username $script:PgRole `
        --dbname $Database --file $Path 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL SQL execution failed; provider details were suppressed."
    }
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
    $ids = @($ids | Sort-Object -Unique)
    if ($ids.Count -eq 0) {
        throw "PostgreSQL EF migration metadata could not be discovered."
    }
    return ,([string[]]$ids)
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
        @($ConstraintNames | Where-Object { $_ -notmatch '\A[A-Za-z0-9_]+\z' }).Count -gt 0) {
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
    Assert-PgTenantRootConstraints, `
    Get-PgTenantRootOrphanCount, Invoke-PgSql, Invoke-PgSqlFile, New-PgTestConnectionString, `
    Read-PgSecurePassword, Write-PgJsonReport
