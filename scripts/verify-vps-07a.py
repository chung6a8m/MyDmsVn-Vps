#!/usr/bin/env python3
"""Independent SQL-source oracle for VPS-07A. No real SQL execution is claimed."""
from __future__ import annotations

from hashlib import sha256
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/MyDmsVn.Server.DbMigrator/Scripts"
MIGRATIONS = ROOT / "src/MyDmsVn.Server.FluentMigrator"
MANIFEST = ROOT / "docs/import/sql-checksums.sha256"
EXPECTED = [
    "001_PersistenceFoundation.sql",
    "002_Identity.sql",
    "003_Catalog.sql",
    "004_Catalog_Nul_Constraints.sql",
    "005_GoodsReceipt.sql",
    "006_GoodsReceipt_Number_Sequence.sql",
    "007_Inventory_Stock.sql",
    "008_Security_Administration.sql",
    "009_Security_Permission_Import.sql",
    "010_DirectSalesAndValuation.sql",
    "011_WebAuthSessions.sql",
    "012_WebOperationJournal.sql",
    "013_WebOperationTerminalResolution.sql",
]
BATCH_COUNTS = [1, 1, 1, 1, 1, 1, 1, 2, 2, 3, 2, 3, 9]
GO = re.compile(rb"^[ \t]*GO[ \t]*(?:--[^\r\n]*)?(?:\r\n|\n|\r|$)", re.M | re.I)

def main() -> None:
    rows = {}
    for line in MANIFEST.read_text(encoding="utf-8").splitlines():
        digest, path = line.split(maxsplit=1)
        rows[Path(path).name] = digest
    assert sorted(rows) == sorted(EXPECTED), "Historical migration inventory drift"
    assert sorted(p.name for p in SOURCE.glob("*.sql")) == sorted(EXPECTED), "SQL file inventory drift"

    total = 0
    for index, name in enumerate(EXPECTED):
        blob = (SOURCE / name).read_bytes()
        assert sha256(blob).hexdigest() == rows[name], "SQL checksum changed: " + name
        batches = []
        last = 0
        for sep in GO.finditer(blob):
            part = blob[last:sep.start()]
            if part.strip():
                batches.append(part)
            last = sep.end()
        if blob[last:].strip():
            batches.append(blob[last:])
        assert len(batches) == BATCH_COUNTS[index], "Wrong SQL batch count: " + name
        assert not re.search(rb"(?m)^[ \t]*:(?:r|setvar|connect)\b", blob, re.I)
        total += len(batches)

    code = (MIGRATIONS / "HistoricalSqlMigrations.cs").read_text(encoding="utf-8")
    attributes = re.findall(r'\[Migration\((\d+), "([^"]+)"\)\]', code)
    assert attributes == [(str(i + 1), x) for i, x in enumerate(EXPECTED)]
    assert code.count("protected override string ScriptName =>") == len(EXPECTED)
    assert "sealed override void Down()" in code and "NotSupportedException" in code
    project = (MIGRATIONS / "MyDmsVn.Server.FluentMigrator.csproj").read_text()
    assert 'EmbeddedResource Include="../MyDmsVn.Server.DbMigrator/Scripts/*.sql"' in project
    assert "dbup-" not in project.lower()
    assert total == 28
    print(f"VPS07A_STATIC_PASS versions=13 sql_batches={total} immutable_sha256=13/13")

if __name__ == "__main__":
    main()
