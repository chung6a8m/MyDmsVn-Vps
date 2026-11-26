# VPS-10 — Native ASP.NET Core 10 Host and typed HTTP contracts

**Quyết định:** [OWN-VPS-05](../decisions/OWN-VPS-05.md), được Owner chấp thuận ngày 2026-11-26 (Asia/Bangkok).
**Mã nguồn đối chiếu:** `6a9649430cfd8ff4576d5e351f4ef6d0080011a0` của chính repo MyDmsVn-Vps.
**Kế hoạch thực hiện:** [VPS-10 implementation plan](../roadmap/vps-10-native-aspnetcore-host.md).
**Trạng thái thực hiện:** chỉ cập nhật tại [roadmap index](../roadmap/README.md). Tài liệu này mô tả thiết kế đã chọn, không phải bằng chứng triển khai hoặc nghiệm thu.

## 1. Mục tiêu và lựa chọn

Server tiếp tục `net10.0`, SDK `10.0.404`; Desktop tiếp tục WinForms `net48` và được nâng cấp đồng bộ với Server. Endpoint nhận DTO có kiểu qua ASP.NET Core, gọi Application qua Mediator, trả DTO có kiểu. Dữ liệu HTTP dùng định dạng máy đọc độc lập culture; UI chịu trách nhiệm parse nhập liệu và format hiển thị theo culture được chọn.

| Phương án đã xem xét | Đánh đổi | Quyết định |
| --- | --- | --- |
| Native host, giữ hợp đồng HTTP hiện hữu | Còn một số converter và quy tắc tương thích; giảm phạm vi thay đổi và dùng lại oracle hiện có | Chọn cho VPS-10 |
| Native host kèm thiết kế lại business API | Có thể thống nhất tên/null/error/representation sâu hơn; cần migration client, fingerprint và replay riêng | Không thuộc đợt này |

Việc cho phép nâng cấp Desktop gỡ yêu cầu duy trì vô thời hạn binary Desktop cũ. Nó không tự đổi wire contract hoặc dữ liệu operation đã lưu. Mỗi increment xác định build Desktop đi cùng Host và kiểm chứng cặp đó; nếu adapter chưa đổi, dùng build Desktop baseline tương ứng. Không đổi SQL schema, thuật toán tính giá vốn, SDK hay package hàng loạt trong đợt refactor.

"Giữ v1" trong trao đổi thiết kế có nghĩa giữ business wire contract hiện tại. Phạm vi thực tế gồm **57 cặp verb/path**: 52 dưới `/api/v1`, 2 dưới `/api/v2`, 3 dưới `/api/v3`. Các route operation resolution/resend v2/v3 hiện hữu vẫn được giữ. Không suy diễn thành chỉ bảo toàn các route có prefix v1.

## 2. Vấn đề đã đo từ source baseline (lịch sử)

- [Host project](../../src/MyDmsVn.Server.AspNetCoreHost/MyDmsVn.Server.AspNetCoreHost.csproj) source-link `Server.HttpHost/*.cs` dưới `Legacy`.
- `CompatWebApi.cs` (file tại baseline) định nghĩa façade `System.Web.Http`/OWIN và request service theo string key.
- `Net10RouteTable.cs` (file tại baseline) chỉ chấp nhận action parameter `string`/`CancellationToken`, dispatch bằng reflection qua `HttpRequestMessage`/`HttpResponseMessage`.
- [StrictWireJson](../../src/MyDmsVn.Server.HttpHost/StrictWireJson.cs), `WireDom.cs` (file tại baseline) và các `*WireInput` đọc/kiểm tra JSON nhiều lượt rồi dựng DTO.
- [HttpDesktopWire](../../src/MyDmsVn.Desktop.Infrastructure.Http/HttpDesktopWire.cs) đã format invariant nhưng còn dựng DOM và normalize GoodsReceipt response bằng parse/rename/serialize.
- [ReceiptNumericInput](../../src/MyDmsVn.Desktop.WinForms/ReceiptNumericInput.cs) đã nhận culture rõ ràng; không cần chuyển việc hiểu dấu thập phân của người dùng sang Server.

