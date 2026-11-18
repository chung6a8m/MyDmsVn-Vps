# VPS-09 protected Windows runner: bounded remote HTTPS/SQL and x86 publish.
# NOT attended GUI, physical Linux provenance, staging acceptance or release.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-f0-9]{40}$')][string]$ReviewedSha,
    [Parameter(Mandatory = $true)][string]$Origin,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
if ($env:OS -ne 'Windows_NT') { throw 'VPS09.WindowsOnly' }
if ($env:VPS09_ISOLATED_SQL_ADMISSION -cne 'reviewed-isolated-sql13-only') {
    throw 'VPS09.IsolatedSqlApprovalMissing'
}
if ($env:VPS04A_LIVE_ALLOW_MUTATION -cne 'approved-nonproduction-only' -or $env:MYDMSVN_WEB_OPERATION_MODE -cne 'web5') {
    throw 'VPS09.Web5NonproductionAdmissionMissing'
}
$sha = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -cne $ReviewedSha) { throw 'VPS09.ReviewedGitShaMismatch' }
$dirty = @(git status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) { throw 'VPS09.TrackedSourceMustBeClean' }
$uri = $null
if (-not [Uri]::TryCreate($Origin, [UriKind]::Absolute, [ref]$uri) -or
    $uri.Scheme -cne 'https' -or $uri.AbsolutePath -ne '/' -or
    $uri.Query -ne '' -or $uri.Fragment -ne '' -or
    -not [string]::IsNullOrEmpty($uri.UserInfo) -or
    $uri.Host -in @('localhost','127.0.0.1','::1')) {
    throw 'VPS09.RequireCanonicalPublicHttpsOrigin'
}
$canonical = $uri.GetLeftPart([UriPartial]::Authority)
if ($Origin.TrimEnd('/') -cne $canonical -or $env:MYDMSVN_WEB_ORIGIN -cne $canonical -or
    $env:MYDMSVN_WEB_JWT_ISSUER -cne $canonical) {
    throw 'VPS09.OriginOrIssuerMismatch'
}
foreach ($key in @(
    'MYDMSVN_WEB_JWT_AUDIENCE', 'VPS04A_SQL_ORACLE_CONNECTION_STRING',
    'VPS04A_LIVE_USERNAME', 'VPS04A_LIVE_PASSWORD',
    'VPS04A_LIVE_DENIED_USERNAME', 'VPS04A_LIVE_DENIED_PASSWORD',
    'VPS04A_LIVE_CUSTOMER_ID', 'VPS04A_LIVE_WAREHOUSE_ID',
    'VPS04A_LIVE_EMPLOYEE_ID', 'VPS04A_LIVE_PRODUCT_ID'
)) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key))) {
        throw "VPS09.RequiredProtectedSettingMissing:$key"
    }
}
$sql = $env:VPS04A_SQL_ORACLE_CONNECTION_STRING
# Parse quoted values and alternate key spellings; do not accept contradictory
# encrypt/trust overrides that a positive regex match can miss.
$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
try { $builder.ConnectionString = $sql }
catch { throw 'VPS09.SqlOracleConnectionStringMalformed' }
$encKeys = @($builder.Keys | Where-Object { (([string]$_ -replace '[\s_]', '').ToLowerInvariant()) -eq 'encrypt' })
$trustKeys = @($builder.Keys | Where-Object { (([string]$_ -replace '[\s_]', '').ToLowerInvariant()) -eq 'trustservercertificate' })
if ($encKeys.Count -ne 1 -or $trustKeys.Count -ne 1 -or
    [regex]::Matches($sql, '(?i)(?:^|;)\s*Encrypt\s*=').Count -ne 1 -or
    [regex]::Matches($sql, '(?i)(?:^|;)\s*Trust\s*Server\s*Certificate\s*=').Count -ne 1 -or
    ([string]$builder[$encKeys[0]]).ToLowerInvariant() -notin @('true', 'mandatory', 'strict') -or
    ([string]$builder[$trustKeys[0]]).ToLowerInvariant() -ne 'false') {
    throw 'VPS09.SqlOracleTlsNotVerified'
}
$report = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $report -Force | Out-Null
$desktop = Join-Path $report 'desktop-net48-x86'
New-Item -ItemType Directory -Path $desktop -Force | Out-Null

