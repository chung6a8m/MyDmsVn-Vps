# VPS-10 — Native ASP.NET Core Host & Typed HTTP Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Repository instructions and the Owner's current task scope take precedence. This document plans future implementation; the current authorization is to record and commit the roadmap.

**Goal:** Thay HTTP compatibility bridge bằng native ASP.NET Core 10, DTO có kiểu và invariant wire boundary; Desktop được nâng cấp đồng bộ nhưng giữ `net48`.

**Architecture:** ControllerBase theo tính năng nhận typed request, gọi generated Mediator và trả typed response. Native formatter/binder xử lý giao thức; Application giữ permission/business/transaction semantics. Bảo toàn toàn bộ hợp đồng WEB2–WEB6/Desktop, canonical operations và stored outcomes trong quá trình chuyển từng route.

**Tech Stack:** ASP.NET Core `net10.0`, SDK `10.0.404`, System.Text.Json, existing Mediator 3.x/SQL infrastructure; WinForms `net48`, existing Json.NET adapter; xUnit v3/VSTest và các workflow hiện hữu.

**Spec:** [Thiết kế đích](../architecture/native-aspnetcore10-host.md), [OWN-VPS-05](../decisions/OWN-VPS-05.md), được Owner chấp thuận ngày 2026-11-26 (Asia/Bangkok). Source baseline `6a9649430cfd8ff4576d5e351f4ef6d0080011a0`.

## Global Constraints

- Server `net10.0` / SDK `10.0.404`; Desktop `net48`. Giữ SDK Windows hiện hữu cho graph net48. Không đổi package ngoài dependency trực tiếp cần cho increment; update lock có review nếu phải thêm.
- Desktop có thể đổi adapter/DTO/UI đồng bộ với Server; không có yêu cầu hỗ trợ vô thời hạn binary Desktop cũ. Mỗi PR ghi build pair và bằng chứng tương thích. Pending operations/replay vẫn cần đọc được sau upgrade.
- Giữ **57 cặp verb/path** gồm 52 `/api/v1`, 2 `/api/v2`, 3 `/api/v3`; giữ các feature-enabled/disabled paths. Không nhầm existing operation v2/v3 với một business API redesign mới.
- Giữ financial decimal và int64 string wire tại những field hiện hữu dùng text; Security `expectedRevision`/`expectedTargetRevision` vẫn là JSON integer/int64. Giữ field-specific precision/scale, calendar date/UTC, null/presence, error/status/header/cookie, query grammar và ordering quan sát được.
- Không đổi SQL schema, auth/permission, transaction, inventory/valuation, idempotency identity/hash, cursor hash hoặc operation result format. Không tái tính hash lịch sử hay xóa pending state để vượt test.
- Giữ immutable source-independent corpus/oracle; tests mới bổ sung hành vi native, không regenerate expected từ implementation mới. Legacy net48 host vẫn dùng cho regression/differential.
- Production net10 closeout không source-link legacy HTTP code, không façade OWIN/Web API, WireDom hoặc reflection action dispatch. Narrow protocol canonical reader được phép có lý do rõ ràng.
- [Roadmap index](README.md) là nguồn trạng thái duy nhất; checkboxes dưới đây là các bước kế hoạch, không phải ledger PASS. Evidence nằm trong PR của chính increment và `docs/evidence/`, không tạo chuỗi PR chỉ ghi trạng thái.
- Tuân thủ [AGENTS](../../AGENTS.md), [test policy](../testing/test-policy.md), UTF-8 không BOM cho Markdown và CRLF khi checkout; Git lưu text chuẩn hóa LF.
- VPS-10 phát triển được bằng DEV/disposable gates. Không phải đợi VPS-08/09 production closeout để bắt đầu; không dùng VPS-10 để thay đổi hoặc đóng các nghĩa vụ vận hành đó.

## Review Focus

| Input/condition dễ bị bỏ sót | Hành vi cần giữ | Task/test owner |
| --- | --- | --- |
| Unknown/duplicate query sau decoding, body trên GET, malformed UTF-8 và content-type | Cùng rejection/status/field path trước side effects như baseline | 10.0, 10.2 `Vps10NativeCatalogTests`, 10.4A |
| Missing/null/false, quoted int, decimal vượt scale, Sales/Receipt Base64 khác nhau | Không default/coerce/round thành dữ liệu hợp lệ; không áp profile chung làm đổi contract | 10.3, 10.4B, 10.5A/B |
| Cùng request dưới vi-VN/en-US/de-DE và timezone khác | Wire/numeric semantics và ngày chứng từ ổn định; UI vẫn nhập/hiển thị đúng | 10.3, 10.4A/B, 10.5A |
| Mất ACK, app restart, kết quả cũ hết hạn/unavailable, user mất Read | Không ghi lại ngoài policy; replay/outcome lookup đúng, không dựng lại từ GET hiện tại | 10.4B, 10.5B, 10.6D |
| Mixed native/legacy, invalid bearer trên health, request fail sau khi chuẩn bị cookie | Một route owner; admission/order/404/405/headers/correlation không bị framework defaults đổi | 10.2, 10.6C/D, 10.7 |

