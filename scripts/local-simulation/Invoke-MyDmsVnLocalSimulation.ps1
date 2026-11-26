[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Preflight', 'Initialize', 'Start', 'Verify', 'Stop', 'Reset', 'PlanUpgrade', 'Upgrade', 'VerifyUpgrade', 'RollbackUpgrade')]
    [string] $Action,

    [string] $StateRoot,

    [ValidateSet('Text', 'Json')]
    [string] $OutputFormat = 'Text',

    [string] $ConfirmInstanceId,

    [string] $CandidateRoot,
    [string] $CandidateSourceSha,
    [string] $DesktopManifestSha256,
    [string] $HostManifestSha256,
    [string] $ExpectedDeploymentEpoch,
    [string] $ExpectedPendingSha256,

    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$allowedStateParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'TestResults\local-simulation'))
if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    $StateRoot = Join-Path $allowedStateParent 'active'
}
$resolvedStateRoot = [IO.Path]::GetFullPath($StateRoot)
$operationMutex = $null
$operationLocked = $false

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
    if ($operationLocked) { $operationMutex.ReleaseMutex() }
    if ($null -ne $operationMutex) { $operationMutex.Dispose() }
    exit $ExitCode
}

function Refuse {
    param([Parameter(Mandatory = $true)] [string] $Code)
    Write-Outcome -Value @{ action = $Action; status = 'REFUSED'; code = $Code } -ExitCode 2
}

if ($PSVersionTable.PSVersion.Major -lt 7) { Refuse 'LocalSimulation.PowerShell7Required' }

