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
