# PRN232 — Kế hoạch áp dụng công nghệ từ slide vào PetShop

> Nguồn: 11 file PPTX trong `drive-download-20261101T140145Z-1-001/` (422 slide) — môn **PRN232: Building Cross-Platform Back-End Application with .NET**.
> Phạm vi đã chốt: **Core 7 chương** → OData, JWT, gRPC, Content Negotiation, Validation, JS Client, Docker.
> Bỏ qua: WCF/CoreWCF (Ch08) và tách Microservices thật (Ch10) — quá nặng so với 1 project Blazor + SQL Server.

---

## 0. Tóm tắt "đến cỡ nào cũng hiểu"

Slide nói về 3 tầng công nghệ:

| Tầng | Slide dạy | PetShop hiện có | Cần làm |
|---|---|---|---|
| **Tầng 1 — API cơ bản** | Ch01 REST, Ch02 Models/DTO, Ch05 Binding/Validation/Routing | ✅ Đã có gần hết | Thêm **Validation thật** + **Content Negotiation** |
| **Tầng 2 — API nâng cao** | Ch04 OData, Ch06 Security (JWT), Ch07 JS Client | ❌ Chưa có | Thêm **OData endpoint**, **JWT Bearer**, **JS client demo** |
| **Tầng 3 — Service-to-service** | Ch09 gRPC, Ch10 Docker/Microservices | ❌ Chưa có | Thêm **gRPC service** (Employee/Pet) + **Dockerfile** |

**Hiểu đơn giản:** PetShop giờ là "web app Blazor + REST API". Sau khi làm xong plan này, nó sẽ là:
- API chuẩn REST có **validate dữ liệu** (Ch05) và **trả về JSON/XML theo yêu cầu** (Ch03)
- Có **OData endpoint** để client query động `$filter/$sort/$expand` (Ch04)
- **Đăng nhập bằng JWT** thay vì chỉ session (Ch06)
- Có **gRPC service** (protobuf) để service gọi nhau nhanh (Ch09)
- Có **JavaScript client** gọi API trực tiếp (Ch07)
- **Chạy được trong Docker** (Ch10)

---

## Trạng thái các phase (cập nhật 01/11/2026)

| Phase | Nội dung | Trạng thái |
|---|---|---|
| **P0** | Dọn code chết + net an toàn | ✅ Xong |
| **P1** | DataAnnotations Validation + ProblemDetails (RFC 7807) | ✅ Xong |
| **P2** | Content Negotiation (JSON / XML / CSV formatter) | ✅ Xong |
| **P3** | **OData 9.5** (`$filter` `$select` `$orderby` `$top` `$skip` `$expand` `$count`) | ✅ **Xong** — prefix `/PetShop` |
| **P4** | JWT Bearer authentication (dual-mode: session nội bộ + Bearer ngoài) | ⬜ Chưa làm |
| **P5** | gRPC service (protobuf, unary + streaming) | ⬜ Chưa làm |
| **P6** | JavaScript client demo (Razor view + jQuery AJAX CRUD) | ⬜ Chưa làm |
| **P7** | Docker containerization (`Dockerfile` + `docker-compose.yml`) | ⬜ Chưa làm |

---

## Phase 0 — Nền tảng: dọn code chết + bảo vệ regression (nền cho mọi phase sau)

> Không làm phase này thì mọi phase sau rất dễ vỡ. Đây là phase "rẻ nhất, giá trị cao nhất".

### P0.1 Xoá file / code chết