[Program](../../src/MyDmsVn.Server.AspNetCoreHost/Program.cs), request scope, Application/Domain/Infrastructure và [generated Mediator](../../src/MyDmsVn.Server.AspNetCoreHost/GeneratedMediatorRegistration.cs) là nền tảng tiếp tục sử dụng. Không coi việc gỡ bridge là lý do viết lại nghiệp vụ. Số lượt parse/allocation là điểm cần đo nếu tối ưu hiệu năng; chưa có kết luận tốc độ trước khi benchmark.

## 3. Tổ chức đích

Các đường dẫn dưới đây mô tả tổ chức đích của VPS-10. Dùng project host hiện tại; chưa tạo thêm project hoặc generic framework.

| Vị trí trong `src/MyDmsVn.Server.AspNetCoreHost/` | Trách nhiệm |
| --- | --- |
| `Program.cs` | Composition root, startup validation trước khi listen, pipeline, endpoint mapping |
| `Configuration/`, `Composition/` | Typed options và DI registration; giữ tên biến môi trường/config đã triển khai |
| `Hosting/` | Proxy admission, startup checks, readiness, portable audit integration |
| `Features/{Catalog,Sales,Receipts,Inventory,Security,Auth,Operations,Foundation}/` | Native controller, HTTP request/response DTO và mapping theo tính năng |
| `Http/Serialization/` | Profile JSON và converter scalar dùng chung có phạm vi rõ |
| `Http/Binding/` | Query/route binding chặt theo contract, strict request metadata và source capture cần thiết |
| `Http/Errors/` | ModelState, business result, challenge/forbid và exception mapping |
| `Http/Identity/` | Admitted current user/session/correlation có kiểu và request lifetime |
| `Http/Operations/` | Versioned canonical intent, operation context và replay wire codec |

Controller kế thừa `Microsoft.AspNetCore.Mvc.ControllerBase`, dùng `[ApiController]`, constructor injection và cancellation token của request. Không tạo base controller mới để chứa parser, service locator hoặc nghiệp vụ. Native MVC chỉ discover các controller native đã chỉ định, tránh việc các class Legacy có hậu tố `Controller` vô tình được nhận diện.

Application tiếp tục sở hữu permission, validation nghiệp vụ và điều phối use case; Infrastructure giữ persistence/transaction. HTTP DTO có thể tách khỏi DTO Application khi khác tên, nullability hoặc wire representation. Một mapping có kiểu ở biên là chấp nhận được; không serialize rồi deserialize lại để map object. Không trả trực tiếp database entity hoặc nhận Application command thay cho request có allowlist trường riêng.

Desktop giữ các lớp `Desktop.Application`, `Desktop.Infrastructure.Http`, WinForms và neutral Contracts; không tham chiếu host/Server assemblies hoặc SQL. HTTP wire DTO của Desktop thuộc adapter; converter Json.NET có thể dùng cùng corpus với converter System.Text.Json của host. Không chuyển toàn bộ Contracts sang `netstandard2.0` chỉ để chia sẻ các type mới.

## 4. Hợp đồng scalar, binding và validation

| Dữ liệu | Wire phải giữ | Kiểu khi xử lý | Quy tắc |
| --- | --- | --- | --- |
| `int32` ID, page size, limit | JSON integer number hoặc query text theo từng route | `int` | Không tự mở rộng sang quoted number; giữ bound/default từng endpoint |
| Existing `int64` ID/count | Canonical decimal string | `long` | Không sign/leading zero/exponent; zero chỉ ở trường đang cho phép |
| Security `expectedRevision`/`expectedTargetRevision` | JSON integer number theo contract hiện hữu | `long` | Giữ nguyên integer token và độ chính xác Int64, kể cả `9007199254740993`; không áp converter ID-text cho revision |
| Quantity/price/amount/cost | Invariant decimal string theo field | `decimal` | Kiểm lexical scale trước parse; giữ sign/range và precision 4 hoặc 8 theo field |
| Calendar date | `yyyy-MM-dd` | Host DTO có thể `DateOnly`; shared/net48 DTO dùng quy ước DateTime hiện tại | Không timezone conversion; giữ quy tắc default date theo endpoint |
| Instant | ISO UTC `Z` theo contract hiện tại | `DateTimeOffset` hoặc existing UTC DateTime với mapping rõ | Không suy đoán timezone từ culture; không diễn giải SQL UTC `Unspecified` thành giờ máy chủ |
| Rowversion/cursor | Opaque string | Token hoặc value có kiểu tại boundary | Không tăng/so sánh như số; giữ validation và filter binding hiện hữu |
| Boolean | JSON `true`/`false` | `bool` | Phân biệt missing với `false` ở trường bắt buộc |