## 1. File map và interface dùng chung

Các prefix dưới đây chỉ là cách viết gọn đường dẫn; file ghi **Create** là file dự kiến, không phải file đã được triển khai:

| Prefix | Đường dẫn |
| --- | --- |
| `H/` | `src/MyDmsVn.Server.AspNetCoreHost/` |
| `L/` | `src/MyDmsVn.Server.HttpHost/` — đọc làm baseline, giữ net48 oracle |
| `HT/` | `tests/MyDmsVn.Server.AspNetCoreHost.Tests/` |
| `D/` | `src/MyDmsVn.Desktop.Infrastructure.Http/` |
| `DT/` | `tests/MyDmsVn.Desktop.Session.Tests/` |
| `WT/` | `tests/MyDmsVn.Desktop.Tests/` |

| Interface đích | Chủ sở hữu và ý nghĩa |
| --- | --- |
| `AddVps10HostServices(IServiceCollection, IConfiguration)` | 10.1: composition/config; giữ tên env vars và startup semantics |
| `HostStartupChecks.ValidateAsync(IServiceProvider, CancellationToken)` | 10.1: gọi hoàn tất trước `RunAsync`, không đưa validation thành background task sau listen |
| `V1HttpJson.Configure(JsonSerializerOptions)` | 10.2: JSON profile cho native boundary; field converter vẫn explicit |
| `V1ApiResults.Map<TSource,TResponse>(ErrorOr<TSource>, Func<TSource,TResponse>, string correlationId)` trả `IActionResult` | 10.2: giữ success/error envelope, status và field path; tái dùng Application mapping |
| `V1ModelStateErrors.Create(ActionContext)` trả `IActionResult` | 10.2: map binder/JSON/validation failures về envelope cũ |
| `V1OperationCanonicalizer.Canonicalize(WebOperationKind, long? targetId, JsonElement validatedSource)` trả `string` | 10.4B: canonical intent tương đương algorithm/version hiện có; không serializer DTO |
| `IV1OperationContextFactory.CreateAsync(HttpContext, WebOperationKind, long? targetId, CancellationToken)` trả `ValueTask<WebOperationContext?>` | 10.4B: header/epoch/path/session và feature flags; dùng validated source request-scoped, bounded/disposed |
| `V1OperationResultCodec : IWebOperationResultCodec` | 10.6D: giữ `Encode<T>`/`Decode<T>` interface Application hiện có và replay validation |

Ở 10.4B/10.5B, operation factory vẫn cấp `WebOperationResultCodec` source-linked hiện hữu cho WebOperationContext. 10.6D mới chuyển đồng thời factory của writes và các recovery readers sang native codec; required test chứng minh legacy-produced outcome vẫn đọc được và fresh/persisted/replayed payload tương đương.

Tên và signature trên là quyết định kỹ thuật cho implementation, không thêm business API. Thay interface trong PR phải cập nhật các task phụ thuộc và test cùng PR; không tạo abstraction rộng hơn nhu cầu này.

## 2. PR sequence và route ownership

| PR increment | Deliverable | Phụ thuộc | Route native thêm |
| --- | --- | --- | --- |
| VPS-10.0 | Baseline manifest, contract characterization, native migration test plan | Approved design | 0 |
| VPS-10.1 | Host composition/options/startup cleanup | 10.0 | 0 |
| VPS-10.2 | Catalog lookup pilot, native MVC + mixed inventory + error/OpenAPI foundation | 10.1 | 4 |
| VPS-10.3 | Desktop typed wire DTO/codec, bỏ response reparse | 10.0; nên sau 10.2 để dùng paired native pilot | 0 |
| VPS-10.4A | Sales list/get native | 10.2 | 2 |
| VPS-10.4B | Sales create/update/post và canonical operation context | 10.3, 10.4A | 3 |
| VPS-10.5A | Receipt reads và Inventory reads native | 10.3, 10.4A | 4 |
| VPS-10.5B | Receipt writes native | 10.4B, 10.5A | 3 |
| VPS-10.6A | Catalog CRUD native | 10.2, 10.3 | 20 |
| VPS-10.6B | Security và Foundation native | 10.6A | 8 |
| VPS-10.6C | Auth và Health native | 10.6B | 6 |
| VPS-10.6D | Operation context/status/resolution/resend native | 10.4B, 10.5B, 10.6C | 7 |
| VPS-10.7 | Gỡ bridge, complete OpenAPI, paired Desktop/Host handoff | Tất cả increment trước | 0; tổng native 57 |