function Test-PathWithinAllowedStateParent {
    $prefix = $allowedStateParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $resolvedStateRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
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
    param([switch] $RequireConfirmation, [switch] $RequireReady, [switch] $AllowStaleMirror)

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
    $bindingPath = Join-Path (Join-Path $env:LOCALAPPDATA 'MyDmsVn\LocalSimulation') 'binding.json'
    if (-not (Test-Path -LiteralPath $bindingPath -PathType Leaf) -or (Test-ReparsePoint $bindingPath)) { Refuse 'LocalSimulation.RegistrationMissing' }
    try {
        $binding = Get-Content -LiteralPath $bindingPath -Raw | ConvertFrom-Json
        $localReceipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    }
    catch { Refuse 'LocalSimulation.ReceiptInvalid' }
    if (-not $AllowStaleMirror -and ($binding.receipt | ConvertTo-Json -Depth 8 -Compress) -cne ($localReceipt | ConvertTo-Json -Depth 8 -Compress)) { Refuse 'LocalSimulation.RegistrationMismatch' }
    $receipt = $binding.receipt

    $required = @('schemaVersion', 'instanceId', 'sourceSha', 'stateRoot', 'phase', 'docker', 'wsl', 'artifacts')
    $properties = @($receipt.PSObject.Properties.Name)
    if (@($required | Where-Object { $_ -notin $properties }).Count -gt 0 -or
        $null -eq $receipt.docker -or $null -eq $receipt.wsl) {
        Refuse 'LocalSimulation.ReceiptInvalid'
    }
    $dockerProperties = @($receipt.docker.PSObject.Properties.Name)
    $wslProperties = @($receipt.wsl.PSObject.Properties.Name)
    if ('container' -notin $dockerProperties -or 'volume' -notin $dockerProperties -or 'engineId' -notin $dockerProperties -or 'intent' -notin $dockerProperties -or 'created' -notin $dockerProperties -or
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
    if ($RequireReady -and ($receipt.docker.intent -ne $true -or $receipt.docker.created -ne $true -or $receipt.wsl.installed -ne $true -or
        $null -eq $receipt.artifacts -or $receipt.artifacts.bashSha256 -notmatch '^[0-9a-f]{64}$' -or
        $receipt.artifacts.sqlHelperSha256 -notmatch '^[0-9a-f]{64}$' -or $receipt.artifacts.hostSha256 -notmatch '^[0-9a-f]{64}$' -or
        $receipt.certificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$')) { Refuse 'LocalSimulation.ReceiptAcquisitionInvalid' }
    return $receipt
}

if (-not (Test-PathWithinAllowedStateParent)) {
    Refuse 'LocalSimulation.StateRootOutsideAllowedParent'
}
if (Test-ReparsePointInStatePath) {
    Refuse 'LocalSimulation.ReparsePointRefused'
}
if ($DryRun -and $Action -notin @('Initialize', 'PlanUpgrade')) {
    Refuse 'LocalSimulation.DryRunUnsupported'
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

$candidate = $null
$candidateInputs = @($CandidateRoot, $CandidateSourceSha, $DesktopManifestSha256, $HostManifestSha256)
$providedCandidateInputs = @($candidateInputs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$candidateOptionNames = @('CandidateRoot', 'CandidateSourceSha', 'DesktopManifestSha256', 'HostManifestSha256')
if (@($candidateOptionNames | Where-Object { $PSBoundParameters.ContainsKey($_) }).Count -gt 0) {
    if ($Action -notin @('Initialize', 'PlanUpgrade', 'Upgrade') -or $providedCandidateInputs.Count -ne $candidateInputs.Count) {
        Refuse 'LocalSimulation.CandidateArgumentsInvalid'
    }
    Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
    $candidate = Invoke-RuntimeAction {
        Get-LocalSimulationCandidate $CandidateRoot $CandidateSourceSha $DesktopManifestSha256 $HostManifestSha256
    }
}

if ($Action -eq 'Initialize' -and $DryRun) {
    $plan = @{
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
    }
    if ($null -ne $candidate) { $plan.candidate = $candidate.identity }
    Write-Outcome -Value $plan -ExitCode 0
}

if ($Action -eq 'Preflight') {
    $checks = [Collections.Generic.List[object]]::new()
    function Add-Check {
        param([string] $Name, [bool] $Passed, [string] $Detail)
        $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    }

    Add-Check 'StateRootPolicy' $true 'State root is inside the repository ignored TestResults/local-simulation tree.'
    $bindingPath = Join-Path (Join-Path $env:LOCALAPPDATA 'MyDmsVn\LocalSimulation') 'binding.json'
    Add-Check 'LocalRegistrationAvailable' (-not (Test-Path -LiteralPath $bindingPath)) 'No prior local simulation binding may exist.'

    $dockerSqlMemory = $false
    try {
        $memoryOutput = @(& docker info --format '{{.MemTotal}}' 2>$null)
        $memoryBytes = 0L
        $dockerSqlMemory = $LASTEXITCODE -eq 0 -and $memoryOutput.Count -eq 1 -and
            [long]::TryParse($memoryOutput[0], [ref]$memoryBytes) -and $memoryBytes -ge 2GB
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

    Add-Check 'DockerSqlMemory' $dockerSqlMemory 'The Docker Linux engine requires at least 2 GiB RAM to start SQL Server.'

    $systemdReady = $false
    try {
        & wsl -d Ubuntu -- systemctl is-system-running --quiet 2>$null
        $systemdReady = $LASTEXITCODE -eq 0
        & wsl -d Ubuntu -- sudo -n true 2>$null
        $sudoReady = $LASTEXITCODE -eq 0
        Add-Check 'WslUbuntuSystemdSudo' ($systemdReady -and $sudoReady) 'Ubuntu must run systemd and allow noninteractive sudo.'
    }
    catch { Add-Check 'WslUbuntuSystemdSudo' $false 'Ubuntu/systemd/passwordless sudo is unavailable.' }

    $wslLoopback = $false
    if ($systemdReady) {
        $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        try {
            $probe.Start()
            $probePort = ([Net.IPEndPoint]$probe.LocalEndpoint).Port
            & wsl -d Ubuntu --exec timeout 5 bash -c 'echo >/dev/tcp/127.0.0.1/$1' _ $probePort 2>$null
            $wslLoopback = $LASTEXITCODE -eq 0 -and $probe.Pending()
        }
        catch { $wslLoopback = $false }
        finally { $probe.Stop() }
    }
    Add-Check 'WslWindowsLoopback' $wslLoopback 'Ubuntu must reach Windows IPv4 loopback, where Docker publishes the isolated SQL port.'

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

$lockedActions = @('Initialize', 'Start', 'Verify', 'Stop', 'Reset', 'PlanUpgrade', 'Upgrade', 'VerifyUpgrade', 'RollbackUpgrade')
if ($Action -in $lockedActions) {
    $bindingPath = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'MyDmsVn\LocalSimulation')).ToUpperInvariant()
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $key = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($bindingPath))).Replace('-', '') }
    finally { $hash.Dispose() }
    try {
        $operationMutex = [Threading.Mutex]::new($false, "Global\MyDmsVn.LocalSimulation.$key")
        try { $operationLocked = $operationMutex.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $operationLocked = $true }
    }
    catch { Refuse 'LocalSimulation.OperationLockUnavailable' }
    if (-not $operationLocked) { Refuse 'LocalSimulation.ConcurrentOperation' }
}

trap {
    if ($operationLocked) { $operationMutex.ReleaseMutex() }
    if ($null -ne $operationMutex) { $operationMutex.Dispose() }
    throw $_
}

if ($Action -in @('PlanUpgrade', 'Upgrade', 'VerifyUpgrade', 'RollbackUpgrade')) {
    if ($Action -in @('PlanUpgrade', 'Upgrade') -and $null -eq $candidate) { Refuse 'LocalSimulation.CandidateArgumentsInvalid' }
    $epoch = [Guid]::Empty
    if ($Action -ne 'PlanUpgrade' -and
        (-not [Guid]::TryParseExact($ExpectedDeploymentEpoch, 'D', [ref]$epoch) -or $epoch -eq [Guid]::Empty -or
         $ExpectedDeploymentEpoch -cne $epoch.ToString('D') -or $ExpectedPendingSha256 -cnotmatch '^[0-9a-f]{64}$')) {
        Refuse 'LocalSimulation.UpgradeArgumentsInvalid'
    }
    $receipt = Read-ValidatedReceipt -RequireConfirmation -AllowStaleMirror
    Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
    $result = Invoke-RuntimeAction {
        Invoke-LocalSimulationUpgrade -Action $Action -RepoRoot $repoRoot -StateRoot $resolvedStateRoot -Receipt $receipt -Candidate $candidate -ExpectedDeploymentEpoch $ExpectedDeploymentEpoch -ExpectedPendingSha256 $ExpectedPendingSha256
    }
    Write-Outcome -Value $result -ExitCode 0
}

if ($Action -eq 'Initialize') {
    $engine = (Get-Process -Id $PID).Path
    $preflightText = & $engine -NoLogo -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Action Preflight -StateRoot $resolvedStateRoot -OutputFormat Json
    $preflightCode = $LASTEXITCODE
    try { $preflight = $preflightText | ConvertFrom-Json }
    catch { Refuse 'LocalSimulation.PreflightInvalid' }
    if ($preflightCode -ne 0 -or $preflight.status -ne 'READY') { Refuse 'LocalSimulation.PreflightBlocked' }
    Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
    $result = Invoke-RuntimeAction { Initialize-MyDmsVnLocalSimulation -RepoRoot $repoRoot -StateRoot $resolvedStateRoot -Candidate $candidate }
    Write-Outcome -Value $result -ExitCode 0
}

if ($Action -eq 'Reset') {
    $bindingPath = Join-Path (Join-Path $env:LOCALAPPDATA 'MyDmsVn\LocalSimulation') 'binding.json'
    if (Test-Path -LiteralPath $bindingPath -PathType Leaf) {
        try { $completed = Get-Content -LiteralPath $bindingPath -Raw | ConvertFrom-Json } catch { Refuse 'LocalSimulation.RegistrationInvalid' }
        if ($completed.cleanupComplete -eq $true) {
            if ($completed.receipt.instanceId -ne $ConfirmInstanceId) { Refuse 'LocalSimulation.RegistrationMismatch' }
            Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
            if (Test-Path -LiteralPath $resolvedStateRoot) {
                if (-not (Test-Path -LiteralPath $resolvedStateRoot -PathType Container) -or (Test-ReparsePoint $resolvedStateRoot)) { Refuse 'LocalSimulation.ReparsePointRefused' }
                Invoke-RuntimeAction { Assert-LocalSimulationRegistration -StateRoot $resolvedStateRoot -Receipt $completed.receipt } | Out-Null
                $retryDeleteTarget = (Get-Item -LiteralPath $resolvedStateRoot -Force).FullName
                if (-not $retryDeleteTarget.Equals($resolvedStateRoot, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-PathWithinAllowedStateParent)) { Refuse 'LocalSimulation.DeleteTargetRefused' }
                Invoke-RuntimeAction { Assert-LocalSimulationRegistration -StateRoot $resolvedStateRoot -Receipt $completed.receipt } | Out-Null
                Remove-Item -LiteralPath $retryDeleteTarget -Recurse -Force
            }
            Invoke-RuntimeAction { Remove-LocalSimulationRegistration -StateRoot $resolvedStateRoot -Receipt $completed.receipt } | Out-Null
            Write-Outcome -Value @{ action = 'Reset'; status = 'RESET'; instanceId = $ConfirmInstanceId } -ExitCode 0
        }
    }
    $receipt = Read-ValidatedReceipt -RequireConfirmation -AllowStaleMirror

    Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
    Invoke-RuntimeAction { Assert-LocalSimulationRegistration -StateRoot $resolvedStateRoot -Receipt $receipt } | Out-Null
    Invoke-RuntimeAction { Assert-LocalSimulationResetFence $receipt } | Out-Null
    Invoke-RuntimeAction { Reset-MyDmsVnLocalSimulationExternal -RepoRoot $repoRoot -StateRoot $resolvedStateRoot -Receipt $receipt } | Out-Null
    Invoke-RuntimeAction { Complete-LocalSimulationRegistration -StateRoot $resolvedStateRoot -Receipt $receipt } | Out-Null

    $deleteTarget = (Get-Item -LiteralPath $resolvedStateRoot -Force).FullName
    if (-not $deleteTarget.Equals($resolvedStateRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-PathWithinAllowedStateParent)) {
        Refuse 'LocalSimulation.DeleteTargetRefused'
    }
    Invoke-RuntimeAction { Assert-LocalSimulationRegistration -StateRoot $resolvedStateRoot -Receipt $receipt } | Out-Null
    Remove-Item -LiteralPath $deleteTarget -Recurse -Force
    Invoke-RuntimeAction { Remove-LocalSimulationRegistration -StateRoot $resolvedStateRoot -Receipt $receipt } | Out-Null
    Write-Outcome -Value @{ action = 'Reset'; status = 'RESET'; instanceId = $ConfirmInstanceId } -ExitCode 0
}

if ($Action -in @('Start', 'Verify', 'Stop')) {
    $receipt = Read-ValidatedReceipt -RequireReady
    Import-Module (Join-Path $PSScriptRoot 'LocalSimulation.Runtime.psm1') -Force -DisableNameChecking
    Invoke-RuntimeAction { Assert-LocalSimulationRegistration -StateRoot $resolvedStateRoot -Receipt $receipt } | Out-Null
    if ($Action -eq 'Start') {
        $result = Invoke-RuntimeAction { Start-MyDmsVnLocalSimulation -RepoRoot $repoRoot -StateRoot $resolvedStateRoot -Receipt $receipt }
    }
    elseif ($Action -eq 'Verify') {
        $result = Invoke-RuntimeAction { Verify-MyDmsVnLocalSimulation -RepoRoot $repoRoot -StateRoot $resolvedStateRoot -Receipt $receipt }
    }
    else {
        $result = Invoke-RuntimeAction { Stop-MyDmsVnLocalSimulation -RepoRoot $repoRoot -StateRoot $resolvedStateRoot -Receipt $receipt }
    }
    Write-Outcome -Value $result -ExitCode 0
}

Write-Outcome -Value @{
    action = $Action
    status = 'NOT_IMPLEMENTED'
    code = 'LocalSimulation.ActionNotImplemented'
} -ExitCode 3