| Xoá | Lý do | Tham chiếu |
|---|---|---|
| `Components/Pages/Counter.razor` | Template thừa, không có `@page`, không ai link tới | 0 ref |
| `Components/Pages/Weather.razor` | Template thừa, không có `@page`, không ai link tới | 0 ref |
| `Components/Pages/ReactRouteBacklog.razor` | Trang "đang phát triển" placeholder, chỉ tự link tới chính nó | 1 ref (self) |
| `Components/Pages/RouteRedirects.razor` | Redirect `/orders`→`/system` & `/profile`→`/profile/edit`; nhưng `/orders` đã có route thật ở `Orders.razor` (`/account/orders`) → **xung đột route** | 21 ref nhưng route `/orders` bị `Account.razor`/`TopBar` dùng → giữ? → **xoá**, dùng link thật |
| `Components/Pages/ProductDeleted.razor` | Truy cập DB trực tiếp từ Razor (vi phạm 3-layer), chưa ai link | 2 ref (self + `Products.razor`?) |
| `Components/Shared/EmptyState.razor` | Không component nào dùng | 0 ref |
| `Components/Shared/Navigation.razor` | 127 ref là **false positive** (chữ "Navigation" trong `NavigationManager`) — không component nào dùng như component | verify |
| `Components/Shared/TemplateModal.razor` | Không ai dùng (đã có `CartDrawer.razor` riêng) | 0 ref |
| `Components/Layout/NavMenu.razor` + `.css` | Layout thừa, `MainLayout` không dùng | 0 ref |
| `Components/Layout/Footer.razor` | Xem `Shared/Footer.razor` mới dùng | verify |
| `Models/ChatMessage.cs` | Entity trong DbContext nhưng không có controller/service dùng | verify |
| `Models/SystemSetting.cs` | Entity trong DbContext, không API nào đụng | verify |
| `Models/Shift.cs` | `ShiftRequests` mới dùng; `Shift` không ai đọc | verify |
| `Data/SessionExtensions.cs` | `SetJson`/`GetJson` không ai gọi | 0 ref |
| `Models/PayrollDtos.cs` → `PayrollResultDto`, `GeneratePayrollDto` | Không controller nào dùng | 0 ref |
| `Models/StaffDtos.cs` → `RevenueDetailDto` | Không ai dùng | 0 ref |

### P0.2 Xoá code chết trong file đang dùng

- `Services/UserSessionService.cs`: bỏ `_loadInvocationCount` / `_loadTotalMs` / `_verboseLogCounter` / `Stopwatch` — đo lường debug tạm, không mang giá trị nghiệp vụ.
- `Services/SpaAuthHeaderHandler.cs`: bỏ `Stopwatch` + 5 counter tĩnh (`_invocationCount`, `_totalMs`, `_attachedHeaders`, `_missingSession`, `_exceptionCount`) — cùng lý do.
- `Services/LoggerExtensions.cs`: giữ, dùng ở mọi nơi.

### P0.3 Tách lớp (fix "Razor truy cập DB trực tiếp")

`ProductDeleted.razor` inject `ShopPetDatabaseContext` → vi phạm kiến trúc 3 tầng của slide Ch02. Nếu giữ lại phải đưa qua Service. **Chọn: xoá** (route `/products/deleted` không có trong menu).

### P0.4 Regression net

- `dotnet build PetShop_PRN222.sln` → 0 error, 0 warning
- `dotnet test` → 22/22 pass
- Smoke test API (danh sách ở §Verification)

---

## Phase 1 — Ch05: Binding, Validation & Routing (nền cho mọi thứ sau)

### P1.1 Thêm DataAnnotations vào DTO

Hiện tại **0 attribute validation** trong toàn bộ `Models/`. Slide Ch05 yêu cầu `[Required]`, `[EmailAddress]`, `[StringLength]`, `[Range]`, `[Compare]`, `[RegularExpression]`.

Files cần sửa:
- `Models/AuthDtos.cs` — `RegisterRequestDto` (`[Required]`, `[EmailAddress]`, `[StringLength(100)]`, `[MinLength(6)]`), `LoginRequestDto`, `ResetPasswordRequestDto` (`[Compare("NewPassword")]`), `GoogleLoginRequestDto`
- `Models/ProductDto.cs` — `[Range]` cho `Price`, `[Range(0,int.MaxValue)]` cho `StockQuantity`
- `Models/BoardingDtos.cs`, `Models/SpaBookingDtos.cs`, `Models/PetCrudDtos.cs`, `Models/BreedPricingDtos.cs` — validate field đầu vào
- `Models/AuthDtos.cs` — `AuthResponseDto` **không** validate (response DTO)

