# VPS-09 protected Windows coordinator. Prepare automates publish/HTTP/SQL
# evidence; Finalize validates a separately completed attended GUI checklist.
# Neither phase signs a packet or approves production.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare','Finalize')][string]$Phase,
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-f0-9]{40}$')][string]$ReviewedSha,
    [Parameter(Mandatory = $true)][string]$Origin,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$Operator,
    [Parameter(Mandatory = $true)][string]$Reviewer,
    [Parameter(Mandatory = $true)][string]$Owner,
    [string]$RoutePlanPath,
    [string]$OraclePlanPath,
    [string]$GuiChecklistPath,
    [string]$LinuxReceiptPath,
    [switch]$CleanupConfirmed
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:VPS09_ISOLATED_SQL_ADMISSION -cne 'reviewed-isolated-sql13-only') {
    throw 'VPS09.IsolatedSqlApprovalMissing'
}
if ($env:OS -ne 'Windows_NT') { throw 'VPS09.WindowsOnly' }
if ($Operator.Trim().Length -lt 3 -or $Reviewer.Trim().Length -lt 3 -or $Owner.Trim().Length -lt 3) {
    throw 'VPS09.PeopleMissing'
}
if ($Operator -ieq $Reviewer -or $Operator -ieq $Owner) { throw 'VPS09.ReviewMustBeIndependent' }

$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$sha = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -cne $ReviewedSha) { throw 'VPS09.ReviewedGitShaMismatch' }
$dirty = @(git status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) { throw 'VPS09.SourceTreeMustBeClean' }

$uri = $null
if (-not [Uri]::TryCreate($Origin, [UriKind]::Absolute, [ref]$uri) -or
    $uri.Scheme -cne 'https' -or $uri.AbsolutePath -ne '/' -or
    $uri.Query -ne '' -or $uri.Fragment -ne '' -or
    -not [string]::IsNullOrEmpty($uri.UserInfo) -or
    $uri.IsLoopback -or $uri.Host -ieq 'localhost') {
    throw 'VPS09.RequireCanonicalPublicHttpsOrigin'
}
$canonical = $uri.GetLeftPart([UriPartial]::Authority)
if ($Origin.TrimEnd('/') -cne $canonical) { throw 'VPS09.RequireCanonicalPublicHttpsOrigin' }

$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$cliDll = Join-Path $evidence 'acceptance-cli\MyDmsVn.Vps09.Acceptance.dll'

function Write-NewJson([string]$Path, [object]$Value) {
    if (Test-Path -LiteralPath $Path) { throw 'VPS09.EvidenceFileMustBeNew' }
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 20) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

function Invoke-Acceptance([string]$Command, [string]$Input, [string]$Output) {
    dotnet $cliDll $Command --input $Input --output $Output
    if ($LASTEXITCODE -ne 0) { throw "VPS09.AcceptanceCommandFailed:$Command" }
}

function Get-ProtectedAccessToken([string]$UsernameName, [string]$PasswordName) {
    $username = [Environment]::GetEnvironmentVariable($UsernameName)
    $password = [Environment]::GetEnvironmentVariable($PasswordName)
    if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) {
        throw "VPS09.RequiredProtectedSettingMissing:$UsernameName/$PasswordName"
    }
    $payload = @{ username = $username; password = $password } | ConvertTo-Json -Compress
    $response = Invoke-RestMethod -Uri "$canonical/api/v1/auth/login" -Method Post `
        -Headers @{ Origin = $canonical; 'X-Requested-With' = 'XMLHttpRequest' } `
        -ContentType 'application/json' -Body $payload -MaximumRedirection 0 -TimeoutSec 30
    $token = [string]$response.data.accessToken
    if ([string]::IsNullOrWhiteSpace($token)) { throw "VPS09.LoginTokenMissing:$UsernameName" }
    return $token
}

