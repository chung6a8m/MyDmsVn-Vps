[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Preflight', 'Initialize', 'Start', 'Verify', 'Stop', 'Reset')]
    [string] $Action,

    [string] $StateRoot,

    [ValidateSet('Text', 'Json')]
    [string] $OutputFormat = 'Text',

    [string] $ConfirmInstanceId,

    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$allowedStateParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'TestResults\local-simulation'))
if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    $StateRoot = $allowedStateParent
}
$resolvedStateRoot = [IO.Path]::GetFullPath($StateRoot)

function Write-Outcome {
    param(
        [Parameter(Mandatory = $true)] [Collections.IDictionary] $Value,
        [Parameter(Mandatory = $true)] [int] $ExitCode
    )

    $Value['notice'] = 'LOCAL SIMULATION - NOT VPS-08/09 ACCEPTANCE'
    if ($OutputFormat -eq 'Json') {
        [Console]::Out.WriteLine(($Value | ConvertTo-Json -Depth 8 -Compress))
    }
    else {
        [Console]::Out.WriteLine(('{0}: {1}' -f $Value.action, $Value.status))
        if ($Value.Contains('code')) {
            [Console]::Out.WriteLine($Value.code)
        }
        [Console]::Out.WriteLine($Value.notice)
    }
    exit $ExitCode
}

function Refuse {
    param([Parameter(Mandatory = $true)] [string] $Code)
    Write-Outcome -Value @{ action = $Action; status = 'REFUSED'; code = $Code } -ExitCode 2
}