Count là số verb/path, không phải số controller. 10.5A gồm 2 receipt reads + 2 inventory reads. 10.6D gồm 2 v1 + 2 v2 + 3 v3 operation routes. Route assignments phải được 10.0 kiểm tra lại từ frozen inventories trước code; nếu main thêm feature hợp lệ sau baseline, ghi delta được review và mở rộng manifest/test, không reset về 57 để xóa feature.

Mỗi hàng là PR độc lập; 20 catalog CRUD routes có thể chia tiếp theo entity nếu diff lớn, nhưng giữ một owner register. Không gộp Auth/Operations vào PR dọn tên/thư mục. 10.3 và các read slices có thể làm song song sau khi shared interface ổn định; branch phải rebase và kiểm chứng HEAD tích hợp trước merge.

## 3. Các increment

### VPS-10.0 — Baseline và contract characterization

**Files:** Create `docs/contracts/vps-10-route-migration.json`, `HT/Vps10ContractCharacterizationTests.cs`, `docs/evidence/vps-10-native-host.md`. Read frozen `docs/contracts/vps-01-route-inventory.json`, `vps-02-route-additions.json`, `vps-05c-desktop-route-net48.json`, `HT/Vps05cDesktopRouteLiveParityTests.cs`, `H/Net10RouteTable.cs`.

**Produces:** Manifest versioned gồm logical route ID `(verb, template)`, order, baseline controller/action, actual active owner, owning increment, contract family, feature flags, required positive/negative cases. Bổ sung metadata, không sửa frozen inventories.

- [ ] Chạy baseline G0/G1/G2 trước thay đổi, ghi SHA/OS/SDK/counts thực tế; các baseline chưa chạy ghi rõ NOT RUN.
- [ ] Viết test manifest: `Assert.Equal(57, uniquePairs.Count)` và phân bố `52/2/3`; mỗi route có owner và increment; duplicate/missing pair làm test fail.
- [ ] Viết characterization cases thiếu/trùng/lạ field, raw query grammar, error precedence, signed/unsigned numeric scale, null và Base64 khác nhau. Existing behavior đã đúng thì test PASS ngay; không sửa code để tạo red giả.
- [ ] Lập coverage matrix raw HTTP / canonical bytes / Desktop / SQL side effect, pin nguồn expected độc lập. Ghi current bridge debt và schema của manifest.
- [ ] Chạy G0/G1/G2; review dữ liệu baseline, commit `test: characterize VPS-10 HTTP migration baseline` cùng evidence. Không chuyển endpoint trong PR này.

### VPS-10.1 — Composition, configuration và startup checks

**Files:** Modify `H/Program.cs`, `H/CoreRequestServices.cs`, `H/GeneratedMediatorRegistration.cs`. Create `H/Composition/HostServiceCollectionExtensions.cs`, `H/Configuration/HostOptionsRegistration.cs`, `H/Hosting/HostStartupChecks.cs`, `HT/Vps10HostCompositionTests.cs`.

**Consumes/produces:** Existing `WebHostOptions`, session/proxy configuration và Application registrations; xuất hai startup interfaces ở section 1. Giữ `--list-routes` SQL-free và không cần deployment secrets.

- [ ] Viết test sai schema/principal/superuser/config/audit làm startup fail trước listener; production `ValidateScopes=true`; identity/correlation không leak giữa request.
- [ ] Thực hiện extraction nhỏ, giữ env names/defaults/flags và pipeline order. Proxy admission trước forwarded headers; routing trước Core API admission; giữ generated Mediator scoped và behavior order.
- [ ] Chạy G1/G2, existing CoreApiBoundary/Loopback/Mediator tests; kiểm source không retarget net48 hoặc đổi lock ngoài scope.
- [ ] Review diff, record evidence và commit `refactor: organize ASP.NET Core host composition`.

### VPS-10.2 — Catalog lookup native pilot và mixed-route boundary

**Files:** Create `H/Features/Catalog/CatalogLookupController.cs`, `H/Features/Catalog/CatalogLookupQuery.cs`, `H/Http/Binding/V1QueryPolicy.cs`, `H/Http/Serialization/V1HttpJson.cs`, `H/Http/Errors/V1ApiResults.cs`, `H/Http/Errors/V1ModelStateErrors.cs`, `H/Composition/NativeApiRegistration.cs`, `H/Composition/EndpointInventory.cs`, `HT/Vps10NativeCatalogTests.cs`, `HT/Vps10EndpointInventoryTests.cs`. Modify `H/Net10RouteTable.cs`, `H/Program.cs`, host package/lock only for pinned net10 OpenAPI, `scripts/verify-vps-04a.py`, relevant HTTP test factories.

**Consumes/produces:** 10.0 manifest; section 1 JSON/results interfaces; four existing lookup routes, actual EndpointDataSource inventory và scoped native OpenAPI. Native controllers được MVC discover riêng; Legacy definitions không bị MVC vô tình activate.

