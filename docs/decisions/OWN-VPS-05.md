# OWN-VPS-05 — Native ASP.NET Core 10 Host and coordinated Desktop net48 updates

- **Status:** APPROVED
- **Decision date:** 2026-11-26 (Asia/Bangkok)
- **Owner authority:** Direct approval of the proposed VPS-10 direction and request to record the roadmap in this repository.

> Tôi đồng ý với đề xuất trên. Desktop vẫn giữ target net48 nhưng có thể nâng cấp phù hợp theo Server. Bạn tạo roadmap và commit vào repo.

## Decision

Modernize the existing `net10.0` Host on the repository-pinned .NET SDK 10.0.404. Organize native ASP.NET Core controllers by feature using `ControllerBase` and `[ApiController]`. Use typed HTTP DTOs and ASP.NET Core input formatting at the HTTP boundary. Preserve culture-invariant machine wire behavior and apply local culture to Desktop display and user input.

Retain WinForms Desktop on `net48`. The Desktop HTTP adapter and UI can evolve in coordination with the Server, and Desktop can continue to use Json.NET. Verify compatibility for the Desktop build paired with each Host increment. The decision places no indefinite support obligation on older Desktop binaries.

## First wave

Preserve the existing WEB2–WEB6 and Desktop HTTP contracts across all 57 verb/path pairs, including the `/api/v1` routes and the `/api/v2` and `/api/v3` operation resolution and resend routes. Preserve existing financial precision, string encoding for the existing `Int64` wire fields, and the distinction between calendar dates and UTC instants. Keep existing null and error semantics, idempotency fingerprints, canonicalization, schema compatibility, authentication, permissions, and SQL transaction and business semantics. A separately versioned redesign of the business API or a change to these existing contracts requires a separate explicit decision.

## Scope of approval

The Owner approved the [VPS-10 target design](../architecture/native-aspnetcore10-host.md) and [implementation roadmap](../roadmap/vps-10-native-aspnetcore-host.md) at the design stage. Design status is `DESIGN_APPROVED` and implementation starts at `NOT_STARTED`. The [live roadmap status](../roadmap/README.md) tracks implementation progress. The documentation commit grants no implementation acceptance, test acceptance, deployment authorization, or release approval.

This decision supplements [OWN-VPS-03](OWN-VPS-03.md) within the existing Desktop and Server runtime targets. The narrow [OWN-VPS-04](OWN-VPS-04.md) exception remains scoped to its VPS-08/VPS-09 closeout candidate.
