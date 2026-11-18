#!/usr/bin/env python3
"""Validate the executable VPS-09 live scenario manifest.

This validates source policy only.  It never performs HTTP or SQL work and
must never be reported as protected staging or release evidence.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from typing import Any


REPOSITORY = "chung6a8m/MyDmsVn-Vps"
SHA256 = re.compile(r"^[0-9a-f]{64}$")
METHODS = {"GET", "POST", "PUT", "DELETE", "PATCH"}
ACTORS = {"anonymous", "writer", "denied", "operator"}
SECRET_KEY = re.compile(
    r"password|secret|token|connection.?string|private.?key|credential",
    re.IGNORECASE,
)


class ManifestError(ValueError):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise ManifestError(code)


def load_json(path: Path) -> Any:
    def unique_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            require(key not in result, "DuplicateJsonMember:" + key)
            result[key] = value
        return result

    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_pairs)


def assert_secret_free(value: Any) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            require(SECRET_KEY.search(key) is None, "SecretMaterialForbidden:" + key)
            assert_secret_free(child)
    elif isinstance(value, list):
        for child in value:
            assert_secret_free(child)


def checked_source_path(root: Path, relative: str) -> Path:
    require(isinstance(relative, str) and relative != "", "SourcePathMissing")
    candidate = (root / relative).resolve()
    try:
        candidate.relative_to(root.resolve())
    except ValueError as error:
        raise ManifestError("SourcePathTraversal") from error
    require(candidate.is_file(), "SourcePathMissing:" + relative)
    return candidate


def validate_manifest(root: Path, manifest_path: Path) -> dict[str, int]:
    root = root.resolve()
    document = load_json(manifest_path)
    require(isinstance(document, dict), "ManifestObjectRequired")
    assert_secret_free(document)
    require(document.get("schemaVersion") == 1, "SchemaVersionUnsupported")
    require(document.get("repository") == REPOSITORY, "RepositoryMismatch")

    frozen_path = checked_source_path(root, document.get("frozenObservations", ""))
    frozen = load_json(frozen_path)
    require(isinstance(frozen, list) and len(frozen) == 293, "FrozenObservationCount")
    frozen_pairs = {(row.get("CaseId"), row.get("Variant")): row for row in frozen}
    require(len(frozen_pairs) == 293, "FrozenObservationPairDuplicate")

    registry_path = checked_source_path(root, "docs/testing/vps-09-gates.json")
    registry = load_json(registry_path)
    registered_oracles = set(registry.get("oracleScenarios", []))
    manifest_oracles = document.get("oracleScenarios")
    require(
        isinstance(manifest_oracles, list)
        and len(manifest_oracles) == 12
        and set(manifest_oracles) == registered_oracles,
        "OracleRegistryMismatch",
    )

    routes = document.get("routes")
    require(isinstance(routes, list) and len(routes) == 57, "RouteCountMismatch")
    route_map: dict[str, dict[str, Any]] = {}
    for route in routes:
        require(isinstance(route, dict), "RouteObjectRequired")
        case_id = route.get("caseId")
        require(isinstance(case_id, str) and case_id != "", "RouteCaseMissing")
        require(case_id not in route_map, "RouteCaseDuplicate:" + case_id)
        require(route.get("method") in METHODS, "RouteMethodInvalid:" + case_id)
        require(
            isinstance(route.get("pathTemplate"), str)
            and str(route["pathTemplate"]).startswith("/api/"),
            "RoutePathInvalid:" + case_id,
        )
        route_map[case_id] = route
    require(set(route_map) == {pair[0] for pair in frozen_pairs}, "RouteCoverageMismatch")

    scenarios = document.get("scenarios")
    require(isinstance(scenarios, list) and len(scenarios) == 293, "ScenarioCountMismatch")
    seen: set[tuple[str, str]] = set()
    for scenario in scenarios:
        require(isinstance(scenario, dict), "ScenarioObjectRequired")
        pair = (scenario.get("caseId"), scenario.get("variant"))
        require(all(isinstance(item, str) and item for item in pair), "ScenarioPairMissing")
        require(pair not in seen, "ScenarioPairDuplicate:" + ":".join(pair))
        seen.add(pair)
        require(pair in frozen_pairs, "ScenarioPairUnknown:" + ":".join(pair))
        route = route_map.get(pair[0])
        require(route is not None, "ScenarioRouteUnknown:" + pair[0])
        expected = scenario.get("expected")
        require(isinstance(expected, dict), "ScenarioExpectedMissing:" + ":".join(pair))
        require(expected.get("status") == frozen_pairs[pair].get("Status"), "ScenarioStatusMismatch")
        require(expected.get("bodyRule") == "independent-oracle-digest", "ScenarioBodyRuleInvalid")
        require(SHA256.fullmatch(str(expected.get("contractBodySha256", ""))) is not None,
                "ScenarioContractDigestInvalid")
        request = scenario.get("request")
        require(isinstance(request, dict), "ScenarioRequestMissing:" + ":".join(pair))
        require(request.get("method") == route["method"], "ScenarioRequestMethodMismatch")
        require(request.get("pathTemplate") == route["pathTemplate"], "ScenarioRequestPathMismatch")
        require(request.get("actor") in ACTORS, "ScenarioActorInvalid")
        require(isinstance(request.get("bodyRecipe"), str), "ScenarioBodyRecipeMissing")
        require(isinstance(request.get("headerRecipe"), str), "ScenarioHeaderRecipeMissing")
        require(isinstance(request.get("preconditions"), list) and request["preconditions"],
                "ScenarioPreconditionMissing")
        require(isinstance(request.get("faultPhase"), str), "ScenarioFaultPhaseMissing")
        require(scenario.get("oracleScenario") in registered_oracles, "OracleScenarioUnknown")
        require(scenario.get("cleanup") == "owned-fixture", "ScenarioCleanupInvalid")

    require(seen == set(frozen_pairs), "ScenarioCoverageMismatch")
    return {"routes": len(routes), "scenarios": len(scenarios),
            "oracleScenarios": len(manifest_oracles)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["validate"])
    parser.add_argument("--root", default=str(Path(__file__).resolve().parents[1]))
    parser.add_argument("--manifest", default="docs/testing/vps-09-live-scenarios.json")
    args = parser.parse_args()
    try:
        root = Path(args.root).resolve()
        stats = validate_manifest(root, Path(args.manifest).resolve())
        print(
            "VPS09_LIVE_MANIFEST_PASS: "
            f"routes={stats['routes']} scenarios={stats['scenarios']} "
            f"oracles={stats['oracleScenarios']}; NOT live SQL or release evidence"
        )
        return 0
    except (ManifestError, OSError, ValueError, TypeError, KeyError) as error:
        print("VPS09_LIVE_MANIFEST_FAIL_CLOSED: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
