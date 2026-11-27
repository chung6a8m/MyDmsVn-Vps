# VPS-10.8 local acceptance evidence

The [roadmap index](../roadmap/README.md) records live status. This ledger records observations from 26–27 November 2026 (UTC+7) and the evidence still required for release.

| Identity | Value |
| --- | --- |
| Integration base | `9b0714b0cd66e1d1e13551cbbe54cb6631b55976` (merged [PR #47](https://github.com/chung6a8m/MyDmsVn-Vps/pull/47)) |
| Upgrade baseline A | `fdd04fea9089706a5394dcd71d1ecf1bc3a10565` |
| Upgrade baseline B | `4207e9bccaaa2ef45b04bf905b6c10bf3f1bcdd5` |
| Code and artifact SHA exercised in the lab | `25e4690318846180b35e13404745ab1644900300` |
| Windows Desktop archive SHA-256 | `ab658b7604c8d7c8aca355cede099b5c61f12139b42412b680b19e69930867e5` (39 verified files) |
| Linux Native Host archive SHA-256 | `974076fb9034c75e2692306d48688f00a47ac9e463eaa98c2786b7e67e247430` (61 verified files) |

The physical run used attended Windows Desktop net48 x86, Ubuntu 22.04.2 on WSL2, verified Kestrel HTTPS on 50444, Nginx HTTPS on 50445, and isolated SQL Server 2022 Developer schema 13. Upgrade lanes used separate SQL clones. The operator entered credentials. Computer Use recorded UI observations, and a read-only SQL principal verified business effects. See the [sanitized run receipt](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45#issuecomment-7198121614), [Desktop artifact](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45858022424/artifacts/15135574581), and [Host artifact](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45858028256/artifacts/15137125998).

## G5 attended paired handoff

| Check | Observation on `25e4690` |
| --- | --- |
| G5-01 | Trusted HTTPS through Kestrel and Nginx returned 200. Desktop signed in against the verified Host. |
| G5-02 | Sign out succeeded after Recovery with the Sales tab open. SQL confirmed revocation of the same writer session. |
| G5-03 | Catalog opened in Desktop and displayed the fixture product. |
| G5-04 | Sales draft was created, edited, posted and reopened as Posted. SQL verified sale and cost amounts. |
| G5-05 | Goods receipt was created, edited, posted and reopened as Posted. SQL verified unit cost and receipt value. |
| G5-06 | Desktop stock and stock card agreed with SQL quantity, movement and valuation. |
| G5-07 | The limited actor received access denial. HTTPS probes returned 403 and SQL recorded zero journal entries for the attempt. |
| G5-08 | A lost Sales.Post acknowledgement left a durable pending record through Host restart. Recovery returned COMMITTED, cleared the matching fence, and SQL showed one movement and one committed journal entry. |
| G5-09 | A read-only SQL principal verified weighted costing and inventory. Deliberate replay returned the same outcome with one set of SQL effects. |
| G5-10 | OpenAPI returned 404 in Production configuration and 200 in Development. All 57 native routes matched the inventory, including 12 integer constraints. |

## Paired upgrade with durable pending

The [earlier receipt](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45#issuecomment-7196684613) records A → B, including the U05 matrix and the same expired U06 receipt on both baselines. The [candidate receipt](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45#issuecomment-7198121614) records B → `25e4690`. The live-session Post permission revocation was exercised on the latter path.

| Check | Verified result |
| --- | --- |
| U01 | Losing the acknowledgement left one durable pending intent, one committed SQL write and one movement. |
| U02 | The artifact cutover preserved origin, SQL/schema, deployment epoch, audit identity, Windows SID and pending ciphertext. |
| U03 | The original actor read the matching committed result and released the fence. SQL recorded no second business write. |
| U04 | Revoking Post authorization in a live session yielded 403 for the outcome and retained the fence. Restoring permissions enabled reconciliation. Other actor, revoked session and mismatched fingerprint checks preserved the fence. |
| U05 | All four real-SQL branches retained UNKNOWN and the fence for NOT_OBSERVED, RESULT_EXPIRED, RESULT_UNAVAILABLE and INDETERMINATE across the paired observations. There were eight Host/Desktop observations, zero failures, zero skips and zero business writes. |
| U06 | An expired B-signed receipt returned DENY / RECEIPT_EXPIRED on B, on the candidate and after its restart. Known committed results, malformed or absent signatures, and unsafe audit permissions failed closed as expected. |

G5 recorded 10/10 successful checks. U01–U04 recorded 4/4 with zero failures or skips. U06 recorded one end-to-end run with its required checks. The receipt records cleanup of the owned upgrade clones and preservation of the independent 50443 pending record.

The artifact-pair tests used the disposable upgrade lanes on 50446–50448. The receipt does not establish an end-to-end execution of the new 50443 operator commands `PlanUpgrade`, `Upgrade`, `VerifyUpgrade` or `RollbackUpgrade`. Runtime acceptance of that harness remains outstanding.

## Diagnostics, draft recovery and review scope

When refresh failed before Save could be sent, the same actor recovered the unsent draft in the same Desktop process. No automatic Sales write occurred. One manual Save created one POST, one draft and one committed journal entry. Recovery after an application exit or crash remains unverified. Keep Issue #45 open and apply its recurrence criteria if a draft is lost or corrupted, Save replays automatically, SQL shows duplicate or incorrect effects, or explicit Sign out fails to revoke the exact session. The original `d90b90c` incident lacks a correlated refresh or transport trace. Further fault simulations for that historical incident are paused. [Draft recovery](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45#issuecomment-7208778114) · [Single manual Save](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45#issuecomment-7208813838) · [Residual risk and recurrence criteria](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45#issuecomment-7211085001).

Review the complete base-to-candidate diff, including isolation and rollback in the local simulation harness, redaction in Serilog and SQL diagnostics, cleanup and retry paths, Desktop logout/session ordering, and unsent draft ownership. The tested branch includes the tooling commits also proposed in [PR #48](https://github.com/chung6a8m/MyDmsVn-Vps/pull/48) and [PR #49](https://github.com/chung6a8m/MyDmsVn-Vps/pull/49). Reconcile that overlap before integration and review the exact final diff.

## CI and outstanding release evidence

GitHub recorded 11/11 successful workflow conclusions for the lab code SHA. The Linux workflow executed the frozen differential **293/293** without skips, Windows ran **281/281** Desktop session cases and **38/38** focused typed-wire cases without skips, and the disposable SQL workflow passed its **16/16** acceptance gate. The migration SQL workflow recorded **58/58**, zero failures or skips. Record CI for the final documentation-inclusive HEAD in the PR. [CI Linux](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45858051411) · [CI Windows](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45858033746) · [CI disposable SQL](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45858045417) · [CI migration SQL](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45858075180).

| Gate | Evidence still required |
| --- | --- |
| Protected SQL and VPS-08 staging | `approved-linux-sql` and `protected-staging` were **SKIPPED** on `25e4690`. The protected environment still needs the VPS08-01..08 deployment, reboot, fault rollback and reconciliation receipts. [VPS-08 workflow](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45858028256) · [Issue #11](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11). |
| VPS-09 protected integrated acceptance | Protected jobs were **SKIPPED** on `25e4690`. Obtain the exact-identified candidate's 293 real-SQL HTTPS variants, 12 Oracle scenarios, attended Windows x86 Desktop to deployed Linux Host and SQL, and recovery, rollback and cleanup receipts. [VPS-09 workflow](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45858080622) · [Issue #12](https://github.com/chung6a8m/MyDmsVn-Vps/issues/12). |
| Release authority | A signed Owner release decision and any production rollout authorization remain outstanding. The [OWN-VPS-04 exception](../decisions/OWN-VPS-04.md) applies only to the candidate pinned for Issue #12. [OWN-VPS-05](../decisions/OWN-VPS-05.md) approves the design. |
