[CmdletBinding()]
param(
    [switch]$PreflightOnly,
    [switch]$CoreOnly,
    [string]$Configuration = 'Release',
    [string]$AttendedResult,
    [string]$LostAckResult
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

$status = @(git status --porcelain)
if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) { throw 'VPS03.WorktreeMustBeClean' }
$head = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'VPS03.HeadUnavailable' }

$required = @(
    'MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING', 'MYDMSVN_VPS03_API_ORIGIN',
    'MYDMSVN_WEB_TEST_TLS_THUMBPRINT'
)
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "VPS03.MissingEnvironment:$name"
    }
}
if ($env:MYDMSVN_VPS03_API_ORIGIN -notmatch '^https://[^/]+$') { throw 'VPS03.NonCanonicalOrigin' }

$resultRoot = Join-Path $root "TestResults\vps03-issue8\$head"
$desktop = Join-Path $resultRoot 'desktop'
$hostPackage = Join-Path $resultRoot 'host'
$hostStaging = Join-Path $resultRoot 'host-staging'
$results = Join-Path $resultRoot 'results'
New-Item -ItemType Directory -Force -Path $desktop,$hostPackage,$hostStaging,$results | Out-Null

if ($PreflightOnly) {
    Write-Host "PASS preflight head=$head results=$resultRoot"
    exit 0
}

foreach ($evidence in @(@{ Name = 'attended'; Path = $AttendedResult }, @{ Name = 'lost-ack'; Path = $LostAckResult })) {
    if ($CoreOnly) { break }
    if ([string]::IsNullOrWhiteSpace($evidence.Path) -or -not (Test-Path -LiteralPath $evidence.Path)) {
        throw "VPS03.RequiredGateMissing:$($evidence.Name)"
    }
    $record = Get-Content -LiteralPath $evidence.Path -Raw | ConvertFrom-Json
    if ($record.headSha -ne $head -or $record.passed -ne $true) {
        throw "VPS03.RequiredGateNotPassed:$($evidence.Name)"
    }
}

dotnet restore src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj --locked-mode -v minimal
if ($LASTEXITCODE -ne 0) { throw 'VPS03.DesktopRestoreFailed' }
dotnet publish src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj -c $Configuration -f net48 --no-restore -p:PlatformTarget=x86 -o $desktop
if ($LASTEXITCODE -ne 0) { throw 'VPS03.DesktopPublishFailed' }
dotnet restore src/MyDmsVn.Server.HttpHost/MyDmsVn.Server.HttpHost.csproj --locked-mode -v minimal
if ($LASTEXITCODE -ne 0) { throw 'VPS03.HostRestoreFailed' }
dotnet publish src/MyDmsVn.Server.HttpHost/MyDmsVn.Server.HttpHost.csproj -c $Configuration -f net48 --no-restore -o $hostStaging
if ($LASTEXITCODE -ne 0) { throw 'VPS03.HostPublishFailed' }
$hostBin = Join-Path $hostPackage 'bin'
New-Item -ItemType Directory -Force -Path $hostBin | Out-Null
Copy-Item -Path (Join-Path $hostStaging '*') -Destination $hostBin -Recurse -Force
Copy-Item -LiteralPath 'src/MyDmsVn.Server.HttpHost/Web.config' -Destination (Join-Path $hostPackage 'Web.config') -Force
Copy-Item -LiteralPath 'src/MyDmsVn.Server.HttpHost/wwwroot' -Destination (Join-Path $hostPackage 'wwwroot') -Recurse -Force

$provenance = @{ headSha = $head } | ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $desktop 'vps03-package-provenance.json'), $provenance)
[IO.File]::WriteAllText((Join-Path $hostPackage 'vps03-package-provenance.json'), $provenance)

$env:MYDMSVN_VPS03_ACCEPTANCE = 'approved'
$env:MYDMSVN_VPS03_DESKTOP_PACKAGE = $desktop
$env:MYDMSVN_VPS03_HOST_PACKAGE = $hostPackage
$env:MYDMSVN_VPS03_HEAD_SHA = $head
$env:MYDMSVN_VPS03_RESULTS_DIRECTORY = $results
dotnet restore tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj --locked-mode -v minimal
if ($LASTEXITCODE -ne 0) { throw 'VPS03.AcceptanceRestoreFailed' }
dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c $Configuration --no-restore --logger "trx;LogFileName=vps03-issue8.trx" --results-directory $results
if ($LASTEXITCODE -ne 0) { throw 'VPS03.AcceptanceFailed' }

$trx = Join-Path $results 'vps03-issue8.trx'
if (-not (Test-Path -LiteralPath $trx)) { throw 'VPS03.EvidenceMissing' }
[xml]$document = Get-Content -LiteralPath $trx -Raw
$counters = $document.TestRun.ResultSummary.Counters
if ([int]$counters.total -le 0 -or [int]$counters.executed -ne [int]$counters.total -or
    [int]$counters.passed -ne [int]$counters.total -or [int]$counters.failed -ne 0 -or
    [int]$counters.notExecuted -ne 0) { throw 'VPS03.EvidenceNotComplete' }
$label = if ($CoreOnly) { 'PARTIAL CORE' } else { 'PASS' }
Write-Host "$label head=$head total=$($counters.total) passed=$($counters.passed) failed=0 skipped=0 results=$resultRoot"
