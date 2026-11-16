#!/usr/bin/env bash
set -Eeuo pipefail

# VPS-00A one-time source copy. The workflow removes this script when done.
if [[ "$GITHUB_REPOSITORY" != "chung6a8m/MyDmsVn-Vps" || "$GITHUB_REF" != "refs/heads/main" ]]; then
  echo "Refusing import outside destination main" >&2; exit 1
fi
if [[ -d src || -d vendor/MyDmsVn.WinFormsKit/src ]]; then
  echo "Refusing a second source import" >&2; exit 1
fi

SOURCE_SHA=bac73004328884f5322c7c87d9787b8150885ac0
KIT_SHA=433e5b53f979e37cc6b69bad6dfcde0975c7ecaf
SOURCEGRID_SHA=f4e457b43582bf01892f50bdc74aa480531e5944
TEMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TEMP_DIR"' EXIT

clone_pinned() {
  local repository="$1" revision="$2" directory="$3"
  git clone --quiet --filter=blob:none --no-checkout \
    "https://github.com/chung6a8m/$repository.git" "$directory"
  if ! git -C "$directory" cat-file -e "$revision^{commit}" 2>/dev/null; then
    git -C "$directory" fetch --quiet origin "$revision"
  fi
  git -C "$directory" checkout --quiet --detach "$revision"
  [[ "$(git -C "$directory" rev-parse HEAD)" == "$revision" ]] || exit 1
  echo "Verified $repository at $revision"
}
clone_pinned MyDmsVn "$SOURCE_SHA" "$TEMP_DIR/source"
clone_pinned MyDmsVn.WinFormsKit "$KIT_SHA" "$TEMP_DIR/kit"
clone_pinned sourcegrid "$SOURCEGRID_SHA" "$TEMP_DIR/sourcegrid"

# Copy tracked working tree without inheriting Git history, legacy operational
# docs, workflow governance, or the excluded net8 Desktop.AppCore executable.
rsync -a \
  --exclude='/.git/' --exclude='/.github/' --exclude='/.agents/' \
  --exclude='/.gitmodules' --exclude='/docs/' \
  --exclude='/README.md' --exclude='/AGENTS.md' --exclude='/AI_CONTEXT.md' \
  --exclude='/src/MyDmsVn.Desktop.AppCore/' \
  --exclude='/vendor/MyDmsVn.WinFormsKit/' \
  "$TEMP_DIR/source/" ./

# WinFormsKit and its own SourceGrid submodule become regular local files.
mkdir -p vendor/MyDmsVn.WinFormsKit/vendor/sourcegrid
rsync -a --exclude='/.git/' --exclude='/.gitmodules' --exclude='/.github/' \
  --exclude='/AGENTS.md' --exclude='/AI_CONTEXT.md' \
  "$TEMP_DIR/kit/" vendor/MyDmsVn.WinFormsKit/
rsync -a --exclude='/.git/' --exclude='/.gitmodules' \
  --exclude='/AGENTS.md' --exclude='/AI_CONTEXT.md' \
  "$TEMP_DIR/sourcegrid/" vendor/MyDmsVn.WinFormsKit/vendor/sourcegrid/

mkdir -p docs/import docs/roadmap
cat > README.md <<'EOF'
# MyDmsVn-Vps

Independent source baseline, imported once without Git history or submodules.

## Approved Owner decisions, 2026-11-16
- OWN-VPS-01: Linux VPS, future ASP.NET Core 10 / Kestrel / systemd / reverse proxy.
- OWN-VPS-02: SQL Server; FluentMigrator must support fresh and valid existing databases.
- OWN-VPS-03: Desktop net48 only; Desktop.AppCore is excluded.

VPS-00A imports the legacy source code, tests and SQL without replatforming.
This is NOT yet a Linux-ready build or production release. The source Desktop
may still depend on the legacy Server until VPS-03.

See docs/import/README.md for source SHA provenance and
docs/roadmap/README.md for independent roadmap status.
VPS-00B establishes independent documentation, Windows/Linux CI and
baseline build/test evidence. Previous source acceptance is not inherited.
EOF
cat > AGENTS.md <<'EOF'
# MyDmsVn-Vps repository instructions

