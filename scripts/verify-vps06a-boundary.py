#!/usr/bin/env python3
"""VPS-06A boundary: no production Mediator generator cutover or staging claim."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
canary = root / 'tests/MyDmsVn.Vps06A.MediatorCanary'
project = (canary / 'MyDmsVn.Vps06A.MediatorCanary.csproj').read_text(encoding='utf-8')
packages = (root / 'Directory.Packages.props').read_text(encoding='utf-8-sig')
prod = (root / 'src/MyDmsVn.Server.Application/DependencyInjection.cs').read_text(encoding='utf-8-sig')
host = (root / 'src/MyDmsVn.Server.AspNetCoreHost/MyDmsVn.Server.AspNetCoreHost.csproj').read_text(encoding='utf-8-sig')
source = (canary / 'Program.cs').read_text(encoding='utf-8')
assert 'AddMediatR(' in prod and 'AddServerApplication(' in prod, 'Production MediatR altered before VPS-06B'
assert 'Mediator.SourceGenerator' not in host, 'Generator must not be in production Host during 06A'
assert 'Mediator.SourceGenerator' in project and 'Mediator.Abstractions' in project
assert 'PrivateAssets>all' in project and 'options.ServiceLifetime = ServiceLifetime.Scoped;' in source
assert len(re.findall(r'PackageVersion Include="Mediator.(?:Abstractions|SourceGenerator)" Version="3.0.2"', packages)) == 2
assert '<TargetFramework>net10.0</TargetFramework>' in project
assert 'ProjectReference' not in project, 'Canary must not pull application/server via project references'
assert (canary / 'global.json').is_file()
print('PASS VPS-06A isolated generator boundary: net10 only, scoped, pinned 3.0.2, production MediatR unchanged')