- [ ] Viết red tests gọi lookup native qua Kestrel: required `limit=1..200`; search encoding; malformed/duplicate/unknown query; GET body; auth/permission; cùng status/envelope như baseline. Bốn lookup routes vẫn khả dụng khi `DesktopApiEnabled=false` vì baseline dùng `Execute`, không `ExecuteDesktop`.
- [ ] Viết inventory negative tests thiếu owner, hai active handlers, sai order/verb; export cả compatibility identity và actual native type/action, kiểm actual endpoints khớp manifest.
- [ ] Đăng ký MVC/input formatter/result mapping và native controller. Map mỗi route đúng một owner; bridge bỏ dispatch các route này. Giữ 404/405 + `Allow` và admission trên unmatched API path.
- [ ] Chuẩn bị endpoint metadata/resource gate cho additive Desktop routes: sau transport/auth admission nhưng trước native model binding. Viết focused test disabled + malformed payload trả baseline 404, enabled + cùng payload đi tiếp tới validation; không dùng flag này cho legacy lookup routes.
- [ ] Tích hợp OpenAPI cho native subset, test schema/runtime agreement và generation không cần SQL/secrets. Giữ các application query handlers.
- [ ] Chạy G1/G2/G3 cho lookup và paired Desktop nếu adapter đổi; review/commit `refactor: migrate catalog lookups to native ASP.NET Core`.

### VPS-10.3 — Desktop typed HTTP DTO và codec

**Files:** Create `D/Wire/SalesWireDtos.cs`, `D/Wire/ReceiptWireDtos.cs`, `D/Wire/V1JsonConverters.cs`, `D/Wire/V1WireMapper.cs`, `DT/Vps10TypedWireTests.cs`. Modify `D/HttpDesktopWire.cs`, `D/HttpDesktopTransport.cs`, `D/HttpFeatureClients.cs`, `D/HttpDesktopOperations.cs`; extend `DT/Vps03HttpAdapterTests.cs`, `WT/ReceiptNumericInputTests.cs`, `WT/DesktopDateInputIntegrationTests.cs` khi liên quan.

**Produces:** Typed request/response objects và explicit mapping cho `saleId`, `receiptId`, `receiptLineId`; decimal/long typed values trong adapter, serialized wire theo corpus. Same codec dùng cho live response và replay.

- [ ] Viết red test nhận GoodsReceipt v1 vào typed response, map ID/lines/null/decimal đúng mà không parse/rename/serialize JSON; kiểm cả replay response path.
- [ ] Viết cross-culture vectors `vi-VN`, `en-US`, `de-DE`, invariant; max Int64 `9223372036854775807`, `9007199254740993`, 4/8-place decimals, signed profit, date-only và UTC.
- [ ] Thay DOM construction/reparse bằng Newtonsoft DTO metadata/converters trong HTTP adapter, giữ transport/session/retry và pending store format. Không đổi shared DTO khiến legacy host/oracle thay behavior.
- [ ] Giữ UI culture policy hiện có khi không có yêu cầu UX mới; không dùng global replace dấu phẩy/chấm. Date editor hiện hữu không tự đổi format chỉ vì thêm DateOnly tại Server.
- [ ] Chạy G3 và focused Windows UI tests; chứng minh request/fingerprint không đổi bằng canonical corpus; review/commit `refactor: use typed Desktop HTTP wire DTOs`.

### VPS-10.4A — Sales list/get native

**Files:** Create `H/Features/Sales/SalesReceiptsController.cs`, `H/Features/Sales/SalesListQuery.cs`, `H/Features/Sales/SalesResponseDtos.cs`, `H/Features/Sales/SalesHttpMapper.cs`, `H/Http/Binding/V1ScalarBinders.cs`, `HT/Vps10SalesReadTests.cs`. Read `L/SalesHttpRequests.cs`, `L/SalesWireInput.cs`, `L/SalesWireOutput.cs`; update active owner manifest.

**Consumes/produces:** Native boundary từ 10.2; typed list query/detail response. DateOnly chỉ ở host-owned DTO nếu dùng; map calendar sang Application DateTime có chủ đích.

- [ ] Red tests query default pageSize **25**, bound **1..200**, calendar exact format, cursor filter/pageSize binding, invalid route ID trả đúng status và no-query/no-body detail.
- [ ] Tests response bảo toàn Sales null omission, represented decimal scale, int64 text, một giá vốn có đủ 8 chữ số thập phân (không ép mọi cost phải pad 8), duplicate-product lines và permissions.
- [ ] Chuyển hai GET qua native DTO/binder, không thay Application pagination/cursor algorithm. Thêm schema cho DTO thực tế.
- [ ] Chạy G1/G2/G3 và targeted read SQL gate khi dữ liệu/query integration thay; review/commit `refactor: migrate Sales reads to native controllers`.

### VPS-10.4B — Sales mutations và canonical operation context