Nguồn hợp đồng: [WEB2 scalar/field tables](../contracts/web2-v1/README.md), [numeric vectors](../contracts/web2-v1/numeric-vectors.json), [WEB3 contract](../contracts/web3-v1/README.md). Sales output giữ biểu diễn scale hiện hữu; Inventory xuất cố định 4/8 theo field. Sales và Receipt có khác biệt chấp nhận Base64 input; không áp converter strict hơn cho cả hai khi chưa qua oracle.

JSON body do System.Text.Json input formatter xử lý. Route/query dùng model binding; form dùng cơ chế khác và không phải phương tiện gửi nghiệp vụ mới của VPS-10. `InvariantCulture` không thay cho kiểm tra grammar, duplicates, bounds hoặc required fields. Không bật invariant globalization mode của runtime để giải quyết hợp đồng HTTP.

Profile cho DTO đã chuyển chọn rõ `PropertyNamingPolicy`, `PropertyNameCaseInsensitive`, `NumberHandling`, `UnmappedMemberHandling`, `AllowDuplicateProperties`, `MaxDepth`, comment/trailing-comma behavior. Có thể dùng .NET 10 built-in options để bỏ code tương đương, sau khi chứng minh hành vi; không bật nguyên preset hoặc thay null policy toàn app mà không đối chiếu từng nhóm route.

Converter chỉ gắn cho field/type cần wire representation riêng. Không dùng global `WriteAsString` hoặc `AllowReadingFromString` để biến mọi số thành chuỗi/chấp nhận mọi token. Các field bắt buộc dùng JSON presence metadata (`JsonRequired` hoặc cơ chế tương đương); non-nullable `int/decimal/bool` đơn thuần không chứng minh field xuất hiện. Kiểm list cardinality và phần tử null riêng. Mapping lỗi giữ path `lines[0].qty`, không để path CLR hoặc `$.` rò ra ngoài thay contract.

Query validation giữ semantics raw hiện có: allowlist ordinal, duplicate sau percent-decoding, malformed UTF-8/percent escape, dấu `+`, bắt buộc dấu `=`, defaults, bounds, empty/unknown query và GET body. Calendar query cần exact grammar, không chỉ parser rộng theo invariant. Không dùng route constraint mới để âm thầm biến lỗi 400 thành 404.

## 5. Kết quả, lỗi và OpenAPI

Giữ success `data`, error `code/message/details/correlationId`, HTTP status ở transport, null omission/explicit null theo endpoint, cookie/header và status create hiện tại. Kết quả typed được serialize trực tiếp qua native result pipeline. `[ApiController]` automatic 400, unsupported content type, challenge/forbid và 404/405 phải được map/test để giữ shape và precedence hiện có.

Feature gating có precedence riêng: bốn Catalog lookup routes dùng legacy `Execute` nên vẫn khả dụng khi `DesktopApiEnabled=false`; 28 additive Desktop routes dùng `ExecuteDesktop` và trả 404 khi tắt sau transport/auth admission nhưng trước payload parsing. Native gate cho additive routes phải dùng endpoint metadata + middleware/resource filter chạy trước model binding, không chỉ kiểm tra trong action. Test disabled + malformed body/query cùng enabled + chính payload đó để phát hiện 400/415 bị trả quá sớm.

Đăng ký `IExceptionHandler` cho lỗi bất ngờ khi nhóm route đã native; phối hợp `InvalidModelStateResponseFactory` cho binding/validation và existing Application error mapping. Xử lý response trước khi bytes được gửi, giữ cleanup của staged `Set-Cookie`/`Location` và correlation. Không đổi public envelope sang ProblemDetails trong VPS-10. Khi dùng .NET 10 handled-exception diagnostics, cấu hình/log đúng một lần để không mất quan sát hoặc log trùng.