Work ONLY from the instructions and plans in this repository. Do not inherit
operational governance, roadmap, CI results or production acceptance from the
source or other repositories. Source links are attribution only.

Approved Owner choices: Linux VPS for HttpHost, SQL Server fresh + existing
database compatibility, and Desktop net48 only. Maintain original security,
transaction, stock/valuation, idempotency and API behavior until each VPS
increment expressly changes it with tests.

docs/roadmap/README.md is the roadmap status entry point.
docs/import/README.md records immutable snapshot provenance.
Never claim PASS for tests that have not run on this repo and environment.
EOF
cat > AI_CONTEXT.md <<'EOF'
# MyDmsVn-Vps — independent AI context

Repository: chung6a8m/MyDmsVn-Vps (main).
Snapshot, not a fork or a continuously synchronized source mirror.

Target architecture: Windows Desktop net48 -> HTTPS JSON API -> Linux VPS
ASP.NET Core 10 -> SQL Server. Existing and fresh database paths must be
validated before the future migration switch to FluentMigrator.
WinFormsKit and SourceGrid are local vendored trees, not submodules.

VPS-00A is a source-only import. VPS-00B creates CI/build foundation,
architecture docs, Owner ADRs and full roadmap. Subsequent VPS increments
handle HTTP parity, Desktop cutover, .NET 10, System.Text.Json, Mediator,
FluentMigrator, deployment and acceptance. Do not reuse source evidence.
EOF
cat > docs/roadmap/README.md <<'EOF'
# Independent roadmap — MyDmsVn-Vps

- VPS-00A: source import and static integrity checks; see docs/import.
- VPS-00B: NOT STARTED — independent architecture, Owner decisions,
  build/test evidence, cross-platform CI, full VPS-00..09 roadmap.
- VPS-01..09: NOT STARTED — test/contracts, HTTP parity, Desktop HTTP
  cutover, .NET 10 Server, JSON, Mediator, FluentMigrator, Linux hardening,
  integrated acceptance and closeout.

Status MUST reflect evidence on the final commit. No historic source
PASS/exception/production readiness carries into MyDmsVn-Vps.
EOF
cat > vendor/MyDmsVn.WinFormsKit/VPS-SNAPSHOT.md <<'EOF'
# Vendored source snapshot

This WinFormsKit directory is local source, not a Git submodule. Its
vendor/sourcegrid directory is likewise local source. Exact pins are in
docs/import/README.md at repository root. Original third-party licenses,
notices and source copyright headers remain in place.
EOF

python3 - <<'PY'
from pathlib import Path
from xml.etree import ElementTree as ET
import sys
missing=[]
skipped=[]
projects=list(Path('.').rglob('*.csproj'))
for proj in projects:
    try:
        root=ET.parse(proj).getroot()
    except ET.ParseError as exc:
        missing.append(f'{proj}: XML parse error {exc}')
        continue
    for item in root.iter():
        if item.tag.rsplit('}',1)[-1] != 'ProjectReference':
            continue
        ref=item.get('Include','').replace('\\','/')
        if '$(' in ref or '@(' in ref:
            skipped.append(f'{proj}: {ref}')
        elif not (proj.parent / ref).exists():
            missing.append(f'{proj}: MISSING ProjectReference {ref}')
print(f'Static ProjectReference inventory: {len(projects)} projects, {len(skipped)} dynamic refs.')
if missing:
    print('\n'.join(missing), file=sys.stderr)
    sys.exit(1)
PY

check_same_blob() {
  local original="$1" original_path="$2" copy="$3"
  [[ "$(git -C "$original" rev-parse "HEAD:$original_path")" == "$(git hash-object "$copy")" ]] || {
    echo "Blob mismatch: $copy" >&2; exit 1;
  }
}
for sql in src/MyDmsVn.Server.DbMigrator/Scripts/[0-9][0-9][0-9]_*.sql; do
  check_same_blob "$TEMP_DIR/source" "$sql" "$sql"