**Files:** Create `H/Features/Sales/SalesRequestDtos.cs`, `H/Http/Serialization/V1ScalarJsonConverters.cs`, `H/Http/Binding/V1ValidatedRequestSource.cs`, `H/Http/Operations/V1OperationCanonicalizer.cs`, `H/Http/Operations/V1OperationContextFactory.cs`, `HT/Vps10SalesWriteTests.cs`, `HT/Vps10CanonicalOperationTests.cs`. Modify Sales controller/mapper; read `L/StrictJsonRequest.cs`, `L/WebOperationWireInput.cs`; update manifest.

**Consumes/produces:** Section 1 canonicalizer/factory interfaces, typed body with exact presence/type rules. Source capture stays request-scoped, bounded/disposed and available to canonicalizer without double-consuming the formatter stream.

- [ ] Red HTTP tests required fields including `false`, null array elements, 1..200 lines, create line ID `"0"`, int tokens, decimal lexical >4 places, Sales 8-byte Base64 behavior; invalid requests never enter mutation handler.
- [ ] Assert canonical UTF-8 bytes/hash equal frozen `web4-v1` vectors for equivalent decimals, note omitted/null, escaped text/U+2028/U+2029, property order, rowversion normalization; changed line order/target must follow existing different-intent rules.
- [ ] Implement native Create/Update/Post DTO binding, preserve command/result and HTTP statuses. Use explicit canonicalizer, never raw-body hash or serialized DTO hash. Avoid a second per-field parser in controller.
- [ ] Gắn existing source-linked `WebOperationResultCodec` vào operation context trong increment này; assert response lần đầu và bytes stored/replayed tương đương, gồm kết quả tạo từ baseline trước upgrade.
- [ ] Validate feature-disabled headers, epoch/session/path, cancellation, stale version, conflict, duplicate submission and loss-of-ACK/restart against durable outcomes.
- [ ] Chạy G1/G2/G3/G4; record paired Desktop/Host and SQL evidence; review/commit `refactor: migrate Sales writes with canonical operation parity`.

### VPS-10.5A — Receipt/Inventory reads native

**Files:** Create `H/Features/Receipts/GoodsReceiptsController.cs`, `H/Features/Receipts/ReceiptResponseDtos.cs`, `H/Features/Receipts/ReceiptHttpMapper.cs`, `H/Features/Inventory/InventoryController.cs`, `H/Features/Inventory/InventoryQueryDtos.cs`, `H/Features/Inventory/InventoryResponseDtos.cs`, `HT/Vps10InventoryReadTests.cs`. Read `L/InventoryControllers.cs`, `L/InventoryWireInput.cs`, `L/InventoryWireOutput.cs`; update manifest.

**Produces:** Four read routes; response DTO metadata for required explicit nulls, fixed 4/8 scale, signed stock-card values và query dates.

- [ ] Red tests receipt IDs/names and nullable labels, zero stock với explicit null `averageUnitCost` và nonzero với scale-8 string, signed movements, totalCount text, fixed scale and UTC query bounds across cultures.
- [ ] Port typed responses và query binder; giữ count/items concurrency semantics, calendar/instant distinction và response invariant checks có giá trị.
- [ ] Chạy G1/G2/G3 và targeted inventory read SQL validation; review/commit `refactor: migrate receipt and inventory reads`.

### VPS-10.5B — Receipt mutations native

**Files:** Create `H/Features/Receipts/ReceiptRequestDtos.cs`, `HT/Vps10ReceiptWriteTests.cs`; modify Receipt controller/mapper, scoped scalar converters and canonicalizer coverage; update manifest.

**Consumes/produces:** 10.4B operation factory, 10.5A responses; three receipt mutation routes. Preserve stricter Receipt canonical Base64 independently of Sales.

- [ ] Red HTTP tests quantity/unit-cost scale, zero/nonzero rules, null/presence, exact version grammar và max line count. Duplicate Receipt product là wire-valid và được Application từ chối bằng business error hiện hữu; giữ owner/precedence của lỗi này và invalid references, không chuyển thành early DTO binding failure.
- [ ] Map typed requests to existing Application commands; giữ receipt canonical byte vectors và stored outcome compatibility.
- [ ] Chạy G1/G2/G3/G4: stale-version race, post once, rollback, inventory/average-cost oracle, lost ACK/restart. Review/commit `refactor: migrate receipt writes with replay parity`.

### VPS-10.6A — Catalog CRUD native

**Files:** Create feature controllers/DTOs under `H/Features/Catalog/` cho Product, Warehouse, Customer, Employee; `HT/Vps10CatalogCrudTests.cs`. Read `L/CatalogCrudControllers.cs`, `L/CatalogWireInput.cs`; update manifest và Desktop wire DTOs chỉ khi cần.

**Produces:** 20 CRUD/list/detail verb/path pairs, explicit request allowlists và independent read/write permissions.

