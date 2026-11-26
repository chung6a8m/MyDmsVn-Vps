# Owner decision register — MyDmsVn-Vps

Initial decision date: **2026-11-16**. These are explicit Owner choices recorded independently in this repository. The Owner approved the narrowly scoped OWN-VPS-04 closeout exception on 2026-11-19 and the OWN-VPS-05 modernization direction on 2026-11-26.

| ID | Status | Decision | Record |
| --- | --- | --- | --- |
| OWN-VPS-01 | APPROVED | Linux VPS deployment for HTTP host | [OWN-VPS-01](OWN-VPS-01.md) |
| OWN-VPS-02 | APPROVED | SQL Server; both existing and fresh database paths | [OWN-VPS-02](OWN-VPS-02.md) |
| OWN-VPS-03 | APPROVED | Keep only Desktop net48 | [OWN-VPS-03](OWN-VPS-03.md) |
| OWN-VPS-04 | APPROVED | Single-Owner VPS-08/VPS-09 closeout exception | [OWN-VPS-04](OWN-VPS-04.md) |
| OWN-VPS-05 | APPROVED | Native ASP.NET Core Host on .NET 10 with existing HTTP contract compatibility and coordinated Desktop net48 updates | [OWN-VPS-05](OWN-VPS-05.md) |

Approval of OWN-VPS-01..03 does **not** approve any untested implementation, migration plan, host topology details, release or exception. OWN-VPS-04 changes only the human-separation rule for one Issue #12 closeout candidate and grants no runtime or production approval by itself. OWN-VPS-05 supplements OWN-VPS-03 and approves the VPS-10 design and roadmap. Implementation acceptance, test evidence and deployment authorization require their respective gates. Any future superseding decision needs its own ID, rationale, date, migration implications and explicit Owner approval. The [roadmap](../roadmap/README.md) tracks execution, not this register.