done
sql_count="$(find src/MyDmsVn.Server.DbMigrator/Scripts -maxdepth 1 -name '[0-9][0-9][0-9]_*.sql' -type f | wc -l | tr -d ' ')"
[[ "$sql_count" == 13 ]] || { echo "Expected 13 original SQL migrations" >&2; exit 1; }
check_same_blob "$TEMP_DIR/source" "src/MyDmsVn.Server.Domain/MyDmsVn.Server.Domain.csproj" "src/MyDmsVn.Server.Domain/MyDmsVn.Server.Domain.csproj"
check_same_blob "$TEMP_DIR/kit" "src/MyDmsVn.Bootstrap5WinFormUI/MyDmsVn.Bootstrap5WinFormUI.csproj" "vendor/MyDmsVn.WinFormsKit/src/MyDmsVn.Bootstrap5WinFormUI/MyDmsVn.Bootstrap5WinFormUI.csproj"
check_same_blob "$TEMP_DIR/sourcegrid" "SourceGrid/SourceGrid.csproj" "vendor/MyDmsVn.WinFormsKit/vendor/sourcegrid/SourceGrid/SourceGrid.csproj"

[[ ! -d src/MyDmsVn.Desktop.AppCore ]] || exit 1
[[ ! -e .gitmodules ]] || exit 1
if find vendor \( -name .git -o -name .gitmodules \) -print -quit | grep -q .; then
  echo "Unexpected nested git metadata" >&2; exit 1
fi
[[ -f vendor/MyDmsVn.WinFormsKit/vendor/sourcegrid/SourceGrid/SourceGrid.License.txt ]] || exit 1
[[ -f vendor/MyDmsVn.WinFormsKit/THIRD_PARTY_NOTICES.md ]] || exit 1
if grep -qi 'MyDmsVn.Desktop.AppCore' MyDmsVn.sln; then
  echo "Main solution retains AppCore" >&2; exit 1
fi

find src/MyDmsVn.Server.DbMigrator/Scripts -maxdepth 1 \
  -name '[0-9][0-9][0-9]_*.sql' -type f -print0 |
  sort -z | xargs -0 sha256sum > docs/import/sql-checksums.sha256

CODE_COUNT="$(find src tests samples tools scripts deployment -type f | wc -l | tr -d ' ')"
VENDOR_COUNT="$(find vendor/MyDmsVn.WinFormsKit -type f | wc -l | tr -d ' ')"
cat > docs/import/README.md <<EOF
# VPS-00A — One-time Source Import (independent)

The destination contains a new Git history and NO upstream remote.

| Component | Pinned snapshot |
| --- | --- |
| MyDmsVn | $SOURCE_SHA |
| MyDmsVn.WinFormsKit | $KIT_SHA |
| SourceGrid, nested kit dependency | $SOURCEGRID_SHA |

Imported: original legacy Domain, Application, Infrastructure, Desktop net48,
HttpHost, DbMigrator, SQL migrations 001–013, tests, samples, tooling,
deployment, and all local vendor sources/assets/licenses.
Import file counts (worktree): $CODE_COUNT code/test/tool/deployment files;
$VENDOR_COUNT WinFormsKit and SourceGrid vendor files.

Excluded: source Git history, submodule links and declarations,
Desktop.AppCore, source-root README/AGENTS/AI_CONTEXT, historical active docs,
roadmaps/evidence, .agents directory and source GitHub workflows.
Vendor technical documents and provenance/notice links are attribution only.

Static validation: 13 original SQL Git blobs identical; source project
and representative vendor blobs identical; local csproj ProjectReferences
exist; vendor notice files present; AppCore and nested git metadata absent.
See sql-checksums.sha256 for independently inspectable SQL checksums.

NOT YET VERIFIED: full Windows net48 build/tests, server build/tests,
Linux runtime or SQL runtime. These are VPS-00B or later gates. No .NET 10,
JSON, mediator or FluentMigrator code change is part of VPS-00A.
EOF

echo "Source import prepared and static checks passed: $CODE_COUNT code files, $VENDOR_COUNT vendor files."
