#!/usr/bin/env python3
"""Keep VPS-06A canary independent even after the VPS-06B production cutover."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
canary = root / 'tests/MyDmsVn.Vps06A.MediatorCanary'
project = (canary / 'MyDmsVn.Vps06A.MediatorCanary.csproj').read_text(encoding='utf-8')
packages = (root / 'Directory.Packages.props').read_text(encoding='utf-8-sig')
prod = (root / 'src/MyDmsVn.Server.Application/DependencyInjection.cs').read_text(encoding='utf-8-sig')
host = (root / 'src/MyDmsVn.Server.AspNetCoreHost/MyDmsVn.Server.AspNetCoreHost.csproj').read_text(encoding='utf-8-sig')
source = (canary / 'Program.cs').read_text(encoding='utf-8')
assert 'AddMediatR(' in prod and 'AddServerApplication(' in prod, 'Legacy net48 MediatR registration missing'
# VPS-06B places production source generation at exactly one *other* outermost
# executable. The original isolated canary remains intact and independent.
if 'Mediator.SourceGenerator' in host:
    app = (root / 'src/MyDmsVn.Server.Application/MyDmsVn.Server.Application.csproj').read_text(encoding='utf-8-sig')
    assert '<Vps06bMediator>true</Vps06bMediator>' in host
    assert 'Mediator.Abstractions' in app and 'Vps06bMediator' in app
    assert '#if !VPS06B_MEDIATOR' in prod, 'Legacy MediatR path must stay isolated from production'
    assert host.count('PackageReference Include="Mediator.SourceGenerator"') == 1
    assert '<PrivateAssets>all</PrivateAssets>' in host
    assert 'Mediator.SourceGenerator' not in app, 'Only the outermost executable may generate'
else:
    assert 'Vps06bMediator' not in host, 'Incomplete production generator registration'
assert 'Mediator.SourceGenerator' in project and 'Mediator.Abstractions' in project
assert 'PrivateAssets>all' in project and 'options.ServiceLifetime = ServiceLifetime.Scoped;' in source
assert len(re.findall(r'PackageVersion Include="Mediator.(?:Abstractions|SourceGenerator)" Version="3.0.2"', packages)) == 2
assert '<TargetFramework>net10.0</TargetFramework>' in project
assert 'ProjectReference' not in project, 'Canary must not pull application/server via project references'
assert (canary / 'global.json').is_file()
print('PASS VPS-06A isolated canary preserved; production generator only at separate net10 outermost Host')
