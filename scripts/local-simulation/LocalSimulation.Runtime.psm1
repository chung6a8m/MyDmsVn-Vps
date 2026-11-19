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
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return 'Aa1!' + [Convert]::ToHexString($bytes).ToLowerInvariant()
}

function New-LocalToken {
    $bytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToHexString($bytes).ToLowerInvariant()
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

function Invoke-DotNet10 {
    param([string] $StateRoot, [string[]] $Arguments, [string] $FailureCode)
    $sdk = Join-Path $StateRoot 'sdk10'
    if (-not (Test-Path -LiteralPath $sdk)) { [IO.Directory]::CreateDirectory($sdk) | Out-Null }
    $global = '{"sdk":{"version":"10.0.404","rollForward":"disable","allowPrerelease":false}}'
    Write-Utf8File (Join-Path $sdk 'global.json') $global
    Push-Location $sdk
    try { return Invoke-ExternalSafe 'dotnet' $Arguments $FailureCode }
    finally { Pop-Location }
}

function Publish-LocalSimulationArtifacts {
    param([string] $RepoRoot, [string] $StateRoot)
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
        Invoke-ExternalSafe 'dotnet' @('restore', 'src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj', '--locked-mode') 'LocalSimulation.DesktopRestoreFailed' | Out-Null
        Invoke-ExternalSafe 'dotnet' @('publish', 'src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj', '-c', 'Release', '-f', 'net48', '--no-restore', '-p:PlatformTarget=x86', '-o', (Join-Path $artifacts 'desktop')) 'LocalSimulation.DesktopPublishFailed' | Out-Null
    }
    finally { Pop-Location }
}