`Microsoft.AspNetCore.OpenApi` theo phiên bản .NET 10 được pin/lock khi thêm ở increment pilot. Chỉ map tài liệu ở Development hoặc môi trường được cấu hình cho phép. Document generation không yêu cầu production SQL/secrets. OpenAPI ban đầu chỉ mô tả route native đã chuyển, ghi rõ coverage; khi closeout phải bao phủ cả 57 route hiện hữu. Schema transformer giữ các decimal/int64 fields dùng text là `string`, Security numeric revisions là `integer/int64`, ngày `date`, instant `date-time`, required/null và envelope đúng runtime. OpenAPI không thay thế corpus độc lập.

## 6. Canonicalization, pending operation và replay

[StrictJsonRequest](../../src/MyDmsVn.Server.HttpHost/StrictJsonRequest.cs) giữ typed value và strictly validated source; [WebOperationWireInput](../../src/MyDmsVn.Server.HttpHost/WebOperationWireInput.cs) hash canonical operation intent, không hash raw request bytes. Hợp đồng hiện tại chuẩn hóa decimal F4, missing note thành null, Sales Base64, ordinal object-key order, giữ array order, method/route/target/contract namespace.

Tách canonicalizer versioned khỏi controller. Trong giai đoạn chuyển, giữ source sau strict validation cùng DTO, request-scoped và dispose đúng; đọc body bounded, không consume stream làm input formatter thấy body rỗng. Nếu còn phải đọc DOM cho fingerprint thì cô lập tại phần operation, không trả DOM về controller nghiệp vụ. Raw source/JSON fingerprint không được log dữ liệu nhạy cảm.

Không thay bằng `JsonSerializer.Serialize(dto)` rồi hash; không đổi namespace `web4-v1`, route scope, operation identity hoặc thuật toán cursor. Stored outcomes qua [WebOperationResultCodec](../../src/MyDmsVn.Server.HttpHost/WebOperationResultCodec.cs) và pending store của Desktop phải đọc/replay được sau upgrade. Không xóa pending store, đổi key hoặc gửi lại lệnh ghi chỉ để tạo demo sạch. Cancellation hoặc mất ACK không chứng minh rollback.

VPS-10.4B/10.5B tạm dùng `WebOperationResultCodec` source-linked hiện hữu khi tạo `WebOperationContext`; chứng minh response lần đầu và bytes persisted/replayed tương đương ngay ở các PR ghi. VPS-10.6D chuyển cả factory của writes và recovery readers sang native `V1OperationResultCodec` trong cùng PR, giữ interface và đọc được legacy-produced outcomes. Không để writes dùng codec chưa được tạo bởi một increment phía sau.

## 7. Security, startup và coexistence

Giữ pre-listen schema/principal/superuser checks, production scope validation, proxy/forwarded-header trust, origin/CSRF rules, body cap cho cả chunked request, JWT + SQL session, permission pipeline, audit durability/readiness. Auth conversion giữ rotation/revoke, login limiter, cookie grammar và post-commit audit/cancellation semantics.

Pilot tiếp tục dùng Core API admission hiện tại. Khi chuyển Auth, đưa thành phần xác thực vào native service/handler nếu hữu ích, đồng thời quy định một owner cho mỗi check; không validate JWT hai lần hoặc bỏ SQL session vì đã có `[Authorize]`. Unknown API paths vẫn có admission theo baseline. Health giữ behavior theo bearer được gửi, readiness/audit và trạng thái 503.

Trước route native đầu tiên, thay guard chỉ đếm Legacy bằng inventory hợp nhất. Mỗi verb/path có đúng một owner active (`legacy` hoặc `native`), giữ order/templates/allowed methods và feature flags. Kiểm actual `EndpointDataSource`, không chỉ manifest tự khai. Legacy controller/action identity là compatibility metadata riêng với actual native type/action; không giả nhãn để che route không được map. Test duplicate/missing route fail.

Giữ legacy net48 host và immutable oracles phục vụ differential trong đợt này. Đích closeout của production net10: không source-link code từ legacy HttpHost, không `CompatWebApi`, không `WireDom`, không reflection action dispatch hoặc request/response bridge; có thể còn narrow canonical reader cho protocol cần nó. Không gỡ net48 project/oracle trong PR cleanup host.