### P1.2 Bật `[ApiController]` automatic 400 (đã có nhưng cần chuẩn hoá)

`[ApiController]` đã bật → ModelState invalid tự trả 400. Nhưng:
- `Program.cs` hiện **không** có `ConfigureApiBehaviorOptions` → response 400 mặc định của ASP.NET là `ValidationProblemDetails` — đúng chuẩn slide Ch01 Slide 37. **Giữ nguyên.**
- Xoá các `if (string.IsNullOrWhiteSpace(...)) throw new ArgumentException` **thừa** trong `AuthService` (đã có attribute) — nhưng **giữ** vì service được gọi cả từ nơi khác. → Chỉ giữ, không xoá (an toàn).

### P1.3 Chuẩn hoá `ProblemDetails` cho toàn bộ API

Tạo `Services/ApiExceptionMiddleware.cs`:
- Bắt `UnauthorizedAccessException` → 401
- Bắt `KeyNotFoundException` → 404
- Bắt `ArgumentException` → 400
- Bắt `InvalidOperationException` → 409
- Bắt `Exception` → 500 + `ProblemDetails` JSON
- Tất cả lỗi trả về đúng format `application/problem+json` (chuẩn slide Ch01 Slide 37)

### P1.4 Model Binding source attributes

Rà soát tất cả action method, đảm bảo dùng đúng `[FromBody]` / `[FromQuery]` / `[FromRoute]` / `[FromHeader]` (slide Ch05 Slide 17-19). Hiện đã dùng đúng phần lớn.

### P1.5 Attribute Routing chuẩn

- Thêm `[ProducesResponseType]` cho từng action → Swagger mô tả đúng status code (slide Ch01 Slide 27).
- Bổ sung route constants để không magic string.

---

## Phase 2 — Ch03: Media Formatters & Content Negotiation

### P2.1 Bật XML formatter

`Program.cs`:
```csharp
builder.Services.AddControllersWithViews()
    .AddXmlSerializerExtensions();   // <-- thêm
```
→ API trả được cả `application/json` **và** `application/xml` tùy `Accept` header.

### P2.2 Custom Output Formatter (CSV) — demo slide Ch03 Slide 17

Tạo `Services/CsvOutputFormatter.cs` (kế thừa `TextOutputFormatter`):
- `SupportedMediaTypes` = `text/csv`
- `CanWriteType` = chỉ nhận `IReadOnlyList<T>` / `List<T>`
- `WriteResponseBodyAsync` → ghi header CSV + serialize từng dòng

Register trong `Program.cs`:
```csharp
builder.Services.AddControllersWithViews(options =>
{
    options.OutputFormatters.Add(new CsvOutputFormatter());
})
.AddXmlSerializerExtensions();
```

### P2.3 Test content negotiation

- `Accept: application/json` → JSON
- `Accept: application/xml` → XML
- `Accept: text/csv` → CSV
- `Accept: application/xml;q=0.9, application/json;q=0.8` → XML (theo slide Ch03 Slide 10)
- Không match → 406 Not Acceptable

---

## Phase 3 — Ch04: OData in ASP.NET Core Web API

### P3.1 Cài package

`PetShop_PRN222.csproj`:
```xml
<PackageReference Include="Microsoft.AspNetCore.OData" Version="9.5.0" />
```
> 9.5.0 là bản stable mới nhất, hỗ trợ .NET 8 (slide Ch04 Slide 32 dùng 9.2.1 — dùng bản mới hơn cùng API).

### P3.2 EDM model

Tạo `Models/PetShopEdmModel.cs` dùng chính entity hiện có. Slide Ch04 nói dùng CLR types làm EDM. Entity phù hợp nhất để demo:
- `Product` (+ `Category`, `Supplier` navigation properties)
- `PetService` (+ `Category`)
- `Pet` (+ `Breed`, `Species`)

