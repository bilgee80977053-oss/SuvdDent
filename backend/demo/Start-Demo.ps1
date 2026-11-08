param(
    [Parameter(Mandatory=$true)][string]$DemoDirectory,
    [Parameter(Mandatory=$true)][string]$PrivateSettingsFile,
    [string]$Urls='https://localhost:7235',
    [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This private-settings wrapper requires Windows DPAPI.' }
if ($DemoDirectory -notmatch '^[A-Za-z]:\\' -or $PrivateSettingsFile -notmatch '^[A-Za-z]:\\') { throw 'Absolute paths are required.' }
$DemoDirectory=[IO.Path]::GetFullPath($DemoDirectory).TrimEnd('\')
$PrivateSettingsFile=[IO.Path]::GetFullPath($PrivateSettingsFile)
$root=Split-Path $DemoDirectory -Parent
if ((Split-Path (Split-Path $PrivateSettingsFile -Parent) -Parent) -ne $root -or [IO.Path]::GetFileName($PrivateSettingsFile) -ne 'private-settings.dpapi.json') { throw 'Private settings must belong to the selected DEMO root.' }
foreach($path in @($DemoDirectory,$PrivateSettingsFile)) {
    for($part=$path; $part; $part=[IO.Path]::GetDirectoryName($part)) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked paths are not permitted.' }
    }
}
$encrypted=Get-Content -LiteralPath $PrivateSettingsFile -Raw | ConvertFrom-Json
function Unprotect([string]$Value) { ([Net.NetworkCredential]::new('',(ConvertTo-SecureString $Value))).Password }
$settings=@{
    'ASPNETCORE_ENVIRONMENT'='Demo'; 'DOTNET_ENVIRONMENT'='Demo';
    'Demo__Root'=$root; 'Demo__DatabasePath'=(Join-Path $DemoDirectory 'nexora-demo.db');
    'Jwt__Key'=(Unprotect $encrypted.ClinicJwtKey); 'PlatformJwt__Key'=(Unprotect $encrypted.PlatformJwtKey)
}
$previous=@{}
foreach($name in $settings.Keys) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
try {
    foreach($name in $settings.Keys) { [Environment]::SetEnvironmentVariable($name,$settings[$name],'Process') }
    $project=Join-Path $PSScriptRoot '..\DentalApi\DentalApi.csproj'
    if ($ValidateOnly) {
        & dotnet run --project $project -c Release --no-launch-profile -- --validate-demo
    } else {
        & dotnet run --project $project -c Release --no-launch-profile -- --urls $Urls
    }
    if ($LASTEXITCODE -ne 0) { throw 'DEMO startup failed. No automatic migration or reseeding is performed.' }
} finally {
    foreach($name in $settings.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
    $settings=$null
}
