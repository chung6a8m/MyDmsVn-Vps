# VPS-00A — One-time Source Import (independent)

The destination contains a new Git history and NO upstream remote.

| Component | Pinned snapshot |
| --- | --- |
| MyDmsVn | bac73004328884f5322c7c87d9787b8150885ac0 |
| MyDmsVn.WinFormsKit | 433e5b53f979e37cc6b69bad6dfcde0975c7ecaf |
| SourceGrid, nested kit dependency | f4e457b43582bf01892f50bdc74aa480531e5944 |

Imported: original legacy Domain, Application, Infrastructure, Desktop net48,
HttpHost, DbMigrator, SQL migrations 001–013, tests, samples, tooling,
deployment, and all local vendor sources/assets/licenses.
Import file counts (worktree): 872 code/test/tool/deployment files;
1401 WinFormsKit and SourceGrid vendor files.

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
