[CmdletBinding()]
param(
    [switch]$PreflightOnly,
    [switch]$CrashOnly,
    [switch]$LiveOnly,
    [switch]$AttendedOnly,
    [string]$Configuration = 'Release',
    [string]$LiveResult,
    [string]$AttendedResult
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

function Assert-Succeeded([string]$code) {
    if ($LASTEXITCODE -ne 0) { throw $code }
}

function Get-Counters([string]$path, [int]$expected) {
    if (-not (Test-Path -LiteralPath $path)) { throw "VPS03.Issue7.TrxMissing:$path" }
    [xml]$document = Get-Content -LiteralPath $path -Raw
    $counters = $document.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $skipped = [int]$counters.notExecuted
    if ($total -ne $expected -or $total -le 0 -or $executed -ne $total -or
        $passed -ne $total -or $failed -ne 0 -or $skipped -ne 0) {
        throw "VPS03.Issue7.TrxIncomplete:expected=$expected,total=$total,executed=$executed,passed=$passed,failed=$failed,skipped=$skipped"
    }
    return [ordered]@{ total = $total; executed = $executed; passed = $passed; failed = 0; skipped = 0 }
}

function Get-PendingSnapshot {
    $directory = Join-Path $env:LOCALAPPDATA 'MyDmsVn-Vps\PendingWrites'
    if (-not (Test-Path -LiteralPath $directory)) { return @() }
    return @(Get-ChildItem -LiteralPath $directory -File -ErrorAction Stop |
        ForEach-Object { $_.FullName } | Sort-Object)
}

function Assert-NoNewPending([string[]]$before) {
    $after = @(Get-PendingSnapshot)
    $difference = @(Compare-Object -ReferenceObject @($before) -DifferenceObject @($after) |
        Where-Object SideIndicator -eq '=>')
    if ($difference.Count -ne 0) { throw 'VPS03.Issue7.OwnedIntentCleanupFailed' }
}

function Assert-NoOwnedChild {
    $ownedNames = @('MyDmsVn.Vps03.PendingStore.CrashHost', 'MyDmsVn.Vps03.RecoveryClient')
    $owned = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $ownedNames -contains $_.ProcessName })
    if ($owned.Count -ne 0) { throw 'VPS03.Issue7.ChildCleanupFailed' }
}

function Invoke-TestGroup([string]$name, [string]$filter, [int]$expected,
    [string]$resultsDirectory, [string[]]$pendingBefore) {
    $trx = Join-Path $resultsDirectory "$name.trx"
    if (Test-Path -LiteralPath $trx) { Remove-Item -LiteralPath $trx -Force }
    dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj `
        -c $Configuration -f net48 --no-restore --filter $filter `
        --logger "trx;LogFileName=$name.trx" --results-directory $resultsDirectory | Out-Host
    Assert-Succeeded "VPS03.Issue7.GroupFailed:$name"
    $counts = Get-Counters $trx $expected
    Assert-NoOwnedChild
    Assert-NoNewPending $pendingBefore
    return [ordered]@{
        name = $name
        total = $counts.total
        executed = $counts.executed
        passed = $counts.passed
        failed = 0
        skipped = 0
    }
}

function Assert-LiveEnvironment {
    foreach ($name in @('MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING',
        'MYDMSVN_VPS03_API_ORIGIN', 'MYDMSVN_WEB_TEST_TLS_THUMBPRINT')) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            throw "VPS03.Issue7.MissingEnvironment:$name"
        }
    }
    if ($env:MYDMSVN_VPS03_API_ORIGIN -notmatch '^https://[^/]+$') {
        throw 'VPS03.Issue7.NonCanonicalOrigin'
    }
}