Vì entity của dự án **không** theo quy ước `Id` / `<Type>Id` (`Product.ProductId`, `PetService.ServiceId`, `Pet.Id`),
phải khai báo key tường minh — nếu không OData sẽ báo lỗi *"entity set has no keys defined"*:

```csharp
var builder = new ODataConventionModelBuilder();

builder.EntityType<Product>().HasKey(p => p.ProductId);
builder.EntityType<ProductCategory>().HasKey(c => c.CategoryId);
builder.EntityType<Supplier>().HasKey(s => s.SupplierId);
builder.EntityType<Admin>().HasKey(a => a.AdminId);
builder.EntityType<PetService>().HasKey(s => s.ServiceId);
builder.EntityType<Pet>().HasKey(p => p.Id);
builder.EntityType<Breed>().HasKey(b => b.BreedId);
builder.EntityType<Species>().HasKey(s => s.SpeciesId);

// Bỏ qua các collection navigation property để $expand không sinh subquery nặng
builder.EntityType<Product>().Ignore(p => p.OrderDetails);
builder.EntityType<Pet>().Ignore(p => p.Bookings);
builder.EntityType<Pet>().Ignore(p => p.MedicalRecords);
builder.EntityType<PetService>().Ignore(s => s.BookingServices);

builder.EntitySet<Product>("Products");
builder.EntitySet<PetService>("PetServices");
builder.EntitySet<Pet>("Pets");
```

### P3.3 Đăng ký OData service

`Program.cs`:
```csharp
builder.Services.AddControllers()
    .AddOData(options => options
        .AddRouteComponents("PetShop", PetShopEdmModel.Build())
        .Select()
        .Filter()
        .OrderBy()
        .Expand()
        .Count()
        .SkipToken()      // server-driven paging (slide Ch04 Slide 21)
        .SetMaxTop(200))
    ;
```

> Prefix là `"PetShop"` → URL đầy đủ `http://localhost:5286/PetShop/Products`.

### P3.4 OData controller

Tạo `Controllers/PetShopODataController.cs` — **attribute routing** (không dùng convention routing):
```csharp
[ODataRouteComponent("PetShop")]
public sealed class PetShopODataController : ODataController
{
    [HttpGet("PetShop/Products")]
    [EnableQuery(PageSize = 50, MaxTop = 200)]
    public IActionResult GetProducts() => Ok(_db.Products.AsNoTracking().Where(p => !p.IsDeleted));

    [HttpGet("PetShop/Products({key})")]
    [EnableQuery]
    public IActionResult GetProduct([FromRoute] int key) =>
        Ok(SingleResult.Create(_db.Products.AsNoTracking()
            .Where(p => p.ProductId == key && !p.IsDeleted)));
}
```
Trả `IQueryable` → OData middleware tự áp `$filter/$select/$orderby/$top/$skip/$expand/$count`.
Dùng `SingleResult.Create(...)` cho endpoint trả 1 bản ghi để giữ đúng cấu trúc payload OData (`@odata.context` trỏ tới `/$entity`).

### P3.5 Test

| URL | Kỳ vọng | Kết quả |
|---|---|---|
| `GET /PetShop/$metadata` | XML mô tả EDM | ✅ 200 |
| `GET /PetShop/Products?$filter=Price gt 40000` | chỉ sản phẩm giá > 40k | ✅ 200 |
| `GET /PetShop/Products?$select=Name,Price` | chỉ 2 field | ✅ 200 |
| `GET /PetShop/Products?$orderby=Price desc&$top=5` | 5 sản phẩm đắt nhất | ✅ 200 |
| `GET /PetShop/Products?$count=true&$top=2` | kèm `@odata.count` | ✅ 200 (`@odata.count: 30`) |
| `GET /PetShop/Products?$expand=Category,Supplier` | kèm Category/Supplier | ✅ 200 |
| `GET /PetShop/Products?$skip=2&$top=2` | phân trang | ✅ 200 |
| `GET /PetShop/Products({key})` | 1 bản ghi (`@odata.context ... /$entity`) | ✅ 200 |
| `GET /PetShop/Products/$count` | tổng số bản ghi (dạng số thuần) | ✅ 200 (`30`) |
| `GET /PetShop/PetServices`, `/PetShop/Pets` | các entity set khác | ✅ 200 |

