# OWN-VPS-03 — Retain only Desktop .NET Framework 4.8

- **Status:** APPROVED
- **Decision date:** 2026-11-16
- **Chosen option:** 1 — Only Desktop net48
- **Owner authority:** direct Owner choice during the MyDmsVn-Vps interview

## Decision

Retain the Windows WinForms Desktop on `net48`. Do not ship or maintain `Desktop.AppCore` in the new product baseline.

## Consequences

- Existing Desktop net48/UI/WinFormsKit code stays on Windows; add a dedicated HTTP transport and feature clients.
- Replace the production Local runtime and all Desktop-to-Server/SQL dependencies only after API parity is demonstrated (VPS-02/VPS-03).
- Keep Desktop Newtonsoft.Json where appropriate while moving Server serialization separately (VPS-05).
- Split mixed Desktop/Server test dependencies before Server projects become `net10.0` (VPS-01/VPS-03/VPS-04).

Exclusion of Desktop.AppCore does not authorize deleting the current transitional Local adapter before the HTTP replacement has passed regression tests.
