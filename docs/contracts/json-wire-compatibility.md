# JSON wire compatibility — contract freeze plan

**Stage:** VPS-01 inventories golden-master fixtures; VPS-05 performs serializer migration. None of the following is a current PASS claim.

Baseline Desktop/Server DTOs use Newtonsoft.Json converters and attributes. Changing Server.HttpHost to System.Text.Json may affect:

- property names/casing, missing versus explicit null, defaults and optional fields;
- decimal precision, trailing zero conventions, locale-independent roundtrip, `long` / int64 limits;
- date/time UTC offsets, parsing rejection and strict JSON request validation;
- enum/bool parsing and custom converters;
- unknown/duplicate property handling, success/error status body shape;
- ordering and byte-level operation fingerprints used for idempotency and transactional commands.

Build a frozen corpus per endpoint with known inputs, malformed payloads and expected JSON/status. Compare parse semantics and any byte-sensitive fingerprints across serializers. Do not silently rewrite historical hashes or conflate a JSON refactor with API behavior change. Desktop may retain Newtonsoft.Json behind its own HTTP adapter even after Server switches.

**Evidence requirement:** list corpus SHA, original host response, migrated response, test count and discrepancies approved intentionally. See [test matrix](../testing/acceptance-matrix.md).

## VPS-01 additions — baseline serializer oracles

`tests/MyDmsVn.Server.HttpHost.Tests/Vps01ContractFreezeTests.cs` freezes these exact **Newtonsoft.Json** outcomes in addition to the 178 unmodified WEB2–WEB6 technical corpus assets:

- HTTP 200, success envelope: `{"data":{"receiptId":"9007199254740993","quantity":"1.2500"}}`. The high int64 and four-decimal quantity are **strings** and the null `missing` property is omitted.
- HTTP 401, error envelope: `{"error":{"code":"Auth.Unauthorized","message":"Authentication is required.","details":[],"correlationId":"vps01-correlation"}}`. Transport status is out-of-band and is not embedded as an error property.
- Route signatures: all 29 current Web API 2 attribute routes must match `vps-01-route-inventory.json`; a route map is not a claim that all endpoints work at runtime.
- Corpus: all 178 local frozen WEB2–WEB6 file names remain pinned by `corpus-blobs.sha1`; the foundation static guard checks Git blob provenance.

These are representative regression tests and imported canonical vectors, **not** a new live-response capture from an isolated SQL host. Dynamic auth/session/cookie behavior, SQL-dependent side effects, real HTTP response captures and serializer parity with System.Text.Json still require explicit runtime gates in VPS-02 and VPS-05.
