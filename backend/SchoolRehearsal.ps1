[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Create', 'Inspect', 'Start')][string]$Mode = 'Plan',
    [string]$DatabasePath = (Join-Path $env:LOCALAPPDATA 'NexoraDental\school-test\step5-c042034-20261028.sqlite'),
    [switch]$AllowFixtureWrites
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'NexoraDental\school-test'))
if (![IO.Path]::IsPathRooted($DatabasePath) -or $DatabasePath -match '^[A-Za-z]:[^\\/]') { throw 'Supply an absolute database FILE PATH.' }
$databaseFile = [IO.Path]::GetFullPath($DatabasePath)
Write-Output "Database FILE PATH: $databaseFile"
if ([IO.Path]::GetDirectoryName($databaseFile) -ne $root -or [IO.Path]::GetFileName($databaseFile) -notlike 'step5-*.sqlite' -or [IO.Path]::GetFileName($databaseFile).Contains(':')) { throw 'Use a direct step5-*.sqlite child of the dedicated school-test directory.' }
$ancestor = New-Object IO.DirectoryInfo($root)
while ($null -ne $ancestor) {
    if ($ancestor.Exists -and (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Linked/junction target directories are forbidden.' }
    $ancestor = $ancestor.Parent
}
if ($Mode -in @('Plan', 'Create')) {
    foreach ($suffix in @('', '-wal', '-shm', '-journal', '.fixture.json')) {
        if (Test-Path -LiteralPath ($databaseFile + $suffix)) { throw 'Database or sidecar already exists; nothing will be overwritten. Choose a new name.' }
    }
}
if ($Mode -eq 'Plan') {
    Write-Output 'PLAN ONLY: no database connection, directory creation, migration or writes.'
    Write-Output "Explicit script write opt-in supplied: $([bool]$AllowFixtureWrites)"
    return
}
if ($Mode -in @('Create', 'Start') -and !$AllowFixtureWrites) { throw 'STOP: supply -AllowFixtureWrites explicitly after reviewing this exact target. No database writes performed.' }
if (Test-Path Env:NEXORA_PG_TEST_CONNECTION) { throw 'STOP: remove the PostgreSQL test opt-in privately before the school rehearsal.' }
$project = Join-Path $PSScriptRoot 'DentalApi.SchoolFixture\DentalApi.SchoolFixture.csproj'
& dotnet build $project --nologo
if ($LASTEXITCODE -ne 0) { throw 'Fixture utility build failed; no rehearsal started.' }
$fixtureDll = Join-Path $PSScriptRoot 'DentalApi.SchoolFixture\bin\Debug\net10.0\DentalApi.SchoolFixture.dll'
$apiDirectory = Join-Path $PSScriptRoot 'DentalApi'
$apiDll = Join-Path $apiDirectory 'bin\Debug\net10.0\DentalApi.dll'
$names = @('NEXORA_SCHOOL_FIXTURE_WRITE_OPT_IN', 'NEXORA_SCHOOL_PATIENT_PASSWORD', 'Database__Provider', 'ConnectionStrings__Default', 'DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'DOTNET_URLS', 'URLS', 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', 'AllowedHosts', 'Cors__AllowedOrigins__0', 'ForwardedHeaders__Enabled', 'Jwt__Key', 'Jwt__Issuer', 'Jwt__Audience', 'Jwt__ExpireHours', 'PlatformJwt__Key', 'PlatformJwt__Issuer', 'PlatformJwt__Audience', 'Email__Host', 'Email__Port', 'Email__From', 'Email__Username', 'Email__Password', 'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
function New-PrivateKey {
    $bytes = New-Object byte[] 48
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); return [Convert]::ToBase64String($bytes) } finally { $rng.Dispose(); [Array]::Clear($bytes, 0, $bytes.Length) }
}
try {
    if ($Mode -eq 'Inspect') {
        & dotnet $fixtureDll inspect $databaseFile
        if ($LASTEXITCODE -ne 0) { throw 'Read-only fixture inspection failed.' }
        return
    }
    $env:NEXORA_SCHOOL_FIXTURE_WRITE_OPT_IN = 'CREATE_NEW_SQLITE_SCHOOL_FIXTURE'
    if ($Mode -eq 'Create') {
        # Password is never a command argument, printed value, source literal or file.
        $privatePassword = Read-Host 'Private synthetic PATIENT password (8+ characters; never share in chat)' -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($privatePassword)
        try { $env:NEXORA_SCHOOL_PATIENT_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $privatePassword.Dispose() }
        & dotnet $fixtureDll create $databaseFile
        if ($LASTEXITCODE -ne 0) { throw 'Fixture creation stopped. Preserve any partial files; never retry creation against the same target.' }
        return
    }
    [Environment]::SetEnvironmentVariable('NEXORA_SCHOOL_PATIENT_PASSWORD', $null, 'Process')
    if (@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object { $_.Port -eq 5018 }).Count -gt 0) { throw 'Port 5018 is already occupied; backend not started.' }
    $env:Database__Provider = 'Sqlite'
    $env:ConnectionStrings__Default = 'Data Source="' + $databaseFile.Replace('"', '""') + '";Mode=ReadWrite;Pooling=False;Foreign Keys=True'
    $env:DOTNET_ENVIRONMENT = 'Development'; $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5018'
    $env:DOTNET_URLS = 'http://127.0.0.1:5018'; $env:URLS = 'http://127.0.0.1:5018'
    $env:ASPNETCORE_FORWARDEDHEADERS_ENABLED = 'false'
    $env:AllowedHosts = '127.0.0.1;localhost'
    $env:Cors__AllowedOrigins__0 = 'http://localhost:7357'
    $env:ForwardedHeaders__Enabled = 'false'
    $env:Jwt__Key = New-PrivateKey; $env:PlatformJwt__Key = New-PrivateKey
    $env:Jwt__Issuer = 'NexoraSchoolClinic'; $env:Jwt__Audience = 'NexoraSchoolFlutter'; $env:Jwt__ExpireHours = '1'
    $env:PlatformJwt__Issuer = 'NexoraSchoolPlatform'; $env:PlatformJwt__Audience = 'NexoraSchoolPlatformClient'
    # Satisfy the unchanged startup validator without real SMTP infrastructure.
    # No sender is invoked in this preverified fixture workflow; attempts fail locally.
    $env:Email__Host = '127.0.0.1'; $env:Email__Port = '9'
    $env:Email__From = 'school-sender@example.invalid'; $env:Email__Username = 'school-sender@example.invalid'
    $env:Email__Password = New-PrivateKey
    ${env:Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command} = 'Warning'
    & dotnet $fixtureDll check-start $databaseFile $apiDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Startup safety/configuration check failed; backend not started. Resolve inherited conflicting settings privately.' }
    Write-Output 'Starting existing API in Development at http://127.0.0.1:5018; Flutter origin http://localhost:7357 only. Ctrl+C stops without deleting data.'
    # No launch profile, --test-email, PostgreSQL context or production startup modifications.
    Push-Location $apiDirectory
    try { & dotnet $apiDll } finally { Pop-Location }
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