- [ ] Red tests paging/search/includeInactive, ID/concurrency, duplicate/code conflicts, field length/Unicode, inactive refs và each read/write permission; disabled Desktop API trả baseline 404 trước payload parsing kể cả malformed body/query, sau existing transport/auth admission.
- [ ] Port theo entity với typed mapping và validation; không đưa generic repository/controller CRUD framework vào scope.
- [ ] Chạy G1/G2/G3/G4 cho mutation families; giữ policy UNKNOWN/unjournalled của Desktop, không thêm automatic write retry. Review/commit theo entity nếu cần.

### VPS-10.6B — Security và Foundation native

**Files:** Create `H/Features/Security/SecurityController.cs`, request/response DTO files, `H/Features/Foundation/FoundationController.cs`, `HT/Vps10SecurityTests.cs`. Read `L/SecurityParityControllers.cs`, `L/DesktopParityWireInput.cs`; update manifest.

**Produces:** Seven security routes và one foundation route; giữ Application authorization/audit boundary.

- [ ] Red tests permission catalog, assignment/import malformed payload, privilege escalation denial, independent read/write, actor from admitted identity và audit outcome. Security revision JSON number `9007199254740993` phải tới Mediator dưới dạng exact Int64; OpenAPI mô tả numeric revision đúng, không string converter hoặc double intermediary.
- [ ] Port native endpoints; không bind trusted actor/current user từ client body; không bỏ Application permission pipeline.
- [ ] Chạy G1/G2/G3 và G4 restricted-principal/security matrix; review/commit `refactor: migrate security and foundation endpoints`.

### VPS-10.6C — Auth và Health native

**Files:** Create `H/Features/Auth/AuthController.cs`, `H/Features/Auth/AuthRequestDtos.cs`, `H/Features/Auth/HealthController.cs`, `H/Http/Identity/NativeAuthenticationRegistration.cs`, `HT/Vps10NativeAuthTests.cs`. Modify admission/identity services khi cần; read `L/AuthController.cs`, current Health in `H/Net10RouteTable.cs`; update manifest.

**Produces:** Five auth routes và health; typed login body và native auth integration with exactly one validation owner per request.

- [ ] Red tests login limiter, refresh rotation/reuse/logout/revoke, cookie flags/path/same-site, Origin/Host/CSRF và duplicate headers; invalid bearer trên anonymous/health theo baseline, degraded audit 503.
- [ ] Wire native auth service/handler với existing bearer validator và SQL session validation. Không suy diễn signature-valid JWT là active session; giữ request identity single-assignment và middleware precedence.
- [ ] Test lỗi sau staged cookie/location không leak headers; cancellation sau commit không đổi outcome; handled exceptions có correlation/logging đúng.
- [ ] Chạy G1/G2/G3/G4 và attended Windows login/refresh/logout nếu UI path đổi. Review/commit `refactor: migrate auth and health to native ASP.NET Core`.

### VPS-10.6D — Operation status/resolution/resend native

**Files:** Create `H/Features/Operations/OperationsController.cs`, `H/Features/Operations/ResolutionController.cs`, `H/Features/Operations/ResendController.cs`, operation HTTP DTOs, `H/Http/Operations/V1OperationResultCodec.cs`, `HT/Vps10OperationRecoveryTests.cs`. Read `L/OperationsController.cs`, `L/OperationResolutionController.cs`, `L/Web6ResendEligibilityController.cs`, `L/WebOperationResultCodec.cs` và related wire helpers; update manifest.

**Produces:** All seven existing operation routes across v1/v2/v3; `IWebOperationResultCodec` implementation preserving serialized stored outcomes and validation.

- [ ] Red tests context/status/resolution/resend eligibility for committed, not observed, not executed, expired/unavailable/indeterminate; epoch/key/fingerprint mismatch and replay without current Read permission.
- [ ] Port native DTOs/binding and response types; replay stored authoritative outcome, không GET lại để dựng response. Native codecs read historical bytes; preserve result size cap và malformed-result refusal.
- [ ] Chuyển operation factory của Sales/Receipt writes và recovery readers sang `V1OperationResultCodec` cùng PR; kiểm fresh/stored/replayed payload, legacy-produced outcome và null/scale/cost precision không drift.
- [ ] Test Desktop upgrade with unresolved pending operations, host restart/lost ACK, same operation ID and protected audit receipt semantics. Unknown/unsafe states không được auto-resend.
- [ ] Chạy G1/G2/G3/G4, including enabled WEB6/audit cases trên Linux và SQL; review/commit `refactor: migrate operation recovery endpoints`.

### VPS-10.7 — Remove bridge và handoff

**Files:** Modify `H/MyDmsVn.Server.AspNetCoreHost.csproj`, `H/Program.cs`, `H/Composition/EndpointInventory.cs`, `scripts/verify-vps04b-runtime-boundary.py`, affected architecture/native tests and runtime inventory scripts. Remove production host `H/CompatWebApi.cs`, `H/WireDom.cs`, old `H/Net10RouteTable.cs` dispatcher after native inventory replacement, and legacy writer/services only when unused. Update architecture/runbook/roadmap evidence in same PR.