function Test-PathWithinAllowedStateParent {
    $prefix = $allowedStateParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $resolvedStateRoot.Equals($allowedStateParent, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedStateRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Test-ReparsePoint {
    param([Parameter(Mandatory = $true)] [string] $LiteralPath)
    if (-not (Test-Path -LiteralPath $LiteralPath)) { return $false }
    $item = Get-Item -LiteralPath $LiteralPath -Force
    return [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
}

function Test-ReparsePointInStatePath {
    $current = $resolvedStateRoot
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ([bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $true }
        }
        if ($current.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        $parent = [IO.Directory]::GetParent($current)
        if ($null -eq $parent -or $parent.FullName.Equals($current, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        $current = $parent.FullName
    }
}

function Read-ValidatedReceipt {
    param([switch] $RequireConfirmation, [switch] $RequireReady)

    if (-not (Test-Path -LiteralPath $resolvedStateRoot -PathType Container)) {
        Refuse 'LocalSimulation.ReceiptMissing'
    }
    if (Test-ReparsePoint $resolvedStateRoot) {
        Refuse 'LocalSimulation.ReparsePointRefused'
    }
    $receiptPath = Join-Path $resolvedStateRoot 'receipt.json'
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
        Refuse 'LocalSimulation.ReceiptMissing'
    }
    if (Test-ReparsePoint $receiptPath) {
        Refuse 'LocalSimulation.ReparsePointRefused'
    }
    try { $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json }
    catch { Refuse 'LocalSimulation.ReceiptInvalid' }

    $required = @('schemaVersion', 'instanceId', 'sourceSha', 'stateRoot', 'phase', 'docker', 'wsl')
    $properties = @($receipt.PSObject.Properties.Name)
    if (@($required | Where-Object { $_ -notin $properties }).Count -gt 0 -or
        $null -eq $receipt.docker -or $null -eq $receipt.wsl) {
        Refuse 'LocalSimulation.ReceiptInvalid'
    }
    $dockerProperties = @($receipt.docker.PSObject.Properties.Name)
    $wslProperties = @($receipt.wsl.PSObject.Properties.Name)
    if ('container' -notin $dockerProperties -or 'volume' -notin $dockerProperties -or 'engineId' -notin $dockerProperties -or 'created' -notin $dockerProperties -or
        'distribution' -notin $wslProperties -or 'service' -notin $wslProperties -or 'installed' -notin $wslProperties -or
        $receipt.schemaVersion -ne 1 -or $receipt.sourceSha -notmatch '^[0-9a-f]{40}$' -or
        $receipt.instanceId -notmatch '^[0-9a-fA-F-]{36}$' -or $receipt.docker.engineId -notmatch '^[0-9a-f-]{36}$') {
        Refuse 'LocalSimulation.ReceiptInvalid'
    }
    $receiptStateRoot = [IO.Path]::GetFullPath([string]$receipt.stateRoot)
    if (-not $receiptStateRoot.Equals($resolvedStateRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Refuse 'LocalSimulation.ReceiptStateMismatch'
    }
    if ($receipt.docker.container -ne 'mydmsvn-local-sim-sql' -or
        $receipt.docker.volume -ne 'mydmsvn-local-sim-sql-data' -or
        $receipt.wsl.distribution -ne 'Ubuntu' -or
        $receipt.wsl.service -ne 'mydmsvn-local-sim.service') {
        Refuse 'LocalSimulation.ResourceIdentityMismatch'
    }
    if ($RequireConfirmation -and
        ([string]::IsNullOrWhiteSpace($ConfirmInstanceId) -or $ConfirmInstanceId -ne $receipt.instanceId)) {
        Refuse 'LocalSimulation.InstanceConfirmationMismatch'
    }
    if ($RequireReady -and $receipt.phase -ne 'ready') {
        Refuse 'LocalSimulation.ReceiptPhaseInvalid'
    }
    return $receipt
}

if (-not (Test-PathWithinAllowedStateParent)) {
    Refuse 'LocalSimulation.StateRootOutsideAllowedParent'
}
if (Test-ReparsePointInStatePath) {
    Refuse 'LocalSimulation.ReparsePointRefused'
}
if ($DryRun -and $Action -ne 'Initialize') {
    Refuse 'LocalSimulation.DryRunUnsupported'
}

if ($Action -eq 'Initialize' -and $DryRun) {
    Write-Outcome -Value @{
        action = 'Initialize'
        status = 'PLANNED'
        resources = [ordered]@{
            container = 'mydmsvn-local-sim-sql'
            volume = 'mydmsvn-local-sim-sql-data'
            service = 'mydmsvn-local-sim.service'
            origin = 'https://localhost:50443'
            kestrel = 'http://127.0.0.1:5080'
            sql = 'localhost,11433'
        }
    } -ExitCode 0
}

function Invoke-RuntimeAction {
    param([scriptblock] $Operation)
    try { return & $Operation }
    catch {
        $code = [string]$_.Exception.Message
        if ($code -notmatch '^LocalSimulation\.[A-Za-z0-9.]+$') { $code = 'LocalSimulation.UnexpectedFailure' }
        Refuse $code
    }
}

if ($Action -eq 'Preflight') {
    $checks = [Collections.Generic.List[object]]::new()
    function Add-Check {
        param([string] $Name, [bool] $Passed, [string] $Detail)
        $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    }

    Add-Check 'StateRootPolicy' $true 'State root is inside the repository ignored TestResults/local-simulation tree.'

    try {
        $dockerOutput = @(& docker version --format '{{.Server.Os}}' 2>$null)
        $dockerCode = $LASTEXITCODE
        $dockerOs = $(if ($dockerOutput.Count -gt 0) { [string]$dockerOutput[0] } else { '' })
        Add-Check 'DockerLinuxEngine' ($dockerCode -eq 0 -and $dockerOs -eq 'linux') 'Docker Server must be reachable and report linux.'
        $context = @(& docker context inspect --format '{{.Name}}|{{.Endpoints.docker.Host}}' 2>$null)
        $dockerInfo = @(& docker info --format '{{.OperatingSystem}}' 2>$null)
        $localDesktop = $LASTEXITCODE -eq 0 -and [string]::IsNullOrWhiteSpace($env:DOCKER_HOST) -and
            $context.Count -eq 1 -and $context[0] -eq 'desktop-linux|npipe:////./pipe/dockerDesktopLinuxEngine' -and
            $dockerInfo.Count -eq 1 -and $dockerInfo[0] -eq 'Docker Desktop'
        Add-Check 'DockerDesktopLocalContext' $localDesktop 'The active context must be the local Docker Desktop Linux named pipe with DOCKER_HOST unset.'
        $containerNames = @(& docker container ls -a --format '{{.Names}}' 2>$null)
        $containerAvailable = $LASTEXITCODE -eq 0 -and 'mydmsvn-local-sim-sql' -notin $containerNames
        $volumeNames = @(& docker volume ls --format '{{.Name}}' 2>$null)
        $volumeAvailable = $LASTEXITCODE -eq 0 -and 'mydmsvn-local-sim-sql-data' -notin $volumeNames
        Add-Check 'DockerResourceNamesAvailable' ($containerAvailable -and $volumeAvailable) 'Local simulation container and volume names must be unused.'
    }
    catch {
        Add-Check 'DockerLinuxEngine' $false 'Docker Server is unavailable.'
        Add-Check 'DockerDesktopLocalContext' $false 'The local Docker Desktop engine identity cannot be verified.'
        Add-Check 'DockerResourceNamesAvailable' $false 'Docker resource identity cannot be verified.'
    }

    try {
        & wsl -d Ubuntu -- systemctl is-system-running --quiet 2>$null
        $systemdReady = $LASTEXITCODE -eq 0
        & wsl -d Ubuntu -- sudo -n true 2>$null
        $sudoReady = $LASTEXITCODE -eq 0
        Add-Check 'WslUbuntuSystemdSudo' ($systemdReady -and $sudoReady) 'Ubuntu must run systemd and allow noninteractive sudo.'
    }
    catch { Add-Check 'WslUbuntuSystemdSudo' $false 'Ubuntu/systemd/passwordless sudo is unavailable.' }

    try {
        $sdks = (& dotnet --list-sdks 2>$null) -join "`n"
        Add-Check 'DotNetSdk10.0.404' ($sdks -match '(?m)^10\.0\.404\s') 'Exact SDK 10.0.404 must be installed on Windows.'
        Push-Location $repoRoot
        try { $repoSdk = [string](& dotnet --version 2>$null) } finally { Pop-Location }
        Add-Check 'RepoSelectedSdk8.0.428' ($repoSdk.Trim() -eq '8.0.428') 'The repository-selected SDK 8.0.428 is required for Desktop publish.'
    }
    catch {
        Add-Check 'DotNetSdk10.0.404' $false 'dotnet SDK inventory is unavailable.'
        Add-Check 'RepoSelectedSdk8.0.428' $false 'The repository-selected dotnet SDK is unavailable.'
    }

    $net48 = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    Add-Check 'Net48TargetingPack' (Test-Path -LiteralPath $net48 -PathType Container) '.NET Framework 4.8 targeting pack must be installed.'

    foreach ($port in @(50443, 5080, 11433)) {
        $available = $false
        try {
            $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
            $listener.Start()
            $available = $true
        }
        catch { $available = $false }
        finally { if ($null -ne $listener) { $listener.Stop() }; $listener = $null }
        Add-Check ("TcpPort{0}" -f $port) $available ("Loopback TCP port {0} must be unused." -f $port)
    }

    $drive = Get-PSDrive -Name ([IO.Path]::GetPathRoot($repoRoot).TrimEnd(':\'))
    Add-Check 'DiskFree' ($drive.Free -ge 15GB) 'At least 15 GB free disk is required.'

    $blocked = @($checks | Where-Object { -not $_.passed }).Count -gt 0
    Write-Outcome -Value @{
        action = 'Preflight'
        status = $(if ($blocked) { 'BLOCKED' } else { 'READY' })
        checks = @($checks)
    } -ExitCode $(if ($blocked) { 2 } else { 0 })
}

if ($Action -eq 'Initialize') {
    $engine = (Get-Process -Id $PID).Path
    $preflightText = & $engine -NoLogo -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Action Preflight -StateRoot $resolvedStateRoot -OutputFormat Json
    $preflightCode = $LASTEXITCODE
    try { $preflight = $preflightText | ConvertFrom-Json }
    catch { Refuse 'LocalSimulation.PreflightInvalid' }
    if ($preflightCode -ne 0 -or $preflight.status -ne 'READY') { Refuse 'LocalSimulation.PreflightBlocked' }
    Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
    $result = Invoke-RuntimeAction { Initialize-MyDmsVnLocalSimulation -RepoRoot $repoRoot -StateRoot $resolvedStateRoot }
    Write-Outcome -Value $result -ExitCode 0
}

if ($Action -eq 'Reset') {
    $receipt = Read-ValidatedReceipt -RequireConfirmation

    Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
    Invoke-RuntimeAction { Reset-MyDmsVnLocalSimulationExternal -RepoRoot $repoRoot -StateRoot $resolvedStateRoot -Receipt $receipt } | Out-Null

    $deleteTarget = (Get-Item -LiteralPath $resolvedStateRoot -Force).FullName
    if (-not $deleteTarget.Equals($resolvedStateRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-PathWithinAllowedStateParent)) {
        Refuse 'LocalSimulation.DeleteTargetRefused'
    }
    Remove-Item -LiteralPath $deleteTarget -Recurse -Force
    Write-Outcome -Value @{ action = 'Reset'; status = 'RESET'; instanceId = $ConfirmInstanceId } -ExitCode 0
}

if ($Action -in @('Start', 'Verify', 'Stop')) {
    $receipt = Read-ValidatedReceipt -RequireReady
    Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
    if ($Action -eq 'Start') {
        $result = Invoke-RuntimeAction { Start-MyDmsVnLocalSimulation -RepoRoot $repoRoot -Receipt $receipt }
    }
    elseif ($Action -eq 'Verify') {
        $result = Invoke-RuntimeAction { Verify-MyDmsVnLocalSimulation -RepoRoot $repoRoot -StateRoot $resolvedStateRoot -Receipt $receipt }
    }
    else {
        $result = Invoke-RuntimeAction { Stop-MyDmsVnLocalSimulation -RepoRoot $repoRoot -Receipt $receipt }
    }
    Write-Outcome -Value $result -ExitCode 0
}

Write-Outcome -Value @{
    action = $Action
    status = 'NOT_IMPLEMENTED'
    code = 'LocalSimulation.ActionNotImplemented'
} -ExitCode 3