> **Lưu ý về prefix:** OData route prefix là `PetShop` (không phải `odata`), tức URL đầy đủ là
> `http://localhost:5286/PetShop/Products`. Trang `$odata` (OData Endpoint Routing Debugger)
> chỉ có ở môi trường Development — dùng để tra cứu nhanh tất cả route hợp lệ.

> **Lưu ý về convention routing:** OData convention routing gắn entity set theo **tên controller**.
> Các tên `ProductsController` / `PetsController` / `PetServicesController` đã bị chiếm bởi API REST cũ,
> nên controller OData phải dùng **attribute routing** với template OData:
> `[HttpGet("PetShop/Products({key})")]`. Khi đó `{key}` là route parameter của ASP.NET Core,
> không phải template OData — middleware OData sẽ tự parse key từ segment.

> **Lưu ý về `$count` path segment:** `/PetShop/Products/$count` (dạng path segment, không phải query option)
> **không** được OData middleware tự xử lý trong 9.5 — phải khai báo route thủ công
> `[HttpGet("PetShop/Products/$count")]` và trả về `Ok(count)`.

---

## Phase 4 — Ch06: Security — JWT Bearer Authentication

> ✅ **ĐÃ HOÀN THÀNH (02/11/2026)** — xem phần "Kết quả thực tế" ở cuối phase này.

### P4.1 Cài package

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.33" />
```
> 8.0.33 là bản patch mới nhất trong nhánh 8.x (dòng 9/10 yêu cầu .NET 9/10).

### P4.2 Cấu hình `JwtSettings`

`appsettings.json`:
```json
"JwtSettings": {
  "Issuer": "PetShop",
  "Audience": "PetShopClients",
  "Key": "<chuỗi 32+ ký tự>",
  "ExpireMinutes": 60
}
```

### P4.3 Tạo `Services/JwtTokenService.cs`

```csharp
public interface IJwtTokenService
{
    string CreateToken(AuthResponseDto user);
}

public sealed class JwtTokenService : IJwtTokenService
{
    // HMAC-SHA256, claim: sub(userId), email, name, role
    // -> JwtSecurityToken -> WriteToken
}
```

### P4.4 Đăng ký scheme

`Program.cs`:
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();
```

### P4.5 Endpoint login trả JWT

`AuthController.Login` → trả **cả** `AuthResponseDto` + `accessToken` + `tokenType` + `expiresAt`:
```csharp
public sealed record AuthTokenResponseDto(
    int UserId, string Name, string Email, string Role, string Status,
    string AccessToken, string TokenType, int ExpiresInSeconds);
```
> Giữ `LoginRequestDto`/`AuthResponseDto` cũ cũng dùng được (backward compatible).

### P4.6 Chuyển API "nhạy cảm" sang `[Authorize]`

| Controller | Policy |
|---|---|
| `PayrollController` | `[Authorize(Roles="admin")]` |
| `AttendanceController` | `[Authorize(Roles="admin,staff")]` |
| `ShiftExchangeController` | `[Authorize(Roles="admin,staff")]` |
| `ProductsController` (write) | `[Authorize(Roles="admin,staff")]` |
| `SpaBookingController` (write) | `[Authorize(Roles="admin,staff,customer")]` |

> **Lưu ý quan trọng:** project chạy 100% local, Blazor Server đang dùng **session header** (`X-User-Id`/`X-User-Role`) chứ không phải JWT. Nếu bắt buộc JWT thì phải đồng bộ cả Blazor.
> → **Thiết kế an toàn (double-tracking)**: API trả JWT **và** giữ header mechanism hiện tại. Guard check **một trong hai**:
> - Header mechanism (`X-User-Id` + `X-User-Rol e`) → cho Blazor internal call
> - JWT Bearer → cho external client (Postman/JS client demo)
> → Như vậy demo JWT (slide Ch06 Slide 41) hoặc demo Blazor Blazor đều chạy được.

