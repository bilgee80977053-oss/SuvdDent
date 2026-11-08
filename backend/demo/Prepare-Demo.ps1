param(
    [Parameter(Mandatory=$true)][string]$Root,
    [Security.SecureString]$PatientPassword,
    [Security.SecureString]$DirectorPassword
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This private-settings wrapper requires Windows DPAPI.' }
if ($Root -notmatch '^[A-Za-z]:\\' -or ($Root -split '[\\/]') -contains '..') { throw 'An absolute local root is required.' }
$Root = [IO.Path]::GetFullPath($Root).TrimEnd('\')
if ([IO.Path]::GetFileName($Root) -ne 'NexoraDental-Demos') { throw 'Root must be named NexoraDental-Demos.' }
for ($part=$Root; $part; $part=[IO.Path]::GetDirectoryName($part)) {
    if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked paths are not permitted.' }
}
if (!(Test-Path -LiteralPath $Root)) { New-Item -ItemType Directory -Path $Root | Out-Null }
$privateDirectory=Join-Path $Root ('private-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $privateDirectory | Out-Null
$acl=Get-Acl -LiteralPath $privateDirectory
$acl.SetAccessRuleProtection($true,$false)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
$acl.AddAccessRule($rule)
Set-Acl -LiteralPath $privateDirectory -AclObject $acl
function New-PrivateValue {
    $bytes=New-Object byte[] 32
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); [Convert]::ToBase64String($bytes) } finally { $rng.Dispose() }
}
function Plaintext([Security.SecureString]$Value) { ([Net.NetworkCredential]::new('', $Value)).Password }
$patient=if($PatientPassword){Plaintext $PatientPassword}else{New-PrivateValue}
$director=if($DirectorPassword){Plaintext $DirectorPassword}else{New-PrivateValue}
$values=@{ PatientPassword=$patient; DirectorPassword=$director; ClinicJwtKey=(New-PrivateValue); PlatformJwtKey=(New-PrivateValue) }
$encrypted=@{}
foreach($name in $values.Keys) { $encrypted[$name]=ConvertFrom-SecureString (ConvertTo-SecureString $values[$name] -AsPlainText -Force) }
$settingsPath=Join-Path $privateDirectory 'private-settings.dpapi.json'
$stream=[IO.File]::Open($settingsPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
try { $writer.Write(($encrypted | ConvertTo-Json)) } finally { $writer.Dispose() }
$names=@('NEXORA_DEMO_ROOT','NEXORA_DEMO_PATIENT_PASSWORD','NEXORA_DEMO_DIRECTOR_PASSWORD')
$previous=@{}
foreach($name in $names) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
try {
    $env:NEXORA_DEMO_ROOT=$Root
    $env:NEXORA_DEMO_PATIENT_PASSWORD=$patient
    $env:NEXORA_DEMO_DIRECTOR_PASSWORD=$director
    $project=Join-Path $PSScriptRoot '..\DentalApi\DentalApi.csproj'
    & dotnet run --project $project -c Release --no-launch-profile -- --prepare-demo
    if ($LASTEXITCODE -ne 0) { throw 'DEMO preparation failed; artifacts are retained.' }
    Write-Output ('PrivateSettingsFile: '+$settingsPath)
} finally {
    foreach($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
    $patient=$null; $director=$null; $values=$null
}
