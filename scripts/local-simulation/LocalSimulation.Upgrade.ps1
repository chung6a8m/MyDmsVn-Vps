function Get-LocalSimulationPendingSlot {
    $homeDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try { $owner = $identity.User }
    finally { $identity.Dispose() }
    if ($null -eq $owner -or -not [IO.Path]::IsPathFullyQualified($homeDirectory)) {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
    }
    $binding = "MyDmsVn-Vps|pending-v1|$($owner.Value)|https://localhost:50443"
    $slot = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes($binding))
    return @{
        owner = $owner; home = $homeDirectory
        path = Join-Path $homeDirectory "MyDmsVn-Vps\PendingWrites\$slot.intent"
        mutexName = "Global\MyDmsVn-Vps.PendingWrite.$slot"
    }
}

function Assert-LocalSimulationPendingAcl {
    param([string] $Path)
    $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected -or $acl.GetOwner([Security.Principal.SecurityIdentifier]) -ne $owner) {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.IdentityReference -notin @($owner, $system) -or
            $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl) {
            throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
        }
        [void]$seen.Add($rule.IdentityReference.Value)
    }
    if (-not ($seen.Contains($owner.Value) -and $seen.Contains($system.Value))) {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
    }
}

function Open-LocalSimulationPendingFence {
    $path = (Get-LocalSimulationPendingSlot).path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingMissing') }
    Assert-CandidateDirectory (Split-Path -Parent $path)
    $file = Get-Item -LiteralPath $path -Force
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
    }
    try {
        Assert-LocalSimulationPendingAcl $file.FullName
        Assert-LocalSimulationPendingAcl $file.DirectoryName
        return [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    }
    catch { throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch') }
}

function Get-LocalSimulationPendingHash {
    param([IO.FileStream] $Fence)
    if ($Fence.Length -le 0 -or $Fence.Length -gt 8192) { throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingMismatch') }
    $Fence.Position = 0
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Fence)).Replace('-', '').ToLowerInvariant()) }
    finally { $sha.Dispose() }
}

function Assert-LocalSimulationResetSlotEmpty {
    param($Slot)
    try {
        Assert-CandidateDirectory $Slot.home
        $directory = $Slot.home
        foreach ($segment in 'MyDmsVn-Vps', 'PendingWrites') {
            $directory = Join-Path $directory $segment
            $attributes = [IO.File]::GetAttributes($directory)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -or
                -not ($attributes -band [IO.FileAttributes]::Directory)) {
                throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
            }
        }
        Assert-LocalSimulationPendingAcl $directory
        $null = [IO.File]::GetAttributes($Slot.path)
    }
    catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] { return }
    catch { throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch', $_.Exception) }
    throw [InvalidOperationException]::new('LocalSimulation.UpgradeFenceRetained')
}

function Enter-LocalSimulationResetFence {
    param([pscustomobject] $Receipt)
    if ($Receipt.phase -eq 'upgrade-pending') {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradeFenceRetained')
    }
    $slot = Get-LocalSimulationPendingSlot
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $security = [Security.AccessControl.MutexSecurity]::new()
    $security.SetOwner($slot.owner)
    $security.SetAccessRuleProtection($true, $false)
    foreach ($sid in $slot.owner, $system) {
        $security.AddAccessRule([Security.AccessControl.MutexAccessRule]::new($sid, 'FullControl', 'Allow'))
    }
    # Match WindowsHttpPendingWriteStore so admission and teardown serialize with Save/Clear.
    $mutex = $null
    $acquired = $false
    try {
        try {
            $created = $false
            $mutex = [Threading.MutexAcl]::Create($false, $slot.mutexName, [ref]$created, $security)
            $acl = [Threading.ThreadingAclExtensions]::GetAccessControl($mutex)
            if (-not $acl.AreAccessRulesProtected -or $acl.GetOwner([Security.Principal.SecurityIdentifier]) -ne $slot.owner) {
                throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
            }
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($rule in $acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier])) {
                if ($rule.IdentityReference -notin @($slot.owner, $system) -or
                    $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
                    ($rule.MutexRights -band [Security.AccessControl.MutexRights]::FullControl) -ne [Security.AccessControl.MutexRights]::FullControl) {
                    throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
                }
                [void]$seen.Add($rule.IdentityReference.Value)
            }
            if (-not ($seen.Contains($slot.owner.Value) -and $seen.Contains($system.Value))) {
                throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch')
            }
            try { $acquired = $mutex.WaitOne(0) }
            catch [Threading.AbandonedMutexException] { $acquired = $true }
        }
        catch { throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingOwnerMismatch', $_.Exception) }
        if (-not $acquired) { throw [InvalidOperationException]::new('LocalSimulation.UpgradeFenceBusy') }
        Assert-LocalSimulationResetSlotEmpty $slot
        return $mutex
    }
    catch {
        try { if ($acquired) { $mutex.ReleaseMutex() } }
        finally { if ($null -ne $mutex) { $mutex.Dispose() } }
        throw
    }
}