### P4.7 Client-side validation

Slide Ch05 Slide 30 nói client-side validation bằng jQuery Validate. PetShop là Blazor Server → validation chạy **server-side** trong Blazor (`EditForm` + `DataAnnotationsValidator`) — **đã có sẵn**. Nhưng `Components/Pages/Register.razor` / `Login.razor` chưa dùng `DataAnnotationsValidator`. → Phase 1 sẽ thêm.

### ✅ Kết quả thực tế (02/11/2026)

**Files đã tạo / sửa:**

| File | Vai trò |
|---|---|
| `Services/JwtTokenService.cs` | `JwtSettings` + `IJwtTokenService` + `JwtTokenService` (HS256) |
| `Services/SessionHeaderAuthenticationHandler.cs` | `AuthSchemes` + scheme `Session` đọc header `X-User-Id`/`X-User-Role` |
| `Controllers/AuthController.cs` | Thêm `POST /api/auth/token` + `GET /api/auth/me` (`[Authorize]`) |
| `Models/AuthDtos.cs` | Thêm `TokenResponseDto` + `CurrentUserDto` |
| `Program.cs` | `AddAuthentication` + `AddPolicyScheme` (chuyển tiếp Bearer/Session) + `UseAuthentication()` |
| `PetShop_PRN222.csproj` | Thêm `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.33 |
| `appsettings.example.json` / `appsettings.Development.json` | Khối `JwtSettings` |
| `tests/PetShop.Tests/JwtTokenServiceTests.cs` | 8 unit test (claim, chữ ký sai, hết hạn, khoá quá ngắn) |

**Cơ chế dual-mode (thay vì bắt buộc JWT cho cả Blazor):**

```
Request ──> AddPolicyScheme("Default")
             ├── có "Authorization: Bearer ..."  ──> JwtBearer  (client ngoài)
             └── không có                        ──> Session    (self-HTTP của Blazor)
```

**Cấu hình `JwtSettings` trong `appsettings.Development.json`:**

```json
{
  "JwtSettings": {
    "Issuer": "PetShop",
    "Audience": "PetShopClients",
    "Key": "petshop-prn232-local-dev-signing-key-32bytes-min",
    "ExpiresInMinutes": 120
  }
}
```

**Smoke test đã chạy (app thật, DB thật):**

| Test | Kết quả |
|---|---|
| `GET /api/auth/me` không token | ✅ 401 |
| `POST /api/auth/token` sai mật khẩu | ✅ 401 + message tiếng Việt |
| `POST /api/auth/token` `leb@petshop.com` / `staff123` | ✅ 200 + accessToken (role `staff`) |
| `GET /api/auth/me` với Bearer token hợp lệ | ✅ 200, đúng userId/role/email |
| `GET /api/auth/me` với Bearer token sai | ✅ 401 |
| `GET /api/auth/me` với header `X-User-Id`/`X-User-Role` | ✅ 200 (self-HTTP nội bộ) |
| Regression: 6 OData route + 6 REST route | ✅ tất cả 200 |
| `dotnet test` | ✅ **30/30 passed** (22 cũ + 8 mới) |

**Quyết định thiết kế:** endpoint `GET /api/auth/me` dùng `[Authorize]` là endpoint đầu tiên
trong project bắt buộc xác thực — dùng làm ví dụ/demo cho slide Ch06. Các endpoint còn lại
**chưa** gắn `[Authorize]` để không phá vỡ luồng hiện tại (xem AGENTS.md §0 — dự án chạy
hoàn toàn local, bảo mật không phải ưu tiên).

---

## Phase 5 — Ch09: gRPC Service

### P5.1 Cài package

```xml
<PackageReference Include="Grpc.AspNetCore" Version="2.85.0" />
<PackageReference Include="Grpc.Tools" Version="2.85.0" PrivateAssets="All" />
```
Client (nếu cần): `Grpc.Net.Client` 2.85.0

### P5.2 File `.proto`

Tạo `Protos/pets.proto`:
```protobuf
syntax = "proto3";
package petshop;