**Produces:** All 57 actual native endpoints and complete OpenAPI; zero legacy HttpHost source links/facade types/reflection action dispatcher in net10 production; paired Desktop net48 artifact.

- [ ] Red boundary checks fail if legacy source input, fake System.Web.Http/Microsoft.Owin, WireDom or reflection dispatcher is reintroduced. Replace historical static assertion requiring exactly one wildcard link with target checks; retain published `.deps.json` dependency ban and scoped Mediator checks.
- [ ] Remove unused bridge code and conditional host-only compatibility branches; keep standalone legacy net48 test/oracle project and frozen corpus. Move any still-needed config/auth/audit helper into native-owned location before unlinking it.
- [ ] Validate inventory all native with unchanged 57 pairs/order/feature flags and full OpenAPI; keep `--list-routes` machine output and SQL-free invocation for existing tooling, or update all consumers atomically.
- [ ] Chạy G0/G1/G2/G3/G4/G5, inspect published graph and Windows x86/target Desktop artifact. Record exact pair and observed UI flows; unused/skipped physical gate remains distinct.
- [ ] Review complete net10 dependency/source diff, update evidence and live status in same PR, commit `refactor: retire ASP.NET Core compatibility bridge`.

## 4. Validation commands và bằng chứng

Commands dưới đây là lệnh để implementer thực hiện, **chưa được chạy bởi commit roadmap**. Chạy trong full checkout, dùng locked restore. Windows tests cần Windows/.NET Framework; SQL gates cần provisioned disposable database. Không tạo credentials/connection string mặc định.

### G0 — Repository/static baseline (repo root)

```bash
python3 scripts/verify-vps-foundation.py --report foundation-report.json
python3 scripts/verify-vps-01.py
python3 scripts/verify-vps-04a-live-route-parity.py
```

Các check structural đang mô tả bridge cũ phải thay bằng target check tương đương trong đúng PR chuyển đổi; không hạ coverage, giữ fixture pins và dependency bans.

### G1 — Host/Application native và route inventory

Chạy từ `src/MyDmsVn.Server.AspNetCoreHost` để chọn nested SDK:

```bash
dotnet --version
dotnet restore MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -p:Vps06bMediator=true --locked-mode
dotnet build MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -p:Vps06bMediator=true -c Release -f net10.0 --no-restore -warnaserror
python3 ../../scripts/verify-vps04b-runtime-boundary.py
dotnet restore ../../tests/MyDmsVn.Server.Application.Tests/MyDmsVn.Server.Application.Tests.csproj -p:Vps04Server=true --locked-mode
dotnet test ../../tests/MyDmsVn.Server.Application.Tests/MyDmsVn.Server.Application.Tests.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore --logger 'trx;LogFileName=vps10-application.trx' --results-directory ../../artifacts/vps10
dotnet restore ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true -p:Vps06bMediator=true --locked-mode
dotnet test ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true -p:Vps06bMediator=true -c Release -f net10.0 --no-restore --logger 'trx;LogFileName=vps10-host.trx' --results-directory ../../artifacts/vps10
dotnet run --project MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -p:Vps06bMediator=true -c Release --no-build -- --list-routes > ../../vps10-routes.json
python3 ../../scripts/verify-vps-04a.py ../../vps10-routes.json
```

`dotnet --version` phải là `10.0.404`; chạy từng lệnh và dừng khi exit code khác 0. TRX phải có actual executed >0, failed=0, notExecuted=0 và đủ các case đã kê. Mỗi new `Vps10*Tests` class cần targeted test run chứng minh discovery có case, rồi suite regression trên HEAD cuối. Ví dụ pilot dùng `--filter FullyQualifiedName~Vps10NativeCatalogTests` với project/TFM/flags như trên.

### G2 — Frozen SQL-free 57-route differential

Sau build G1, cùng working directory:

```bash
dotnet test ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true -p:Vps06bMediator=true -c Release -f net10.0 --no-build --no-restore --filter FullyQualifiedName~Vps05cDesktopRouteLiveParityTests --logger 'trx;LogFileName=vps10-differential.trx' --results-directory ../../artifacts/vps10
```

Baseline frozen selector phải thực thi **293/293**, zero failed/skipped, theo `.github/workflows/vps-04a-linux.yml`. Cases mới nằm ngoài frozen set; không thay expected bằng output native. Nếu refactor test factory/route owner selection, cùng PR chứng minh requests thực sự đi qua native implementation thay vì vẫn đi bridge. SQL-free PASS không thay G4.

### G3 — Desktop net48 (PowerShell, repo root trên Windows)