if ($Phase -eq 'Prepare') {
    if (Test-Path -LiteralPath $evidence) { throw 'VPS09.EvidenceDirectoryMustBeNew' }
    if ([string]::IsNullOrWhiteSpace($RoutePlanPath) -or
        -not (Test-Path -LiteralPath $RoutePlanPath -PathType Leaf)) { throw 'VPS09.RoutePlanMissing' }
    if ([string]::IsNullOrWhiteSpace($OraclePlanPath) -or
        -not (Test-Path -LiteralPath $OraclePlanPath -PathType Leaf)) { throw 'VPS09.OraclePlanMissing' }
    [IO.Directory]::CreateDirectory($evidence) | Out-Null

    $desktop = Join-Path $evidence 'desktop-net48-x86'
    if ((dotnet --version).Trim() -cne '8.0.400') { throw 'VPS09.DesktopSdkMismatch' }
    dotnet restore src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'VPS09.DesktopRestoreFailed' }
    dotnet publish src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj -c Release -f net48 `
        --no-restore -p:PlatformTarget=x86 -o $desktop
    if ($LASTEXITCODE -ne 0) { throw 'VPS09.DesktopPublishFailed' }
    $desktopExe = Join-Path $desktop 'MyDmsVn.Desktop.App.exe'
    if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) { throw 'VPS09.DesktopExeMissing' }
    $desktopHash = (Get-FileHash -LiteralPath $desktopExe -Algorithm SHA256).Hash.ToLowerInvariant()

    Push-Location 'src/MyDmsVn.Server.AspNetCoreHost'
    try {
        if ((dotnet --version).Trim() -cne '10.0.404') { throw 'VPS09.ServerSdkMismatch' }
        dotnet restore '../../tools/MyDmsVn.Vps09.Acceptance/MyDmsVn.Vps09.Acceptance.csproj' --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'VPS09.AcceptanceRestoreFailed' }
        dotnet publish '../../tools/MyDmsVn.Vps09.Acceptance/MyDmsVn.Vps09.Acceptance.csproj' `
            -c Release -f net10.0 --no-restore -o (Join-Path $evidence 'acceptance-cli')
        if ($LASTEXITCODE -ne 0) { throw 'VPS09.AcceptancePublishFailed' }
    }
    finally { Pop-Location }

    $preflightInput = Join-Path $evidence 'preflight-input.json'
    Write-NewJson $preflightInput ([ordered]@{
        reviewedSha = $ReviewedSha
        origin = $canonical
        sqlOracleConnectionStringEnvironment = 'VPS04A_SQL_ORACLE_CONNECTION_STRING'
        admission = 'reviewed-isolated-sql13-only'
        operator = $Operator
        reviewer = $Reviewer
        owner = $Owner
        databaseClassification = 'isolated-nonproduction'
    })
    Invoke-Acceptance 'preflight' $preflightInput (Join-Path $evidence 'preflight-receipt.json')
    $env:VPS09_WRITER_BEARER_TOKEN = Get-ProtectedAccessToken 'VPS09_WRITER_USERNAME' 'VPS09_WRITER_PASSWORD'
    $env:VPS09_DENIED_BEARER_TOKEN = Get-ProtectedAccessToken 'VPS09_DENIED_USERNAME' 'VPS09_DENIED_PASSWORD'
    $env:VPS09_OPERATOR_BEARER_TOKEN = Get-ProtectedAccessToken 'VPS09_OPERATOR_USERNAME' 'VPS09_OPERATOR_PASSWORD'
    try {
        Invoke-Acceptance 'routes' ([IO.Path]::GetFullPath($RoutePlanPath)) (Join-Path $evidence 'route-observations.json')
    }
    finally {
        Remove-Item Env:VPS09_WRITER_BEARER_TOKEN -ErrorAction SilentlyContinue
        Remove-Item Env:VPS09_DENIED_BEARER_TOKEN -ErrorAction SilentlyContinue
        Remove-Item Env:VPS09_OPERATOR_BEARER_TOKEN -ErrorAction SilentlyContinue
    }
    Invoke-Acceptance 'oracles' ([IO.Path]::GetFullPath($OraclePlanPath)) (Join-Path $evidence 'sql-oracle.json')

    python scripts/vps09_gui_checklist.py template --source-sha $ReviewedSha `
        --desktop-sha256 $desktopHash --operator $Operator --reviewer $Reviewer `
        --output (Join-Path $evidence 'gui-checklist.template.json')
    if ($LASTEXITCODE -ne 0) { throw 'VPS09.GuiTemplateFailed' }
    Write-Host "VPS09_PREPARE_PASS: SHA=$ReviewedSha routes=293 oracles=12 GUI=NOT_RECORDED release=NOT_APPROVED"
    exit 0
}