function Get-LocalSimulationUpgradeTemplateHashes {
    param([string] $RepoRoot)
    $templates = Join-Path $RepoRoot 'scripts\local-simulation\templates'
    return @((Get-FileSha256 (Join-Path $templates 'mydmsvn-local-sim.service.tpl')),
             (Get-FileSha256 (Join-Path $templates 'mydmsvn-local-sim-start.tpl')))
}

function Get-LocalSimulationUpgradeRelease {
    param([pscustomobject] $Receipt)
    if ('candidate' -notin $Receipt.artifacts.PSObject.Properties.Name) {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradePrebuiltPairRequired')
    }
    return [ordered]@{
        releaseId = Get-LocalSimulationReleaseId $Receipt
        hostSha256 = $Receipt.artifacts.hostSha256
        candidate = $Receipt.artifacts.candidate
    }
}

function Get-LocalSimulationUpgradeProof {
    param([string] $RepoRoot, [pscustomobject] $Receipt, $Release, [string] $AuditPrefix)
    $args = @($Receipt.instanceId, $Receipt.sourceSha, $Release.releaseId, $Release.hostSha256)
    $args += @(Get-LocalSimulationUpgradeTemplateHashes $RepoRoot)
    $args += $AuditPrefix
    return ((@(Invoke-WslHelper $RepoRoot 'upgrade-observe' $args -Sudo) -join "`n") | ConvertFrom-Json)
}

function Assert-LocalSimulationDatabaseContinuity {
    param($Current, $Baseline)
    if ($Current.databaseSchema -ne 13 -or $Current.database -ne $Baseline.database -or
        $Current.instanceId -ne $Baseline.instanceId -or $Current.databaseGuid -ne $Baseline.databaseGuid -or
        $Current.deploymentEpoch -ne $Baseline.deploymentEpoch) {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradeDatabaseMismatch')
    }
}

function Get-LocalSimulationUpgradeDockerProof {
    param([pscustomobject] $Receipt)
    $container = Get-DockerInspectObject container $Receipt.docker.container 'LocalSimulation.SqlContainerMissing'
    $volume = Get-DockerInspectObject volume $Receipt.docker.volume 'LocalSimulation.SqlVolumeMissing'
    $mount = @($container.Mounts | Where-Object Destination -eq '/var/opt/mssql')
    $port = @($container.HostConfig.PortBindings.'1433/tcp')
    if ($container.Id -cnotmatch '^[0-9a-f]{64}$' -or
        $container.Config.Labels.'mydmsvn.local-sim.instance' -ne $Receipt.instanceId -or
        $volume.Labels.'mydmsvn.local-sim.instance' -ne $Receipt.instanceId -or
        $mount.Count -ne 1 -or $mount[0].Type -ne 'volume' -or $mount[0].Name -ne $volume.Name -or
        $volume.Name -ne $Receipt.docker.volume -or $port.Count -ne 1 -or
        $port[0].HostIp -ne '127.0.0.1' -or $port[0].HostPort -ne '11433' -or
        $container.Config.Image -ne 'mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04') {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradeDockerMismatch')
    }
    return [ordered]@{ containerId = $container.Id; volume = $volume.Name; volumeCreatedAt = [string]$volume.CreatedAt }
}

