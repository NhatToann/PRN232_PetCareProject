# ARCHITECTURE_REQUIREMENTS — Yêu cầu kiến trúc chính thức (PRN232 PetCareProject)

> **Trạng thái:** 🟡 **CHỜ CHỦ DỰ ÁN DUYỆT**
> **Đây là bản chính thức** — mọi thay đổi code về sau phải đối chiếu với tài liệu này.
> **Tài liệu đồng hành:** `docs/_inventory/ARCHITECT_DESIGN.md` (bản thiết kế đầy đủ, 14 lát cắt, ma trận quyết định).
> **Nguồn:** 11 file text slide trong `_slides_text/` + 4 file inventory trong `docs/_inventory/`.
> **Ngày lập:** 08/11/2026.

---

## 0. Nguyên tắc của tài liệu này

| # | Nguyên tắc |
|---|---|
| 1 | **Chỉ ghi công nghệ slide THẬT SỰ nêu.** Mỗi mục bắt buộc kèm `(ChXX, Slide YY)`. |
| 2 | Slide không nói → ghi rõ **"slide không đề cập"**. Không suy diễn. |
| 3 | Phân 3 mức: **Yêu cầu bắt buộc** / **Khuyến nghị** / **Không áp dụng (có lý do)**. |
| 4 | Mỗi mục đều có dòng ánh xạ "đã có ở đích / file / trạng thái". |
| 5 | Quyết định "không áp dụng" phải nêu lý do + slide nào nói. |

> **Quy ước trích dẫn:** `ChXX` = số chương (00–10), `Slide YY` = số thứ tự slide trong file text của chương đó (đã trích sẵn).

---

# 1. YÊU CẦU BẮT BUỘC

> Mục này = những gì slide nêu rõ và **phải có** trong PRN232 để đạt "giống 100% bản gốc".