## 8. Kiểm chứng và phạm vi được phép

Tái sử dụng bộ 57-route/293-case SQL-free differential cùng targeted binding/serializer tests. Bổ sung HTTP tests qua Kestrel cho native routes; tests chỉ gọi converter hoặc controller trực tiếp không chứng minh middleware/error precedence. SQL mutation/concurrency/inventory/recovery cần disposable SQL gate phù hợp; Desktop cần Windows net48 build và tests, có observed UI exercise khi luồng UI thay đổi.

Các culture tối thiểu: `vi-VN`, `en-US`, `de-DE`, invariant; restore culture sau test và tránh process-global culture race. Thêm timezone boundary cho ngày/thời điểm. Chạy valid/invalid numeric vectors, missing/null/false, duplicate/unknown/Unicode, canonical Base64 differences, query grammar, body/content-type, 404/405, cookies, fingerprint, restart/replay/cursor, scope/cancellation và feature-enabled/disabled paths.

VPS-10 có thể phát triển trên môi trường DEV/disposable hiện hữu trong khi VPS-08/09 còn nghĩa vụ vận hành riêng. Không đặt production closeout làm điều kiện để bắt đầu refactor, cũng không dùng refactor để closeout các nghĩa vụ đó. Thực hiện theo [test policy](../testing/test-policy.md); ghi actual counts/environment/SHA, không coi skipped hoặc historical PASS là kết quả mới.

## 9. Tài liệu kỹ thuật chính thức

- [ASP.NET Core 10 model binding](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-10.0): body formatter, route/query/form và culture.
- [ASP.NET Core controller APIs](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0): ControllerBase, ApiController và automatic 400.
- [.NET 10 JSON options](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries): duplicate-property handling và strict options.
- [System.Text.Json converters](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to) và [required properties](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties).
- [DateOnly/TimeOnly](https://learn.microsoft.com/en-us/dotnet/standard/datetime/how-to-use-dateonly-timeonly): framework support và date-only semantics.
- [ASP.NET Core 10 error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0) và [OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0).

Các tham chiếu được dùng để thiết kế, không phải quyền tự nâng phiên bản package/SDK. Đối chiếu API với target net10 của repo khi thực hiện.

## Handoff VPS-10.7 — Native-only production boundary

- [NativeEndpointRoutes](../../src/MyDmsVn.Server.AspNetCoreHost/Composition/NativeEndpointRoutes.cs) maps MVC and the compatibility 404/405 HTTP responses. It never invokes controller actions itself. [EndpointInventory](../../src/MyDmsVn.Server.AspNetCoreHost/Composition/EndpointInventory.cs) reads actual endpoint metadata and checks it against the 57-row [NativeRouteContract](../../src/MyDmsVn.Server.AspNetCoreHost/Composition/NativeRouteContract.cs); the compiled inventory is independently checked against frozen route manifests.
- Configuration, token validation/issuance, login admission, signed WEB6 receipts and trace audit helpers are owned by the native Host. Signed claim parsing uses System.Text.Json with strict UTF-8, bounded depth, unique members and explicit scalar checks; signature/identity/lifetime rules and signing-key purposes remain unchanged.
- Admission errors outside MVC use the same typed error envelope and JSON profile as native endpoints. No request-message or response-message bridge, fake OWIN/Web API namespace, or legacy DOM is needed in production.
- The separate net48 HttpHost and original frozen corpus remain unchanged. Historical STJ wire helpers are compiled only into the net10 test project as an oracle, not as an endpoint owner; production tests must exercise native handlers, not just the oracle.
- CI rejects source links, retired production types, external evaluated Compile inputs and blocked runtime dependencies; checks both built and published .deps.json. Development OpenAPI checks every method/path and response schema, not merely distinct paths. The `--list-routes` diagnostic is SQL-free and preserves its existing JSON contract.

See [VPS-10.7 evidence](../evidence/vps-10-7-bridge-removal.md) for source scope and outstanding validation. A source change or successful CI is not proof of a physical paired Desktop/Host upgrade.
