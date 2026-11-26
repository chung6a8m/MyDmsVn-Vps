# VPS-10.5B — Receipt writes

- Baseline: merged PR #39, main `a8a83e137bb194e523b439175fe8e51f641fb4f7`.
- Production source commit: `f4fda05c2fccfee95e2b16d51852bb8c9418cc58`.
- Migrate only Receipt Create, Update and Post to native MVC; 16 native / 41 bridge, 57 frozen verb/path pairs. Keep all 293 independent differential variants and 46 remote Desktop methods.
- Typed request bodies and boundary mapping; invariant decimal and calendar grammar, quantity > 0, unitCost >= 0, canonical eight-byte Base64 rowversion. Duplicate products remain an Application rule, not a transport rule.
- Extend the existing operation context/canonicalizer without changing stored codec, Application handlers, SQL schema or Desktop wire. Four frozen Receipt vectors; live outcome projection equality for all three mutations.
- 59 focused cases are required by Linux CI; existing Sales/Inventory and HTTP differential gates retained. The disposable SQL valuation oracle must still prove Receipt create/update/post, journal, inventory and costing.

## Acceptance status

SOURCE IMPLEMENTED; NOT VERIFIED. No .NET SDK, Windows runner or disposable SQL available to the editing session. Required CI, independent review and merge remain pending; predecessor CI is not current evidence. Receipt-specific SQL race, rollback and restart coverage must be proven before closeout. Record final SHA, checks, counts and merge receipt in implementation PR. No production or deployment approval.