function Invoke-SqlHelper {
    param([string] $StateRoot, [pscustomobject] $Receipt, [string] $Command)
    $secrets = Join-Path $StateRoot 'secrets'
    $sa = Get-Content -LiteralPath (Join-Path $secrets 'sa_password') -Raw
    $settings = @{
        MYDMSVN_LOCAL_SIM_ADMISSION = 'approved-synthetic-local-only'
        MYDMSVN_LOCAL_SIM_INSTANCE_ID = [string]$Receipt.instanceId
        MYDMSVN_LOCAL_SIM_SQL_ADMIN_CONNECTION_STRING = "Server=localhost,11433;Initial Catalog=master;User ID=sa;Password=$sa;Encrypt=True;TrustServerCertificate=False;Pooling=False;Connect Timeout=5"
        MYDMSVN_LOCAL_SIM_RUNTIME_PASSWORD = (Get-Content -LiteralPath (Join-Path $secrets 'runtime_password') -Raw)
        MYDMSVN_LOCAL_SIM_ORACLE_PASSWORD = (Get-Content -LiteralPath (Join-Path $secrets 'oracle_password') -Raw)
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
    $sa = Get-Content -LiteralPath (Join-Path $StateRoot 'secrets\sa_password') -Raw
    $old = Set-ProcessSettings @{
        VPS07B_OPERATOR_APPROVAL = 'approved-maintenance-and-verified-backup'
        VPS07B_SQL_CONNECTION_STRING = "Server=localhost,11433;Initial Catalog=MyDmsVnVps_LocalSimulation;User ID=sa;Password=$sa;Encrypt=True;TrustServerCertificate=False;Pooling=False;Connect Timeout=5"
    }
    try {
        $dll = Join-Path $StateRoot 'artifacts\migrator\MyDmsVn.Server.FluentMigrator.Cli.dll'
        Invoke-ExternalSafe 'dotnet' @($dll, 'migrate', 'schema13') 'LocalSimulation.MigrationFailed' | Out-Null
    }
    finally { Restore-ProcessSettings $old }
}

function Write-Receipt {
    param([string] $Path, [hashtable] $Receipt)
    Write-Utf8File $Path ($Receipt | ConvertTo-Json -Depth 8)
}

function Initialize-MyDmsVnLocalSimulation {
    param([string] $RepoRoot, [string] $StateRoot)
    if (Test-Path -LiteralPath $StateRoot) { throw [InvalidOperationException]::new('LocalSimulation.StateAlreadyExists') }
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
        docker = [ordered]@{ container = 'mydmsvn-local-sim-sql'; volume = 'mydmsvn-local-sim-sql-data'; created = $false }
        wsl = [ordered]@{ distribution = 'Ubuntu'; service = 'mydmsvn-local-sim.service'; installed = $false }
        certificateThumbprint = $null
    }
    $receiptPath = Join-Path $StateRoot 'receipt.json'
    Write-Receipt $receiptPath $receipt

    $secretValues = @{
        sa_password = New-LocalPassword; runtime_password = New-LocalPassword
        oracle_password = New-LocalPassword; writer_password = New-LocalPassword
        proxy_token = New-LocalToken; jwt_keys = ('local-key:' + (New-LocalToken))
    }
    foreach ($name in $secretValues.Keys) { Write-Utf8File (Join-Path $StateRoot "secrets\$name") $secretValues[$name] }

    Publish-LocalSimulationArtifacts $RepoRoot $StateRoot
    $stateWsl = Convert-ToWslPath $StateRoot
    Invoke-WslHelper $RepoRoot 'certificates' @($stateWsl) | Out-Null
    $ca = Import-Certificate -FilePath (Join-Path $StateRoot 'tls\ca.crt') -CertStoreLocation 'Cert:\CurrentUser\Root'
    $receipt.certificateThumbprint = $ca.Thumbprint
    Write-Receipt $receiptPath $receipt

    Write-Utf8File (Join-Path $StateRoot 'tls\mssql.conf') "[network]`ntlscert = /var/opt/mssql/tls/sql.crt`ntlskey = /var/opt/mssql/tls/sql.key`nforceencryption = 1`ntlsprotocols = 1.2`n"
    & docker container inspect mydmsvn-local-sim-sql 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw [InvalidOperationException]::new('LocalSimulation.SqlContainerNameCollision') }
    & docker volume inspect mydmsvn-local-sim-sql-data 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw [InvalidOperationException]::new('LocalSimulation.SqlVolumeNameCollision') }
    $labels = @('--label', "mydmsvn.local-sim.instance=$instanceId")
    Invoke-ExternalSafe 'docker' (@('volume', 'create') + $labels + @('mydmsvn-local-sim-sql-data')) 'LocalSimulation.SqlVolumeCreateFailed' | Out-Null
    $tls = Join-Path $StateRoot 'tls'
    $image = 'mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04'
    $copyScript = 'mkdir -p /var/opt/mssql/tls /var/opt/mssql/backup; cp /source/sql.crt /source/sql.key /var/opt/mssql/tls/; cp /source/mssql.conf /var/opt/mssql/mssql.conf; chown -R 10001:0 /var/opt/mssql/tls /var/opt/mssql/backup /var/opt/mssql/mssql.conf; chmod 600 /var/opt/mssql/tls/sql.key; chmod 644 /var/opt/mssql/tls/sql.crt /var/opt/mssql/mssql.conf'
    Invoke-ExternalSafe 'docker' @('run', '--rm', '--user', '0', '--entrypoint', '/bin/bash', '--mount', 'type=volume,source=mydmsvn-local-sim-sql-data,target=/var/opt/mssql', '--mount', "type=bind,source=$tls,target=/source,readonly", $image, '-c', $copyScript) 'LocalSimulation.SqlTlsProvisionFailed' | Out-Null
    Invoke-ExternalSafe 'docker' @('run', '-d', '--name', 'mydmsvn-local-sim-sql', '--hostname', 'mydmsvn-local-sim-sql', '--label', "mydmsvn.local-sim.instance=$instanceId", '-e', 'ACCEPT_EULA=Y', '-e', 'MSSQL_PID=Developer', '-e', ("MSSQL_SA_PASSWORD=" + $secretValues.sa_password), '-p', '127.0.0.1:11433:1433', '-v', 'mydmsvn-local-sim-sql-data:/var/opt/mssql', $image) 'LocalSimulation.SqlContainerCreateFailed' | Out-Null
    $receipt.docker.created = $true
    Write-Receipt $receiptPath $receipt

    $created = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { Invoke-SqlHelper $StateRoot ([pscustomobject]$receipt) 'create' | Out-Null; $created = $true; break }
        catch { if ($attempt -eq 29) { throw }; Start-Sleep -Seconds 2 }
    }
    if (-not $created) { throw [InvalidOperationException]::new('LocalSimulation.SqlReadinessFailed') }
    Invoke-SqlHelper $StateRoot ([pscustomobject]$receipt) 'backup-verify' | Out-Null
    Invoke-FluentMigration $StateRoot
    Invoke-SqlHelper $StateRoot ([pscustomobject]$receipt) 'provision' | Out-Null

    $runtimeSql = "Server=host.docker.internal,11433;Initial Catalog=MyDmsVnVps_LocalSimulation;User ID=mydmsvn_local_sim_runtime;Password=$($secretValues.runtime_password);Encrypt=True;TrustServerCertificate=False;Pooling=False"
    Write-Utf8File (Join-Path $StateRoot 'secrets\sql_connection_string') $runtimeSql
    $instanceEnv = "INSTANCE_ID=$instanceId`nSOURCE_SHA=$sourceSha`nPUBLIC_ORIGIN=https://localhost:50443`nSQL_SERVER=host.docker.internal,11433`n"
    Write-Utf8File (Join-Path $StateRoot 'instance.env') $instanceEnv
    $rendered = Join-Path $StateRoot 'rendered'
    $renderedWsl = Convert-ToWslPath $rendered
    $hostWsl = Convert-ToWslPath (Join-Path $StateRoot 'artifacts\host')
    Invoke-WslHelper $RepoRoot 'render' @($stateWsl, $renderedWsl, $hostWsl) | Out-Null
    Invoke-WslHelper $RepoRoot 'prerequisites' @() -Sudo | Out-Null
    $receipt.wsl.installed = $true
    Write-Receipt $receiptPath $receipt
    Invoke-WslHelper $RepoRoot 'install' @($stateWsl, $renderedWsl, $hostWsl, $instanceId) -Sudo | Out-Null
    Invoke-WslHelper $RepoRoot 'start' @() -Sudo | Out-Null
    Invoke-WslHelper $RepoRoot 'verify' @() -Sudo | Out-Null
    $receipt.phase = 'ready'
    Write-Receipt $receiptPath $receipt
    return [ordered]@{ action = 'Initialize'; status = 'READY'; instanceId = $instanceId; desktopPath = (Join-Path $StateRoot 'artifacts\desktop\MyDmsVn.Desktop.App.exe') }
}

