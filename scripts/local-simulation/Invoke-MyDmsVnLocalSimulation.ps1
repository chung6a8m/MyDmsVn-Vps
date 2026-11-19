[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Preflight', 'Initialize', 'Start', 'Verify', 'Stop', 'Reset')]
    [string] $Action,

    [string] $StateRoot,

    [ValidateSet('Text', 'Json')]
    [string] $OutputFormat = 'Text',

    [string] $ConfirmInstanceId
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
        [Parameter(Mandatory = $true)] [hashtable] $Value,
        [Parameter(Mandatory = $true)] [int] $ExitCode
    )

    $Value['notice'] = 'LOCAL SIMULATION - NOT VPS-08/09 ACCEPTANCE'
    if ($OutputFormat -eq 'Json') {
        [Console]::Out.WriteLine(($Value | ConvertTo-Json -Depth 8 -Compress))
    }
    else {
        [Console]::Out.WriteLine(('{0}: {1}' -f $Value.action, $Value.status))
        if ($Value.ContainsKey('code')) {
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

if (-not (Test-PathWithinAllowedStateParent)) {
    Refuse 'LocalSimulation.StateRootOutsideAllowedParent'
}

if ($Action -eq 'Preflight') {
    $checks = [Collections.Generic.List[object]]::new()
    function Add-Check {
        param([string] $Name, [bool] $Passed, [string] $Detail)
        $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    }

    Add-Check 'StateRootPolicy' $true 'State root is inside the repository ignored TestResults/local-simulation tree.'

    try {
        $dockerOs = (& docker version --format '{{.Server.Os}}' 2>$null | Select-Object -First 1)
        Add-Check 'DockerLinuxEngine' ($LASTEXITCODE -eq 0 -and $dockerOs -eq 'linux') 'Docker Server must be reachable and report linux.'
    }
    catch { Add-Check 'DockerLinuxEngine' $false 'Docker Server is unavailable.' }

    try {
        $wslResult = (& wsl -d Ubuntu -- bash -lc 'test "$(ps -p 1 -o comm= | tr -d " ")" = systemd && sudo -n true' 2>$null)
        Add-Check 'WslUbuntuSystemdSudo' ($LASTEXITCODE -eq 0) 'Ubuntu must run systemd and allow noninteractive sudo.'
    }
    catch { Add-Check 'WslUbuntuSystemdSudo' $false 'Ubuntu/systemd/passwordless sudo is unavailable.' }

    try {
        $sdks = (& dotnet --list-sdks 2>$null) -join "`n"
        Add-Check 'DotNetSdk10.0.404' ($sdks -match '(?m)^10\.0\.404\s') 'Exact SDK 10.0.404 must be installed on Windows.'
    }
    catch { Add-Check 'DotNetSdk10.0.404' $false 'dotnet SDK inventory is unavailable.' }

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

if ($Action -eq 'Reset') {
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

    $requiredReceiptProperties = @('schemaVersion', 'instanceId', 'sourceSha', 'stateRoot', 'docker', 'wsl')
    $receiptProperties = @($receipt.PSObject.Properties.Name)
    if (@($requiredReceiptProperties | Where-Object { $_ -notin $receiptProperties }).Count -gt 0) {
        Refuse 'LocalSimulation.ReceiptInvalid'
    }
    if ($null -eq $receipt.docker -or $null -eq $receipt.wsl) {
        Refuse 'LocalSimulation.ReceiptInvalid'
    }
    $dockerProperties = @($receipt.docker.PSObject.Properties.Name)
    $wslProperties = @($receipt.wsl.PSObject.Properties.Name)
    if ('container' -notin $dockerProperties -or 'volume' -notin $dockerProperties -or
        'distribution' -notin $wslProperties -or 'service' -notin $wslProperties) {
        Refuse 'LocalSimulation.ReceiptInvalid'
    }

    if ($receipt.schemaVersion -ne 1 -or
        $receipt.sourceSha -notmatch '^[0-9a-f]{40}$' -or
        $receipt.instanceId -notmatch '^[0-9a-fA-F-]{36}$') {
        Refuse 'LocalSimulation.ReceiptInvalid'
    }
    $receiptStateRoot = [IO.Path]::GetFullPath([string]$receipt.stateRoot)
    if (-not $receiptStateRoot.Equals($resolvedStateRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Refuse 'LocalSimulation.ReceiptStateMismatch'
    }
    if ([string]::IsNullOrWhiteSpace($ConfirmInstanceId) -or $ConfirmInstanceId -ne $receipt.instanceId) {
        Refuse 'LocalSimulation.InstanceConfirmationMismatch'
    }
    if ($receipt.docker.container -ne 'mydmsvn-local-sim-sql' -or
        $receipt.docker.volume -ne 'mydmsvn-local-sim-sql-data' -or
        $receipt.wsl.distribution -ne 'Ubuntu' -or
        $receipt.wsl.service -ne 'mydmsvn-local-sim.service') {
        Refuse 'LocalSimulation.ResourceIdentityMismatch'
    }

    $deleteTarget = (Get-Item -LiteralPath $resolvedStateRoot -Force).FullName
    if (-not $deleteTarget.Equals($resolvedStateRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-PathWithinAllowedStateParent)) {
        Refuse 'LocalSimulation.DeleteTargetRefused'
    }
    Remove-Item -LiteralPath $deleteTarget -Recurse -Force
    Write-Outcome -Value @{ action = 'Reset'; status = 'RESET'; instanceId = $ConfirmInstanceId } -ExitCode 0
}

Write-Outcome -Value @{
    action = $Action
    status = 'NOT_IMPLEMENTED'
    code = 'LocalSimulation.ActionNotImplemented'
} -ExitCode 3
