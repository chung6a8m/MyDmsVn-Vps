#!/usr/bin/env python3
"""Static 57-route parity coverage inventory gate; NOT an HTTP acceptance test."""
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent
FROZEN = (
    ROOT / "docs/contracts/vps-01-route-inventory.json",
    ROOT / "docs/contracts/vps-02-route-additions.json",
)
LEDGER = ROOT / "docs/evidence/vps-04a-live-route-parity.md"
ROW = re.compile(
    r"^\| (WEB2|WEB3|WEB4|WEB5|WEB6|VPS-02) \| "
    r"\x60(GET|POST|PUT) ([^\x60 ]+)\x60 \| \x60([^\x60]+)\x60 \| "
    r"\x60(GROUP_EAD_SQL|PRIOR_LIVE_BOUNDED|DISABLED_404|ROUTE_ONLY)\x60 \|$"
)
EXPLICIT = {
    "GET /api/v1/auth/me",
    "GET /api/v1/auth/capabilities",
    "GET /api/v1/catalog/products/lookup",
    "GET /api/v1/sales-receipts",
    "GET /api/v1/sales-receipts/{saleId}",
    "GET /api/v1/goods-receipts",
    "GET /api/v1/goods-receipts/{receiptId}",
    "GET /api/v1/inventory/balances",
    "GET /api/v1/inventory/stock-card",
    "GET /api/v1/operations/context",
    "GET /api/v1/operations/{operationId}",
    "GET /api/v2/operations/context",
    "GET /api/v2/operations/{operationId}/resolution",
}
PREVIOUS = {
    "POST /api/v1/auth/login",
    "POST /api/v1/auth/refresh",
    "POST /api/v1/auth/logout",
    "POST /api/v1/sales-receipts",
    "PUT /api/v1/sales-receipts/{saleId}/draft",
    "POST /api/v1/sales-receipts/{saleId}/post",
    "POST /api/v1/goods-receipts",
    "PUT /api/v1/goods-receipts/{receiptId}/draft",
    "POST /api/v1/goods-receipts/{receiptId}/post",
    "GET /api/v1/health",
}


def group(path: str) -> str:
    if path.startswith("/api/v3/operations/"):
        return "WEB6"
    if path.startswith("/api/v2/operations/"):
        return "WEB5"
    if path.startswith("/api/v1/operations/"):
        return "WEB4"
    if path.startswith("/api/v1/goods-receipts") or path.startswith("/api/v1/inventory/"):
        return "WEB3"
    if path.startswith("/api/v1/security/") or path.startswith("/api/v1/foundation/"):
        return "VPS-02"
    if re.fullmatch(
        r"/api/v1/catalog/(?:products|warehouses|employees|customers)"
        r"(?:/\{id:int\}(?:/active)?)?", path
    ):
        return "VPS-02"
    return "WEB2"


def main() -> None:
    sources = []
    for file in FROZEN:
        doc = json.loads(file.read_text(encoding="utf-8-sig"))
        sources.extend(doc["endpoints"])
    if len(sources) != 57 or len({(r["httpMethod"], r["path"]) for r in sources}) != 57:
        raise SystemExit("FAIL: source inventory is not 57 unique routes")

    actual = {}
    for line in LEDGER.read_text(encoding="utf-8").splitlines():
        match = ROW.fullmatch(line)
        if not match:
            continue
        family, method, path, action, state = match.groups()
        key = (method, path)
        if key in actual:
            raise SystemExit("FAIL: duplicate route " + str(key))
        actual[key] = (family, action, state)
    if len(actual) != 57:
        raise SystemExit(f"FAIL: ledger has {len(actual)} route rows instead of 57")

    counts = {"GROUP_EAD_SQL": 0, "PRIOR_LIVE_BOUNDED": 0,
              "DISABLED_404": 0, "ROUTE_ONLY": 0}
    for route in sources:
        key = (route["httpMethod"], route["path"])
        full_key = " ".join(key)
        family = group(route["path"])
        state = (
            "GROUP_EAD_SQL" if full_key in EXPLICIT
            else "DISABLED_404" if family == "WEB6"
            else "PRIOR_LIVE_BOUNDED" if full_key in PREVIOUS
            else "ROUTE_ONLY"
        )
        expected = (family, route["controller"] + "." + route["action"], state)
        if actual.get(key) != expected:
            raise SystemExit(f"FAIL: {full_key}: {actual.get(key)} != {expected}")
        counts[state] += 1
    if counts != {"GROUP_EAD_SQL": 13, "PRIOR_LIVE_BOUNDED": 10,
                  "DISABLED_404": 3, "ROUTE_ONLY": 31}:
        raise SystemExit("FAIL: unexpected coverage counts")
    print("PASS: 57/57 ledger; 13 new bounded live, 10 prior bounded, "
          "3 WEB6 disabled, 31 route-only. No exhaustive response claim.")


if __name__ == "__main__":
    main()