# TLS is never disabled. A green health response alone does not bind remote SHA.
$response = Invoke-WebRequest -Uri "$canonical/api/v1/health" -Method Get -MaximumRedirection 0 -TimeoutSec 15
if ([int]$response.StatusCode -ne 200) { throw 'VPS09.RemoteLinuxHealthNotReady' }
Write-Host 'VPS09_REMOTE_TLS_HEALTH_PASS (remote binary SHA not attested here)'
if ((dotnet --version).Trim() -ne '8.0.400') { throw 'VPS09.DesktopSdkMismatch' }
dotnet restore src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'VPS09.DesktopRestoreFailed' }
dotnet publish src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj -c Release -f net48 --no-restore -p:PlatformTarget=x86 -o $desktop
if ($LASTEXITCODE -ne 0) { throw 'VPS09.DesktopPublishFailed' }
$exe = Join-Path $desktop 'MyDmsVn.Desktop.App.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'VPS09.DesktopExeMissing' }
$stream = [IO.File]::OpenRead($exe)
try {
    $reader = [IO.BinaryReader]::new($stream)
    if ($stream.Length -lt 0x88 -or $reader.ReadUInt16() -ne 0x5a4d) { throw 'VPS09.DesktopNotPe' }
    $stream.Position = 0x3c
    $peOffset = $reader.ReadInt32()
    if ($peOffset -lt 0x40 -or $peOffset -gt $stream.Length - 6) { throw 'VPS09.DesktopPeOffsetInvalid' }
    $stream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x014c) {
        throw 'VPS09.DesktopMustBeX86'
    }
}
finally { $stream.Dispose() }
$desktopHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()

# Existing four real remote tests: JWT/session, same-key idempotency,
# invalid/stale epoch and permission-denied actor with SQL Oracle.
$hostProject = 'tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj'
Push-Location 'src/MyDmsVn.Server.AspNetCoreHost'
try {
    if ((dotnet --version).Trim() -ne '10.0.404') { throw 'VPS09.ServerTestSdkMismatch' }
    dotnet restore "../../$hostProject" -p:Vps04Server=true -p:Vps06bMediator=true -p:Vps04ALive=true --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'VPS09.LiveTestsLockedRestoreFailed' }
    dotnet test "../../$hostProject" -p:Vps04Server=true -p:Vps06bMediator=true -p:Vps04ALive=true -c Release -f net10.0 --no-restore --filter 'FullyQualifiedName~Vps04aLiveSqlAcceptanceTests' --logger 'trx;LogFileName=vps09-protected-https-sql.trx' --results-directory $report
    if ($LASTEXITCODE -ne 0) { throw 'VPS09.RealSqlTestsFailed' }
}
finally { Pop-Location }
$trx = Join-Path $report 'vps09-protected-https-sql.trx'
if (-not (Test-Path -LiteralPath $trx -PathType Leaf)) { throw 'VPS09.ProtectedTrxMissing' }
[xml]$result = Get-Content -LiteralPath $trx -Raw
$cnt = $result.TestRun.ResultSummary.Counters
if ([int]$cnt.total -ne 4 -or [int]$cnt.executed -ne 4 -or [int]$cnt.passed -ne 4 -or
    [int]$cnt.failed -ne 0 -or [int]$cnt.notExecuted -ne 0) {
    throw 'VPS09.ProtectedSqlCasesIncomplete'
}
$summary = [ordered]@{
    schemaVersion = 1
    sourceSha = $ReviewedSha
    desktopExecutableSha256 = $desktopHash
    desktopFramework = 'net48-x86'
    remoteHttpsHost = $uri.DnsSafeHost
    remoteSqlCasesDiscovered = 4
    remoteSqlCasesExecuted = 4
    remoteSqlCasesPassed = 4
    remoteSqlCasesFailed = 0
    remoteSqlCasesSkipped = 0
    observedAtUtc = [DateTime]::UtcNow.ToString('o')
    physicalLinuxBinaryIdentity = 'NOT_VERIFIED_BY_THIS_RUNNER'
    attendedGui = 'NOT_RUN'
    protectedStaging = 'NOT_VERIFIED_BY_THIS_RUNNER'
    exhaustive57RouteRealSql = 'NOT_RUN'
    independentSqlOracleReconciliation = 'NOT_RUN'
    overallRelease = 'NOT_APPROVED_BY_THIS_RUNNER'
}
[IO.File]::WriteAllText((Join-Path $report 'vps09-windows-bounded-summary.json'), ($summary | ConvertTo-Json -Depth 4))
Write-Host "VPS09_WINDOWS_BOUNDED_PASS: SHA=$ReviewedSha remote_https_sql=4/4 GUI=NOT_RUN release=NOT_APPROVED"