if (-not (Test-Path -LiteralPath $evidence -PathType Container)) { throw 'VPS09.EvidenceDirectoryMissing' }
if (-not (Test-Path -LiteralPath $cliDll -PathType Leaf)) { throw 'VPS09.AcceptanceCliMissing' }
if ([string]::IsNullOrWhiteSpace($GuiChecklistPath) -or
    -not (Test-Path -LiteralPath $GuiChecklistPath -PathType Leaf)) { throw 'VPS09.GuiChecklistMissing' }
if ([string]::IsNullOrWhiteSpace($LinuxReceiptPath) -or
    -not (Test-Path -LiteralPath $LinuxReceiptPath -PathType Leaf)) { throw 'VPS09.LinuxReceiptMissing' }
if ([string]::IsNullOrWhiteSpace($env:SESSIONNAME) -or $env:SESSIONNAME -ieq 'Services') {
    throw 'VPS09.InteractiveWindowsSessionRequired'
}
if (-not $CleanupConfirmed) { throw 'VPS09.CleanupNotConfirmed' }

$linuxReceipt = Get-Content -LiteralPath $LinuxReceiptPath -Raw | ConvertFrom-Json
if ($linuxReceipt.sourceSha -cne $ReviewedSha -or
    $linuxReceipt.overall -cne 'STRUCTURE_COMPLETE_NOT_REVIEWED' -or
    $linuxReceipt.physicalEvidenceReview -cne 'REQUIRED' -or
    @($linuxReceipt.events).Count -ne 8) {
    throw 'VPS09.LinuxReceiptInvalid'
}
$retainedLinuxReceipt = Join-Path $evidence 'linux-receipt.json'
if (Test-Path -LiteralPath $retainedLinuxReceipt) { throw 'VPS09.EvidenceFileMustBeNew' }
[IO.File]::Copy([IO.Path]::GetFullPath($LinuxReceiptPath), $retainedLinuxReceipt, $false)

$guiReceipt = Join-Path $evidence 'gui-checklist-receipt.json'
python scripts/vps09_gui_checklist.py validate --input ([IO.Path]::GetFullPath($GuiChecklistPath)) --output $guiReceipt
if ($LASTEXITCODE -ne 0) { throw 'VPS09.GuiChecklistInvalid' }
$gates = Get-Content -LiteralPath 'docs/testing/vps-09-gates.json' -Raw | ConvertFrom-Json
$summaryInput = Join-Path $evidence 'summary-input.json'
Write-NewJson $summaryInput ([ordered]@{
    sourceSha = $ReviewedSha
    routeEvidencePath = (Join-Path $evidence 'route-observations.json')
    oracleEvidencePath = (Join-Path $evidence 'sql-oracle.json')
    requiredOracleScenarios = @($gates.oracleScenarios)
    cleanupConfirmed = $true
})
Invoke-Acceptance 'summarize' $summaryInput (Join-Path $evidence 'vps09-sanitized-summary.json')
Write-Host "VPS09_FINALIZE_STRUCTURE_PASS: SHA=$ReviewedSha GUI=9/9 cleanup=confirmed physical_review=REQUIRED release=NOT_APPROVED"