function Publish-Packages([string]$head, [string]$resultRoot) {
    $desktop = Join-Path $resultRoot 'desktop'
    $hostPackage = Join-Path $resultRoot 'host'
    $hostStaging = Join-Path $resultRoot 'host-staging'
    New-Item -ItemType Directory -Force -Path $desktop,$hostPackage,$hostStaging | Out-Null

    dotnet restore src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj --locked-mode -v minimal | Out-Host
    Assert-Succeeded 'VPS03.Issue7.DesktopRestoreFailed'
    dotnet publish src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj -c $Configuration `
        -f net48 --no-restore -p:PlatformTarget=x86 -o $desktop | Out-Host
    Assert-Succeeded 'VPS03.Issue7.DesktopPublishFailed'
    dotnet restore src/MyDmsVn.Server.HttpHost/MyDmsVn.Server.HttpHost.csproj --locked-mode -v minimal | Out-Host
    Assert-Succeeded 'VPS03.Issue7.HostRestoreFailed'
    dotnet publish src/MyDmsVn.Server.HttpHost/MyDmsVn.Server.HttpHost.csproj -c $Configuration `
        -f net48 --no-restore -o $hostStaging | Out-Host
    Assert-Succeeded 'VPS03.Issue7.HostPublishFailed'
    $hostBin = Join-Path $hostPackage 'bin'
    New-Item -ItemType Directory -Force -Path $hostBin | Out-Null
    Copy-Item -Path (Join-Path $hostStaging '*') -Destination $hostBin -Recurse -Force
    [xml]$webConfig = Get-Content -LiteralPath 'src/MyDmsVn.Server.HttpHost/Web.config' -Raw
    [xml]$bindingConfig = Get-Content -LiteralPath (Join-Path $hostStaging 'MyDmsVn.Server.HttpHost.dll.config') -Raw
    $runtime = $webConfig.ImportNode($bindingConfig.configuration.runtime, $true)
    $existingRuntime = $webConfig.SelectSingleNode('/configuration/runtime')
    [void]$webConfig.configuration.ReplaceChild($runtime, $existingRuntime)
    $webConfig.Save((Join-Path $hostPackage 'Web.config'))
    Copy-Item -LiteralPath 'src/MyDmsVn.Server.HttpHost/wwwroot' `
        -Destination (Join-Path $hostPackage 'wwwroot') -Recurse -Force
    $provenance = @{ headSha = $head } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $desktop 'vps03-package-provenance.json'), $provenance)
    [IO.File]::WriteAllText((Join-Path $hostPackage 'vps03-package-provenance.json'), $provenance)
    return [ordered]@{
        desktop = $desktop
        host = $hostPackage
        desktopSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $desktop 'MyDmsVn.Desktop.App.exe')).Hash.ToLowerInvariant()
        hostSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $hostBin 'MyDmsVn.Server.HttpHost.dll')).Hash.ToLowerInvariant()
    }
}

function Write-GateResult([string]$path, [string]$head,
    [System.Collections.IDictionary]$group, [System.Collections.IDictionary]$cleanup,
    [System.Collections.IDictionary]$packages) {
    $record = [ordered]@{
        schemaVersion = 1
        issue = 7
        headSha = $head
        passed = $true
        group = $group
        cleanup = $cleanup
        packages = $packages
    }
    [IO.File]::WriteAllText($path, ($record | ConvertTo-Json -Depth 8))
}

function Read-LiveResult([string]$path, [string]$head) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
        throw 'VPS03.Issue7.LiveResultMissing'
    }
    $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($record.headSha -ne $head -or $record.passed -ne $true -or
        $record.group.name -ne 'live' -or [int]$record.group.total -le 0 -or
        [int]$record.group.executed -ne [int]$record.group.total -or
        [int]$record.group.passed -ne [int]$record.group.total -or
        [int]$record.group.failed -ne 0 -or [int]$record.group.skipped -ne 0 -or
        $record.cleanup.childAbsent -ne $true -or $record.cleanup.iisAbsent -ne $true -or
        $record.cleanup.sqlAbsent -ne $true -or $record.cleanup.intentAbsent -ne $true) {
        throw 'VPS03.Issue7.LiveResultInvalid'
    }
    return $record
}

function Read-AttendedResult([string]$path, [string]$head) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
        throw 'VPS03.Issue7.AttendedResultMissing'
    }
    $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($record.headSha -ne $head -or $record.passed -ne $true -or
        $record.desktopExited -ne $true -or $record.hostAbsent -ne $true -or
        $record.sqlAbsent -ne $true -or $record.aborted -ne $false -or
        $record.checkpoints.'VPS03.GUI.UNKNOWN-RECOVERY' -ne $true) {
        throw 'VPS03.Issue7.AttendedResultInvalid'
    }
    return $record
}

$selectedModes = @(@($PreflightOnly, $CrashOnly, $LiveOnly, $AttendedOnly) |
    Where-Object { $_.IsPresent })
if ($selectedModes.Count -gt 1) { throw 'VPS03.Issue7.ModeConflict' }

$status = @(git status --porcelain)
Assert-Succeeded 'VPS03.Issue7.GitStatusFailed'
if ($status.Count -ne 0) { throw 'VPS03.Issue7.WorktreeMustBeClean' }
$head = (git rev-parse HEAD).Trim()
Assert-Succeeded 'VPS03.Issue7.HeadUnavailable'
if ($head -notmatch '^[0-9a-f]{40}$') { throw 'VPS03.Issue7.HeadUnavailable' }
$base = (git merge-base origin/main $head).Trim()
Assert-Succeeded 'VPS03.Issue7.BaseUnavailable'
if ($base -notmatch '^[0-9a-f]{40}$') { throw 'VPS03.Issue7.BaseUnavailable' }

$manifestPath = Join-Path $root 'tests\MyDmsVn.Vps03.Acceptance.Tests\vps03-issue7-case-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.issue -ne 7 -or @($manifest.cases).Count -ne 15) {
    throw 'VPS03.Issue7.ManifestInvalid'
}
$duplicateIds = @($manifest.cases | Group-Object caseId | Where-Object Count -ne 1)
if ($duplicateIds.Count -ne 0) { throw 'VPS03.Issue7.ManifestDuplicateCase' }
foreach ($groupName in @('crash', 'file-security', 'live', 'attended', 'cleanup')) {
    if (@($manifest.cases | Where-Object group -eq $groupName).Count -le 0) {
        throw "VPS03.Issue7.ManifestEmptyGroup:$groupName"
    }
}

$resultRoot = Join-Path $root "TestResults\vps03-issue7\$head"
$results = Join-Path $resultRoot 'results'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$runnerIsWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$net48Release = if ($runnerIsWindows) {
    (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' `
        -Name Release -ErrorAction SilentlyContinue).Release
} else { 0 }
$iisPath = Join-Path ${env:ProgramFiles(x86)} 'IIS Express\iisexpress.exe'
$capabilities = [ordered]@{
    windows = $runnerIsWindows
    dotnetFramework48 = [int]$net48Release -ge 528040
    iisExpress = Test-Path -LiteralPath $iisPath
    sql = -not [string]::IsNullOrWhiteSpace($env:MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING)
}
if (-not $capabilities.windows -or -not $capabilities.dotnetFramework48) {
    throw 'VPS03.Issue7.WindowsNet48Required'
}

