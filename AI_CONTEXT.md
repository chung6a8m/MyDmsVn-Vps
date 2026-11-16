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
