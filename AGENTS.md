# Repository instructions — MyDmsVn-Vps

These instructions are scoped ONLY to this repository. Do not inherit plans, tool-specific instructions, agents, active tasks, archived decisions, CI evidence, or release approvals from MyDmsVn or any other repository. Source links in [docs/import](docs/import/README.md) are immutable provenance, not operational dependencies.

## Authority and current stage

1. [docs/decisions/README.md](docs/decisions/README.md): the four APPROVED Owner decisions (OWN-VPS-01..03 on 2026-11-16; scoped OWN-VPS-04 on 2026-11-19). A future change requires a separate Owner decision.
2. [docs/roadmap/README.md](docs/roadmap/README.md): **only** live task-status authority; [detailed roadmap](docs/roadmap/vps-00-vps-09.md) holds scope and acceptance.
3. [docs/architecture/dependency-boundaries.md](docs/architecture/dependency-boundaries.md): distinguish existing dependency debt from the required future target.
4. [docs/testing/test-policy.md](docs/testing/test-policy.md): test scope, non-empty discovery, and evidence rules.
5. [docs/import/README.md](docs/import/README.md): pinned source provenance, not inherited acceptance.

## Non-negotiable execution rules

- Preserve current authentication, permissions, SQL transaction semantics, inventory and costing, idempotency, JSON wire behavior and schema compatibility until an explicit increment introduces changes with tests.
- VPS-00B is documentation, static path verification and CI only. Do **not** retarget `net48` production projects, replace DbUp/MediatR/Newtonsoft, remove the Local adapter or rename namespaces here.
- The desired Desktop `net48` to Server `net10.0` dependency cutover must occur in order: HTTP parity (VPS-02), Desktop cutover (VPS-03), Server replatform (VPS-04). Do not enforce future rules as if they already passed.
- Import/package references must resolve inside this Git tree, except external packages acquired from declared NuGet feeds. Never require a sibling checkout, git submodule, private mapped drive, upstream repository or machine-specific local package.
- For each PR: record base/head SHA, exact commands/environment, tests discovered/executed/skipped, failures, CI run URL and explicit blockers. A skipped SQL test or empty filter is NOT PASS.
- Make smallest reviewable changes; run CI on the final HEAD after findings are fixed. Never mark production-ready based on successful static validation or compilation alone.
- Linux runtime, SQL integration and attended Windows GUI tests require their own real environments. Do not invent evidence or silently accept exceptions.

Maintain root source under UTF-8/CRLF where required by repository attributes; respect existing encoding for vendor snapshots and generated files. Treat secrets as unavailable in CI unless explicitly provisioned; never print SQL connection strings or tokens.