```powershell
dotnet restore src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj --locked-mode
dotnet build src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj -c Release -f net48 --no-restore
dotnet restore tests/MyDmsVn.Desktop.Session.Tests/MyDmsVn.Desktop.Session.Tests.csproj --locked-mode
dotnet test tests/MyDmsVn.Desktop.Session.Tests/MyDmsVn.Desktop.Session.Tests.csproj -c Release -f net48 --no-restore --logger 'trx;LogFileName=vps10-desktop-session.trx' --results-directory artifacts/vps10
dotnet restore tests/MyDmsVn.Desktop.Tests/MyDmsVn.Desktop.Tests.csproj --locked-mode
dotnet test tests/MyDmsVn.Desktop.Tests/MyDmsVn.Desktop.Tests.csproj -c Release -f net48 --no-restore --filter 'FullyQualifiedName~ReceiptNumericInputTests|FullyQualifiedName~DesktopDateInputIntegrationTests' --logger 'trx;LogFileName=vps10-desktop-culture.trx' --results-directory artifacts/vps10
```

Kiểm `$LASTEXITCODE` sau từng lệnh, dừng khi khác 0; TRX phải non-empty và không skip các case yêu cầu. Tiếp tục `.github/workflows/vps-03-desktop-artifact.yml` và Windows legacy/discovery regressions trên final HEAD. UI native/attended behavior chỉ claim khi đã quan sát trong môi trường tương ứng.

### G4 — Side effects, SQL và recovery

Dùng `.github/workflows/vps-04a-ci-sql.yml` và existing disposable bootstrap/oracles, thêm native HTTP cases vào cùng lane khi chuyển write/auth/operation. Existing test source: `HT/Vps04aLiveSqlAcceptanceTests.cs`, `HT/Vps04aCiFaultSqlAcceptanceTests.cs`, `HT/Vps04aCiOversellSqlAcceptanceTests.cs`, `HT/Vps04aCiValuationSqlAcceptanceTests.cs`, `HT/Vps04bWeb6LiveSqlTests.cs`, `HT/Vps06bSqlGeneratedMediatorParityTests.cs`.

Không chạy các SQL opt-in tests bằng một filter thiếu provisioning rồi coi skip là pass. Compile `Vps04ALive=true` chỉ là compile evidence. Kiểm current workflow selectors/discovered cases trước chạy; ghi SQL instance class, principal class, native routes exercised, exact counts, invariant/oracle results và logs đã loại secrets. Giữ protected live workflow và VPS-08/09 gates riêng; không sửa approval/ref checks để phục vụ VPS-10.

Workflow hiện có kiểm exact counts cho từng TRX. Native SQL cases mới phải có named selector/TRX và required counts riêng, hoặc count delta được review rõ ràng; không chèn case vào frozen selector rồi hạ/bỏ count assertion. Chạy đủ disposable workflow với các kịch bản ordered oversell/rollback/recovery/WEB6 thay vì lấy riêng một filter thuận lợi.

### G5 — Closeout paired artifacts

Chạy required existing workflows trên final reviewed HEAD theo `.github/workflows/`, thêm boundary/native docs checks gắn vào lane thích hợp. Kiểm published net10 dependency graph, complete route inventory/OpenAPI và Windows net48 artifact. Exercise paired Desktop → Linux host → isolated SQL: lookup, Sales create/update/post, receipt create/update/post, inventory, permission denial, login/refresh/logout và reopen pending operation sau lost ACK/restart. Dùng [local simulation runbook](../runbooks/local-fullstack-simulation.md) khi phù hợp, ghi rõ diagnostics vs attended/SQL scope.

## 5. Evidence, review và acceptance

Mỗi PR ghi: base/head SHA; routes chuyển và actual owner; files/interfaces đổi; paired client/host build; exact commands/runner/SDK; discovered/executed/passed/failed/skipped; raw wire/canonical/SQL/UI evidence tương ứng; findings và kết quả sau fix. Final-HEAD CI và independent review đi cùng PR implementation, không thay bằng số PASS của baseline trong tài liệu.

Khi thiếu môi trường, tiếp tục phần source/test preparation có thể làm và ghi đúng gate chưa thực thi; không tự nhận increment complete. Thay đổi source sau review phải chạy lại các gate liên quan và required final-HEAD CI. Không tạo scope ngoài được Owner duyệt chỉ để làm test xanh.

VPS-10 hoàn tất khi actual native inventory bao phủ toàn bộ surface, Desktop net48 paired build hoạt động, transport DTO/codecs đọc rõ và giữ hợp đồng, old operation state/replay không mất, net10 published source/runtime không còn bridge, evidence đúng phạm vi đã được review. Production release và các nghĩa vụ VPS-08/09 vẫn có trạng thái riêng tại [index](README.md).

**Bước triển khai đầu tiên:** VPS-10.0. Commit roadmap chỉ ghi quyết định/thiết kế/kế hoạch; không đánh dấu bất kỳ implementation increment hoặc runtime test nào đã PASS.
