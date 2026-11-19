Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Utf8File {
    param([string] $Path, [string] $Value)
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function Invoke-ExternalSafe {
    param([string] $FilePath, [string[]] $Arguments, [string] $FailureCode)
    $output = & $FilePath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw [InvalidOperationException]::new($FailureCode) }
    return @($output)
}

function Convert-ToWslPath {
    param([string] $Path)
    $portable = [IO.Path]::GetFullPath($Path).Replace('\', '/')
    $result = @(Invoke-ExternalSafe 'wsl' @('-d', 'Ubuntu', '--', 'wslpath', '-a', $portable) 'LocalSimulation.WslPathFailed')
    $value = [string]$result[0]
    if (-not $value.StartsWith('/mnt/', [StringComparison]::Ordinal)) {
        throw [InvalidOperationException]::new('LocalSimulation.WslPathRefused')
    }
    return $value.Trim()
}

function Invoke-WslHelper {
    param([string] $RepoRoot, [string] $Command, [string[]] $Arguments, [switch] $Sudo)
    $helper = Convert-ToWslPath (Join-Path $RepoRoot 'scripts\local-simulation\mydmsvn-local-sim')
    $commandArguments = @('-d', 'Ubuntu', '--')
    if ($Sudo) { $commandArguments += @('sudo', '--') }
    $commandArguments += @('bash', $helper, $Command)
    $commandArguments += $Arguments
    return Invoke-ExternalSafe 'wsl' $commandArguments ("LocalSimulation.Wsl.{0}Failed" -f $Command)
}

function New-LocalPassword {
    $bytes = [byte[]]::new(18)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return 'Aa1!' + (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function New-LocalToken {
    $bytes = [byte[]]::new(32)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Set-ProcessSettings {
    param([hashtable] $Values)
    $old = @{}
    foreach ($name in $Values.Keys) {
        $old[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, [string]$Values[$name], 'Process')
    }
    return $old
}

function Restore-ProcessSettings {
    param([hashtable] $Values)
    foreach ($name in $Values.Keys) {
        [Environment]::SetEnvironmentVariable($name, $Values[$name], 'Process')
    }
}

function Protect-LocalDirectory {
    param([string] $Path)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    Invoke-ExternalSafe 'icacls.exe' @($Path, '/inheritance:r', '/grant:r', ("{0}:(OI)(CI)F" -f $identity)) 'LocalSimulation.StateAclFailed' | Out-Null
    Invoke-ExternalSafe 'icacls.exe' @((Join-Path $Path '*'), '/inheritance:r', '/grant:r', ("{0}:F" -f $identity), '/t', '/c') 'LocalSimulation.StateAclFailed' | Out-Null
}

function Invoke-DotNet10 {
    param([string] $StateRoot, [string[]] $Arguments, [string] $FailureCode)
    $sdk = Join-Path $StateRoot 'sdk10'
    if (-not (Test-Path -LiteralPath $sdk)) { [IO.Directory]::CreateDirectory($sdk) | Out-Null }
    $global = '{"sdk":{"version":"10.0.404","rollForward":"disable","allowPrerelease":false}}'
    Write-Utf8File (Join-Path $sdk 'global.json') $global
    Push-Location $sdk
    $boundedArguments = @($Arguments) + @('-nodeReuse:false', '-p:UseSharedCompilation=false')
    try { return Invoke-ExternalSafe 'dotnet' $boundedArguments $FailureCode }
    finally { Pop-Location }
}

function Publish-LocalSimulationArtifacts {
    param([string] $RepoRoot, [string] $StateRoot)
    $buildSettings = Set-ProcessSettings @{
        MSBUILDDISABLENODEREUSE = '1'
        DOTNET_CLI_USE_MSBUILD_SERVER = '0'
        UseSharedCompilation = 'false'
    }
    try {
    $artifacts = Join-Path $StateRoot 'artifacts'
    [IO.Directory]::CreateDirectory($artifacts) | Out-Null
    $helper = Join-Path $RepoRoot 'tools\MyDmsVn.LocalSimulation\MyDmsVn.LocalSimulation.csproj'
    $migrator = Join-Path $RepoRoot 'src\MyDmsVn.Server.FluentMigrator.Cli\MyDmsVn.Server.FluentMigrator.Cli.csproj'
    $host = Join-Path $RepoRoot 'src\MyDmsVn.Server.AspNetCoreHost\MyDmsVn.Server.AspNetCoreHost.csproj'
    # Restore immediately before each publish. These projects intentionally use
    # different conditional dependency graphs and share referenced-project obj paths.
    Invoke-DotNet10 $StateRoot @('restore', $helper, '-p:Vps04Server=true', '-p:Vps06bMediator=true', '--locked-mode') 'LocalSimulation.LockedRestoreFailed' | Out-Null
    Invoke-DotNet10 $StateRoot @('publish', $helper, '-p:Vps04Server=true', '-p:Vps06bMediator=true', '-c', 'Release', '-f', 'net10.0', '--no-restore', '-o', (Join-Path $artifacts 'sql-helper')) 'LocalSimulation.SqlHelperPublishFailed' | Out-Null
    Invoke-DotNet10 $StateRoot @('restore', $migrator, '-p:Vps04Server=true', '--locked-mode') 'LocalSimulation.LockedRestoreFailed' | Out-Null
    Invoke-DotNet10 $StateRoot @('publish', $migrator, '-p:Vps04Server=true', '-c', 'Release', '-f', 'net10.0', '--no-restore', '-o', (Join-Path $artifacts 'migrator')) 'LocalSimulation.MigratorPublishFailed' | Out-Null
    Invoke-DotNet10 $StateRoot @('restore', $host, '-p:Vps04Server=true', '-p:Vps06bMediator=true', '--locked-mode') 'LocalSimulation.LockedRestoreFailed' | Out-Null
    Invoke-DotNet10 $StateRoot @('publish', $host, '-p:Vps04Server=true', '-p:Vps06bMediator=true', '-p:UseAppHost=false', '-c', 'Release', '-f', 'net10.0', '--no-restore', '-warnaserror', '-o', (Join-Path $artifacts 'host')) 'LocalSimulation.HostPublishFailed' | Out-Null

    Push-Location $RepoRoot
    try {
        Invoke-ExternalSafe 'dotnet' @('restore', 'src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj', '--locked-mode', '-nodeReuse:false', '-p:UseSharedCompilation=false') 'LocalSimulation.DesktopRestoreFailed' | Out-Null
        Invoke-ExternalSafe 'dotnet' @('publish', 'src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj', '-c', 'Release', '-f', 'net48', '--no-restore', '-p:PlatformTarget=x86', '-nodeReuse:false', '-p:UseSharedCompilation=false', '-o', (Join-Path $artifacts 'desktop')) 'LocalSimulation.DesktopPublishFailed' | Out-Null
    }
    finally { Pop-Location }
    }
    finally { Restore-ProcessSettings $buildSettings }
}

function Invoke-SqlHelper {
    param([string] $StateRoot, [pscustomobject] $Receipt, [string] $Command)
    $secrets = Join-Path $StateRoot 'secrets'
    $sa = Get-Content -LiteralPath (Join-Path $secrets 'sa_password') -Raw
    $settings = @{
        MYDMSVN_LOCAL_SIM_ADMISSION = 'approved-synthetic-local-only'
        MYDMSVN_LOCAL_SIM_INSTANCE_ID = [string]$Receipt.instanceId
        MYDMSVN_LOCAL_SIM_SQL_ADMIN_CONNECTION_STRING = "Server=127.0.0.1,11433;Initial Catalog=master;User ID=sa;Password=$sa;Encrypt=True;TrustServerCertificate=False;Pooling=False;Connect Timeout=5"
        MYDMSVN_LOCAL_SIM_RUNTIME_PASSWORD = (Get-Content -LiteralPath (Join-Path $secrets 'runtime_password') -Raw)
        MYDMSVN_LOCAL_SIM_ORACLE_PASSWORD = (Get-Content -LiteralPath (Join-Path $secrets 'oracle_password') -Raw)
        MYDMSVN_LOCAL_SIM_MIGRATION_PASSWORD = (Get-Content -LiteralPath (Join-Path $secrets 'migration_password') -Raw)
        MYDMSVN_LOCAL_SIM_DENIED_PASSWORD = (Get-Content -LiteralPath (Join-Path $secrets 'denied_password') -Raw)
        MYDMSVN_LOCAL_SIM_WRITER_PASSWORD = (Get-Content -LiteralPath (Join-Path $secrets 'writer_password') -Raw)
    }
    $old = Set-ProcessSettings $settings
    try {
        $dll = Join-Path $StateRoot 'artifacts\sql-helper\MyDmsVn.LocalSimulation.dll'
        $result = @(Invoke-ExternalSafe 'dotnet' @($dll, $Command) ("LocalSimulation.Sql.{0}Failed" -f $Command))
        return ($result -join "`n") | ConvertFrom-Json
    }
    finally { Restore-ProcessSettings $old }
}

function Invoke-FluentMigration {
    param([string] $StateRoot)
    $migrationPassword = Get-Content -LiteralPath (Join-Path $StateRoot 'secrets\migration_password') -Raw
    $old = Set-ProcessSettings @{
        VPS07B_OPERATOR_APPROVAL = 'approved-maintenance-and-verified-backup'
        VPS07B_SQL_CONNECTION_STRING = "Server=127.0.0.1,11433;Initial Catalog=MyDmsVnVps_LocalSimulation;User ID=mydmsvn_local_sim_migration;Password=$migrationPassword;Encrypt=True;TrustServerCertificate=False;Pooling=False;Connect Timeout=5"
    }
    try {
        $dll = Join-Path $StateRoot 'artifacts\migrator\MyDmsVn.Server.FluentMigrator.Cli.dll'
        Invoke-ExternalSafe 'dotnet' @($dll, 'migrate', 'schema13') 'LocalSimulation.MigrationFailed' | Out-Null
    }
    finally { Restore-ProcessSettings $old }
}

function Invoke-HttpExercise {
    param([string] $StateRoot, [pscustomobject] $Receipt)
    $origin = 'https://localhost:50443'
    $writerPassword = Get-Content -LiteralPath (Join-Path $StateRoot 'secrets\writer_password') -Raw
    $deniedPassword = Get-Content -LiteralPath (Join-Path $StateRoot 'secrets\denied_password') -Raw
    $loginHeaders = @{ Origin = $origin; 'X-Requested-With' = 'XMLHttpRequest' }
    $writerBody = @{ username = 'local_sim_writer'; password = $writerPassword } | ConvertTo-Json -Compress
    $writer = Invoke-RestMethod -Uri "$origin/api/v1/auth/login" -Method Post -Headers $loginHeaders -ContentType 'application/json' -Body $writerBody
    $writerToken = [string]$writer.data.accessToken
    if ([string]::IsNullOrWhiteSpace($writerToken)) { throw [InvalidOperationException]::new('LocalSimulation.WriterLoginFailed') }
    $writerHeaders = @{ Authorization = "Bearer $writerToken" }
    $context = Invoke-RestMethod -Uri "$origin/api/v1/operations/context" -Method Get -Headers $writerHeaders
    $epoch = [string]$context.data.deploymentEpoch
    if ($epoch -notmatch '^[0-9a-fA-F-]{36}$') { throw [InvalidOperationException]::new('LocalSimulation.OperationContextFailed') }

    $deniedBody = @{ username = 'local_sim_denied'; password = $deniedPassword } | ConvertTo-Json -Compress
    $denied = Invoke-RestMethod -Uri "$origin/api/v1/auth/login" -Method Post -Headers $loginHeaders -ContentType 'application/json' -Body $deniedBody
    $deniedHeaders = @{ Authorization = ("Bearer " + [string]$denied.data.accessToken) }
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "$origin/api/v1/sales-receipts" -Method Get -Headers $deniedHeaders | Out-Null
        throw [InvalidOperationException]::new('LocalSimulation.DeniedActorUnexpectedlyAuthorized')
    }
    catch {
        if ($_.Exception.Message -eq 'LocalSimulation.DeniedActorUnexpectedlyAuthorized') { throw }
        $status = [int]$_.Exception.Response.StatusCode
        if ($status -ne 403) { throw [InvalidOperationException]::new('LocalSimulation.DeniedActorStatusInvalid') }
    }

    $operationId = [Guid]::NewGuid().ToString('D')
    $note = 'LOCAL-VERIFY-' + [Guid]::NewGuid().ToString('N')
    $writerHeaders['X-MyDmsVn-Deployment-Epoch'] = $epoch
    $writerHeaders['Idempotency-Key'] = $operationId
    $body = @{
        saleDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
        customerId = 1; warehouseId = 1; employeeId = 1; note = $note
        lines = @(@{ saleLineId = '0'; productId = 1; qty = '1.0000'; unitPrice = '2.0000'; isGift = $false })
    } | ConvertTo-Json -Depth 5 -Compress
    $sale = Invoke-RestMethod -Uri "$origin/api/v1/sales-receipts" -Method Post -Headers ($writerHeaders + @{ Origin = $origin }) -ContentType 'application/json' -Body $body
    $saleId = [string]$sale.data.saleId
    if ($saleId -notmatch '^[1-9][0-9]*$') { throw [InvalidOperationException]::new('LocalSimulation.WriterMutationFailed') }
    $proofSettings = Set-ProcessSettings @{
        MYDMSVN_LOCAL_SIM_PROOF_SALE_ID = $saleId
        MYDMSVN_LOCAL_SIM_PROOF_OPERATION_ID = $operationId
        MYDMSVN_LOCAL_SIM_PROOF_NOTE = $note
    }
    try { Invoke-SqlHelper $StateRoot $Receipt 'verify-exercise' | Out-Null }
    finally { Restore-ProcessSettings $proofSettings }
}

function Write-Receipt {
    param([string] $Path, [hashtable] $Receipt)
    Write-Utf8File $Path ($Receipt | ConvertTo-Json -Depth 8)
}

function Assert-LocalSimulationIdentity {
    param([string] $RepoRoot, [pscustomobject] $Receipt)
    $engineId = @(Invoke-ExternalSafe 'docker' @('info', '--format', '{{.ID}}') 'LocalSimulation.DockerBackendUnavailable')
    if (([string]$engineId[0]).Trim() -ne [string]$Receipt.docker.engineId) { throw [InvalidOperationException]::new('LocalSimulation.DockerEngineMismatch') }
    $containerLabel = @(Invoke-ExternalSafe 'docker' @('inspect', '-f', '{{ index .Config.Labels "mydmsvn.local-sim.instance" }}', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlContainerMissing')
    $volumeLabel = @(Invoke-ExternalSafe 'docker' @('volume', 'inspect', '-f', '{{ index .Labels "mydmsvn.local-sim.instance" }}', 'mydmsvn-local-sim-sql-data') 'LocalSimulation.SqlVolumeMissing')
    $mount = @(Invoke-ExternalSafe 'docker' @('inspect', '-f', '{{range .Mounts}}{{if eq .Destination "/var/opt/mssql"}}{{.Name}}{{end}}{{end}}', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlMountMissing')
    if (([string]$containerLabel[0]).Trim() -ne [string]$Receipt.instanceId -or
        ([string]$volumeLabel[0]).Trim() -ne [string]$Receipt.instanceId -or
        ([string]$mount[0]).Trim() -ne 'mydmsvn-local-sim-sql-data') {
        throw [InvalidOperationException]::new('LocalSimulation.ResourceIdentityMismatch')
    }
    Invoke-WslHelper $RepoRoot 'identity' @([string]$Receipt.instanceId, [string]$Receipt.sourceSha) -Sudo | Out-Null
}

function Initialize-MyDmsVnLocalSimulation {
    param([string] $RepoRoot, [string] $StateRoot)
    if (Test-Path -LiteralPath $StateRoot) {
        $stateItem = Get-Item -LiteralPath $StateRoot -Force
        if (-not $stateItem.PSIsContainer -or
            [bool]($stateItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            @(Get-ChildItem -LiteralPath $StateRoot -Force).Count -gt 0) {
            throw [InvalidOperationException]::new('LocalSimulation.StateAlreadyExists')
        }
    }
    $dirty = @(Invoke-ExternalSafe 'git' @('-C', $RepoRoot, 'status', '--porcelain') 'LocalSimulation.GitStateUnavailable')
    if ($dirty.Count -gt 0) { throw [InvalidOperationException]::new('LocalSimulation.SourceTreeMustBeClean') }
    $sourceLines = @(Invoke-ExternalSafe 'git' @('-C', $RepoRoot, 'rev-parse', 'HEAD') 'LocalSimulation.GitStateUnavailable')
    $sourceSha = ([string]$sourceLines[0]).Trim()
    if ($sourceSha -notmatch '^[0-9a-f]{40}$') { throw [InvalidOperationException]::new('LocalSimulation.SourceShaInvalid') }

    [IO.Directory]::CreateDirectory($StateRoot) | Out-Null
    foreach ($name in @('secrets', 'artifacts')) { [IO.Directory]::CreateDirectory((Join-Path $StateRoot $name)) | Out-Null }
    $instanceId = [Guid]::NewGuid().ToString('D')
    $receipt = [ordered]@{
        schemaVersion = 1; instanceId = $instanceId; sourceSha = $sourceSha
        stateRoot = [IO.Path]::GetFullPath($StateRoot); createdAtUtc = [DateTime]::UtcNow.ToString('O')
        phase = 'initializing'
        docker = [ordered]@{ container = 'mydmsvn-local-sim-sql'; volume = 'mydmsvn-local-sim-sql-data'; engineId = ((@(& docker info --format '{{.ID}}'))[0]).Trim(); created = $false }
        wsl = [ordered]@{ distribution = 'Ubuntu'; service = 'mydmsvn-local-sim.service'; installed = $false }
        certificateThumbprint = $null
    }
    $receiptPath = Join-Path $StateRoot 'receipt.json'
    Write-Receipt $receiptPath $receipt

    $secretValues = @{
        sa_password = New-LocalPassword; runtime_password = New-LocalPassword
        oracle_password = New-LocalPassword; migration_password = New-LocalPassword
        denied_password = New-LocalPassword; writer_password = New-LocalPassword
        proxy_token = New-LocalToken; jwt_keys = ('local-key:' + (New-LocalToken))
    }
    foreach ($name in $secretValues.Keys) { Write-Utf8File (Join-Path $StateRoot "secrets\$name") $secretValues[$name] }
    Protect-LocalDirectory (Join-Path $StateRoot 'secrets')

    Publish-LocalSimulationArtifacts $RepoRoot $StateRoot
    $stateWsl = Convert-ToWslPath $StateRoot
    Invoke-WslHelper $RepoRoot 'certificates' @($stateWsl, $instanceId) | Out-Null
    Protect-LocalDirectory (Join-Path $StateRoot 'tls')
    $caFile = Join-Path $StateRoot 'tls\ca.crt'
    $caFileCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($caFile)
    $receipt.certificateThumbprint = $caFileCertificate.Thumbprint
    Write-Receipt $receiptPath $receipt
    $ca = Import-Certificate -FilePath $caFile -CertStoreLocation 'Cert:\CurrentUser\Root'
    if ($ca.Thumbprint -ne $receipt.certificateThumbprint) { throw [InvalidOperationException]::new('LocalSimulation.CertificateImportMismatch') }

    Write-Utf8File (Join-Path $StateRoot 'tls\mssql.conf') "[network]`ntlscert = /var/opt/mssql/tls/sql.crt`ntlskey = /var/opt/mssql/tls/sql.key`nforceencryption = 1`ntlsprotocols = 1.2`n"
    & docker container inspect mydmsvn-local-sim-sql 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw [InvalidOperationException]::new('LocalSimulation.SqlContainerNameCollision') }
    & docker volume inspect mydmsvn-local-sim-sql-data 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw [InvalidOperationException]::new('LocalSimulation.SqlVolumeNameCollision') }
    $labels = @('--label', "mydmsvn.local-sim.instance=$instanceId")
    Invoke-ExternalSafe 'docker' (@('volume', 'create') + $labels + @('mydmsvn-local-sim-sql-data')) 'LocalSimulation.SqlVolumeCreateFailed' | Out-Null
    $receipt.docker.created = $true
    Write-Receipt $receiptPath $receipt
    $tls = Join-Path $StateRoot 'tls'
    $image = 'mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04'
    $copyScript = 'mkdir -p /var/opt/mssql/tls /var/opt/mssql/backup; cp /source/sql.crt /source/sql.key /var/opt/mssql/tls/; cp /source/mssql.conf /var/opt/mssql/mssql.conf; chown -R 10001:0 /var/opt/mssql/tls /var/opt/mssql/backup /var/opt/mssql/mssql.conf; chmod 600 /var/opt/mssql/tls/sql.key; chmod 644 /var/opt/mssql/tls/sql.crt /var/opt/mssql/mssql.conf'
    Invoke-ExternalSafe 'docker' @('run', '--rm', '--user', '0', '--entrypoint', '/bin/bash', '--mount', 'type=volume,source=mydmsvn-local-sim-sql-data,target=/var/opt/mssql', '--mount', "type=bind,source=$tls,target=/source,readonly", $image, '-c', $copyScript) 'LocalSimulation.SqlTlsProvisionFailed' | Out-Null
    $dockerEnvironment = Set-ProcessSettings @{ MSSQL_SA_PASSWORD = $secretValues.sa_password }
    try {
        Invoke-ExternalSafe 'docker' @('run', '-d', '--name', 'mydmsvn-local-sim-sql', '--hostname', 'mydmsvn-local-sim-sql', '--label', "mydmsvn.local-sim.instance=$instanceId", '-e', 'ACCEPT_EULA=Y', '-e', 'MSSQL_PID=Developer', '-e', 'MSSQL_SA_PASSWORD', '-p', '127.0.0.1:11433:1433', '-v', 'mydmsvn-local-sim-sql-data:/var/opt/mssql', $image) 'LocalSimulation.SqlContainerCreateFailed' | Out-Null
    }
    finally { Restore-ProcessSettings $dockerEnvironment }

    $created = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { Invoke-SqlHelper $StateRoot ([pscustomobject]$receipt) 'create' | Out-Null; $created = $true; break }
        catch { if ($attempt -eq 29) { throw }; Start-Sleep -Seconds 2 }
    }
    if (-not $created) { throw [InvalidOperationException]::new('LocalSimulation.SqlReadinessFailed') }
    Invoke-SqlHelper $StateRoot ([pscustomobject]$receipt) 'backup-verify' | Out-Null
    Invoke-FluentMigration $StateRoot
    Invoke-SqlHelper $StateRoot ([pscustomobject]$receipt) 'provision' | Out-Null

    $runtimeSql = "Server=127.0.0.1,11433;Initial Catalog=MyDmsVnVps_LocalSimulation;User ID=mydmsvn_local_sim_runtime;Password=$($secretValues.runtime_password);Encrypt=True;TrustServerCertificate=False;Pooling=False"
    Write-Utf8File (Join-Path $StateRoot 'secrets\sql_connection_string') $runtimeSql
    $instanceEnv = "INSTANCE_ID=$instanceId`nSOURCE_SHA=$sourceSha`nPUBLIC_ORIGIN=https://localhost:50443`nSQL_SERVER=127.0.0.1,11433`n"
    Write-Utf8File (Join-Path $StateRoot 'instance.env') $instanceEnv
    $rendered = Join-Path $StateRoot 'rendered'
    $renderedWsl = Convert-ToWslPath $rendered
    $hostWsl = Convert-ToWslPath (Join-Path $StateRoot 'artifacts\host')
    Invoke-WslHelper $RepoRoot 'render' @($stateWsl, $renderedWsl, $hostWsl) | Out-Null
    Invoke-WslHelper $RepoRoot 'prerequisites' @($stateWsl) -Sudo | Out-Null
    $receipt.wsl.installed = $true
    Write-Receipt $receiptPath $receipt
    Invoke-WslHelper $RepoRoot 'install' @($stateWsl, $renderedWsl, $hostWsl, $instanceId) -Sudo | Out-Null
    Invoke-WslHelper $RepoRoot 'start' @($instanceId, $sourceSha) -Sudo | Out-Null
    Invoke-WslHelper $RepoRoot 'verify' @($instanceId, $sourceSha) -Sudo | Out-Null
    $receipt.phase = 'ready'
    Write-Receipt $receiptPath $receipt
    return [ordered]@{ action = 'Initialize'; status = 'READY'; instanceId = $instanceId; desktopPath = (Join-Path $StateRoot 'artifacts\desktop\MyDmsVn.Desktop.App.exe') }
}

function Start-MyDmsVnLocalSimulation {
    param([string] $RepoRoot, [pscustomobject] $Receipt)
    Assert-LocalSimulationIdentity $RepoRoot $Receipt
    $running = @(Invoke-ExternalSafe 'docker' @('inspect', '-f', '{{.State.Running}}', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlContainerMissing')
    if ([string]$running[0] -ne 'true') { Invoke-ExternalSafe 'docker' @('start', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlStartFailed' | Out-Null }
    Invoke-WslHelper $RepoRoot 'start' @([string]$Receipt.instanceId, [string]$Receipt.sourceSha) -Sudo | Out-Null
    return [ordered]@{ action = 'Start'; status = 'STARTED'; instanceId = $Receipt.instanceId }
}

function Verify-MyDmsVnLocalSimulation {
    param([string] $RepoRoot, [string] $StateRoot, [pscustomobject] $Receipt)
    Assert-LocalSimulationIdentity $RepoRoot $Receipt
    Invoke-SqlHelper $StateRoot $Receipt 'verify' | Out-Null
    Invoke-WslHelper $RepoRoot 'verify' @([string]$Receipt.instanceId, [string]$Receipt.sourceSha) -Sudo | Out-Null
    Invoke-HttpExercise $StateRoot $Receipt
    Invoke-WslHelper $RepoRoot 'recovery' @([string]$Receipt.instanceId, [string]$Receipt.sourceSha) -Sudo | Out-Null
    return [ordered]@{ action = 'Verify'; status = 'VERIFIED'; instanceId = $Receipt.instanceId; executed = 4; passed = 4; failed = 0; skipped = 0 }
}

function Stop-MyDmsVnLocalSimulation {
    param([string] $RepoRoot, [pscustomobject] $Receipt)
    Assert-LocalSimulationIdentity $RepoRoot $Receipt
    Invoke-WslHelper $RepoRoot 'stop' @([string]$Receipt.instanceId, [string]$Receipt.sourceSha) -Sudo | Out-Null
    Invoke-ExternalSafe 'docker' @('stop', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlStopFailed' | Out-Null
    return [ordered]@{ action = 'Stop'; status = 'STOPPED'; instanceId = $Receipt.instanceId }
}

function Get-ResetDockerLabel {
    param([ValidateSet('container','volume')] [string] $Kind, [string] $Name)
    if ($Kind -eq 'container') {
        $label = & docker inspect -f '{{ index .Config.Labels "mydmsvn.local-sim.instance" }}' $Name 2>$null
    }
    else {
        $label = & docker volume inspect -f '{{ index .Labels "mydmsvn.local-sim.instance" }}' $Name 2>$null
    }
    if ($LASTEXITCODE -eq 0) { return ([string]$label).Trim() }
    & docker info 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw [InvalidOperationException]::new('LocalSimulation.DockerBackendUnavailable') }
    if ($Kind -eq 'container') {
        $found = @(& docker container ls -a --filter "name=^/$Name`$" --format '{{.Names}}' 2>$null)
    }
    else {
        $found = @(& docker volume ls --filter "name=^$Name`$" --format '{{.Name}}' 2>$null)
    }
    if ($LASTEXITCODE -ne 0) { throw [InvalidOperationException]::new('LocalSimulation.DockerInventoryUnavailable') }
    if ($found.Count -ne 0) { throw [InvalidOperationException]::new('LocalSimulation.DockerIdentityUnreadable') }
    return $null
}

function Reset-MyDmsVnLocalSimulationExternal {
    param([string] $RepoRoot, [string] $StateRoot, [pscustomobject] $Receipt)
    $engineId = @(Invoke-ExternalSafe 'docker' @('info', '--format', '{{.ID}}') 'LocalSimulation.DockerBackendUnavailable')
    if (([string]$engineId[0]).Trim() -ne [string]$Receipt.docker.engineId) { throw [InvalidOperationException]::new('LocalSimulation.DockerEngineMismatch') }
    if ([bool]$Receipt.wsl.installed) {
        Invoke-WslHelper $RepoRoot 'reset-admission' @([string]$Receipt.instanceId, [string]$Receipt.sourceSha) -Sudo | Out-Null
    }
    $containerLabel = $null
    $volumeLabel = $null
    if ([bool]$Receipt.docker.created) {
        $containerLabel = Get-ResetDockerLabel container 'mydmsvn-local-sim-sql'
        if ($null -ne $containerLabel) {
            if (([string]$containerLabel).Trim() -ne [string]$Receipt.instanceId) { throw [InvalidOperationException]::new('LocalSimulation.SqlContainerIdentityMismatch') }
        }
        $volumeLabel = Get-ResetDockerLabel volume 'mydmsvn-local-sim-sql-data'
        if ($null -ne $volumeLabel) {
            if (([string]$volumeLabel).Trim() -ne [string]$Receipt.instanceId) { throw [InvalidOperationException]::new('LocalSimulation.SqlVolumeIdentityMismatch') }
        }
    }
    $certificatePath = $null
    if ($Receipt.PSObject.Properties.Name -contains 'certificateThumbprint' -and $Receipt.certificateThumbprint -match '^[0-9A-Fa-f]{40}$') {
        $caFile = Join-Path $StateRoot 'tls\ca.crt'
        if (-not (Test-Path -LiteralPath $caFile -PathType Leaf)) { throw [InvalidOperationException]::new('LocalSimulation.CertificateIdentityMissing') }
        $fileCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($caFile)
        $expectedSubject = "CN=MyDmsVn Local Sim {0}" -f $Receipt.instanceId
        if ($fileCertificate.Thumbprint -ne [string]$Receipt.certificateThumbprint -or $fileCertificate.Subject -ne $expectedSubject) {
            throw [InvalidOperationException]::new('LocalSimulation.CertificateIdentityMismatch')
        }
        $certificatePath = "Cert:\CurrentUser\Root\{0}" -f $Receipt.certificateThumbprint
        if (Test-Path -LiteralPath $certificatePath) {
            $storedCertificate = Get-Item -LiteralPath $certificatePath
            if ($storedCertificate.Subject -ne $expectedSubject) { throw [InvalidOperationException]::new('LocalSimulation.CertificateIdentityMismatch') }
        }
    }
    if ([bool]$Receipt.wsl.installed) {
        Invoke-WslHelper $RepoRoot 'reset' @([string]$Receipt.instanceId, [string]$Receipt.sourceSha) -Sudo | Out-Null
    }
    if ($null -ne $containerLabel) {
        Invoke-ExternalSafe 'docker' @('rm', '-f', '-v', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlContainerRemoveFailed' | Out-Null
    }
    if ($null -ne $volumeLabel) {
        Invoke-ExternalSafe 'docker' @('volume', 'rm', 'mydmsvn-local-sim-sql-data') 'LocalSimulation.SqlVolumeRemoveFailed' | Out-Null
    }
    if ($null -ne $certificatePath -and (Test-Path -LiteralPath $certificatePath)) {
        Invoke-ExternalSafe 'certutil.exe' @('-user', '-delstore', 'Root', [string]$Receipt.certificateThumbprint) 'LocalSimulation.CertificateRemoveFailed' | Out-Null
    }
}

Export-ModuleMember -Function Initialize-MyDmsVnLocalSimulation,Start-MyDmsVnLocalSimulation,Verify-MyDmsVnLocalSimulation,Stop-MyDmsVnLocalSimulation,Reset-MyDmsVnLocalSimulationExternal