function Start-MyDmsVnLocalSimulation {
    param([string] $RepoRoot, [pscustomobject] $Receipt)
    $label = @(Invoke-ExternalSafe 'docker' @('inspect', '-f', '{{ index .Config.Labels "mydmsvn.local-sim.instance" }}', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlContainerMissing')
    if (([string]$label[0]).Trim() -ne [string]$Receipt.instanceId) { throw [InvalidOperationException]::new('LocalSimulation.SqlContainerIdentityMismatch') }
    $running = @(Invoke-ExternalSafe 'docker' @('inspect', '-f', '{{.State.Running}}', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlContainerMissing')
    if ([string]$running[0] -ne 'true') { Invoke-ExternalSafe 'docker' @('start', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlStartFailed' | Out-Null }
    Invoke-WslHelper $RepoRoot 'start' @() -Sudo | Out-Null
    return [ordered]@{ action = 'Start'; status = 'STARTED'; instanceId = $Receipt.instanceId }
}

function Verify-MyDmsVnLocalSimulation {
    param([string] $RepoRoot, [string] $StateRoot, [pscustomobject] $Receipt)
    Invoke-SqlHelper $StateRoot $Receipt 'verify' | Out-Null
    Invoke-WslHelper $RepoRoot 'verify' @() -Sudo | Out-Null
    Invoke-WslHelper $RepoRoot 'recovery' @() -Sudo | Out-Null
    return [ordered]@{ action = 'Verify'; status = 'VERIFIED'; instanceId = $Receipt.instanceId; executed = 3; passed = 3; failed = 0; skipped = 0 }
}

function Stop-MyDmsVnLocalSimulation {
    param([string] $RepoRoot, [pscustomobject] $Receipt)
    Invoke-WslHelper $RepoRoot 'stop' @() -Sudo | Out-Null
    Invoke-ExternalSafe 'docker' @('stop', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlStopFailed' | Out-Null
    return [ordered]@{ action = 'Stop'; status = 'STOPPED'; instanceId = $Receipt.instanceId }
}

function Reset-MyDmsVnLocalSimulationExternal {
    param([string] $RepoRoot, [pscustomobject] $Receipt)
    if ([bool]$Receipt.wsl.installed) {
        & wsl -d Ubuntu -- sudo -- test -f /etc/mydmsvn-local-sim/instance-id 2>$null
        if ($LASTEXITCODE -eq 0) { Invoke-WslHelper $RepoRoot 'reset' @([string]$Receipt.instanceId) -Sudo | Out-Null }
    }
    if ([bool]$Receipt.docker.created) {
        $containerLabel = & docker inspect -f '{{ index .Config.Labels "mydmsvn.local-sim.instance" }}' mydmsvn-local-sim-sql 2>$null
        if ($LASTEXITCODE -eq 0) {
            if (([string]$containerLabel).Trim() -ne [string]$Receipt.instanceId) { throw [InvalidOperationException]::new('LocalSimulation.SqlContainerIdentityMismatch') }
            Invoke-ExternalSafe 'docker' @('rm', '-f', '-v', 'mydmsvn-local-sim-sql') 'LocalSimulation.SqlContainerRemoveFailed' | Out-Null
        }
        $volumeLabel = & docker volume inspect -f '{{ index .Labels "mydmsvn.local-sim.instance" }}' mydmsvn-local-sim-sql-data 2>$null
        if ($LASTEXITCODE -eq 0) {
            if (([string]$volumeLabel).Trim() -ne [string]$Receipt.instanceId) { throw [InvalidOperationException]::new('LocalSimulation.SqlVolumeIdentityMismatch') }
            Invoke-ExternalSafe 'docker' @('volume', 'rm', 'mydmsvn-local-sim-sql-data') 'LocalSimulation.SqlVolumeRemoveFailed' | Out-Null
        }
    }
    if ($Receipt.PSObject.Properties.Name -contains 'certificateThumbprint' -and $Receipt.certificateThumbprint -match '^[0-9A-Fa-f]{40}$') {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\Root\{0}" -f $Receipt.certificateThumbprint) -Force -ErrorAction SilentlyContinue
    }
}

Export-ModuleMember -Function Initialize-MyDmsVnLocalSimulation,Start-MyDmsVnLocalSimulation,Verify-MyDmsVnLocalSimulation,Stop-MyDmsVnLocalSimulation,Reset-MyDmsVnLocalSimulationExternal