service PetCatalog {
  service PetCatalog {
  rpc GetPets(PetQuery) returns (PetList);
  rpc GetPetById(PetId) returns (Pet);
  rpc WatchBookings(BookingQuery) returns (stream BookingEvent);
}
```
> `service PetCatalog {` bị lặp trong ví dụ — **sửa**: chỉ 1 `service` block.

### P5.3 Cấu hình csproj

```xml
<ItemGroup>
  <Protobuf Include="Protos\pets.proto" GrpcServices="Server" />
</ItemGroup>
```

### P5.4 Implement service

Tạo `Services/Grpc/PetCatalogService.cs` kế thừa `PetCatalog.PetCatalogBase`:
- `GetPets` → query EF Core (read-only, `AsNoTracking`)
- `GetPetById` → `NotFound` nếu không tồn tại
- `WatchBookings` → **server streaming** (slide Ch09 Slide 17) — demo 4 loại RPC

### P5.5 Đăng ký + map

`Program.cs`:
```csharp
builder.Services.AddGrpc();
...
app.MapGrpcService<PetCatalogService>();
```

### P5.6 Client demo

Tạo `tests/PetShop.Tests` hoặc `Services/Grpc/PetCatalogGrpcClient.cs`:
- `GrpcChannel.ForAddress("http://localhost:5286")`
- `new PetCatalogClient(channel)` → gọi `GetPetsAsync`
- Test: gRPC server có phản hồi không?

---

## Phase 6 — Ch07: JavaScript Client

### P6.1 HTML demo page

Tạo `Components/Pages/ApiDemo.razor` (route `/api-demo`):
- Bảng hiển thị sản phẩm / thú cưng
- Nút "Load" → gọi API bằng **jQuery AJAX** (slide Ch07 Slide 15)
- Nút "Load by ID" → AJAX GET có tham số
- Nút "Update" → AJAX PUT
- Nút "Create" → AJAX POST
- Nút "Gọi gRPC qua gateway" → gọi REST thay vì gRPC trực tiếp (trình duyệt không gọi được gRPC)

> **Lưu ý thực tế:** slide Ch07 dùng **Razor Pages/MVC view** có `<script>` thuần. PetShop là Blazor Server, không có MVC view. → Dùng **Blazor page + JSInterop**, hoặc tạo **MVC View** riêng cho demo.
> → **Chọn: tạo `Controllers/ApiDemoController.cs` (MVC) + `Views/ApiDemo/Index.cshtml`** — khớp 100% với slide, không đụng vào Blazor.

### P6.2 jQuery

- `wwwroot/lib/jquery/jquery.min.js` — tải từ CDN hoặc nuget
- Demo dùng `$.ajax({ url, type, data, success })`

### P6.3 Test bằng Postman / browser

- `GET /api/products` → bảng fill dữ liệu
- `POST /api/products` → tạo mới
- `PUT /api/products/{id}` → cập nhật

> Hiện tại `ProductsController` chỉ có **GET** (không có POST/PUT/DELETE). Cần thêm CRUD để demo Ch07 đầy đủ.

---

## Phase 7 — Ch10 (phần Docker): Containerize

### P7.1 Dockerfile

Tạo `Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY PetShop_PRN222.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Development
EXPOSE 80
ENTRYPOINT ["dotnet", "PetShop_PRN222.dll"]
```

### P7.2 .dockerignore

```
bin/
obj/
.git/
tests/
_slides/
drive-download-*/
```

### P7.3 docker-compose.yml

```yaml
services:
  petshop-api:
    build: .
    ports: ["5286:80"]
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Server=host.docker.internal,1433;Database=SHOP_PET_Database;...
```

### P7.4 Test

```bash
docker build -t petshop .
docker run -it --rm -p 5286:80 --name petshop-container petshop
```

---

## Verification — cách kiểm tra từng phase

| Phase | Kiểm tra | Lệnh |
|---|---|---|
| P0 | Build sạch | `dotnet build PetShop_PRN222.sln` → 0 error 0 warning |
| P0 | Test pass | `dotnet test` → 22/22 |
| P1 | 400 tự động | `POST /api/auth/register` với email sai → 400 + `ValidationProblemDetails` |
| P1 | ProblemDetails | Exception bất kỳ → `application/problem+json` |
| P2 | Content negotiation | `Accept: application/xml` → XML; `Accept: text/csv` → CSV |
| P3 | OData | `GET /PetShop/Products?$filter=Price gt 40000&$top=5` → 200 ✅ |
| P4 | JWT | `POST /api/auth/login` → `accessToken`; `GET /api/payroll` với `Authorization: Bearer <token>` → 200 |
| P5 | gRPC | Client gọi `GetPets` → nhận `PetList` |
| P6 | JS client | Mở `/api-demo` → bấm Load → bảng có dữ liệu |
| P7 | Docker | `docker build` OK, `docker run` → `http://localhost:5286` trả 200 |

---

## Thứ tự đề xuất & ước lượng

| Phase | Nội dung | Ước lượng | Rủi ro |
|---|---|---|---|
| **P0** | Dọn code chết + net an toàn | 1–2 giờ | Thấp |
| **P1** | Validation + ProblemDetails | 2–3 giờ | Thấp |
| **P2** | Content Negotiation + CSV formatter | 1–2 giờ | Thấp |
| **P3** | OData | 3–4 giờ | Trung bình (version API) |
| **P4** | JWT | 4–5 giờ | Cao (đụng auth flow) |
| **P4b** | JWT client-side integration | 2 giờ | Trung bình |
| **P5** | gRPC | 3–4 giờ | Trung bình |
| **P6** | JS Client | 2–3 giờ | Thấp |
| **P7** | Docker | 1 giờ | Thấp |

**Tổng: ~20–28 giờ**

### Có thể bỏ nếu thiếu thời gian (theo thứ tự ưu tiên cắt)

1. **P7 Docker** — ít điểm nhất về mặt kiến thức, chỉ là config
2. **P2 CSV formatter** — XML formatter của framework đã đủ để demo content negotiation
3. **P6 JS client** — trùng lặp chức năng với Blazor hiện tại

---

## Rủi ro & cách xử lý

| Rủi ro | Mức | Xử lý |
|---|---|---|
| JWT phá vỡ flow Blazor hiện tại | 🔴 Cao | **Double-tracking**: API trả JWT + giữ header cũ; guard accept cả hai |
| OData 9.x đổi API so với 8.x | 🟡 TB | Dùng `ODataConventionModelBuilder` + `AddRouteComponents` (API ổn định) |
| EF Core `Include` + OData `$expand` xung đột | 🟡 TB | OData controller trả `IQueryable` **không** `Include` thủ công, để middleware lo |
| `dotnet restore` cần mạng | 🟡 TB | Đã kiểm tra nuget.org reachable (HTTP 200) |
| Thêm package làm tăng thời gian build | 🟢 Thấp | Chấp nhận |

---

## Phần KHÔNG làm (nêu rõ để không bị hiểu nhầm)

- ❌ **WCF / CoreWCF** (Ch08) — không phải .NET 8 cross-platform, chỉ chạy Windows, `CoreWCF` là dự án độc lập
- ❌ **Tách microservices thật** (Ch10) — project chạy local, 1 DB, tách service chỉ gây rắc rối
- ❌ **Docker Swarm / Kubernetes / Azure Service Bus / Service Fabric** — vượt xa phạm vi bài học
- ❌ **Identity UI / Razor class library** — thay thế cơ chế session hiện tại quá lớn
- ❌ **AutoMapper** — slide Ch02/Ch05 nhắc nhưng map tay ở 63 file Model hiện tại rõ ràng hơn, thêm AutoMapper là chi phí không đổi lại
- ❌ **Refresh token flow** — slide có nhắc, nhưng local project không cần, sẽ thêm bề mặt lỗi
- ❌ **Redis / distributed cache** — `AddDistributedMemoryCache` đủ cho local