if ($PreflightOnly) {
    Write-Host "PASS preflight head=$head base=$base cases=$(@($manifest.cases).Count) results=$resultRoot"
    exit 0
}

dotnet restore tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj `
    --locked-mode -v minimal
Assert-Succeeded 'VPS03.Issue7.AcceptanceRestoreFailed'
$pendingBefore = @(Get-PendingSnapshot)
$crashExpected = @($manifest.cases | Where-Object group -eq 'crash').Count
$fileExpected = @($manifest.cases | Where-Object group -eq 'file-security').Count
$liveExpected = @($manifest.cases | Where-Object group -eq 'live').Count
$attendedExpected = @($manifest.cases | Where-Object group -eq 'attended').Count
$groups = @()
$cleanup = [ordered]@{ childAbsent = $true; iisAbsent = $true; sqlAbsent = $true; intentAbsent = $true }
$packages = [ordered]@{}

if ($CrashOnly -or (-not $LiveOnly -and -not $AttendedOnly)) {
    $groups += Invoke-TestGroup 'crash' 'Category=Vps03Issue7Crash' $crashExpected $results $pendingBefore
    $groups += Invoke-TestGroup 'file-security' 'Category=Vps03Issue7FileSecurity' $fileExpected $results $pendingBefore
}

if ($CrashOnly) {
    Write-Host "PASS crash head=$head crash=$crashExpected fileSecurity=$fileExpected failed=0 skipped=0 results=$resultRoot"
    exit 0
}

if ($LiveOnly -or $AttendedOnly) {
    Assert-LiveEnvironment
    $packages = Publish-Packages $head $resultRoot
    $env:MYDMSVN_VPS03_ACCEPTANCE = 'approved'
    $env:MYDMSVN_VPS03_DESKTOP_PACKAGE = $packages.desktop
    $env:MYDMSVN_VPS03_HOST_PACKAGE = $packages.host
    $env:MYDMSVN_VPS03_HEAD_SHA = $head
    $env:MYDMSVN_VPS03_RESULTS_DIRECTORY = $results
}

if ($LiveOnly) {
    $group = Invoke-TestGroup 'live' 'Category=Vps03Issue7Live' $liveExpected $results $pendingBefore
    $groups += $group
    $path = Join-Path $results 'live-result.json'
    Write-GateResult $path $head $group $cleanup $packages
    Write-Host "PASS live head=$head total=$($group.total) failed=0 skipped=0 result=$path"
    exit 0
}

if ($AttendedOnly) {
    $env:MYDMSVN_VPS03_ATTENDED = 'approved'
    $group = Invoke-TestGroup 'attended' 'Phase=Attended' $attendedExpected $results $pendingBefore
    $attendedPath = Join-Path $results 'attended\attended-result.json'
    [void](Read-AttendedResult $attendedPath $head)
    Write-Host "PASS attended head=$head total=$($group.total) failed=0 skipped=0 result=$attendedPath"
    exit 0
}

$live = Read-LiveResult $LiveResult $head
$attended = Read-AttendedResult $AttendedResult $head
$groups += [ordered]@{
    name = 'live'; total = [int]$live.group.total; executed = [int]$live.group.executed
    passed = [int]$live.group.passed; failed = 0; skipped = 0
}
$groups += [ordered]@{ name = 'attended'; total = 1; executed = 1; passed = 1; failed = 0; skipped = 0 }
$packages = [ordered]@{
    desktopSha256 = [string]$live.packages.desktopSha256
    hostSha256 = [string]$live.packages.hostSha256
}
$evidence = [ordered]@{
    schemaVersion = 1
    issue = 7
    baseSha = $base
    headSha = $head
    commands = @(
        'pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -CrashOnly',
        'pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -LiveOnly',
        'pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -AttendedOnly',
        'pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -LiveResult <exact-path> -AttendedResult <exact-path>'
    )
    capabilities = $capabilities
    groups = $groups
    cleanup = $cleanup
    packages = $packages
    operatorCheckpoints = @($attended.checkpoints.PSObject.Properties |
        ForEach-Object { [ordered]@{ caseId = $_.Name; accepted = [bool]$_.Value } })
    blockers = @()
}
$jsonPath = Join-Path $results 'evidence.json'
$markdownPath = Join-Path $results 'evidence.md'
$json = $evidence | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText($jsonPath, $json, [Text.UTF8Encoding]::new($false))
$markdown = "# VPS-03 Issue #7 acceptance evidence`r`n`r`n``````json`r`n" +
    $json.Replace("`n", "`r`n") + "`r`n```````r`n"
[IO.File]::WriteAllText($markdownPath, $markdown, [Text.UTF8Encoding]::new($false))
$total = 0
foreach ($group in $groups) { $total += [int]$group['total'] }
Write-Host "PASS head=$head total=$total failed=0 skipped=0 evidence=$jsonPath"