## 1.1 Nền tảng & môi trường

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.01 | `.NET 8 or later` | (Ch00, Slide 8) | Có | `PetShop_PRN222.csproj` → `<TargetFramework>net8.0</TargetFramework>` | ✅ Đạt |
| B.02 | `C# 12` / `.NET 8` | (Ch00, Slide 7) | Có | `PetShop_PRN222.csproj` (`net8.0` mặc định C# 12) | ✅ Đạt |
| B.03 | `MS SQL Server 2016 or later` | (Ch00, Slide 8) | Có | `appsettings.json` (`ConnectionStrings`), `Data/ShopPetDatabaseContext.cs` | ✅ Đạt |
| B.04 | `Visual Studio 2022 or later` | (Ch00, Slide 8) | — | Môi trường dev, không phải mã nguồn | ✅ Đạt |
| B.05 | `Entity Framework Core` (ORM) | (Ch02, Slide 6–8) | Có | `Microsoft.EntityFrameworkCore` **8.0.8** + `.SqlServer` **8.0.8** + `.Design` + `.Tools` — `PetShop_PRN222.csproj` | ✅ Đạt |
| B.06 | Database-First: `Scaffold-DbContext` + `Microsoft.EntityFrameworkCore.Tools` | (Ch02, Slide 11–12) | Có | `Data/ShopPetDatabaseContext.cs` — 39 `DbSet`, `OnModelCreating` có `HasKey(...).HasName("PK__...")` | ✅ Đạt |
| B.07 | Code-First: `Add-Migration` / `Update-Database` | (Ch02, Slide 13, 19) | Có (công cụ) | `Microsoft.EntityFrameworkCore.Tools` 8.0.8 đã cài; `SHOP_PET_Database.sql` là script đã scaffold | ✅ Đạt *(áp dụng khi user yêu cầu — AGENTS.md §2.5 cấm tự ý đổi schema)* |
| B.08 | `DbContext` đăng ký qua DI + Seed data | (Ch02, Slide 17) | Có | `Program.cs` → `AddDbContext<ShopPetDatabaseContext>(UseSqlServer)` | ✅ Đạt |
| B.09 | Connection string trong `appsettings.json` | (Ch02, Slide 19) | Có | `appsettings.json`, `appsettings.Development.json`, `appsettings.example.json` | ✅ Đạt |
| B.10 | **3-Layer Application**: Presentation / Business / Data Access | (Ch02, Slide 5) | Có | `Components/` + `Controllers/` → `Services/` → `Repositories/` + `Data/` | ✅ Đạt |

## 1.2 Models, DTO & Data Access

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.11 | **DTO** tách khỏi Entity; chỉ dùng cho request/response | (Ch02, Slide 20–22) | Có | 22 DTO/record trong `Models/` (`AuthDtos.cs`, `ProductDto.cs`, `SpaBookingDtos.cs`, `PetCrudDtos.cs`, `BoardingDtos.cs`, `BreedPricingDtos.cs`, `ShiftExchangeDtos.cs`, `PayOSDtos.cs`, `FrontendDtos.cs`…) | ✅ Đạt |
| B.12 | **Repository Pattern** — abstraction layer giữa data access và business logic | (Ch05, Slide 32–40) | Có | 7 cặp `Interfaces/I*Repository.cs` + `Repositories/*Repository.cs` | ✅ Đạt |
| B.13 | Interface abstraction để testable; controller mỏng | (Ch02, Slide 26) | Có | 30 interface trong `Interfaces/`; 22 controller trong `Controllers/` | ✅ Đạt |
| B.14 | `ServiceExtensions` class đăng ký dependency | (Ch05, Slide 34) | Có | `Services/SelfHttpClientExtensions.cs`, `Services/MvcBuilderExtensions.cs` | ✅ Đạt |

## 1.3 REST API nền (Ch01)

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.15 | `[ApiController]` + **attribute routing bắt buộc** | (Ch01, Slide 35) | Có | `Controllers/*.cs` — 100% dùng `[Route("api/...")]` | ✅ Đạt |
| B.16 | `ControllerBase` helper: `Ok`, `NotFound`, `BadRequest`, `NoContent`, `Accepted`, `CreatedAtAction`, `Content`, `PhysicalFile`, `TryValidateModel` | (Ch01, Slide 21–22) | Có | `Controllers/*.cs` | ✅ Đạt |
| B.17 | HTTP verbs `GET/POST/PUT/DELETE` + attributes `HEAD/OPTIONS/PATCH` | (Ch01, Slide 24, 28, 32–33) | Có | `[HttpGet] [HttpPost] [HttpPut] [HttpPatch] [HttpDelete]` trong `Controllers/*.cs` | ✅ Đạt |
| B.18 | Status codes 200/201/204/400/403/404/500 | (Ch01, Slide 25) | Có | `Services/ApiExceptionMiddleware.cs` + `Controllers/*.cs` | ✅ Đạt |
| B.19 | `ModelStateInvalidFilter` → **automatic 400** | (Ch01, Slide 35) | Có | Framework mặc định + DataAnnotations trong `Models/*Dtos.cs` | ✅ Đạt |
| B.20 | **`ProblemDetails`** cho status ≥ 400 | (Ch01, Slide 37) | Có | `Services/ApiExceptionMiddleware.cs` → `application/problem+json` | ✅ Đạt |
| B.21 | `ModelState` / `RouteData` / `Request` / `Response` properties | (Ch01, Slide 21) | Có | Framework `ControllerBase` | ✅ Đạt |

## 1.4 Content Negotiation & Media Formatters (Ch03)

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.22 | Content negotiation qua `Accept` header (`application/json`, `application/xml`) | (Ch03, Slide 21–25) | Có | `Services/XmlOutputFormatter.cs`, `Services/MvcBuilderExtensions.cs`, `Program.cs` | ✅ Đạt |
| B.23 | **Custom output formatter** kế thừa `TextOutputFormatter`, override `CanWriteType` + `WriteResponseBodyAsync` | (Ch03, Slide 17, 29) | Có | `Services/CsvOutputFormatter.cs` (`text/csv`) | ✅ Đạt |
| B.24 | Đăng ký formatter vào `MvcOptions.OutputFormatters` | (Ch03, Slide 17) | Có | `Program.cs` → `AddControllersWithViews(options => options.OutputFormatters.Add(new CsvOutputFormatter()))` | ✅ Đạt |
| B.25 | `System.Text.Json` — namespace serialize/deserialize JSON | (Ch03, Slide 19) | Có | `System.Text.Json` (mặc định ASP.NET Core 8) | ✅ Đạt |
| B.26 | `JsonIgnore` để ẩn property khỏi JSON | (Ch03, Slide 11) | Có | `Models/*.cs` | ✅ Đạt |
| B.27 | `ObjectResult` hỗ trợ negotiation; `ContentResult`/`JsonResult` không | (Ch03, Slide 24) | Có | Controller dùng `Ok(obj)` / `ActionResult<T>` | ✅ Đạt |

## 1.5 OData (Ch04)

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.28 | Package `Microsoft.AspNetCore.OData` | (Ch04, Slide 4, 28) | Có | `Microsoft.AspNetCore.OData` **9.5.0** — `PetShop_PRN222.csproj` | ✅ Đạt *(version 9.5.0 ≠ 9.2.1 của slide — xem §3 "Không áp dụng")* |
| B.29 | EDM model (`ODataConventionModelBuilder`) + khai báo key tường minh | (Ch04, Slide 29, 44–46) | Có | `Models/PetShopEdmModel.cs` — 8 entity có `HasKey` | ✅ Đạt |
| B.30 | `AddOData()` + đăng ký `Select/Filter/Count/OrderBy/Expand` | (Ch04, Slide 34, 47) | Có | `Program.cs` → `.AddRouteComponents("PetShop", PetShopEdmModel.Build()).Select().Filter().OrderBy().Expand().Count().SkipToken().SetMaxTop(200)` | ✅ Đạt |
| B.31 | Trả `IQueryable` + `[EnableQuery]` để middleware xử lý | (Ch04, Slide 35) | Có | `Controllers/PetShopODataController.cs` | ✅ Đạt |
| B.32 | Query options `$filter $sort $select $top $skip $expand $count $inlinecount` | (Ch04, Slide 11, 36–38) | Có | `Controllers/PetShopODataController.cs` | ✅ Đạt |
| B.33 | `$count` path segment (`/PetShop/Products/$count`) | (Ch04, Slide 38, 40) | Có | `Controllers/PetShopODataController.cs` — route thủ công | ✅ Đạt |
| B.34 | Server-driven paging | (Ch04, Slide 21) | Có (một phần) | `Program.cs` → `.SkipToken()` | ✅ Đạt *(không trả "next link")* |
| B.35 | OData metadata document (`/$metadata`) | (Ch04, Slide 52) | Có | `GET /PetShop/$metadata` | ✅ Đạt |

## 1.6 Binding, Validation & Routing (Ch05)

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.36 | `UseRouting()` + `UseEndpoints()` | (Ch05, Slide 4) | Có | `Program.cs` → `MapControllers()`, `MapRazorComponents<App>()` | ✅ Đạt |
| B.37 | **Attribute Routing** là cách ưu tiên | (Ch05, Slide 5) | Có | `Controllers/*.cs` | ✅ Đạt |
| B.38 | Route variable `{param}` + query string | (Ch05, Slide 6) | Có | `api/pets/{petId:int}`, `?customerId=` | ✅ Đạt |
| B.39 | Binding source attributes `[FromBody] [FromForm] [FromHeader] [FromQuery] [FromRoute] [FromServices]` | (Ch05, Slide 17–19) | Có | `Controllers/PetsController.cs` (`[FromForm] IFormFile`), các controller khác | ✅ Đạt |
| B.40 | Validation attributes `[Required] [EmailAddress] [StringLength] [Range] [RegularExpression] [Compare] [Phone] [Url] [CreditCard] [ValidateNever]` | (Ch05, Slide 26–27) | Có | `Models/AuthDtos.cs`, `Models/ProductDto.cs`, `Models/BoardingDtos.cs`, `Models/SpaBookingDtos.cs`, `Models/PetCrudDtos.cs`, `Models/BreedPricingDtos.cs` | ✅ Đạt |
| B.41 | `ModelState` / `ModelStateDictionary.ClearValidationState` + `TryValidateModel` | (Ch05, Slide 24–25) | Có | Framework + `Services/ApiExceptionMiddleware.cs` | ✅ Đạt |
| B.42 | **Server-side validation** trước khi action chạy | (Ch05, Slide 29) | Có | `[ApiController]` + `DataAnnotationsValidator` trong `.razor` | ✅ Đạt |
| B.43 | `[ProducesResponseType]` mô tả status code | (Ch01, Slide 27) | Có (một phần) | `Controllers/*.cs` — cần rà soát bổ sung | 🟡 **Cần rà soát** |

## 1.7 Security — JWT (Ch06)

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.44 | Package `Microsoft.AspNetCore.Authentication.JwtBearer` | (Ch06, Slide 16, 31) | Có | **8.0.33** — `PetShop_PRN222.csproj` | ✅ Đạt |
| B.45 | `AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, ...)` + `Configuration.Bind("JwtSettings", options)` | (Ch06, Slide 16) | Có | `Program.cs` → `.AddJwtBearer("Bearer", options => Configuration.Bind("JwtSettings", options))` | ✅ Đạt |
| B.46 | JWT 3 phần Header/Payload/Signature; ký HMAC SHA256 | (Ch06, Slide 32–33) | Có | `Services/JwtTokenService.cs` (HS256) | ✅ Đạt |
| B.47 | Endpoint login trả **access token** | (Ch06, Slide 40–41) | Có | `Controllers/AuthController.cs` → `POST /api/auth/token` trả `TokenResponseDto` | ✅ Đạt |
| B.48 | Client gửi token qua header `Authorization: Bearer <token>` | (Ch06, Slide 29, 41) | Có | `Program.cs` → `AddPolicyScheme("Default")` forward | ✅ Đạt |
| B.49 | `AllowAnonymous` cho endpoint đăng nhập | (Ch06, Slide 27) | Có | `Controllers/AuthController.cs` → `[AllowAnonymous]` trên `POST /api/auth/token` | ✅ Đạt |
| B.50 | `UseAuthentication()` + `UseAuthorization()` middleware | (Ch06, Slide 25–26) | Có | `Program.cs` (đúng thứ tự) | ✅ Đạt |
| B.51 | Authentication **schemes** đăng ký qua `AuthenticationBuilder` | (Ch06, Slide 15–16) | Có | `Services/SessionHeaderAuthenticationHandler.cs` (scheme `Session`) + scheme `Bearer` | ✅ Đạt |
| B.52 | `AuthorizeAttribute` / `AllowAnonymousAttribute` trong `Microsoft.AspNetCore.Authorization` | (Ch06, Slide 18) | Có | `Controllers/AuthController.cs` → `[Authorize]` trên `GET /api/auth/me` | ✅ Đạt |
| B.53 | Best practice: validate all input, no sensitive data in URL, restrict method execution, generic error messages | (Ch06, Slide 3–5) | Có (một phần) | Input validation ✅; generic error ✅ qua ProblemDetails; ⚠️ `api/pets/.../pricing?basePrice=` còn cho client override giá | 🟡 **Cần xử lý (LS-13)** |
| B.54 | TLS/SSL transport security | (Ch06, Slide 7) | Có | `Program.cs` → `UseHttpsRedirection()`, `UseHsts()` | ✅ Đạt |

## 1.8 gRPC (Ch09)

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.55 | Package `Grpc.AspNetCore` | (Ch09, Slide 29) | **Chưa có** | — | 🔴 **Thiếu → lát cắt LS-09** |
| B.56 | Packages client: `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools` | (Ch09, Slide 42) | **Chưa có** | — | 🔴 **Thiếu → LS-09** |
| B.57 | **Protocol Buffers proto3** — file `.proto` trong thư mục `Protos/` | (Ch09, Slide 9–13, 35) | **Chưa có** | — | 🔴 **Thiếu → LS-09** |
| B.58 | `AddGrpc()` + `MapGrpcService<T>()` | (Ch09, Slide 29–30, 36) | **Chưa có** | — | 🔴 **Thiếu → LS-09** |
| B.59 | **4 loại RPC**: Unary / Server streaming / Client streaming / Bidirectional streaming | (Ch09, Slide 16–18) | **Chưa có** | — | 🔴 **Thiếu → LS-09** |
| B.60 | gRPC service dùng **DI** + **Logging** (resolve `ILogger` qua constructor) | (Ch09, Slide 31) | **Chưa có** | — | 🔴 **Thiếu → LS-09** |
| B.61 | Client Controller gọi gRPC (`GrpcChannel.ForAddress`) | (Ch09, Slide 44) | **Chưa có** | — | 🔴 **Thiếu → LS-09** |
| B.62 | gRPC host được bởi Kestrel / IIS | (Ch09, Slide 30) | Có | `Program.cs` (Kestrel mặc định) | ✅ Đạt |

## 1.9 JavaScript Client (Ch07)

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.63 | **jQuery** — cú pháp `$(selector).action()`, selectors, event methods | (Ch07, Slide 6–12) | **Chưa có** | — | 🔴 **Thiếu → LS-10** |
| B.64 | **AJAX** qua `$.ajax()`, `$.get()`, `$.post()`, `$.param()` | (Ch07, Slide 13–15) | **Chưa có** | — | 🔴 **Thiếu → LS-10** |
| B.65 | **`XMLHttpRequest`** demo song song | (Ch07, Slide 4–5, 27, 30, 33, 36) | **Chưa có** | — | 🔴 **Thiếu → LS-10** |
| B.66 | Demo trên **Razor View / MVC** (HTML + `<script>` thuần) | (Ch07, Slide 16, 25, 28, 31, 34) | **Chưa có** | Không có `Views/` | 🔴 **Thiếu → LS-10** |
| B.67 | **`IRepository`** interface + implementation + CRUD controller | (Ch07, Slide 18–24) | Có (Repository) / **Thiếu CRUD write** | `Interfaces/IProductRepository.cs`, `Repositories/ProductRepository.cs`, `Controllers/ProductsController.cs` | 🟡 **Thiếu POST/PUT/DELETE → LS-10** |
| B.68 | **IoC container** cấu hình trong `Program.cs` | (Ch07, Slide 21) | Có | `Program.cs` — 45+ registration DI | ✅ Đạt |

## 1.10 Docker (Ch10)

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.69 | **`Dockerfile`** multi-stage `mcr.microsoft.com/dotnet/sdk:8.0` → `aspnet:8.0` | (Ch10, Slide 28) | **Chưa có** | — | 🔴 **Thiếu → LS-12** |
| B.70 | `docker build -t <tag> .` / `docker run -it --rm -p <port>:<port>` | (Ch10, Slide 29–30) | **Chưa có** | — | 🔴 **Thiếu → LS-12** |

## 1.11 Chatbot AI (theo mục tiêu chương + nhu cầu "100% bản gốc")

> Slide **không có chương riêng** về chatbot; nguồn OJT có đầy đủ và là phần còn thiếu lớn nhất của đích.

| # | Yêu cầu | Slide | Đã có ở đích | File đích | Trạng thái |
|---|---|---|---|---|---|
| B.71 | AI chatbot backend (không đề cập trực tiếp trong slide) | (Ch00, Slide 5 — "RESTful Service applications") | **Chưa có** | — | 🔴 **Thiếu → LS-08** |
| B.72 | `ChatMessage` entity + `DbSet` | (không đề cập) | **Có** | `Models/ChatMessage.cs`, `Data/ShopPetDatabaseContext.cs` → `DbSet<ChatMessage> ChatMessages` | ✅ Đạt (tái dùng) |
| B.73 | DI đăng ký service qua `Program.cs` | (Ch02, Slide 17) | Có | `Program.cs` | ✅ Đạt |

---

# 2. KHUYẾN NGHỊ

> Mục này = slide có nhắc nhưng **không bắt buộc**, hoặc bổ sung chất lượng mà slide không đề cập.

## 2.1 Bổ sung do slide gợi ý nhưng chưa làm

| # | Khuyến nghị | Slide | Lý do | File đích dự kiến | Trạng thái |
|---|---|---|---|---|---|
| K.01 | **Custom input formatter** (`TextInputFormatter`, override `CanReadType` + `ReadRequestBodyAsync`) + demo import CSV | (Ch03, Slide 17, 29) | Slide mô tả cả input lẫn output formatter; đích mới có output | `Services/CsvInputFormatter.cs`, `Controllers/ProductImportController.cs` | ⬜ Chưa làm → LS-03 |
| K.02 | **Custom validation attribute** kế thừa `ValidationAttribute`, override `IsValid` | (Ch05, Slide 28) | Hợp với rule `PriceAdjustPercent` 1–500% | `Models/Validation/PriceAdjustPercentAttribute.cs` | ⬜ Chưa làm → LS-04 |
| K.03 | **Route constants** để không magic string | (Ch01, Slide 27–28) *(không nêu)* | Slide nói "Specify the format of response data using a schema" (Ch01, Slide 15) — route convention là hình thức chuẩn hoá | `Constants/ApiRoutes.cs` | ⬜ Chưa làm → LS-05 |
| K.04 | **`POST /api/auth/refreshtoken`** | (Ch06, Slide 40, 42) | Slide demo Postman có rõ 3 request: login → private API → refresh token | `Controllers/AuthController.cs`, `Models/AuthDtos.cs`, `Services/JwtTokenService.cs` | ✅ **Đã làm (Q2, 08/11/2026)** — user chốt "Có". `Services/JwtTokenService.cs`: `CreateRefreshToken` (cùng key/issuer/audience, hạn `JwtSettings:RefreshTokenExpiresInMinutes` = 10 080 phút = 7 ngày, claim riêng `token_type`), `ValidateRefreshToken` / `ValidateAccessToken` (access token **không** dùng để refresh được). `Controllers/AuthController.cs`: `POST api/auth/refreshtoken` (`[AllowAnonymous]`, body `{ refreshToken, accessToken? }` theo slide S42) ⇒ access token mới **+ rotate refresh token mới**. **KHÔNG** yêu cầu username/password. **Verify:** login 200 · refresh hợp lệ 200 (có rotate) · access token sai loại **401** · rác 401 · rỗng 400 · `/api/auth/me` 200. Test: **14 test** mới ⇒ **93/93 pass**. Xem `docs/PARITY_REPORT.md` §4.13 |
| K.05 | **`.dockerignore`** | (Ch10, Slide 28–29) *(không nêu tên file)* | Best-practice đi kèm Dockerfile | `.dockerignore` | ⬜ Chưa làm → LS-12 |
| K.06 | **`docker-compose.yml`** | (Ch10, Slide 28–30) *(không nêu)* | Tiện cho demo 1 lệnh | `docker-compose.yml` | ⬜ Chưa làm → LS-12 |
| K.07 | **`Microsoft.EntityFrameworkCore.InMemory`** cho demo OData không cần DB | (Ch04, Slide 43) | Slide demo 2 dùng InMemory | `PetShop_PRN222.csproj` | ⬜ Chưa làm (chưa cần) |
| K.08 | **`[ProducesResponseType]`** đầy đủ cho Swagger | (Ch01, Slide 27) | Swagger là công cụ demo chính của Ch06/Ch07 | `Controllers/*.cs` | 🟡 Rà soát → LS-05 |
| K.09 | **Client-side validation** jQuery Validate 1.19.1 + `jquery-validation-unobtrusive` 3.2.11 | (Ch05, Slide 30) | Slide nêu rõ 3 script CDN; dùng cho trang demo MVC Ch07 | `wwwroot/lib/jquery/*` | ⬜ Chưa làm → LS-10 |
| K.10 | **`default-service-placeholder.png`** | (không đề cập) | Nguồn tham chiếu file này nhưng file không tồn tại → tạo để tránh 404 | `wwwroot/images/default-service-placeholder.png` | ⬜ Chưa làm → LS-12 |
| K.11 | **Vendor font Google** về `wwwroot/fonts/` | (không đề cập) | 4 file CSS ở đích đang `@import` từ `fonts.googleapis.com` → demo offline sẽ mất font | `wwwroot/fonts/*.woff2` + sửa `@font-face` | ⬜ Chưa làm → LS-12 |
| K.12 | **Tách CSS > 500 dòng** | (không đề cập) | `home.css` 2788 dòng, `service.css` 3681 dòng (đã tách 1 phần), `profile.css` 917, `checkout.css` 864, `productDetails.css` 678, `cart.css` 611 | `wwwroot/css/template/*.css` | ⬜ Chưa làm → LS-12 |
| K.13 | **SignalR** push notification thay cho pull | (không đề cập) | Slide không đề cập; là cải tiến chất lượng so với nguồn (nguồn chỉ pull-based) | `Hubs/NotificationHub.cs`, `Services/RealtimeNotificationService.cs` + `Interfaces/IRealtimeNotificationService.cs` | ✅ **Đã làm (LS-11)** — hub `/hubs/notification` (group `user:{id}`, method `JoinGroup`/`LeaveGroup`/`SendMessage`); service 3-layer dùng `IHubContext<NotificationHub>`; client SignalR trong `Components/Staff/NotificationBell.razor.cs` (fallback HTTP nếu lỗi, **không** đổi markup); gắn push vào `ShiftExchangeController` (create/accept/decline/cancel); trang demo `/signalr-demo` (không thêm vào menu). **Verify:** `POST /hubs/notification/negotiate` → 200 · `GET /hubs/notification` → 400 (đúng) · client .NET thật nhận được `ChatReceived` **và** `NotificationReceived` từ server push · **93/93** test pass |
| K.14 | **Ghi lịch sử chatbot vào DB** | (không đề cập) | Bảng `ChatMessages` đã có sẵn nhưng nguồn không dùng | `Services/ChatbotService.cs` | ⬜ Chưa làm → LS-08 (cần user duyệt — Q5) |
| K.15 | **Trang `/about`** | (không đề cập) | Nguồn React có `AboutPage.jsx`; link trong `Navigation`/`Footer` dẫn tới | `Components/Pages/About.razor` | ⬜ Chưa làm → LS-12 (cần user duyệt — Q9) |
| K.31 | **Cột `Customer.avatar_url`** (ảnh đại diện khách hàng) | (không đề cập) | Nguồn OJT lưu avatar ở `localStorage` (`userProfile:{userId}`) — đích thiếu cột ⇒ mất avatar khi xoá localStorage / đăng nhập máy khác | `Models/Customer.cs`, `Data/ShopPetDatabaseContext.cs`, `Services/AuthService.Avatar.cs`, `Repositories/AuthRepository.cs`, `Controllers/AuthController.cs` | ✅ **Đã làm (Q9)** — user duyệt "thêm vào cột url cho avatar": `ALTER TABLE Customer ADD avatar_url NVARCHAR(500) NULL` (idempotent, **không** xoá dữ liệu, **không** chạy EF migration) + EF mapping + 3-layer (`IAuthRepository` → `IAuthService` → `IAuthClient`) + API `POST/PUT api/auth/avatar` (`[Authorize]`, customerId từ claim) + UI `/account`,`/profile`,`/profile/edit` đọc avatar theo thứ tự localStorage → DB → rỗng (markup **không đổi**). **Verify:** build 0 error/0 warning · **93/93** test pass · 401 không token · 200 giá trị hợp lệ · 400 `javascript:` · 400 >500 ký tự · DB `customer_id=1` đã lưu. ✅ **`birth_date` cũng đã được user duyệt và làm xong (Q22, 08/11/2026)** — `Customer.birth_date DATE NULL` (ALTER idempotent) + EF mapping + API `POST/PUT api/auth/birth-date` + 3-layer + UI đọc ngày sinh theo thứ tự localStorage → DB → rỗng |

## 2.2 Quy ước bắt buộc của đích (không từ slide, nhưng bắt buộc theo `AGENTS.md`)

> Đây là phần **không thuộc slide** nhưng bắt buộc tuân thủ khi viết code. Nêu ở đây để tài liệu chính thức là đầy đủ.

| # | Quy ước | Nguồn | File áp dụng |
|---|---|---|---|
| K.16 | Giao tiến với chủ dự án: **TIẾNG VIỆT**; code tiếng Anh, thông báo nghiệp vụ tiếng Việt | `AGENTS.md` §2.4 | Toàn bộ |
| K.17 | **KHÔNG** `catch { }` / `catch { return false; }` — log qua `ILogger<T>` rồi trả lỗi có thông tin | `AGENTS.md` §2.2 | `Services/`, `Controllers/`, `Repositories/` |
| K.18 | **KHÔNG** `Console.WriteLine` trong code ứng dụng — dùng `ILogger<T>` + `LoggerExtensions`; không log mật khẩu/token/`ChecksumKey` | `AGENTS.md` §2.2 | Toàn bộ |
| K.19 | `Nullable = enable` → không warning CS86xx | `AGENTS.md` §2.3 | Toàn bộ |
| K.20 | `async`/`await` bắt buộc cho I/O; không `.Result`/`.Wait()` | `AGENTS.md` §2.3 | Toàn bộ |
| K.21 | EF read-only → `AsNoTracking()`; dùng `.Select(...)` projection | `AGENTS.md` §2.3 | `Repositories/` |
| K.22 | SQL thô chỉ `ExecuteSqlRawAsync` với tham số `@p0`; **cấm `FromSqlRaw`** | `AGENTS.md` §2.3 | `Services/`, `Repositories/` |
| K.23 | **File > 500 dòng / component > 25KB → tách**: C# dùng `partial class` + `<Tên>.<Phần>.cs`; Razor tách `@code` → `<Tên>.razor.cs` | `AGENTS.md` §2.3 | Toàn bộ |
| K.24 | Business logic trùng lặp → gom về `Services/`; **không copy-paste** | `AGENTS.md` §2.3 | Toàn bộ |
| K.25 | Sửa đúng phạm vi; **không tự ý đổi schema DB / seed data / `SHOP_PET_Database.sql`** | `AGENTS.md` §2.5 | Toàn bộ |
| K.26 | **KHÔNG** push / commit / force-push nếu chưa được yêu cầu | `AGENTS.md` §2.5 | Toàn bộ |
| K.27 | **KHÔNG** tự ý gắn `[Authorize]` vào endpoint đang chạy tốt | `AGENTS.md` §5 | `Controllers/` |
| K.28 | **KHÔNG** hardcode `JwtSettings:Key` — ở `appsettings.Development.json` / User Secrets, ≥ 32 ký tự | `AGENTS.md` §5 | `appsettings*.json` |
| K.29 | Đọc `.editorconfig` trước khi sửa (nullability warning → error) | `AGENTS.md` §2.3 | Toàn bộ |
| K.30 | Tên file/class: bỏ hậu tố `_temp` | `AGENTS.md` §2.3 | Toàn bộ |

---

# 3. KHÔNG ÁP DỤNG (CÓ LÝ DO)

> Mục này = công nghệ slide có nhắc tới nhưng **quyết định không áp dụng** vào PRN232. Mỗi mục nêu slide + lý do.

## 3.1 Theo chương

| # | Công nghệ slide nêu | Slide | Quyết định | Lý do |
|---|---|---|---|---|
| N.01 | **SOAP / WS-\* / WSDL** | (Ch01, Slide 9, 16) | ❌ Không áp dụng | Slide chỉ so sánh SOAP vs REST. Dự án dùng REST/JSON. |
| N.02 | **DTD / W3C Schema / RelaxNG / Schematron** cho response | (Ch01, Slide 15) | ❌ Không áp dụng | Slide nêu như best-practice lý thuyết về "schema"; không có bằng chứng slide yêu cầu công cụ/schema cụ thể. Dự án không có hợp đồng schema ngoài OpenAPI của Swagger. |
| N.03 | **AutoMapper** | (Ch02, Slide 23; Ch05, Slide 43) | ❌ Không áp dụng | Slide nói "AutoMapper is *one of the best* libraries" (không phải bắt buộc). Đích đã map tay ở 63 file Model, build sạch, 30/30 test pass. Thêm AutoMapper = thêm package + ~22 profile mapping + phải test lại, lợi ích học thuật không đổi lại. `PRN232_PLAN.md` đã chốt ❌. **Cần user duyệt nếu muốn đổi (Q3).** |
| N.04 | **`JsonMediaTypeFormatter` / `XmlMediaTypeFormatter` / `MediaTypeFormatter` / `DataContractSerializer`** | (Ch03, Slide 8–9, 12) | ❌ Không áp dụng | Đây là API của **ASP.NET Web API 2 (`System.Web`)** — không tồn tại trong ASP.NET Core 8. Slide trộn lẫn 2 thế hệ framework. Đích dùng `System.Text.Json` (Ch03, Slide 19) + `XmlSerializer` của ASP.NET Core. |
| N.05 | **`PropertyNamingPolicy = null` (chuyển camelCase → PascalCase)** | (Ch03, Slide 18, 20) | ❌ Không áp dụng | Slide nói "nếu migrate từ Web API 2.0". Đích đã quyết định **camelCase** vì client là Blazor/JS — đổi sang PascalCase sẽ phá toàn bộ client. |
| N.06 | **OData version `9.2.1`** | (Ch04, Slide 4 notes, 32, 43) | ❌ Không áp dụng | Đích đang chạy **9.5.0** với cùng API `AddRouteComponents`. Hạ version để khớp slide = rủi ro regression mà không có lợi ích học thuật. **Cần user duyệt nếu muốn đổi (Q1).** |
| N.07 | **EF Core version `8.0.15`** | (Ch04, Slide 32) | ❌ Không áp dụng | Đích đang dùng **8.0.8** đồng bộ cả 3 package EF. Chênh lệch patch 8.0.8 → 8.0.15 không đổi API; đổi version giữa lúc đang chạy = rủi ro restore. **Cần user duyệt nếu muốn đổi (Q1).** |
| N.08 | **OData `$format` (atom/json/xml)** | (Ch04, Slide 20) | ❌ Không áp dụng | OData v9 của ASP.NET Core không hỗ trợ `application/atom+xml`. JSON là mặc định; XML đã phục vụ qua `XmlOutputFormatter` riêng (Ch03). |
| N.09 | **OData Batch requests / ChangeSet** | (Ch04, Slide 25) | ❌ Không áp dụng | OData v9 ASP.NET Core không hỗ trợ batch/ChangeSet. Không có cách khác ngoài viết middleware tay — tốn công, không phục vụ bài nộp. |
| N.10 | **OData Functions / Actions / `$links` / deep insert** | (Ch04, Slide 8, 26–27) | ❌ Không áp dụng | OData v9 ASP.NET Core không có Actions/Functions server-side. Các thao tác này đã được REST endpoint phục vụ đầy đủ (`api/spa-booking/*`, `api/orders/*`). |
| N.11 | **API versioning** (URL segment / request header / query string) | (Ch04, Slide 7) | ❌ Không áp dụng | Slide nhắc trong "OData Feature Areas" nhưng không nêu package hay cách implement cụ thể. Dự án có 1 phiên bản API. |
| N.12 | **Repository Manager** (unit-of-work trên nhiều repository) | (Ch05, Slide 37) | ❌ Không áp dụng | Đích dùng `ShopPetDatabaseContext` trực tiếp trong service cho thao tác nhiều bảng (vd spa booking) — EF Core `DbContext` đã là unit-of-work. Thêm lớp Manager là thừa lớp. |
| N.13 | **ASP.NET Core Identity** (Managers/Stores, `UserManager`, `SignInManager`, password hashing, lockout, MFA, external providers) | (Ch06, Slide 19–24) | ❌ Không áp dụng | Triển khai Identity = thêm bảng Identity vào DB đang scaffold → vi phạm `AGENTS.md` §2.5 (cấm đổi schema DB). Đã có BCrypt + JWT thay thế đầy đủ. `AGENTS.md` §4.4 đã chốt ❌. |
| N.14 | **Refresh token flow** | (Ch06, Slide 40, 42) | ✅ **Đã áp dụng (Q2, 08/11/2026)** | Ban đầu `PRN232_PLAN.md` chốt ❌, nhưng user đã duyệt **"Có"** (08/11/2026) ⇒ đã port tối giản theo slide Ch06 S40–S42: `JwtTokenService.CreateRefreshToken` (claim `token_type`, hạn 7 ngày) + `POST api/auth/refreshtoken` (**rotate**). **KHÔNG** cần bảng DB (stateless) ⇒ không ảnh hưởng ràng buộc schema. Xem K.04 |
| N.15 | **`AddCookie` authentication scheme** | (Ch06, Slide 16) | ❌ Không áp dụng | Đích dùng Blazor `ProtectedLocalStorage` cho session nội bộ; không cần cookie scheme của ASP.NET Core. |
| N.16 | **Autoscaling** (vertical/horizontal, Azure VM scale sets, Service Fabric, App Service, Azure Functions) | (Ch06, Slide 8–13) | ❌ Không áp dụng | Slide nói về hạ tầng Azure cloud. Dự án chạy **100% LOCAL** (`AGENTS.md` §0) — không có Azure, không có scale set, 1 instance. |
| N.17 | **Throttling / Competing Consumers / Monitoring & diagnostics** (autoscaling patterns) | (Ch06, Slide 13) | ❌ Không áp dụng | Pattern của hệ thống nhiều instance. Với dự án local 1 instance, pattern này không có ý nghĩa vận hành. **Ghi nhận là gap tham chiếu** (không phải yêu cầu slide cụ thể). |
| N.18 | **`EventSource` (server-sent events)** | (Ch07, Slide 4) | ❌ Không áp dụng | Slide chỉ nhắc "consider using" như ghi chú bên lạnh; không có demo. Nguồn OJT cũng không dùng SSE. |
| N.19 | **React / Vite / React Router / Chart.js** | (không đề cập) | ❌ Không áp dụng | Slide Ch07 dạy **jQuery thuần**, không yêu cầu React. Đích là Blazor Server. Giữ React = 2 frontend chạy song song, không khớp mục tiêu 1 back-end app. |
| N.20 | **CoreWCF** (`CoreWCF.Http`, `CoreWCF.Primitives`) | (Ch08, Slide 29–31, 34) | ❌ Không áp dụng | Slide nói rõ CoreWCF "**không phải** bản thay thế đầy đủ WCF", chỉ hỗ trợ SOAP server-side. Đề bài PRN232 nhấn mạnh *cross-platform* — WCF chỉ chạy Windows. Đã có REST API hoàn chỉnh. |
| N.21 | **`dotnet new --install CoreWCF.Templates`**, `dotnet-svcutil`, `System.ServiceModel.*` | (Ch08, Slide 33, 39–40) | ❌ Không áp dụng | Cùng lý do N.20. |
| N.22 | **WCF attributes**: `[ServiceContract] [OperationContract] [DataContract] [DataMember] [MessageContract] [MessageHeader] [MessageBody]` | (Ch08, Slide 16–19, 28) | ❌ Không áp dụng | Attribute của hệ sinh thái WCF/SOAP. |
| N.23 | **WCF Bindings**: `NetTCPBinding`, `WSDualHttpBinding`, `WebHttpBinding`, `NetMsmqBinding`, `WSFederationHttpBinding`, `NetPeerTCPBinding`, `MsmqIntegrationBinding` | (Ch08, Slide 20–22) | ❌ Không áp dụng | Toàn bộ binding của WCF. |
| N.24 | **WCF Architecture 4 tầng**, Address-Binding-Contract, message patterns (One-Way/Request-Reply/Duplex), hosting IIS/WAS/Self-host/Windows Service, transactions, reliable sessions, behaviors | (Ch08, Slide 7–14, 23–27) | ❌ Không áp dụng | Slide giảng thuyết WCF. Đích host bằng Kestrel. |
| N.25 | **proto2** (Protocol Buffers version 2) | (Ch09, Slide 13) | ❌ Không áp dụng | Slide nói rõ "we recommend that you use proto3 with gRPC as it lets you use the full range of gRPC-supported languages, as well as avoiding compatibility issues". Dùng proto3. |
| N.26 | **Tách microservices thật** (nhiều service độc lập, mỗi service sở hữu data riêng) | (Ch10, Slide 3–5) | ❌ Không áp dụng | Slide giới thiệu khái niệm ("What are microservices?"), không yêu cầu triển khai. Đích chạy **1 DB local, 1 project** (`AGENTS.md` §0). Tách thật = phá vỡ cơ chế self-HTTP của Blazor, phá schema, vượt xa phạm vi bài nộp. `PRN232_PLAN.md` đã chốt ❌. |
| N.27 | **Azure Service Fabric** (stateless/stateful services) | (Ch10, Slide 19) | ❌ Không áp dụng | Dịch vụ Azure cloud, dự án local. |
| N.28 | **Event bus / publish-subscribe / AMQP / RabbitMQ / Azure Service Bus** | (Ch10, Slide 9–10, 16) | ❌ Không áp dụng | Slide nói "**might** be using an event bus". Không có message broker trong môi trường local. |
| N.29 | **Autoscaling patterns** (throttling, competing consumers) | (Ch10, Slide 13) | ❌ Không áp dụng | Cùng lý do N.16. |
| N.30 | **Docker Desktop for Windows / WSL 2** | (Ch10, Slide 23) | ⚠️ Áp dụng ở mức môi trường | Yêu cầu cài đặt trước khi demo, không phải mã nguồn project. |
| N.31 | **`Startup.cs`** (cấu trúc project gRPC mẫu) | (Ch09, Slide 32, 37) | ❌ Không áp dụng (1 phần) | Đích dùng **minimal hosting** của .NET 8 (`Program.cs` top-level) — không có `Startup.cs`. Slide chỉ là ví dụ cấu trúc. Giữ nguyên `Program.cs` của đích. |
| N.32 | **Identity UI / Razor class library** | (không đề cập) | ❌ Không áp dụng | `PRN232_PLAN.md` đã chốt ❌ — thay thế cơ chế session hiện tại quá lớn. |

## 3.2 Bảng tổng hợp theo chương

| Chương | Bắt buộc | Khuyến nghị | Không áp dụng | Tổng số mục |
|---|---|---|---|---|
| Ch00 — Course Introduction | B.01–B.04 | — | N.31* | 4 |
| Ch01 — ASP.NET Core & RESTful | B.15–B.21 | K.03, K.08 | N.01, N.02 | 9 |
| Ch02 — Models in Web API | B.05–B.14 | — | N.03 | 10 |
| Ch03 — Media Formatters | B.22–B.27 | K.01 | N.04, N.05 | 8 |
| Ch04 — OData | B.28–B.35 | K.07 | N.06–N.11 | 14 |
| Ch05 — Binding/Validation/Routing | B.36–B.43 | K.02, K.09 | N.12 | 10 |
| Ch06 — Security | B.44–B.54 | K.04, K.13, K.14 | N.13–N.17 | 19 |
| Ch07 — JavaScript Clients | B.63–B.68 | K.09 | N.18, N.19 | 8 |
| Ch08 — WCF | — | — | N.20–N.24 | 5 |
| Ch09 — gRPC | B.55–B.62 | — | N.25, N.31 | 10 |
| Ch10 — Microservices | B.69–B.70 | K.05, K.06 | N.26–N.30 | 9 |
| Ngoài slide (chatbot AI + đích) | B.71–B.73 | K.10–K.12, K.14 | — | 6 |

> \* N.31 thuộc Ch09.

---

# 4. CẤU TRÚC SOLUTION / PROJECT

## 4.1 Quyết định: GIỮ 1 PROJECT

> **Chốt: KHÔNG tách solution thành nhiều project.**
> Dùng **1 project** `PetShop_PRN222.csproj` + **1 test project** `tests/PetShop.Tests`.

**Lý do (6 lý do, đầy đủ trong `ARCHITECT_DESIGN.md` §B.1.1):**

1. Slide Ch02 Slide 5 nói **"3-Layer Application"** — 3 *layer trong 1 ứng dụng*, không phải 3 project.
2. Slide Ch10 mô tả microservices như **khái niệm**, không yêu cầu triển khai.
3. Cơ chế **self-HTTP của Blazor** sẽ vỡ nếu tách project (`AGENTS.md` §1).
4. `TARGET_EXISTING.md` xác nhận đích đã là bản Blazor hoàn chỉnh — ưu tiên **tái sử dụng nguyên vẹn**.
5. `PRN232_PLAN.md` đã chốt ❌ tách microservices thật.
6. Chi phí tách project > lợi ích (`.csproj` mới, `ProjectReference`, DI composition root mới, CI multi-project).

## 4.2 Cấu trúc thư mục chuẩn theo slide (3-layer)

```
PetShop_PRN222.sln
└── PetShop_PRN222.csproj                        ← 1 project, net8.0
    │
    ├── Program.cs                                ← Composition Root (DI + Middleware pipeline)
    ├── appsettings.json / .Development.json / .example.json
    │
    ├── Components/                               ← PRESENTATION LAYER (Ch02 Slide 5)
    │   ├── Pages/          (34 .razor, + .razor.cs partial)
    │   ├── Layout/         (MainLayout, StaffWorkspaceLayout, EmptyLayout)
    │   ├── Shared/         (16 component)
    │   ├── Home/           (HeroBanner, FeaturedCategories, ProductList, CustomerReviews)
    │   ├── Products/       (ProductsSidebar)
    │   ├── App.razor, Routes.razor, _Imports.razor
    │
    ├── Controllers/                               ← PRESENTATION LAYER
    │   ├── AuthController.cs          api/auth/*
    │   ├── ProductsController.cs      api/products/*
    │   ├── PetShopODataController.cs  PetShop/*  (Ch04)
    │   ├── SpaBookingController.cs    api/spa-booking/*
    │   ├── PayOSController.cs         api/payos/*
    │   ├── ShiftExchangeController.cs (+ .Requests.cs, .Processing.cs)
    │   └── ... (Boarding, BreedPricing, Cart, Checkout, Config, EmailTest,
    │             Notification, Orders, Payroll, Pets, PetServices, Review, Attendance)
    │   └── ApiDemoController.cs       ★ MỚI (Ch07, LS-10)
    │
    ├── Services/                                  ← BUSINESS LAYER (Ch02 Slide 5)
    │   ├── *Service.cs      nghiệp vụ (Auth, SpaBooking, Boarding, Payroll,
    │   │                     Attendance, Staff*, Product, PetService)
    │   ├── *Client.cs       typed client gọi chính API (self-HTTP)
    │   ├── *Formatter.cs    CsvOutputFormatter, XmlOutputFormatter  (Ch03)
    │   ├── JwtTokenService.cs                            (Ch06)
    │   ├── ApiExceptionMiddleware.cs                     (Ch01 Slide 37)
    │   ├── SessionHeaderAuthenticationHandler.cs        (Ch06)
    │   ├── SelfHttpClientExtensions.cs                  (Ch07 Slide 21)
    │   ├── MvcBuilderExtensions.cs                      (Ch03 Slide 17)
    │   ├── LoggerExtensions.cs                          (AGENTS.md §2.2)
    │   ├── AI/              ★ MỚI (LS-08)
    │   └── Grpc/            ★ MỚI (LS-09, Ch09)
    │
    ├── Repositories/                              ← DATA ACCESS LAYER
    │   ├── AuthRepository.cs       ├── PetServiceRepository.cs
    │   ├── ProductRepository.cs    ├── ReviewRepository.cs
    │   ├── SpaBookingRepository.cs ├── BoardingRepository.cs
    │   └── StaffRepository.cs
    │
    ├── Data/
    │   ├── ShopPetDatabaseContext.cs   ← 39 DbSet + OnModelCreating
    │   └── SessionExtensions.cs
    │
    ├── Interfaces/    ← 30 file (IRepository / IService / IClient)  (Ch02 Slide 26)
    ├── Models/        ← 52 file (30 entity + 22 DTO/record)        (Ch02 Slide 20-22)
    │
    ├── Protos/                    ★ MỚI (LS-09, Ch09 Slide 35)
    │   ├── pets.proto
    │   └── bookings.proto
    ├── Views/                     ★ MỚI (LS-10, Ch07 Slide 25-36)
    │   └── ApiDemo/Index.cshtml
    ├── EmailTemplates/            (OrderInvoice.hbs, SpaInvoice.hbs)
    │
    ├── wwwroot/                   ← ASSET (xem §6)
    │   ├── app.css, favicon.png, bootstrap/
    │   ├── css/template/ (16 file)   css/spa/ (2 file)
    │   ├── images/        (144 file)
    │   ├── js/            (3 file)   lib/jquery/  ★ MỚI (LS-10)
    │
    ├── tests/PetShop.Tests/       (4 file / 30 test, xUnit)
    └── docs/                      (ARCHITECTURE_REQUIREMENTS.md, _inventory/)
```

## 4.3 Dependency direction (bắt buộc)

```
   PRESENTATION (Components/ + Controllers/)
              │  chỉ gọi interface qua DI
              ▼
   BUSINESS   (Services/)
              │  chỉ gọi IRepository
              ▼
   DATA ACCESS (Repositories/ + Data/)
              ▼
          SQL Server
```

**Cấm:**
- ❌ `.razor` inject `ShopPetDatabaseContext` → phải qua Service (tiền lệ: `ProductDeleted.razor` đã bị xoá vì vi phạm — `PRN232_PLAN.md` P0.3).
- ❌ `Repositories/` gọi `Services/` (đảo chiều).
- ❌ `Services/*Service` ghi SQL thô không tham số.
- ❌ `Controllers/` chứa business logic.

## 4.4 Các project trong solution

| Project | Loại | Vai trò | Trạng thái |
|---|---|---|---|
| `PetShop_PRN222.csproj` | ASP.NET Core Web App (`net8.0`) | Toàn bộ ứng dụng: Blazor Server + API Controllers + gRPC + MVC demo | ✅ Đang chạy |
| `tests/PetShop.Tests/` | xUnit test project | Unit test (30 test) | ✅ Đang chạy |

> `PetShop_PRN222.csproj` có `Compile Remove="tests\**\*.cs"` để không biên dịch lẫn file test.

---

# 5. QUY ƯỚC ĐẶT TÊN

## 5.1 Quy ước lấy từ slide

| # | Quy ước | Slide | Ví dụ ở đích |
|---|---|---|---|
| 1 | **3 namespace theo tầng**: `PetShop.Controllers`, `PetShop.Services`, `PetShop.Repositories`, `PetShop.Interfaces`, `PetShop.Models`, `PetShop.Data` | (Ch02, Slide 5) | `namespace PetShop.Models` — `Models/AuthDtos.cs` |
| 2 | **Repository**: interface `I<Entity>Repository` (`Interfaces/`), implementation `<Entity>Repository` (`Repositories/`) | (Ch05, Slide 32–40) | `IProductRepository` / `ProductRepository` |
| 3 | **Service**: interface `I<Entity>Service` (`Interfaces/`), implementation `<Entity>Service` (`Services/`) | (Ch02, Slide 18, 24; Ch05, Slide 34) | `IAuthService` / `AuthService` |
| 4 | **DTO suffix `Dto`**; request DTO hậu tố `Request`, response DTO hậu tố `Response` | (Ch02, Slide 20–22; Ch05, Slide 41–42) | `RegisterRequestDto`, `AuthResponseDto`, `TokenResponseDto` |
| 5 | **Entity tên số nhiều**, bảng DB số nhiều | (Ch02, Slide 7) | `Customer` → bảng `Customers` |
| 6 | **Controller hậu tố `Controller`** | (Ch02, Slide 25; Ch05, Slide 44–50) | `ProductsController`, `PetShopODataController` |
| 7 | **`DbContext` hậu tố `Context`** | (Ch02, Slide 16; Ch04, Slide 45) | `ShopPetDatabaseContext` |
| 8 | **Attribute routing template** `api/<resource>` — lowercase, phân tách bằng `-` | (Ch01, Slide 27–28; Ch05, Slide 6) | `api/spa-booking`, `api/shift-exchange`, `api/pet-services` |
| 9 | **Route constraint** `{id:int}` | (Ch05, Slide 6) | `api/pets/{petId:int}` |
| 10 | **HTTP verb attribute** theo action | (Ch01, Slide 28, 32–33) | `[HttpGet]`, `[HttpPost]`, `[HttpPut]`, `[HttpPatch]`, `[HttpDelete]` |
| 11 | **EDM model** dùng CLR type trực tiếp, `HasKey` khai báo tường minh | (Ch04, Slide 46) | `PetShopEdmModel.Build()` |
| 12 | **`.proto`** trong `Protos/`, cấu trúc `package` + `service` + `rpc` | (Ch09, Slide 12, 35) | `Protos/pets.proto` (LS-09) |
| 13 | **Method action**: `Get` / `GetById` / `Add` / `Update` / `Delete` hoặc `GetAllAsync` | (Ch01, Slide 38; Ch05, Slide 33–36) | `GetAllAsync`, `GetByIdAsync`, `CreateAsync` |

## 5.2 Quy ước bổ sung từ `AGENTS.md` đích (bắt buộc)

| # | Quy ước |
|---|---|
| 1 | **Giao tiến tiếng Việt**; code tiếng Anh; thông báo nghiệp vụ tiếng Việt |
| 2 | **KHÔNG** `catch { }` — log qua `ILogger<T>` + `LoggerExtensions` rồi trả lỗi có thông tin |
| 3 | **KHÔNG** `Console.WriteLine` trong code ứng dụng; không log mật khẩu/token/`ChecksumKey` |
| 4 | `Nullable = enable` → không warning CS86xx; xử lý null bằng `?.` / `?? string.Empty` / `required` |
| 5 | `async`/`await` bắt buộc cho I/O; **không** `.Result`/`.Wait()` |
| 6 | EF read-only → `AsNoTracking()`; dùng `.Select(...)` projection |
| 7 | SQL thô chỉ `ExecuteSqlRawAsync` với `@p0`; **cấm `FromSqlRaw`** |
| 8 | **File > 500 dòng / component > 25KB → tách**: C# → `partial class` + `<Tên>.<Phần>.cs`; Razor → `<Tên>.razor.cs` |
| 9 | **KHÔNG** copy-paste logic — gom về `Services/` dùng chung |
| 10 | Sửa đúng phạm vi; **không tự ý đổi schema DB / seed data / `SHOP_PET_Database.sql`** |
| 11 | **KHÔNG** push / commit / force-push nếu chưa được yêu cầu |
| 12 | **KHÔNG** tự ý gắn `[Authorize]` vào endpoint đang chạy tốt |
| 13 | **KHÔNG** hardcode `JwtSettings:Key` — ≥ 32 ký tự, ở `appsettings.Development.json` / User Secrets |
| 14 | Đọc `.editorconfig` trước khi sửa (nullability warning → error) |
| 15 | Bỏ hậu tố `_temp` khỏi tên file/class |

## 5.3 Quy ước đặc biệt do thiết kế này đề xuất (cần duyệt)

| # | Đề xuất | Lý do |
|---|---|---|
| 16 | Giữ **namespace `PetShop.*`** hiện tại, **không** đổi | 0 churn, không rủi ro build. Xem Q12. |
| 17 | Tạo thư mục con theo miền nghiệp vụ trong `Services/`: `Services/AI/`, `Services/Grpc/` | Dễ tra cứu, không vi phạm 3-layer. |
| 18 | File C# > 500 dòng → `partial class` + `<Tên>.<Phần>.cs` (VD `ChatbotService.Prompt.cs`) | Quy ước AGENTS.md §2.3 áp dụng cho chatbot mới. |

---

# 6. CHIẾN LƯỢC ASSET

## 6.1 Yêu cầu (nguyên văn từ chủ dự án)

> **"Copy nguyên vẹn asset vào wwwroot, giữ đúng đường dẫn tham chiếu"**

## 6.2 Nguyên tắc bắt buộc

1. **Copy nguyên vẹn** — không nén, không resize, không đổi tên file.
2. **Vào `wwwroot/`** — `StaticFileMiddleware` phục vụ trực tiếp từ `wwwroot/`.
3. **Giữ đúng đường dẫn tham chiếu** — mọi `src` trong Razor và mọi URL trong DB (`Product.ImagePath`, `Pet.ImagePath`, `PetService.ImageUrl`) phải trỏ đúng file đã copy.
4. **Không sửa DB** — nếu DB lưu `/images/toy_1.jpg` thì file phải nằm ở `wwwroot/images/toy_1.jpg` (`AGENTS.md` §2.5).

## 6.3 Bảng mapping nguồn → đích

### (a) Ảnh — `Projectojt/frontend/src/images/` → `wwwroot/images/`

| Nhóm | File | SL | Đích | Trạng thái |
|---|---|---|---|---|
| Dịch vụ tắm | `bath1.png`, `bath2.jpg`, `bath3.jpg` | 3 | `wwwroot/images/` | ✅ Đã copy |
| Dịch vụ cắt tỉa | `cut1.jpg`…`cut3.jpg` | 3 | `wwwroot/images/` | ✅ Đã copy |
| Dịch vụ spa cao cấp | `spa-caocap1.jpg`…`spa-caocap3.jpg` | 3 | `wwwroot/images/` | ✅ Đã copy |
| Dịch vụ răng miệng | `teeth1.jpg`…`teeth3.jpg` | 3 | `wwwroot/images/` | ✅ Đã copy |
| Placeholder | `placeholder.jpg` | 1 | `wwwroot/images/placeholder.jpg` | ✅ Đã copy |
| Phòng mèo | `room-cat-normal1..3.jpg`, `room-cat-vip1..3.jpg` | 6 | `wwwroot/images/` | ✅ Đã copy |
| Phòng chó | `room-dog-large1..3.jpg`, `room-dog-small1..3.jpg` | 6 | `wwwroot/images/` | ✅ Đã copy |
| Slider | `slide_1.jpg`…`slide_4.jpg` | 4 | `wwwroot/images/` | ✅ Đã copy |
| Tin tức pet | `petnews1..6.jpg` | 6 | `wwwroot/images/` | ✅ Đã copy |
| Đánh giá | `meovat1..6.jpg` | 6 | `wwwroot/images/` | ✅ Đã copy |
| Thành viên | `member1.jpg`, `member3.jpg` | 2 | `wwwroot/images/` | ✅ Đã copy |
| Đồ chơi | `toy_1..35.jpg`, `toy_156..216.jpg` | 96 | `wwwroot/images/` | ✅ Đã copy |
| Khác | `gt.jpg`, `gt.png`, `nember_9.png` | 3 | `wwwroot/images/` | ✅ Đã copy |
| Video | `video.mp4`, `video.MOV` | 2 | `wwwroot/images/` | ✅ Đã copy |
| **Tổng** | | **144** | **`wwwroot/images/`** | ✅ **144/144** |

### (b) Ảnh — `Projectojt/frontend/public/` → `wwwroot/images/`

| Nguồn | Đích | Trạng thái |
|---|---|---|
| `public/logo-pet-toy.png` | `wwwroot/images/logo-pet-toy.png` | 🔴 **Chưa copy** → LS-12 |
| `public/ziggy-hero-reference.png` | `wwwroot/images/ziggy-hero-reference.png` | 🔴 **Chưa copy** → LS-12 |
| `public/default-service-placeholder.png` | `wwwroot/images/default-service-placeholder.png` | 🔴 **KHÔNG TỒN TẠI trong nguồn** → tạo mới (K.10) → LS-12 |

### (c) CSS — `frontend/src/styles/` → `wwwroot/css/template/` + `wwwroot/css/spa/`

| Nguồn | Đích | Dòng | Trạng thái |
|---|---|---|---|
| `home.css` | `css/template/home.css` | 2788 | ✅ Đã copy ⚠️ > 500 dòng (K.12) |
| `service.css` | `css/template/spa.css` + `css/spa/service-react.css` | 3681 | ✅ Đã copy ⚠️ > 500 dòng (K.12) |
| `systemPage.css` | đã chia vào `css/template/*.css` | 1426 | ✅ Đã copy |
| `profile.css` | `css/template/profile.css` | 917 | ✅ Đã copy ⚠️ > 500 dòng (K.12) |
| `checkout.css` | `css/template/checkout.css` | 864 | ✅ Đã copy ⚠️ > 500 dòng (K.12) |
| `productDetails.css` | `css/template/productDetails.css` | 678 | ✅ Đã copy ⚠️ > 500 dòng (K.12) |
| `cart.css` | `css/template/cart.css` | 611 | ✅ Đã copy ⚠️ > 500 dòng (K.12) |
| `ordersStaff.css` | `css/template/ordersStaff.css` | 312 | ✅ Đã copy |
| `codConfirmation.css` | `css/template/codConfirmation.css` | 277 | ✅ Đã copy |
| `cartDrawer.css` | `css/template/cartDrawer.css` | 273 | ✅ Đã copy |
| `searchProducts.css` | `css/template/searchProducts.css` | 267 | ✅ Đã copy |
| `auth.css` | `css/template/auth.css` | 186 | ✅ Đã copy |
| `createProduct.css` | *(chưa thấy file tương ứng ở đích)* | 158 | ⚠️ Cần verify (K.12) |
| `pageContextBar.css` | `css/template/pageContextBar.css` | 113 | ✅ Đã copy |
| `src/index.css` | `css/template/index.css` | 66 | ✅ Đã copy (đã bỏ nền tối Vite) |
| `src/App.css` | `css/template/App.css` | — | ✅ Đã copy |
| *(đích có thêm)* | `css/template/checkoutResult.css`, `css/template/index.css`, `css/template/invoice.css` | — | ✅ có sẵn |
| *(đích có thêm)* | `css/spa/cart-react.css`, `css/spa/service-react.css` | — | ✅ có sẵn |

**Quy ước đặt tên CSS ở đích:** `wwwroot/css/template/<tên>.css` = style dùng chung toàn trang; `wwwroot/css/spa/<tên>.css` = style riêng luồng spa.

### (d) JS

| Nguồn | Đích | Trạng thái |
|---|---|---|
| `frontend/src/utils/userProfileStore.js` | `Services/UserSessionService.cs` | ✅ Đã chuyển sang C# |
| `frontend/src/api/*.js` (10 file, 70 hàm) | `Services/*Client.cs` (10 typed client) | ✅ Đã chuyển sang C# |
| `frontend/src/api/chatbotApi.js` | `Services/ChatbotClient.cs` | 🔴 Chưa có → LS-08 |
| *(đích có sẵn)* | `wwwroot/js/blazorAuth.js` | ✅ |
| *(đích có sẵn)* | `wwwroot/js/cartStorage.js` | ✅ |
| *(đích có sẵn)* | `wwwroot/js/spaCart.js` | ✅ |
| *(mới)* | `wwwroot/js/api-demo.js` | 🔴 → LS-10 (Ch07) |
| *(mới)* | `wwwroot/js/daNangData.js` | 🔴 → LS-12 (Q10) |
| *(mới)* | `wwwroot/lib/jquery/jquery.min.js` | 🔴 → LS-10 (Ch07 Slide 8) |
| *(mới)* | `wwwroot/lib/jquery/jquery.validate.min.js` | 🔴 → LS-10 (Ch05 Slide 30) |
| *(mới)* | `wwwroot/lib/jquery/jquery.validate.unobtrusive.min.js` | 🔴 → LS-10 (Ch05 Slide 30) |

### (e) Font

| Nguồn | Đích | Trạng thái |
|---|---|---|
| Google Fonts qua `@import url("https://fonts.googleapis.com/...")` trong `systemPage.css`, `ordersStaff.css`, `home.css` | `wwwroot/fonts/*.woff2` + `@font-face` | 🔴 **Chưa vendor** — 4 file CSS ở đích vẫn `@import` CDN: `css/template/home.css` (Be Vietnam Pro, Poppins, Quicksand, Nunito, Baloo 2), `css/template/codConfirmation.css` (Quicksand, Nunito), `css/template/cart.css` (Quicksand, Nunito), `css/spa/cart-react.css` (Quicksand, Nunito). **Demo offline sẽ mất font.** → K.11, LS-12 |

## 6.4 Cơ chế xử lý khác biệt React import ↔ Blazor

### (a) Nguyên tắc

| Môi trường | Cú pháp | Đường dẫn | Đặc điểm |
|---|---|---|---|
| **React + Vite (nguồn)** | `import hero from '../../images/slide_1.jpg'` | **Tương đối tới file source** | Vite build → hash → `/assets/slide_1-<hash>.jpg`; dev mode → `/src/images/slide_1.jpg`. **URL thay đổi theo build** → không thể hard-code, không lưu được vào DB. |
| **Blazor Server (đích)** | `<img src="/images/slide_1.jpg" />` | **Tuyệt đối từ root** | `StaticFileMiddleware` phục vụ `wwwroot/` tại `/`. URL **cố định** → lưu DB được, cache được, CDN được. |

### (b) Quy tắc quy đổi 1:1

```
React:  import x from '../../images/toy_1.jpg'
   ↓
Blazor: <img src="/images/toy_1.jpg" />

React:  import x from '../images/slide_1.jpg'          (1 cấp)
   ↓
Blazor: <img src="/images/slide_1.jpg" />              (KHÔNG giữ dấu ..)

React:  import x from '/logo-pet-toy.png'              (từ public/)
   ↓
Blazor: <img src="/images/logo-pet-toy.png" />          (đưa vào images/ cho nhất quán)
```

**Quy tắc vàng:**
- ❌ **KHÔNG** giữ `../../` — browser resolve theo URL hiện tại (`/account/orders/5` → hỏng).
- ❌ **KHÔNG** dùng đường dẫn tương đối (`images/x.jpg`).
- ✅ **LUÔN** dùng đường dẫn tuyệt đối bắt đầu bằng `/`.

### (c) Ba nguồn đường dẫn ảnh trong hệ thống đích

| Nguồn | Ví dụ | Nơi lưu | Xử lý |
|---|---|---|---|
| (1) URL tuyệt đối tĩnh | `/images/toy_1.jpg` | Hard-code trong `.razor` | ✅ Giữ nguyên (đã làm ở đích) |
| (2) URL trong DB | `Product.ImagePath = "/images/toy_1.jpg"` | Cột DB | ✅ Đã đúng. **Không sửa DB.** |
| (3) URL tương đối từ upload | `Pet.ImagePath = "/uploads/pets/pet_<guid>.jpg"` | Cột DB | ✅ `POST /api/pets/upload-image` lưu vào `wwwroot/uploads/pets/`. Giữ nguyên. |

### (d) Quy trình copy asset

```
Nguồn:  Projectojt/frontend/src/images/*.jpg
                    ↓  copy nguyên vẹn (không nén, không đổi tên)
Đích:   PRN232_PetCareProject/wwwroot/images/*.jpg
                    ↓  URL tuyệt đối
Dùng:   <img src="/images/toy_1.jpg" />       (Razor)
        Product.ImagePath = "/images/toy_1.jpg" (DB)
```

### (e) Checklist sau khi copy

- ☐ Số file `wwwroot/images/` = số file nguồn
- ☐ Grep `.razor` / `.cs` / `.cshtml` cho `../../images` → **0 kết quả**
- ☐ Grep cho `src="images/` (thiếu `/`) → **0 kết quả**
- ☐ Mọi `ImagePath` trong DB có file tương ứng trong `wwwroot/images/`
- ☐ Mở 34 trang → không ảnh vỡ
- ☐ `wwwroot/uploads/` trong `.gitignore` / `.dockerignore` (thư mục runtime)

---

# 7. ÁNH XẠ SLIDE → HIỆN THỰC TRONG PRN232

> Cột "Đã có ở đích" = trạng thái **hiện tại** trong codebase.
> "Trạng thái": ✅ Đạt · 🟡 Cần làm/thêm · 🔴 Chưa có · ❌ Quyết định không áp dụng.

## Ch00 — Course Introduction

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| `.NET 8 or later` | Ch00, Slide 8 | `net8.0` | `PetShop_PRN222.csproj` | ✅ Đạt |
| `MS SQL Server 2016 or later` | Ch00, Slide 8 | `SHOP_PET_Database` | `appsettings.json`, `Data/ShopPetDatabaseContext.cs` | ✅ Đạt |
| Security bằng JWT | Ch00, Slide 5 | JWT Bearer dual-mode | `Services/JwtTokenService.cs`, `Controllers/AuthController.cs` | ✅ Đạt |
| Gửi Ajax request tới Web API | Ch00, Slide 5 | Chưa có demo | — | 🟡 LS-10 |
| gRPC | Ch00, Slide 5 | Chưa có | — | 🔴 LS-09 |
| WCF | Ch00, Slide 5 | Không áp dụng (N.20) | — | ❌ |
| Microservices thật | Ch00, Slide 4-5 | Không áp dụng (N.26) | — | ❌ |

## Ch01 — ASP.NET Core & RESTful Service

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| `[ApiController]` + attribute routing | Ch01, Slide 35 | 22 controller | `Controllers/*.cs` | ✅ Đạt |
| `ControllerBase` helper methods | Ch01, Slide 21-22 | Đầy đủ | `Controllers/*.cs` | ✅ Đạt |
| HTTP verbs + attributes | Ch01, Slide 24, 28, 32-33 | Có cả `[HttpPatch]` | `Controllers/*.cs` | ✅ Đạt |
| Status codes chuẩn | Ch01, Slide 25 | Chuẩn hoá ProblemDetails | `Services/ApiExceptionMiddleware.cs` | ✅ Đạt |
| `[ProducesResponseType]` | Ch01, Slide 27 | Có một phần | `Controllers/*.cs` | 🟡 LS-05 |
| Automatic 400 (`ModelStateInvalidFilter`) | Ch01, Slide 35 | Framework + DataAnnotations | `Models/*Dtos.cs` | ✅ Đạt |
| `ProblemDetails` | Ch01, Slide 37 | `application/problem+json` | `Services/ApiExceptionMiddleware.cs` | ✅ Đạt |
| SOAP / WS-* | Ch01, Slide 9, 16 | Không áp dụng (N.01) | — | ❌ |
| Schema (DTD/W3C/RelaxNG/Schematron) | Ch01, Slide 15 | Không áp dụng (N.02) | — | ❌ |

## Ch02 — Models in Web API

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| **3-Layer** (Presentation/Business/Data) | Ch02, Slide 5 | Đúng cấu trúc thư mục | `Components/` `Controllers/` `Services/` `Repositories/` `Data/` | ✅ Đạt |
| EF Core (ORM) | Ch02, Slide 6-8 | 8.0.8 | `PetShop_PRN222.csproj` | ✅ Đạt |
| Database-First (`Scaffold-DbContext`) | Ch02, Slide 11-12 | 39 DbSet | `Data/ShopPetDatabaseContext.cs` | ✅ Đạt |
| Code-First (`Add-Migration`) | Ch02, Slide 13, 19 | Công cụ đã cài | `Microsoft.EntityFrameworkCore.Tools` | ✅ Đạt |
| DTO tách Entity | Ch02, Slide 20-22 | 22 DTO | `Models/*Dtos.cs` | ✅ Đạt |
| AutoMapper | Ch02, Slide 23 | Không áp dụng (N.03) — cần duyệt Q3 | — | ❌ / 🟡 |
| Interface abstraction, controller mỏng | Ch02, Slide 26 | 30 interface | `Interfaces/*.cs` | ✅ Đạt |

## Ch03 — Media Formatters & Content Negotiation

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| Content negotiation `Accept` header | Ch03, Slide 21-25 | JSON + XML + CSV | `Services/XmlOutputFormatter.cs`, `Services/CsvOutputFormatter.cs` | ✅ Đạt |
| **Custom output formatter** (`TextOutputFormatter`) | Ch03, Slide 17, 29 | `CsvOutputFormatter` | `Services/CsvOutputFormatter.cs` | ✅ Đạt |
| **Custom input formatter** (`TextInputFormatter`) | Ch03, Slide 17, 29 | Chưa có | — | 🟡 K.01 → LS-03 |
| `MvcOptions.OutputFormatters` | Ch03, Slide 17 | Có | `Program.cs` | ✅ Đạt |
| `System.Text.Json` | Ch03, Slide 19 | Mặc định | Framework | ✅ Đạt |
| `JsonIgnore` | Ch03, Slide 11 | Có | `Models/*.cs` | ✅ Đạt |
| `PropertyNamingPolicy = null` (PascalCase) | Ch03, Slide 18, 20 | Giữ camelCase — không áp dụng (N.05) | `Program.cs` | ❌ |
| `JsonMediaTypeFormatter` / `XmlMediaTypeFormatter` | Ch03, Slide 8-9, 12 | Không áp dụng (N.04) — API Web API 2 | — | ❌ |

## Ch04 — OData in ASP.NET Core Web API

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| Package `Microsoft.AspNetCore.OData` | Ch04, Slide 4, 28 | **9.5.0** | `PetShop_PRN222.csproj` | ✅ Đạt |
| Version `9.2.1` | Ch04, Slide 32, 43 | 9.5.0 — không áp dụng (N.06) — cần duyệt Q1 | `PetShop_PRN222.csproj` | ❌ / 🟡 |
| EF Core `8.0.15` | Ch04, Slide 32 | 8.0.8 — không áp dụng (N.07) | `PetShop_PRN222.csproj` | ❌ / 🟡 |
| EDM model + `HasKey` | Ch04, Slide 29, 44-46 | 8 entity | `Models/PetShopEdmModel.cs` | ✅ Đạt |
| `AddOData()` + query registration | Ch04, Slide 34, 47 | Select/Filter/OrderBy/Expand/Count/SkipToken | `Program.cs` | ✅ Đạt |
| `IQueryable` + `[EnableQuery]` | Ch04, Slide 35 | Có | `Controllers/PetShopODataController.cs` | ✅ Đạt |
| `$filter $sort $select $top $skip $expand $count $inlinecount` | Ch04, Slide 11, 36-38 | Đủ | `Controllers/PetShopODataController.cs` | ✅ Đạt |
| `$count` path segment | Ch04, Slide 38, 40 | Route thủ công | `Controllers/PetShopODataController.cs` | ✅ Đạt |
| `$format` (atom/xml) | Ch04, Slide 20 | Không áp dụng (N.08) | — | ❌ |
| Batch / ChangeSet | Ch04, Slide 25 | Không áp dụng (N.09) | — | ❌ |
| Functions / Actions / `$links` | Ch04, Slide 8, 26-27 | Không áp dụng (N.10) — có REST thay thế | — | ❌ |
| API versioning | Ch04, Slide 7 | Không áp dụng (N.11) | — | ❌ |
| `Microsoft.EntityFrameworkCore.InMemory` | Ch04, Slide 43 | Chưa cần | `PetShop_PRN222.csproj` | 🟡 K.07 |

## Ch05 — Binding, Validation and Routing

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| `UseRouting()` + `UseEndpoints()` | Ch05, Slide 4 | Có | `Program.cs` | ✅ Đạt |
| **Attribute Routing** | Ch05, Slide 5 | 100% controller | `Controllers/*.cs` | ✅ Đạt |
| Route variable + query string | Ch05, Slide 6 | Có | `Controllers/*.cs` | ✅ Đạt |
| **Repository Pattern** | Ch05, Slide 32-40 | 7 repo + 7 interface | `Repositories/*.cs`, `Interfaces/*.cs` | ✅ Đạt |
| Repository Manager | Ch05, Slide 37 | Không áp dụng (N.12) — DbContext đã là UoW | — | ❌ |
| Binding source attributes | Ch05, Slide 17-19 | Có | `Controllers/*.cs` | ✅ Đạt |
| Validation attributes (9 loại) | Ch05, Slide 26-27 | Có | `Models/*Dtos.cs` | ✅ Đạt |
| Custom validation attribute | Ch05, Slide 28 | Chưa có | — | 🟡 K.02 → LS-04 |
| `ModelState` + `TryValidateModel` | Ch05, Slide 24-25 | Có | Framework + middleware | ✅ Đạt |
| Server-side validation | Ch05, Slide 29 | Có | `[ApiController]` + `DataAnnotationsValidator` | ✅ Đạt |
| Client-side validation (jQuery Validate) | Ch05, Slide 30 | Chưa có — chỉ cần cho trang demo MVC | — | 🟡 K.09 → LS-10 |
| Postman để test | Ch05, Slide 52 | Đã dùng | `docs/_inventory/` (cần bổ sung collection) | 🟡 LS-06 |

## Ch06 — Implement security in RESTful WebService

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Ch06, Slide 16, 31 | 8.0.33 | `PetShop_PRN222.csproj` | ✅ Đạt |
| `AddJwtBearer` + `Configuration.Bind("JwtSettings")` | Ch06, Slide 16 | Có | `Program.cs` | ✅ Đạt |
| JWT Header/Payload/Signature, HMAC-SHA256 | Ch06, Slide 32-33 | HS256 | `Services/JwtTokenService.cs` | ✅ Đạt |
| Access token endpoint | Ch06, Slide 40-41 | `POST /api/auth/token` | `Controllers/AuthController.cs` | ✅ Đạt |
| `Authorization: Bearer` header | Ch06, Slide 29, 41 | Có | `Program.cs` | ✅ Đạt |
| `AllowAnonymous` cho login | Ch06, Slide 27 | Có | `Controllers/AuthController.cs` | ✅ Đạt |
| `UseAuthentication()` + `UseAuthorization()` | Ch06, Slide 25-26 | Có | `Program.cs` | ✅ Đạt |
| Schemes qua `AuthenticationBuilder` | Ch06, Slide 15-16 | `Session` + `Bearer` | `Services/SessionHeaderAuthenticationHandler.cs` | ✅ Đạt |
| `AuthorizeAttribute` / `AllowAnonymousAttribute` | Ch06, Slide 18 | Có | `Controllers/AuthController.cs` | ✅ Đạt |
| Refresh token | Ch06, Slide 40, 42 | Có — `POST /api/auth/refreshtoken` (claim `token_type`, hạn 7 ngày, rotate) | `Controllers/AuthController.cs`, `Services/JwtTokenService.cs` | ✅ Đạt (Q2) |
| ASP.NET Core Identity | Ch06, Slide 19-24 | Không áp dụng (N.13) | — | ❌ |
| `AddCookie` scheme | Ch06, Slide 16 | Không áp dụng (N.15) | — | ❌ |
| Best practice (validate input, generic error…) | Ch06, Slide 3-5 | Một phần — còn `?basePrice=` override | `Controllers/PetsController.cs` | 🟡 LS-13 |
| TLS/SSL | Ch06, Slide 7 | Có | `Program.cs` | ✅ Đạt |
| Autoscaling (Azure) | Ch06, Slide 8-13 | Không áp dụng (N.16) | — | ❌ |

## Ch07 — JavaScript Clients and ASP.NET Web API

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| jQuery (syntax, selector, event) | Ch07, Slide 6-12 | Chưa có | — | 🔴 LS-10 |
| AJAX (`$.ajax`, `$.get`, `$.post`) | Ch07, Slide 13-15 | Chưa có | — | 🔴 LS-10 |
| `XMLHttpRequest` | Ch07, Slide 4-5, 27, 30, 33, 36 | Chưa có | — | 🔴 LS-10 |
| Demo Razor View / MVC | Ch07, Slide 16, 25-36 | Chưa có `Views/` | — | 🔴 LS-10 |
| `IRepository` + implementation | Ch07, Slide 18-20 | Có | `Interfaces/IProductRepository.cs` | ✅ Đạt |
| CRUD controller (POST/PUT/DELETE) | Ch07, Slide 21-24 | Chỉ có GET | `Controllers/ProductsController.cs` | 🔴 LS-10 |
| IoC container trong `Program.cs` | Ch07, Slide 21 | 45+ registration | `Program.cs` | ✅ Đạt |
| `EventSource` (SSE) | Ch07, Slide 4 | Không áp dụng (N.18) | — | ❌ |
| React / Vite | không đề cập | Không áp dụng (N.19) — đích là Blazor | — | ❌ |

## Ch08 — Building RESTful Service with WCF

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| CoreWCF packages | Ch08, Slide 29-31, 34 | Không áp dụng (N.20) | — | ❌ |
| `dotnet new --install CoreWCF.Templates` | Ch08, Slide 33 | Không áp dụng (N.21) | — | ❌ |
| `dotnet-svcutil` + `System.ServiceModel.*` | Ch08, Slide 33, 39-40 | Không áp dụng (N.21) | — | ❌ |
| WCF attributes | Ch08, Slide 16-19, 28 | Không áp dụng (N.22) | — | ❌ |
| WCF Bindings | Ch08, Slide 20-22 | Không áp dụng (N.23) | — | ❌ |
| WCF Architecture / hosting / transactions | Ch08, Slide 7-14, 23-27 | Không áp dụng (N.24) | — | ❌ |

## Ch09 — Introduction to gRPC Service

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| `Grpc.AspNetCore` | Ch09, Slide 29 | Chưa có | — | 🔴 LS-09 |
| `Grpc.Net.Client` / `Google.Protobuf` / `Grpc.Tools` | Ch09, Slide 42 | Chưa có | — | 🔴 LS-09 |
| proto3 `.proto` trong `Protos/` | Ch09, Slide 9-13, 35 | Chưa có | — | 🔴 LS-09 |
| `AddGrpc()` + `MapGrpcService<T>()` | Ch09, Slide 29-30, 36 | Chưa có | — | 🔴 LS-09 |
| 4 loại RPC | Ch09, Slide 16-18 | Chưa có | — | 🔴 LS-09 |
| DI + Logging trong gRPC service | Ch09, Slide 31 | Chưa có | — | 🔴 LS-09 |
| Client Controller (`GrpcChannel.ForAddress`) | Ch09, Slide 44 | Chưa có | — | 🔴 LS-09 |
| Host bởi Kestrel | Ch09, Slide 30 | Có | `Program.cs` | ✅ Đạt |
| `Startup.cs` | Ch09, Slide 32, 37 | Minimal hosting — không áp dụng (N.31) | `Program.cs` | ❌ |
| proto2 | Ch09, Slide 13 | Không áp dụng (N.25) — dùng proto3 | — | ❌ |

## Ch10 — Introduction to Microservices Architecture

| Yêu cầu slide | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| `Dockerfile` multi-stage | Ch10, Slide 28 | Chưa có | — | 🔴 LS-12 |
| `docker build` / `docker run` | Ch10, Slide 29-30 | Chưa có | — | 🔴 LS-12 |
| `.dockerignore` | Ch10, Slide 28-29 | Chưa có | — | 🟡 K.05 → LS-12 |
| `docker-compose.yml` | không đề cập | Chưa có | — | 🟡 K.06 → LS-12 |
| Tách microservices thật | Ch10, Slide 3-5 | Không áp dụng (N.26) | — | ❌ |
| Azure Service Fabric | Ch10, Slide 19 | Không áp dụng (N.27) | — | ❌ |
| Event bus / AMQP / RabbitMQ | Ch10, Slide 9-10, 16 | Không áp dụng (N.28) | — | ❌ |
| Autoscaling patterns | Ch10, Slide 13 | Không áp dụng (N.29) | — | ❌ |
| Docker Desktop + WSL 2 | Ch10, Slide 23 | Môi trường (N.30) | — | ⚠️ Môi trường |

## Ngoài slide — Chatbot AI (từ nguồn OJT)

| Yêu cầu | Slide | Đã có ở đích | File | Trạng thái |
|---|---|---|---|---|
| `ChatbotController` (4 endpoint) | không đề cập | Chưa có | — | 🔴 LS-08 |
| `IChatbotService` / `IAiProviderService` | không đề cập | Chưa có | — | 🔴 LS-08 |
| `ChatbotService` + 2 provider | không đề cập | Chưa có | — | 🔴 LS-08 |
| `ChatbotDtos` / `ChatbotOptions` | không đề cập | Chưa có | — | 🔴 LS-08 |
| `ChatMessage` entity + DbSet | không đề cập | **Có** | `Models/ChatMessage.cs`, `Data/ShopPetDatabaseContext.cs` | ✅ Đạt |

---

# 8. TÓM TẮT TRẠNG THÁI

| Nhóm | Bắt buộc | ✅ Đạt | 🟡 Cần làm | 🔴 Chưa có | ❌ Không áp dụng |
|---|---|---|---|---|---|
| Tổng các mục | 73 | **52** | 6 | 15 | 32 |
| Tỷ lệ | — | **71%** | 8% | 21% | — |

**Ưu tiên triển khai (theo thứ tự đề xuất):**

| # | Nhóm | Lát cắt | Ước lượng |
|---|---|---|---|
| 1 | OData test đầy đủ | LS-02 | 2-3h |
| 2 | Content Negotiation + input formatter | LS-03 | 2h |
| 3 | Validation + ProblemDetails + custom attribute | LS-04 | 2-3h |
| 4 | Routing + Swagger metadata | LS-05 | 1-2h |
| 5 | JWT demo hoàn chỉnh + Postman collection | LS-06 | 2-3h |
| 6 | PayOS webhook idempotent | LS-07 | 3-4h |
| 7 | Chatbot AI | LS-08 | 6-8h |
| 8 | gRPC | LS-09 | 3-4h |
| 9 | JavaScript client demo | LS-10 | 3-4h |
| 10 | Notification + chat UI | LS-11 | 3-4h |
| 11 | Docker + dọn gap UI | LS-12 | 2-3h |
| 12 | Robustness (giá, status, OTP) | LS-13 | 3-4h |
| 13 | Tài liệu + báo cáo | LS-14 | 2h |

---

# 9. ĐIỂM CẦN CHỦ DỰ ÁN QUYẾT

> 12 câu hỏi, mỗi câu có lựa chọn + khuyến nghị. Chi tiết đầy đủ tại `ARCHITECT_DESIGN.md` §PHẦN F.

| # | Câu hỏi | Lựa chọn | Khuyến nghị |
|---|---|---|---|
| Q1 | OData giữ 9.5.0 hay hạ về 9.2.1 theo slide? EF 8.0.8 hay 8.0.15? | A. Giữ 9.5.0 / 8.0.8 · B. Hạ về 9.2.1 / 8.0.15 | **A** |
| Q2 | Refresh token: có làm theo slide Ch06 không? | A. Không · B. Có (tối giản) | **B** (để khớp slide 100%) — ✅ **đã thực hiện (Q2, 08/11/2026)**: `POST api/auth/refreshtoken` + `token_type` + rotate, 14 test ⇒ **93/93 pass**. Xem K.04 |
| Q3 | AutoMapper: dùng hay không? | A. Không (map tay) · B. Có | **A** |
| Q4 | Chatbot provider nào? | A. Gemini + Ollama multi-provider · B. Chỉ Gemini · C. Chỉ Ollama | **A** (hoặc **C** nếu demo offline) |
| Q5 | Lưu lịch sử chatbot vào bảng `ChatMessages`? | A. Có · B. Không | **A** (bảng đã có sẵn) |
| Q6 | Gộp 2 luồng PayOS? | A. Gộp · B. Giữ nguyên | **A** |
| Q7 | Thêm CRUD write cho `ProductsController`? | A. Có (soft-delete) · B. Không · C. API demo riêng | **A** (hoặc **C** nếu lo dữ liệu thật) |
| Q8 | Docker compose có cần SQL Server? | A. Có compose · B. Chỉ Dockerfile | **Cả hai** |
| Q9 | Port trang `/about` sang Blazor? | A. Có · B. Không | **A** |
| Q10 | `daNangData.js` xử lý thế nào? | A. Copy nguyên `.js` · B. Viết lại C# · C. Bỏ | **A** |
| Q11 | Trạng thái booking: string / enum / chặn client set trạng thái thanh toán? | A. Giữ string · B. Enum · C. Giữ string + chặn webhook-only | **C** |
| Q12 | Namespace gốc: giữ hay đổi? | A. Giữ `PetShop.*` · B. Đổi | **A** |

---

# 10. PHÊ DUYỆT

| | |
|---|---|
| **Người lập** | Kiến trúc sư trưởng (Hội đồng khắc nghiệt) — 08/11/2026 |
| **Người duyệt** | ☐ Chủ dự án PRN232 |
| **Ngày duyệt** | ☐ \_\_\_\_/\_\_/\_\_\_\_\_\_ |
| **Ghi chú** | ☐ Duyệt nguyên trạng ☐ Duyệt có điều chỉnh (ghi ở §9) ☐ Yêu cầu làm lại |

> **Sau khi duyệt:** tài liệu này trở thành hợp đồng kỹ thuật. Mọi thay đổi code phải được phản ánh trong bảng §7 (Ánh xạ slide → hiện thực) và checklist của lát cắt tương ứng trong `ARCHITECT_DESIGN.md` §PHẦN D.

---

*Tài liệu yêu cầu kiến trúc chính thức — đối chiếu trực tiếp với 11 file text trong `_slides_text/` và 4 file inventory trong `docs/_inventory/`. **Không có file nào trong nguồn hay đích bị chỉnh sửa bởi việc soạn tài liệu này.***
