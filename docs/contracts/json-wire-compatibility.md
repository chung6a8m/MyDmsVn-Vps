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
