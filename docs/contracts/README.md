# Frozen HTTP contract corpora — MyDmsVn-Vps

**Purpose:** canonical technical test data referenced directly by legacy HttpHost tests and future VPS-01/VPS-05 golden-master tests. These data files are not active planning documents, approvals or inherited CI evidence.

During VPS-00B the CI baseline exposed that VPS-00A omitted the test-owned corpora while excluding legacy active docs. The five directories below were copied **once**, preserving their original relative paths and content from immutable source commit `bac73004328884f5322c7c87d9787b8150885ac0`. They are now in-tree assets; the build/test never downloads them from the source repository.

| Corpus directory | Number of tracked source files | Role |
| --- | ---: | --- |
| `web2-v1/` | 34 | Auth, Catalog and Sales request/response cases |
| `web3-v1/` | 38 | Goods Receipt, Inventory, capabilities and strict validation |
| `web4-v1/` | 48 | Web operation status, idempotency and fingerprint scenarios |
| `web5-resolution-v1/` | 41 | Terminal operation resolution states |
| `web6-resend-eligibility-v1/` | 17 | Resend eligibility and receipt cases |
| **Total** | **178** | Frozen technical fixtures only |

The in-corpus READMEs and scripts belong to the original fixture format and may reference historical design concepts. They do **not** govern MyDmsVn-Vps. Refer to [independent JSON contract policy](json-wire-compatibility.md) and [test policy](../testing/test-policy.md).

**Change control:** keep these files immutable until an explicit contract-change increment documents a compatibility diff, test oracle and owner acceptance. Do not silently regenerate or rewrite snapshots. The root `docs/roadmap/README.md` remains the only live task-status authority.