function Assert-LocalSimulationAuditContinuity {
    param($Current, $Baseline)
    if ($Current.auditSize -lt $Baseline.auditSize -or $Current.auditPrefixSha256 -ne $Baseline.auditPrefixSha256 -or
        $Current.continuitySha256 -ne $Baseline.continuitySha256) {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradeAuditMismatch')
    }
}

function New-LocalSimulationStagedRelease {
    param([string] $StateRoot, [pscustomobject] $Candidate)
    $candidateId = $Candidate.identity
    Assert-CandidateDirectory (Join-Path $StateRoot 'artifacts')
    $releases = Join-Path $StateRoot 'artifacts\releases'
    [IO.Directory]::CreateDirectory($releases) | Out-Null
    Assert-CandidateDirectory $releases
    $destination = Join-Path $releases $candidateId.sourceSha
    if (-not (Test-Path -LiteralPath $destination)) {
        $stage = Join-Path $releases ('.stage-' + [guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($stage) | Out-Null
        Protect-LocalDirectory $stage
        Copy-LocalSimulationCandidate $Candidate $stage | Out-Null
        [IO.Directory]::Move($stage, $destination)
    }
    Get-CandidateTree $destination 'desktop' $candidateId.sourceSha $candidateId.desktopManifestSha256 | Out-Null
    $hostTree = Get-CandidateTree $destination 'host' $candidateId.sourceSha $candidateId.hostManifestSha256 -AllowRuntimeManifest
    $expected = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(($hostTree.runtimeLines -join "`n") + "`n"))
    Assert-ArtifactManifest (Join-Path $destination 'host') $expected
    return [ordered]@{ releaseId = $candidateId.sourceSha; hostSha256 = $expected; candidate = $candidateId }
}

function Save-LocalSimulationUpgradeReceipt {
    param([string] $StateRoot, [pscustomobject] $Receipt)
    $path = Join-Path $StateRoot 'receipt.json'
    Update-LocalSimulationRegistration $StateRoot $Receipt $path
    Write-AtomicUtf8File $path ($Receipt | ConvertTo-Json -Depth 8)
}

function Invoke-LocalSimulationUpgrade {
    param([string] $Action, [string] $RepoRoot, [string] $StateRoot, [pscustomobject] $Receipt,
          [pscustomobject] $Candidate, [string] $ExpectedDeploymentEpoch, [string] $ExpectedPendingSha256)

    $interrupted = $Receipt.phase -eq 'upgrade-pending'
    if ($Receipt.phase -ne 'ready' -and -not $interrupted) { throw [InvalidOperationException]::new('LocalSimulation.ReceiptPhaseInvalid') }
    Assert-LocalSimulationIdentity $RepoRoot $StateRoot $Receipt -UpgradeInProgress:$interrupted
    $activeRelease = Get-LocalSimulationUpgradeRelease $Receipt
    $previous = if ('upgrade' -in $Receipt.PSObject.Properties.Name) { $Receipt.upgrade } else { $null }
    if (Get-Process -Name 'MyDmsVn.Desktop.App','MyDmsVn.Vps03.RecoveryClient' -ErrorAction SilentlyContinue) {
        throw [InvalidOperationException]::new('LocalSimulation.UpgradeDesktopStillRunning')
    }

    $fence = Open-LocalSimulationPendingFence
    try {
        $pendingSha = Get-LocalSimulationPendingHash $fence
        $dockerProof = Get-LocalSimulationUpgradeDockerProof $Receipt
        $db = Invoke-SqlHelper $StateRoot $Receipt 'observe-upgrade'
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $sidHash = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes($sid))
        if ($Action -eq 'PlanUpgrade') {
            if ($interrupted -or $Candidate.identity.sourceSha -in @($activeRelease.candidate.sourceSha, (Get-LocalSimulationInitialReleaseId $Receipt))) {
                throw [InvalidOperationException]::new('LocalSimulation.ReleaseIdentityMismatch')
            }
            $wsl = Get-LocalSimulationUpgradeProof $RepoRoot $Receipt $activeRelease 'current'
            if ($wsl.pid -le 0) { throw [InvalidOperationException]::new('LocalSimulation.UpgradeProcessMismatch') }
            return [ordered]@{
                action = $Action; status = 'PLANNED'; instanceId = $Receipt.instanceId
                current = $activeRelease.candidate; candidate = $Candidate.identity
                database = $db.database; schema = $db.databaseSchema; deploymentEpoch = $db.deploymentEpoch
                pendingSha256 = $pendingSha; currentHostPid = $wsl.pid
            }
        }
        if ($ExpectedPendingSha256 -cne $pendingSha -or $ExpectedDeploymentEpoch -cne $db.deploymentEpoch) {
            throw [InvalidOperationException]::new('LocalSimulation.UpgradeFenceOrEpochMismatch')
        }

        $sameTarget = $null -ne $previous -and $null -ne $Candidate -and
            ($previous.to.candidate | ConvertTo-Json -Compress) -ceq ($Candidate.identity | ConvertTo-Json -Compress)
        if ($Action -eq 'Upgrade' -and -not $interrupted -and $sameTarget -and $activeRelease.releaseId -eq $previous.to.releaseId) {
            $Action = 'VerifyUpgrade'
        }

        if ($Action -eq 'Upgrade' -and -not $interrupted) {
            if ($Candidate.identity.sourceSha -in @($activeRelease.candidate.sourceSha, (Get-LocalSimulationInitialReleaseId $Receipt))) {
                throw [InvalidOperationException]::new('LocalSimulation.ReleaseIdentityMismatch')
            }
            $wsl = Get-LocalSimulationUpgradeProof $RepoRoot $Receipt $activeRelease 'current'
            if ($wsl.pid -le 0) { throw [InvalidOperationException]::new('LocalSimulation.UpgradeProcessMismatch') }
            $destination = New-LocalSimulationStagedRelease $StateRoot $Candidate
            $journal = [ordered]@{
                action = 'Upgrade'; id = [guid]::NewGuid().ToString('N')
                from = $activeRelease; to = $destination
                database = $db; docker = $dockerProof; audit = $wsl; pendingSha256 = $pendingSha; sidSha256 = $sidHash
            }
            $Receipt | Add-Member -NotePropertyName upgrade -NotePropertyValue ([pscustomobject]$journal) -Force
            $Receipt.phase = 'upgrade-pending'
            Save-LocalSimulationUpgradeReceipt $StateRoot $Receipt
            $previous = $Receipt.upgrade
        }
        elseif ($null -eq $previous -or ($Action -eq 'Upgrade' -and (-not $sameTarget -or $previous.action -ne 'Upgrade'))) {
            throw [InvalidOperationException]::new('LocalSimulation.UpgradeJournalMismatch')
        }

        if ($pendingSha -ne $previous.pendingSha256 -or $sidHash -ne $previous.sidSha256) {
            throw [InvalidOperationException]::new('LocalSimulation.UpgradePendingMismatch')
        }
        Assert-LocalSimulationDatabaseContinuity $db $previous.database
        if (($dockerProof | ConvertTo-Json -Compress) -cne ($previous.docker | ConvertTo-Json -Compress)) {
            throw [InvalidOperationException]::new('LocalSimulation.UpgradeDockerMismatch')
        }

        if ($Action -eq 'VerifyUpgrade') {
            if ($interrupted -or $activeRelease.releaseId -ne $previous.to.releaseId) {
                throw [InvalidOperationException]::new('LocalSimulation.UpgradeJournalMismatch')
            }
            $target = $previous.to
        }
        else {
            $rollback = $Action -eq 'RollbackUpgrade'
            $target = if ($rollback) { $previous.from } else { $previous.to }
            $other = if ($rollback) { $previous.to } else { $previous.from }
            $targetTree = Get-LocalSimulationArtifactRoot $StateRoot $Receipt $target.releaseId
            Get-CandidateTree $targetTree 'desktop' $target.candidate.sourceSha $target.candidate.desktopManifestSha256 | Out-Null
            Assert-ArtifactManifest (Join-Path $targetTree 'host') $target.hostSha256
            $currentReleaseId = (@(Invoke-WslHelper $RepoRoot 'upgrade-current' @($Receipt.instanceId, $Receipt.sourceSha, $target.releaseId, $other.releaseId) -Sudo) -join '').Trim()
            if ($currentReleaseId -ne $target.releaseId -and $currentReleaseId -ne $other.releaseId) {
                throw [InvalidOperationException]::new('LocalSimulation.ReleaseIdentityMismatch')
            }
            $currentRelease = if ($currentReleaseId -eq $target.releaseId) { $target } else { $other }
            $current = Get-LocalSimulationUpgradeProof $RepoRoot $Receipt $currentRelease ([string]$previous.audit.auditSize)
            Assert-LocalSimulationAuditContinuity $current $previous.audit
            if ($rollback -and (-not $interrupted -or $previous.action -ne 'RollbackUpgrade')) {
                $previous.action = 'RollbackUpgrade'
                $Receipt.phase = 'upgrade-pending'
                Save-LocalSimulationUpgradeReceipt $StateRoot $Receipt
            }
            $publish = Convert-ToWslPath (Join-Path $targetTree 'host')
            $args = @($Receipt.instanceId, $Receipt.sourceSha, $other.releaseId, $target.releaseId,
                      $other.hostSha256, $target.hostSha256, $publish)
            $args += @(Get-LocalSimulationUpgradeTemplateHashes $RepoRoot)
            $args += @($Action, $previous.id)
            Invoke-WslHelper $RepoRoot 'upgrade-activate' $args -Sudo | Out-Null
        }

        $after = Get-LocalSimulationUpgradeProof $RepoRoot $Receipt $target ([string]$previous.audit.auditSize)
        Assert-LocalSimulationAuditContinuity $after $previous.audit
        $afterDocker = Get-LocalSimulationUpgradeDockerProof $Receipt
        if (($afterDocker | ConvertTo-Json -Compress) -cne ($previous.docker | ConvertTo-Json -Compress)) {
            throw [InvalidOperationException]::new('LocalSimulation.UpgradeDockerMismatch')
        }
        Assert-LocalSimulationDatabaseContinuity (Invoke-SqlHelper $StateRoot $Receipt 'observe-upgrade') $previous.database
        if ($pendingSha -cne (Get-LocalSimulationPendingHash $fence) -or $after.pid -le 0 -or
            ($target.releaseId -eq $previous.to.releaseId -and $after.pid -eq $previous.audit.pid)) {
            throw [InvalidOperationException]::new('LocalSimulation.UpgradeContinuityMismatch')
        }
        try {
            $health = Invoke-WebRequest -Uri 'https://localhost:50443/api/v1/health' -TimeoutSec 10
            if ($health.StatusCode -ne 200) { throw [InvalidOperationException]::new('LocalSimulation.UpgradeHttpsFailed') }
        }
        catch { throw [InvalidOperationException]::new('LocalSimulation.UpgradeHttpsFailed') }

        $Receipt.wsl | Add-Member -NotePropertyName releaseId -NotePropertyValue $target.releaseId -Force
        $Receipt.artifacts.candidate = $target.candidate
        $Receipt.artifacts.hostSha256 = $target.hostSha256
        $Receipt.phase = 'ready'
        Save-LocalSimulationUpgradeReceipt $StateRoot $Receipt
        $desktop = Join-Path (Get-LocalSimulationActiveArtifactRoot $StateRoot $Receipt) 'desktop\MyDmsVn.Desktop.App.exe'
        return [ordered]@{
            action = $Action; status = $(if ($Action -eq 'RollbackUpgrade') { 'ROLLED_BACK' } else { 'VERIFIED' })
            instanceId = $Receipt.instanceId; toolingSourceSha = $Receipt.sourceSha
            candidate = $target.candidate; hostBeforePid = $previous.audit.pid; hostAfterPid = $after.pid
            database = $db.database; schema = $db.databaseSchema; deploymentEpoch = $db.deploymentEpoch
            pendingSha256 = $pendingSha; auditPrefixSha256 = $after.auditPrefixSha256
            hostSha256 = $target.hostSha256; desktopPath = $desktop
        }
    }
    finally { $fence.Dispose() }
}
