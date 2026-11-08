# ARCHITECT_DESIGN — Thiết kế kiến trúc cuối cùng cho PRN232 PetCareProject

> **Vai trò:** Kiến trúc sư trưởng — Hội đồng khắc nghiệt migration OJT → PRN232.
> **Nguồn slide:** `_slides_text/` (11 chương, 422 slide).
> **Nguồn inventory:** `docs/_inventory/` (4 file: `SOURCE_API_ENDPOINTS.md`, `SOURCE_FRONTEND.md`, `SOURCE_COMPLEX_FLOWS.md`, `TARGET_EXISTING.md`).
> **Nguồn code:** `Projectojt/BackEnd` (.NET 8 MVC API) + `Projectojt/frontend` (React 19 + Vite 7) — CHỈ ĐỌC.
> **Đích:** `PRN232_PetCareProject` — Blazor Server + API Controllers, .NET 8, 1 project.
> **Ngày lập:** 08/11/2026.
> **Phạm vi tài liệu này:** CHI ĐỌC + thiết kế. Không sửa code nguồn hay đích.

---

## 0. Tóm tắt điều hành (Executive Summary)

| # | Kết luận kiến trúc | Lý do |
|---|---|---|
| 1 | **GIỮ 1 PROJECT** (`PetShop_PRN222.csproj`), không tách solution nhiều project | Slide Ch02 nói "3-Layer Application" là 3 *layer trong 1 app*, không phải 3 project. Slide Ch10 mô tả microservices như *giới thiệu*, không yêu cầu tách. Tách project lúc này = phá vỡ self-HTTP của Blazor (AGENTS.md §1) với lợi ích = 0. |
| 2 | **3-layer được hiện thực bằng thư mục**, không bằng project | `Controllers/` (Presentation) → `Services/` (Business) → `Repositories/` (Data Access) + `Data/` (DbContext). Khớp 100% sơ đồ slide Ch02 Slide 5. |
| 3 | **Chỉ 1 file cần viết mới cho Chatbot** là `Interfaces/IAiProviderService.cs` + `Services/AI/*` | Đích thiếu toàn bộ stack AI (TARGET_EXISTING §8.1). Đây là lát cắt lớn nhất còn lại. |
| 4 | **WCF/CoreWCF (Ch08) — KHÔNG ÁP DỤNG** | Slide nói rõ CoreWCF chỉ hỗ trợ SOAP server-side, không thay thế WCF đầy đủ, chạy .NET 8+ nhưng chỉ là demo giáo trình. Không có nhu cầu nghiệp vụ SOAP. |
| 5 | **Microservices thật (Ch10) — KHÔNG ÁP DỤNG**, nhưng **Docker (Ch10 Slide 28-30) — ÁP DỤNG** | Docker là 1 Dockerfile, rẻ và khớp slide. Tách microservice thật không khớp "1 DB local, 1 project". |
| 6 | **OData giữ version 9.5.0** (đích) chứ không nâng/đổi về 9.2.1 (slide Ch04 Slide 32) | Slide nêu 9.2.1 nhưng cùng API `AddRouteComponents`. Đổi version hiện đang chạy = rủi ro không cần thiết. Ghi nhận là "Không áp dụng (có lý do)". |
| 7 | **Asset: copy nguyên vẹn vào `wwwroot/`, giữ đường dẫn `/images/x.png`** | Đích đã có sẵn 146 ảnh tại `wwwroot/images/` và đã dùng style Blazor `/images/...`. Nguồn React dùng import `../../images/x.png` → quy đổi 1:1 sang đường dẫn tuyệt đối. |

---

# PHẦN A — BẢNG ÁNH XẠ SLIDE → QUYẾT ĐỊNH KIẾN TRÚC

> Quy ước: "Slide XX" = số thứ tự slide trong file text đã trích (file `Chapter_XX-*.txt`).
> Chỉ ghi nội dung slide THẬT SỰ nêu. Mục nào slide không nói → ghi "slide không đề cập".

---

## A.1 Chapter 00 — Course Introduction

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.1.1 | `.NET 8 or later` | **ÁP DỤNG** | Đích đã `net8.0`; slide là yêu cầu tối thiểu môi trường. | Chapter_00, Slide 8 ("Learning Environments") |
| A.1.2 | `Visual Studio 2022 or later` | **ÁP DỤNG** (môi trường, không phải code) | Đích build bằng SDK .NET 8 — tương thích. | Chapter_00, Slide 8 |
| A.1.3 | `MS SQL Server 2016 or later` | **ÁP DỤNG** | Đích dùng SQL Server qua `Microsoft.EntityFrameworkCore.SqlServer`. | Chapter_00, Slide 8; đích `Data/ShopPetDatabaseContext.cs` |
| A.1.4 | `C# 12` / `.NET 8` | **ÁP DỤNG** | `net8.0` mặc định LangVersion 12. | Chapter_00, Slide 7 (Materials/References) |
| A.1.5 | `EF Core` tài liệu tham chiếu | **ÁP DỤNG** | Đã có `Microsoft.EntityFrameworkCore` 8.0.8. | Chapter_00, Slide 7; đích `.csproj` |
| A.1.6 | "Implement security in the ASP.NET Core Web API using JWT" | **ÁP DỤNG** | Đã xong Phase P4. | Chapter_00, Slide 5 |
| A.1.7 | "sending Ajax request to ASP.NET Core Web API" | **ÁP DỤNG** (còn thiếu → lát cắt LS-11) | Đích chưa có demo JS client. | Chapter_00, Slide 5 |
| A.1.8 | "RESTful Service applications by ASP.NET Web API and WCF, gRPC" | **ÁP DỤNG một phần** | REST ✅ có; gRPC ⬜ chưa; WCF ❌ bỏ (xem A.8). | Chapter_00, Slide 5 |
| A.1.9 | "developing the distributed applications based on Microservice architecture" | **KHÔNG ÁP DỤNG (bản thân hóa)** | Slide chỉ nêu trong mục "Introduce about" — mục tiêu giới thiệu, không phải triển khai. Đích chạy 1 DB local 1 project. | Chapter_00, Slide 4-5 |

---

## A.2 Chapter 01 — Introduction to ASP.NET Core and RESTful Service

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.2.1 | `[ApiController]` + **attribute routing bắt buộc** | **ÁP DỤNG** | Mọi controller đích đều `[ApiController]` + route attribute (`api/...`). | Chapter_01, Slide 35; đích `Controllers/*.cs` |
| A.2.2 | `ControllerBase` helper methods: `Ok`, `NotFound`, `BadRequest`, `NoContent`, `Accepted`, `CreatedAtAction`, `Content`, `PhysicalFile`, `TryValidateModel` | **ÁP DỤNG** | Đích dùng đầy đủ bộ helper này. | Chapter_01, Slide 21-22; đích `Controllers/*.cs` |
| A.2.3 | HTTP verbs `GET/POST/PUT/DELETE` (+ `HEAD/OPTIONS/PATCH` attributes) | **ÁP DỤNG** | Đích có cả `[HttpPatch]` (orders confirm/delivered, spa status). | Chapter_01, Slide 24, 28, 32-33 |
| A.2.4 | Status codes chuẩn 200/201/204/400/403/404/500 | **ÁP DỤNG** | Đã chuẩn hoá qua `ApiExceptionMiddleware` → ProblemDetails (RFC 7807). | Chapter_01, Slide 25; đích `Services/ApiExceptionMiddleware.cs` |
| A.2.5 | `[ProducesResponseType(StatusCodes.Status201Created)]` trên action | **ÁP DỤNG (khuyến nghị)** | Đích có dùng ở một số action; nên rà soát bổ sung cho Swagger đúng slide. | Chapter_01, Slide 27 |
| A.2.6 | `ModelStateInvalidFilter` → **Automatic 400** khi dùng `[ApiController]` | **ÁP DỤNG** | Đã có sẵn theo framework; P1 đã thêm DataAnnotations → 400 tự động. | Chapter_01, Slide 35; đích `Models/*Dtos.cs` |
| A.2.7 | `ProblemDetails` cho status ≥ 400 | **ÁP DỤNG** | `ApiExceptionMiddleware` trả `application/problem+json`. | Chapter_01, Slide 37; đích `Services/ApiExceptionMiddleware.cs` |
| A.2.8 | `ModelState` property trên `ControllerBase` | **ÁP DỤNG** | Dùng qua validation filter + `TryValidateModel`. | Chapter_01, Slide 21 |
| A.2.9 | "Running OWIN middleware in ASP.NET Core pipeline" | **ÁP DỤNG (hiểu là middleware pipeline)** | OWIN không tồn tại ở ASP.NET Core; slide chỉ mô tả pipeline middleware. Đích có pipeline đầy đủ. | Chapter_01, Slide 2, 39 |
| A.2.10 | SOAP (WS-*, WSDL, XML envelope) | **KHÔNG ÁP DỤNG** | Slide chỉ so sánh SOAP vs REST; dự án dùng REST/JSON. | Chapter_01, Slide 9, 16 |
| A.2.11 | Schema (DTD/W3C Schema/RelaxNG/Schematron) mô tả response | **KHÔNG ÁP DỤNG** | Slide nói "provide a schema" như đây là best-practice lý thuyết; dự án không có hợp đồng schema ngoài. Không có bằng chứng slide yêu cầu công cụ cụ thể. | Chapter_01, Slide 15 (mục 7) |

---

## A.3 Chapter 02 — Models in Web API

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.3.1 | **3-Layer Application**: Presentation / Business / Data Access | **ÁP DỤNG** | Cấu trúc đích map 1:1: `Controllers/` = Presentation, `Services/` = Business, `Repositories/` + `Data/` = Data Access. | **Chapter_02, Slide 5** (có sơ đồ); đích `AGENTS.md` §1 |
| A.3.2 | ORM = **Entity Framework Core** | **ÁP DỤNG** | `Microsoft.EntityFrameworkCore` 8.0.8 + `.SqlServer` 8.0.8. | Chapter_02, Slide 6-8; đích `.csproj` |
| A.3.3 | Code-First development với `Add-Migration` / `Update-Database` | **ÁP DỤNG** | Đích scaffold code-first từ DB có sẵn; `Microsoft.EntityFrameworkCore.Tools` đã cài. **Lưu ý:** AGENTS.md §2.5 cấm tự ý đổi schema/seed — migration chỉ dùng khi user yêu cầu. | Chapter_02, Slide 13, 19; đích `.csproj`, `SHOP_PET_Database.sql` |
| A.3.4 | Database-First với `Scaffold-DbContext` + `Microsoft.EntityFrameworkCore.Tools` | **ÁP DỤNG (đã thực hiện xong)** | Đích scaffold từ `SHOP_PET_Database` → 39 DbSet, `OnModelCreating` có `HasKey(...).HasName("PK__...")`. | Chapter_02, Slide 11-12; đích `Data/ShopPetDatabaseContext.cs` |
| A.3.5 | **DTO** tách khỏi Entity; DTO chỉ dùng cho request/response | **ÁP DỤNG** | Đích có 22 DTO/record tách riêng trong `Models/`. | Chapter_02, Slide 20-22; đích `Models/*Dtos.cs` |
| A.3.6 | **AutoMapper** — object-object mapping giữa layer | **KHÔNG ÁP DỤNG (có lý do)** | Slide nói "one of the best libraries" (không bắt buộc) và đích đã map tay ở hàng chục DTO, build sạch + có 30 unit test. Thêm AutoMapper = thêm package + thêm cấu hình profile + không tăng giá trị bài nộp. **Cần user duyệt nếu muốn đổi.** | Chapter_02, Slide 23; đích `PRN232_PLAN.md` "Phần KHÔNG làm" (❌ AutoMapper) |
| A.3.7 | `DbContext` đăng ký như service + Seed data | **ÁP DỤNG** | `AddDbContext<ShopPetDatabaseContext>(UseSqlServer)` trong `Program.cs`. | Chapter_02, Slide 17 |
| A.3.8 | `ConnectionString` trong `appsettings.json` | **ÁP DỤNG** | Đích có `appsettings.json` + `appsettings.Development.json`. | Chapter_02, Slide 19; đích `Program.cs` |
| A.3.9 | "API controllers as thin as possible" + interface abstraction để testable | **ÁP DỤNG** | 30 interface trong `Interfaces/`; controller chỉ gọi service. | Chapter_02, Slide 26 |

---

## A.4 Chapter 03 — Media Formatters and Content Negotiation

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.4.1 | Content negotiation qua `Accept` header (`application/json`, `application/xml`) | **ÁP DỤNG** | Đã có `XmlOutputFormatter` + `AddPetShopXmlFormatters()`. | Chapter_03, Slide 21-25; đích `Services/XmlOutputFormatter.cs`, `Services/MvcBuilderExtensions.cs` |
| A.4.2 | **Custom output formatter** kế thừa `TextOutputFormatter` (override `CanWriteType`, `WriteResponseBodyAsync`) | **ÁP DỤNG** | Đích có `CsvOutputFormatter` (`text/csv`) — khớp 100% slide. | **Chapter_03, Slide 17, 29**; đích `Services/CsvOutputFormatter.cs` |
| A.4.3 | **Custom input formatter** kế thừa `TextInputFormatter` (override `CanReadType`, `ReadRequestBodyAsync`) | **ÁP DỤNG (khuyến nghị)** | Đích có output formatter nhưng chưa có input formatter. Có thể demo `text/csv` import sản phẩm — lát cắt LS-13. | **Chapter_03, Slide 17, 29**; đích `Services/CsvOutputFormatter.cs` |
| A.4.4 | `MvcOptions.InputFormatters` / `OutputFormatters` collection | **ÁP DỤNG** | Đăng ký qua `AddControllersWithViews(options => options.OutputFormatters.Add(...))`. | Chapter_03, Slide 17; đích `Program.cs` |
| A.4.5 | `System.Text.Json` — namespace serialize/deserialize JSON | **ÁP DỤNG** | `System.Text.Json` mặc định của ASP.NET Core 8. | Chapter_03, Slide 19 |
| A.4.6 | `AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null)` để đổi camelCase → PascalCase | **KHÔNG ÁP DỤNG (có lý do)** | Slide nói "nếu migrate từ Web API 2.0". Đích đã quyết định **camelCase** (`PropertyNamingPolicy = CamelCase`) vì client là Blazor/JS — đổi sang PascalCase sẽ phá vỡ toàn bộ client. | Chapter_03, Slide 18, 20; đích `Program.cs` `.AddJsonOptions` |
| A.4.7 | `JsonIgnore` để ẩn property | **ÁP DỤNG (đã có)** | Dùng trong entity/DTO khi cần. | Chapter_03, Slide 11 |
| A.4.8 | `JsonMediaTypeFormatter` / `XmlMediaTypeFormatter` / `DataContractSerializer` | **KHÔNG ÁP DỤNG (có lý do)** | Đây là API của **ASP.NET Web API 2 (System.Web)** đã lỗi thời, không tồn tại trong ASP.NET Core 8. Slide trộn lẫn 2 thế hệ framework. Đích dùng `System.Text.Json` + `XmlSerializer` của ASP.NET Core. | Chapter_03, Slide 8-9, 12 |
| A.4.9 | `IContentNegotiator.Negotiate` trả `null` → HTTP 406 | **ÁP DỤNG (hành vi framework)** | ASP.NET Core tự trả 406 khi không khớp media type. | Chapter_03, Slide 23 |
| A.4.10 | `JsonResult` / `ContentResult` **không** hỗ trợ content negotiation | **ÁP DỤNG (hiểu đúng)** | Đích dùng `Ok(obj)` / `ActionResult<T>` để có negotiation. | Chapter_03, Slide 24 |

---

## A.5 Chapter 04 — OData in ASP.NET Core Web API

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.5.1 | Package `Microsoft.AspNetCore.OData` | **ÁP DỤNG** | Đã cài 9.5.0. | Chapter_04, Slide 4, 28; đích `.csproj` |
| A.5.2 | Version `9.2.1` | **KHÔNG ÁP DỤNG (có lý do)** | Slide ghi 9.2.1 (Slide 4 notes, Slide 32, Slide 43). Đích đang chạy **9.5.0** với cùng API `AddRouteComponents`. Hạ/xoá version để khớp slide = rủi ro regression mà không có lợi ích học thuật. | Chapter_04, Slide 32, 43; đích `.csproj` |
| A.5.3 | `Microsoft.EntityFrameworkCore` **8.0.15** + `.SqlServer` **8.0.15** | **KHÔNG ÁP DỤNG (có lý do)** | Slide chỉ định 8.0.15. Đích đang dùng **8.0.8** đồng bộ cả 3 package EF. Chênh lệch patch 8.0.8 → 8.0.15 không đổi API, đổi version giữa lúc đang chạy = rủi ro restore. | Chapter_04, Slide 32; đích `.csproj` |
| A.5.4 | `Microsoft.EntityFrameworkCore.InMemory` | **ÁP DỤNG (chỉ cho demo/test)** | Chưa có ở đích. Chỉ cần khi demo OData không cần DB thật. | Chapter_04, Slide 43 |
| A.5.5 | EDM model (`ODataConventionModelBuilder`) + khai báo key tường minh | **ÁP DỤNG** | `Models/PetShopEdmModel.cs` đã có — khai báo `HasKey` cho 8 entity. | Chapter_04, Slide 29, 44-46; đích `Models/PetShopEdmModel.cs` |
| A.5.6 | `AddOData()` + đăng ký query: `Select/Filter/Count/OrderBy/Expand` | **ÁP DỤNG** | `Program.cs` đã có `.Select().Filter().OrderBy().Expand().Count().SkipToken().SetMaxTop(200)`. | Chapter_04, Slide 34, 47; đích `Program.cs` |
| A.5.7 | `IQueryable` + `[EnableQuery]` để middleware xử lý query | **ÁP DỤNG** | `PetShopODataController` trả `IQueryable` + `SingleResult.Create`. | Chapter_04, Slide 35; đích `Controllers/PetShopODataController.cs` |
| A.5.8 | Query options `$filter $sort $select $top $skip $expand $count $inlinecount` | **ÁP DỤNG** | Đã hỗ trợ đầy đủ (P3 xong). | Chapter_04, Slide 11, 36-38; đích `Controllers/PetShopODataController.cs` |
| A.5.9 | `$count` cần **OData routing** (path segment `/PetShop/Products/$count`) | **ÁP DỤNG** | Đích khai báo route thủ công `[HttpGet("PetShop/Products/$count")]`. | Chapter_04, Slide 38, 40; đích `Controllers/PetShopODataController.cs` |
| A.5.10 | `$format` (atom/json/xml) | **KHÔNG ÁP DỤNG (có lý do)** | OData v9 không hỗ trợ `application/atom+xml`; JSON là mặc định, XML đã có qua `XmlOutputFormatter` riêng. | Chapter_04, Slide 20 |
| A.5.11 | Server-driven paging / "next link" | **ÁP DỤNG (một phần)** | Đích có `SkipToken` (server-driven) nhưng không trả "next link". | Chapter_04, Slide 21; đích `Program.cs` |
| A.5.12 | OData **Batch requests / ChangeSet** | **KHÔNG ÁP DỤNG (có lý do)** | Slide mô tả giao thức; OData v9 của ASP.NET Core không hỗ trợ batch/ChangeSet. Không có cách khác ngoài viết middleware tay — tốn công, không phục vụ bài nộp. | Chapter_04, Slide 25 |
| A.5.13 | OData **Functions / Actions** (`$links`, deep insert) | **KHÔNG ÁP DỤNG (có lý do)** | OData v9 ASP.NET Core không có Actions/Functions server-side. Các thao tác này đã được REST endpoint (`api/spa-booking/*`, `api/orders/*`) phục vụ đầy đủ. | Chapter_04, Slide 8, 26-27 |
| A.5.14 | API versioning (URL segment / request header / query string) | **KHÔNG ÁP DỤNG (có lý do)** | Slide nhắc OData "Feature Areas" nhưng không nêu package/implementation cụ thể. Dự án 1 phiên bản API. | Chapter_04, Slide 7 |
| A.5.15 | `Microsoft.AspNetCore.OData` hỗ trợ `.NET 8` | **ÁP DỤNG** | `net8.0` + 9.5.0 tương thích. | Chapter_04, Slide 28 |
| A.5.16 | Swagger UI hiển thị OData service | **ÁP DỤNG** | `AddSwaggerGen` đã bật. | Chapter_04, Slide 51 |

---

## A.6 Chapter 05 — Binding, Validation and Routing

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.6.1 | `UseRouting()` + `UseEndpoints()` middleware | **ÁP DỤNG** | `MapControllers()` + `MapRazorComponents` đã map endpoint. | Chapter_05, Slide 4; đích `Program.cs` |
| A.6.2 | **Attribute Routing** là cách ưu tiên | **ÁP DỤNG** | 100% controller đích dùng `[Route("api/...")]` + `[HttpGet]`/`[HttpPost]`. | **Chapter_05, Slide 5**; đích `Controllers/*.cs` |
| A.6.3 | Route variable dùng `{param}`; query string | **ÁP DỤNG** | Ví dụ `api/pets/{petId:int}`, `?customerId=`. | Chapter_05, Slide 6 |
| A.6.4 | **Repository Pattern** — abstraction layer giữa data access và business logic | **ÁP DỤNG** | 7 repository + interface tương ứng trong `Repositories/` + `Interfaces/`. | **Chapter_05, Slide 32-37**; đích `Repositories/*.cs` |
| A.6.5 | **Repository Manager** (unit-of-work trên nhiều repo) | **KHÔNG ÁP DỤNG (có lý do)** | Đích dùng `ShopPetDatabaseContext` trực tiếp trong service cho thao tác nhiều bảng (ví dụ spa booking) — EF Core DbContext đã là unit-of-work. Thêm lớp Manager là thừa. | Chapter_05, Slide 37; đích `Data/ShopPetDatabaseContext.cs` |
| A.6.6 | **DTO** + **AutoMapper** trong Repository demo | **ÁP DỤNG một phần** | DTO ✅ có. AutoMapper ❌ (xem A.3.6). | Chapter_05, Slide 41-43 |
| A.6.7 | **Binding source attributes**: `[FromBody] [FromForm] [FromHeader] [FromQuery] [FromRoute] [FromServices]` | **ÁP DỤNG** | Đã dùng đúng ở các action; `[FromForm] IFormFile` ở upload ảnh pet. | **Chapter_05, Slide 17-19**; đích `Controllers/PetsController.cs` |
| A.6.8 | Primitive vs Complex model binder (GET/DELETE vs POST/PUT) | **ÁP DỤNG (hành vi framework)** | Không cần code riêng. | Chapter_05, Slide 11-13 |
| A.6.9 | Validation attributes: `[Required] [EmailAddress] [StringLength] [Range] [RegularExpression] [Compare] [Phone] [Url] [CreditCard] [ValidateNever]` | **ÁP DỤNG** | P1 đã thêm vào DTO (Auth, Product, Boarding, SpaBooking, PetCrud, BreedPricing). | **Chapter_05, Slide 26-27**; đích `Models/*Dtos.cs` |
| A.6.10 | Custom validation attribute kế thừa `ValidationAttribute` override `IsValid` | **ÁP DỤNG (khuyến nghị)** | Chưa có trong đích. Có thể dùng cho `PriceAdjustPercent` 1–500% hoặc giá spa theo giống. | Chapter_05, Slide 28 |
| A.6.11 | `ModelState` / `ModelStateDictionary.ClearValidationState` + `TryValidateModel` | **ÁP DỤNG** | Dùng trong `ApiExceptionMiddleware` / Blazor `EditForm`. | Chapter_05, Slide 24-25 |
| A.6.12 | **Server-side validation** trước khi action chạy | **ÁP DỤNG** | `[ApiController]` + `ModelStateInvalidFilter`. | Chapter_05, Slide 29 |
| A.6.13 | **Client-side validation** bằng **jQuery Validate 1.19.1** + `jquery-validation-unobtrusive 3.2.11` | **ÁP DỤNG (chỉ ở trang demo JS client)** | Blazor Server đã có `DataAnnotationsValidator` (server-side). jQuery Validate chỉ cần cho trang demo MVC của Ch07. **Lưu ý:** slide trích CDN — khuyến nghị copy local vào `wwwroot/lib/` để demo offline. | **Chapter_05, Slide 30**; đích `Components/Pages/*.razor` |
| A.6.14 | Test API bằng **Postman** | **ÁP DỤNG (quy trình, không phải code)** | Đã dùng để verify JWT/OData. | Chapter_05, Slide 52 |
| A.6.15 | `ServiceExtensions` class để đăng ký dependency | **ÁP DỤNG** | `Services/SelfHttpClientExtensions.cs`, `Services/MvcBuilderExtensions.cs`. | Chapter_05, Slide 34; đích `Services/*.cs` |

---

## A.7 Chapter 06 — Implement security in RESTful WebService

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.7.1 | **JWT Bearer** authentication (`AddJwtBearer`, `JwtBearerDefaults.AuthenticationScheme`) | **ÁP DỤNG** | Đã cài `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.33, có `JwtTokenService`. | **Chapter_06, Slide 16, 31**; đích `Services/JwtTokenService.cs`, `.csproj` |
| A.7.2 | `Configuration.Bind("JwtSettings", options)` | **ÁP DỤNG** | `Configure<JwtSettings>` + section `JwtSettings` trong `appsettings.Development.json`. | Chapter_06, Slide 16; đích `Program.cs` |
| A.7.3 | JWT 3 phần: Header / Payload / Signature; HMAC SHA256 hoặc RSA | **ÁP DỤNG** | `JwtTokenService` dùng HS256 + kiểm issuer/audience/signature/lifetime. | Chapter_06, Slide 32-33 |
| A.7.4 | Endpoint login trả **access token** + **refresh token** | **ÁP DỤNG một phần** | Đích có `POST /api/auth/token` trả access token. **Refresh token ❌** — xem A.7.5. | Chapter_06, Slide 40-42; đích `Controllers/AuthController.cs` |
| A.7.5 | **Refresh token** flow (`POST /api/account/refreshtoken`) | **KHÔNG ÁP DỤNG (có lý do)** | Slide demo Postman. Dự án chạy 100% local, access token 120 phút — thêm refresh token = thêm bề mặt lỗi (lưu trữ, thu hồi, race) không đổi giá trị bài nộp. | **Chapter_06, Slide 40, 42**; đích `PRN232_PLAN.md` "❌ Refresh token flow" |
| A.7.6 | Client gửi token qua header `Authorization: Bearer <token>` | **ÁP DỤNG** | `AddPolicyScheme("Default")` forward: có Bearer → JwtBearer. | Chapter_06, Slide 29, 41; đích `Program.cs` |
| A.7.7 | `AllowAnonymous` cho endpoint login | **ÁP DỤNG** | `POST /api/auth/token` là endpoint public. | **Chapter_06, Slide 27** ("sign-in endpoint must have Allowanonymous decorator"); đích `Controllers/AuthController.cs` |
| A.7.8 | Authorization: Roles / Policies / handlers / Resource-based | **ÁP DỤNG một phần** | Đích có `AddAuthorization()` + `[Authorize]` ở `GET /api/auth/me`. Theo AGENTS.md §0, **không** gắn `[Authorize]` hàng loạt (phá self-HTTP Blazor). | Chapter_06, Slide 2, 17-18; đích `Program.cs`, `AGENTS.md` §5 |
| A.7.9 | `AuthorizeAttribute` / `AllowAnonymousAttribute` trong `Microsoft.AspNetCore.Authorization` | **ÁP DỤNG** | Đã import ở `AuthController`. | Chapter_06, Slide 18 |
| A.7.10 | **ASP.NET Core Identity** (Managers/Stores, UserManager, SignInManager, password hashing, lockout, MFA, external providers) | **KHÔNG ÁP DỤNG (có lý do)** | Slide nói "you need not write any code" — nhưng triển khai Identity = thêm bảng Identity vào DB đang scaffold, vi phạm AGENTS.md §2.5 (cấm đổi schema DB). Đã có BCrypt + JWT thay thế. | **Chapter_06, Slide 19-24**; đích `AGENTS.md` §4.4 "❌ Triển khai ASP.NET Core Identity" |
| A.7.11 | `UseAuthentication()` / `UseAuthorization()` middleware | **ÁP DỤNG** | Pipeline có đủ 2 middleware, đúng thứ tự (Auth trước Authz). | **Chapter_06, Slide 25-26**; đích `Program.cs` |
| A.7.12 | `IAuthenticationService`, "schemes", `AuthenticationBuilder.AddScheme` | **ÁP DỤNG** | Đích có scheme `Session` + `Bearer` qua `AddPolicyScheme`. | Chapter_06, Slide 15-16; đích `Services/SessionHeaderAuthenticationHandler.cs` |
| A.7.13 | Best practice: **Validate all inputs**, **no sensitive data in URL**, **restrict method execution**, **validate malformed XML/JSON**, **generic error messages** | **ÁP DỤNG (khuyến nghị)** | Input validation ✅ (P1). Sensitive data in URL ⚠️ — `api/pet-services` nhận PascalCase, `api/pets/.../pricing?basePrice=` cho phép client override giá (SOURCE_API_ENDPOINTS §7). Generic error ✅ qua ProblemDetails. | **Chapter_06, Slide 3-5**; đích `Controllers/*.cs` |
| A.7.14 | **TLS/SSL** transport security | **ÁP DỤNG** | `UseHttpsRedirection()` + `UseHsts()` trong pipeline Production. | Chapter_06, Slide 7; đích `Program.cs` |
| A.7.15 | Autoscaling (vertical/horizontal), Azure VM scale sets, Service Fabric, App Service, Azure Functions | **KHÔNG ÁP DỤNG (có lý do)** | Slide nói về hạ tầng Azure cloud. Dự án chạy **100% LOCAL** (AGENTS.md §0) — không có Azure, không có scale set. | Chapter_06, Slide 8-13; đích `AGENTS.md` §0 |
| A.7.16 | Autoscaling patterns: Throttling, Competing Consumers, Monitoring & diagnostics | **KHÔNG ÁP DỤNG (một phần)** | Throttling/rate-limit ⚠️ chưa có. Monitoring ⚠️ chưa có. Với dự án local 1 instance, pattern này không có ý nghĩa vận hành. **Ghi nhận là gap tham chiếu.** | Chapter_06, Slide 13 |
| A.7.17 | `AddCookie` authentication scheme | **KHÔNG ÁP DỤNG (có lý do)** | Đích dùng Blazor `ProtectedLocalStorage` cho session nội bộ, không cần cookie scheme của ASP.NET Core. | Chapter_06, Slide 16 |

---

## A.8 Chapter 07 — JavaScript Clients and ASP.NET Web API

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.8.1 | **jQuery** — cú pháp `$(selector).action()`, selectors (`#id`, `.class`), event methods | **ÁP DỤNG (lát cắt LS-11)** | Chưa có trong đích — `wwwroot/lib/jquery/` rỗng/missing. Cần demo client gọi API. | **Chapter_07, Slide 6-12** |
| A.8.2 | **AJAX** qua `$.ajax()`, `$.get()`, `$.post()`, `$.param()` | **ÁP DỤNG (lát cắt LS-11)** | Slide demo đầy đủ CRUD (GET all / GET by id / UPDATE / CREATE). | **Chapter_07, Slide 13-15, 25-36** |
| A.8.3 | **`XMLHttpRequest`** (raw API) — demo song song với jQuery | **ÁP DỤNG (khuyến nghị trong lát LS-11)** | Slide có 4 slide demo XHR (Slide 27, 30, 33, 36). Nên làm để khớp 100%. | **Chapter_07, Slide 4-5, 27, 30, 33, 36** |
| A.8.4 | Demo trên **Razor View / MVC** (HTML + `<script>` thuần) | **ÁP DỤNG** | Đích không có `Views/`. Cần tạo `Controllers/ApiDemoController.cs` (MVC) + `Views/ApiDemo/Index.cshtml`. | **Chapter_07, Slide 16, 25, 28, 31, 34** |
| A.8.5 | **IoC container** cấu hình trong `Program.cs` (đăng ký `IRepository` + service) | **ÁP DỤNG** | Đích đã có DI đầy đủ 45+ registration. | **Chapter_07, Slide 18-21**; đích `Program.cs` |
| A.8.6 | `IRepository` interface + implementation + CRUD controller | **ÁP DỤNG** | `IProductRepository`/`ProductRepository` đã có. **Gap:** `ProductsController` chỉ có GET — cần thêm POST/PUT/DELETE cho demo (PRN232_PLAN P6.3). | **Chapter_07, Slide 18-24**; đích `Controllers/ProductsController.cs` |
| A.8.7 | jQuery tải từ `jquery.com` (production minified / development) | **ÁP DỤNG (khuyến nghị copy local)** | Slide nói đặt `<script>` trong `<head>`. Với demo offline → copy `jquery.min.js` vào `wwwroot/lib/jquery/`. | **Chapter_07, Slide 8** |
| A.8.8 | `EventSource` (server-sent events) | **KHÔNG ÁP DỤNG (có lý do)** | Slide chỉ nhắc "consider using" như một ghi chú bên lạnh; không có demo. Nguồn OJT cũng không dùng SSE. | Chapter_07, Slide 4 |
| A.8.9 | React / Vite / React Router / Chart.js | **KHÔNG ÁP DỤNG (có lý do)** | Nguồn dùng React nhưng slide Ch07 **không** yêu cầu React — slide dạy jQuery thuần. Đích là Blazor Server. Giữ React = 2 frontend chạy song song, không khớp "100% bản gốc" về mặt học phần. | Chapter_07 (không đề cập React); đích `AGENTS.md` §1 |

---

## A.9 Chapter 08 — Building RESTful Service with WCF

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.9.1 | **CoreWCF** NuGet: `CoreWCF.Http`, `CoreWCF.Primitives` | **KHÔNG ÁP DỤNG (có lý do)** | Slide nói rõ CoreWCF "**không phải** bản thay thế đầy đủ WCF", chỉ hỗ trợ SOAP server-side. Dự án có REST API hoàn chỉnh; thêm SOAP song song là chi phí lớn, rủi ro đụng port/pipeline. | **Chapter_08, Slide 29-31, 34** |
| A.9.2 | `dotnet new --install CoreWCF.Templates` | **KHÔNG ÁP DỤNG** | Cùng lý do A.9.1. | Chapter_08, Slide 33 |
| A.9.3 | `dotnet-svcutil` (tạo proxy client) + `System.ServiceModel.*` packages | **KHÔNG ÁP DỤNG** | Cùng lý do A.9.1. | Chapter_08, Slide 33, 39-40 |
| A.9.4 | `[ServiceContract] [OperationContract] [DataContract] [DataMember] [MessageContract] [MessageHeader] [MessageBody]` | **KHÔNG ÁP DỤNG** | Attribute của `System.ServiceModel` — hệ sinh thái WCF/SOAP. | **Chapter_08, Slide 16-19, 28** |
| A.9.5 | Bindings: `NetTCPBinding`, `WSDualHttpBinding`, `WebHttpBinding`, `NetMsmqBinding`, `WSFederationHttpBinding`, `NetPeerTCPBinding`, `MsmqIntegrationBinding` | **KHÔNG ÁP DỤNG** | Toàn bộ binding của WCF. | Chapter_08, Slide 20-22 |
| A.9.6 | WCF Architecture 4 tầng (Contracts/Service Runtime/Messaging/Hosting), Address-Binding-Contract, message patterns (One-Way/Request-Reply/Duplex) | **KHÔNG ÁP DỤNG** | Slide giảng thuyết WCF. | Chapter_08, Slide 7-14 |
| A.9.7 | Hosting: IIS, WAS, Self-hosting console, Windows Service | **KHÔNG ÁP DỤNG** | Đích host bằng Kestrel. | Chapter_08, Slide 26-27 |
| A.9.8 | WCF transactions, reliable sessions, behaviors, WS-* security | **KHÔNG ÁP DỤNG** | Slide giảng thuyết. | Chapter_08, Slide 23-25 |

> **Lưu ý kiến trúc:** Ch08 là chương **giới thiệu công nghệ lịch sử**. Đề bài PRN232 ("Building Cross-Platform Back-End Application") nhấn mạnh *cross-platform* — WCF chỉ chạy Windows. Đây là lý do cốt lõi để bỏ.

---

## A.10 Chapter 09 — Introduction to gRPC Service

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.10.1 | Package `Grpc.AspNetCore` | **ÁP DỤNG (lát cắt LS-09)** | Chưa có ở đích. Slide nêu rõ package. | **Chapter_09, Slide 29**; đích `.csproj` (thiếu) |
| A.10.2 | Client packages: `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools` | **ÁP DỤNG (lát cắt LS-09)** | Cần cho server + client demo. | **Chapter_09, Slide 42** |
| A.10.3 | **Protocol Buffers / proto3** — file `.proto` trong thư mục `Protos/` | **ÁP DỤNG (lát cắt LS-09)** | Slide yêu cầu viết service definition trong `.proto` dùng proto3. | **Chapter_09, Slide 9-13, 35** |
| A.10.4 | Cấu trúc project gRPC: `Protos/greet.proto`, thư mục `Services/`, `Program.cs`, `Startup.cs` | **ÁP DỤNG một phần** | Đích không có `Startup.cs` (minimal hosting .NET 8) → chỉ giữ `Protos/` + `Services/Grpc/`. | **Chapter_09, Slide 32**; đích `Program.cs` |
| A.10.5 | `AddGrpc()` + `MapGrpcService<T>()` | **ÁP DỤNG (lát cắt LS-09)** | Slide nói rõ extension method. | **Chapter_09, Slide 29-30, 36** |
| A.10.6 | **4 loại RPC**: Unary / Server streaming / Client streaming / Bidirectional streaming | **ÁP DỤNG (lát cắt LS-09)** | Slide nêu cả 4, khớp "4 Types of API in gRPC" (Slide 16). Nên demo `GetPets` (unary) + `WatchBookings` (server streaming). | **Chapter_09, Slide 16-18** |
| A.10.7 | gRPC chạy trên **Kestrel / IIS** | **ÁP DỤNG** | Đã dùng Kestrel. | Chapter_09, Slide 30 |
| A.10.8 | gRPC service có đầy đủ **DI** + **Logging** (resolve `ILogger` qua constructor) | **ÁP DỤNG** | ASP.NET Core DI sẵn có; dùng `ILogger<PetCatalogService>`. | **Chapter_09, Slide 31** |
| A.10.9 | Client Controller gọi gRPC (`GrpcChannel.ForAddress`) | **ÁP DỤNG (lát cắt LS-09)** | Slide demo có bước "Client Controller". | **Chapter_09, Slide 44** |
| A.10.10 | HTTP/2, metadata, channels, deadlines, cancellation | **ÁP DỤNG (hành vi framework)** | Grpc.AspNetCore xử lý; metadata dùng cho auth. | Chapter_09, Slide 19-28 |
| A.10.11 | `ServiceDefinition`, `ProtoBuf`, `proto2` | **KHÔNG ÁP DỤNG (có lý do)** | Slide nói rõ "recommend proto3 ... avoiding compatibility issues". Dùng proto3. | Chapter_09, Slide 13 |

---

## A.11 Chapter 10 — Introduction to Microservices Architecture

| # | Yêu cầu công nghệ slide nêu | Quyết định | Lý do | Bằng chứng |
|---|---|---|---|---|
| A.11.1 | **Docker** — `Dockerfile` multi-stage `mcr.microsoft.com/dotnet/sdk:8.0` → `aspnet:8.0` | **ÁP DỤNG (lát cắt LS-14)** | Slide có code Dockerfile đầy đủ (Slide 28). Rẻ, khớp slide, không phá hệ thống. | **Chapter_10, Slide 28-30** |
| A.11.2 | `docker build -t <tag> .` / `docker run -it --rm -p 3000:80` | **ÁP DỤNG (lát cắt LS-14)** | Lệnh vận hành cơ bản. | Chapter_10, Slide 29-30 |
| A.11.3 | **`.dockerignore`** | **ÁP DỤNG (khuyến nghị)** | Không có trong slide nhưng là best-practice đi kèm Dockerfile. PRN232_PLAN P7.2 đã có sẵn kế hoạch. | Chapter_10, Slide 28-29 |
| A.11.4 | `docker-compose.yml` (multi-service) | **ÁP DỤNG (khuyến nghị)** | Hữu ích để nối app ↔ SQL Server. Slide không đề cập → ghi "khuyến nghị". | PRN232_PLAN P7.3 |
| A.11.5 | **Tách microservice thật** — nhiều service độc lập, mỗi service sở hữu data riêng | **KHÔNG ÁP DỤNG (có lý do)** | Slide giới thiệu khái niệm ("What are microservices?"). Đích chạy **1 DB local, 1 project** (AGENTS.md §0). Tách thật = phá vỡ self-HTTP, phá schema, vượt xa phạm vi bài nộp. | **Chapter_10, Slide 3-5**; đích `AGENTS.md` §0 |
| A.11.6 | **Azure Service Fabric** — stateless/stateful services | **KHÔNG ÁP DỤNG (có lý do)** | Dịch vụ Azure cloud, dự án local. | Chapter_10, Slide 19 |
| A.11.7 | **Event bus / publish-subscribe / AMQP / RabbitMQ / Azure Service Bus** | **KHÔNG ÁP DỤNG (có lý do)** | Slide nói "**might** be using an event bus". Không có broker trong môi trường local. Thay thế bằng in-process background service nếu cần (chưa cần). | Chapter_10, Slide 9-10, 16 |
| A.11.8 | **Docker Desktop for Windows** + WSL 2 | **ÁP DỤNG (môi trường)** | Yêu cầu cài đặt trước khi demo. | Chapter_10, Slide 23 |
| A.11.9 | Microservice attributes: isolated functionality/data, independent deployment, technology adoption | **KHÔNG ÁP DỤNG (cùng lý do A.11.5)** | Slide định nghĩa khái niệm. | Chapter_10, Slide 4 |
| A.11.10 | Synchronous REST vs Asynchronous messaging (chain-request anti-pattern) | **ÁP D�_APPLY ở mức nhận thức** | Đích đã tránh chain-request nhờ self-HTTP trực tiếp + cùng DB transaction. Ghi nhận để trình bày. | Chapter_10, Slide 8, 12 |

---

# PHẦN B — SƠ ĐỒ SOLUTION / PROJECT CUỐI CÙNG

## B.1 Quyết định: GIỮ 1 PROJECT

**Quyết định: GIỮ 1 project `PetShop_PRN222.csproj` (Blazor Server + API Controllers). KHÔNG tách solution.**

### B.1.1 Lý do giữ 1 project

| # | Lý do | Bằng chứng |
|---|---|---|
| 1 | **Slide Ch02 Slide 5 nói "3-Layer Application" — 3 LAYER, không phải 3 project.** Slide vẽ Presentation/Business/Data Access như 3 khối trong 1 ứng dụng. Tách project là hiểu sai slide. | Chapter_02, Slide 5 |
| 2 | **Slide Ch10 mô tả microservices như khái niệm**, không yêu cầu triển khai. Mục tiêu của PRN232 là xây 1 back-end app cross-platform. | Chapter_10, Slide 3-5 |
| 3 | **Cơ chế self-HTTP của Blazor sẽ vỡ nếu tách project.** App gọi chính nó qua HTTP: `Blazor page → Service → HttpClient (BaseAddress = origin) → Controller → Repository → DB`. Tách project ⇒ phải cấu hình lại `BaseAddress`, DI cross-project, CORS, và `session header`. AGENTS.md §1 đã ghi rõ đây là điểm đặc biệt cần hiểu. | `AGENTS.md` §1 "Luồng kiến trúc đặc biệt cần hiểu" |
| 4 | **Target inventory xác nhận đích đã là bản Blazor hoàn chỉnh** — 68 Razor, 22 Controller, 39 Service/Repository, 52 Model, 30 test. Tái sử dụng nguyên vẹn (§7.1) là ưu tiên cao nhất. | `TARGET_EXISTING.md` §0, §7.1 |
| 5 | **PRN232_PLAN đã chốt**: "❌ Tách microservices thật (Ch10) — project chạy local, 1 DB, tách service chỉ gây rắc rối". | `PRN232_PLAN.md` "Phần KHÔNG làm" |
| 6 | **Chi phí tách project > lợi ích**: phải thêm `.csproj` mới, `ProjectReference`, `InternalsVisibleTo`, versioning package, CI multi-project, DI composition root mới. Trong khi bài chấm là 1 nhóm project local. | Đánh giá kiến trúc |

### B.1.2 Điều kiện để xem xét lại (nếu user muốn)

Tách project **chỉ** hợp lý khi thỏa **cả 3**:
1. Có nhiều nhóm dev song song làm việc trên cùng codebase → cần biên dịch độc lập.
2. Có ≥ 2 service thực sự cần deploy độc lập + DB riêng.
3. Nhóm chấp nhận chuyển self-HTTP → gọi service thật qua HTTP/gRPC + service discovery.

Hiện tại **không** thỏa điều kiện nào. → Giữ 1 project.

### B.1.3 Layout thư mục chuẩn theo slide (3-layer)

```
PetShop_PRN222.sln
└── PetShop_PRN222.csproj                    ← 1 project duy nhất, net8.0
    │
    ├── Program.cs                            ← Composition Root (DI + Middleware pipeline)
    ├── appsettings.json / .Development.json   ← Configuration
    │
    │╔══════════════════════════════════════════════════════════════════╗
    │║  PRESENTATION LAYER  (slide Ch02 Slide 5)                          ║
    │║  — giao diến người dùng, KHÔNG được chạm DB trực tiếp            ║
    │╚══════════════════════════════════════════════════════════════════╝
    ├── Components/
    │   ├── Pages/         (34 .razor — route @page, + .razor.cs partial)
    │   ├── Layout/        (MainLayout, StaffWorkspaceLayout, EmptyLayout)
    │   ├── Shared/        (16 component tái sử dụng)
    │   ├── Home/          (HeroBanner, FeaturedCategories, ProductList, CustomerReviews)
    │   ├── Products/      (ProductsSidebar)
    │   ├── App.razor, Routes.razor, _Imports.razor
    │
    ├── Controllers/                        (22 file — API endpoints [ApiController])
    │   ├── AuthController.cs               api/auth/*
    │   ├── ProductsController.cs           api/products/*
    │   ├── PetShopODataController.cs       PetShop/* (OData)
    │   ├── SpaBookingController.cs         api/spa-booking/*
    │   ├── PayOSController.cs              api/payos/*
    │   ├── ShiftExchangeController.cs (+ .Requests.cs, .Processing.cs)
    │   ├── ... (Boarding, BreedPricing, Cart, Checkout, Config, EmailTest,
    │   │         Notification, Orders, Payroll, Pets, PetServices, Review, Attendance)
    │   └── ApiDemoController.cs            ★ MỚI — lát LS-11 (MVC demo Ch07)
    │
    │╔══════════════════════════════════════════════════════════════════╗
    │║  BUSINESS LAYER  (slide Ch02 Slide 5)                             ║
    │║  — logic nghiệp vụ, KHÔNG ghi SQL, KHÔNG biết HTTP                  ║
    │╚══════════════════════════════════════════════════════════════════╝
    ├── Services/                           (39 file)
    │   ├── *Service.cs                     nghiệp vụ (Auth, SpaBooking, Boarding, Payroll,
    │   │                                   Attendance, Staff*, Product, PetService)
    │   ├── *Client.cs                      typed client gọi chính API (self-HTTP)
    │   ├── *Formatter.cs                   CsvOutputFormatter, XmlOutputFormatter
    │   ├── JwtTokenService.cs              JWT (Ch06)
    │   ├── ApiExceptionMiddleware.cs       ProblemDetails RFC 7807 (Ch01 Slide 37)
    │   ├── SessionHeaderAuthenticationHandler.cs  scheme Session (Ch06)
    │   ├── SelfHttpClientExtensions.cs     IoC container helper (Ch07 Slide 21)
    │   ├── MvcBuilderExtensions.cs         đăng ký formatter (Ch03 Slide 17)
    │   ├── LoggerExtensions.cs             ILogger extension (AGENTS.md §2.2)
    │   └── Grpc/                           ★ MỚI — lát LS-09 (Ch09)
    │       └── PetCatalogService.cs
    │
    │╔══════════════════════════════════════════════════════════════════╗
    │║  DATA ACCESS LAYER  (slide Ch02 Slide 5)                           ║
    │║  — truy vấn EF Core, không chứa business rule                     ║
    │╚══════════════════════════════════════════════════════════════════╝
    ├── Repositories/                       (7 file — IRepository + implementation)
    │   ├── AuthRepository.cs, ProductRepository.cs, PetServiceRepository.cs,
    │   │   ReviewRepository.cs, SpaBookingRepository.cs, BoardingRepository.cs,
    │   │   StaffRepository.cs
    │
    ├── Data/
    │   ├── ShopPetDatabaseContext.cs       39 DbSet + OnModelCreating
    │   └── SessionExtensions.cs
    │
    │╔══════════════════════════════════════════════════════════════════╗
    │║  SHARED / CROSS-CUTTING  (slide Ch02 Slide 20-23: DTO + abstraction) ║
    │╚══════════════════════════════════════════════════════════════════╝
    ├── Interfaces/                         (30 file — IRepository/IService/IClient)
    ├── Models/                             (52 file — 30 entity + 22 DTO/record)
    │
    │╔══════════════════════════════════════════════════════════════════╗
    │║  PROTOCOL LAYER  (slide Ch09)                                       ║
    │╚══════════════════════════════════════════════════════════════════╝
    ├── Protos/                             ★ MỚI — lát LS-09
    │   ├── pets.proto
    │   └── bookings.proto
    │
    ├── Views/                              ★ MỚI — lát LS-11 (MVC view Ch07)
    │   └── ApiDemo/
    │       └── Index.cshtml
    │
    ├── EmailTemplates/                     (OrderInvoice.hbs, SpaInvoice.hbs)
    │
    ├── wwwroot/                            ★ ASSET — xem PHẦN E
    │   ├── app.css, favicon.png
    │   ├── bootstrap/
    │   ├── css/template/  (17 file)   css/spa/ (2 file)
    │   ├── images/         (146 file)  ← nguồn: frontend/src/images/ + frontend/public/
    │   ├── js/             (3 file: blazorAuth.js, cartStorage.js, spaCart.js)
    │   └── lib/jquery/     ★ MỚI — lát LS-11 (Ch07)
    │
    ├── tests/PetShop.Tests/                (4 file / 30 test — xUnit)
    └── docs/                               (tài liệu kiến trúc + inventory)
```

### B.1.4 Dependency direction (quy tắc bắt buộc)

```
        ┌───────────────────────────────────────────────┐
        │           PRESENTATION LAYER                  │
        │  Components/Pages  +  Controllers             │
        └───────────────────┬───────────────────────────┘
                            │ chỉ gọi interface (DI)
                            ▼
        ┌───────────────────────────────────────────────┐
        │             BUSINESS LAYER                   │
        │  Services/*.Service  +  Services/*.Client     │
        │  + Grpc/ + Middleware + Formatter             │
        └───────────────────┬───────────────────────────┘
                            │ chỉ gọi IRepository
                            ▼
        ┌───────────────────────────────────────────────┐
        │           DATA ACCESS LAYER                  │
        │  Repositories/*  →  Data/ShopPetDatabaseContext│
        └───────────────────┬───────────────────────────┘
                            ▼
                         SQL Server
```

**Quy tắc cấm (vi phạm = không merge):**
- ❌ `Components/Pages/*.razor` inject `ShopPetDatabaseContext` → phải qua Service. (Đã có tiền lệ: `ProductDeleted.razor` bị xoá vì vi phạm — PRN232_PLAN P0.3.)
- ❌ `Repositories/*` gọi `Services/*` (đảo chiều).
- ❌ `Services/*Service` ghi SQL thô (`ExecuteSqlRawAsync` với nối chuỗi, `FromSqlRaw` — AGENTS.md §2.3).
- ❌ `Controllers/*` chứa business logic (controller phải mỏng — slide Ch02 Slide 26).

### B.1.5 Bảng mapping file nguồn → file đích (chi tiết)

#### (a) Backend — đã có ở đích (KHÔNG viết lại)

| Nguồn `Projectojt/BackEnd/` | Đích `PRN232_PetCareProject/` | Trạng thái |
|---|---|---|
| `Controllers/AuthController.cs` | `Controllers/AuthController.cs` | ✅ có (+ `api/auth/token`, `api/auth/me`) |
| `Controllers/ProductsController_temp.cs` | `Controllers/ProductsController.cs` | ✅ đã đổi tên (route `api/products`) |
| `Controllers/OrdersController_temp.cs` | `Controllers/OrdersController.cs` | ✅ đã đổi tên |
| `Controllers/CheckoutController_temp.cs` | `Controllers/CheckoutController.cs` | ✅ đã đổi tên |
| `Controllers/SpaBookingController.cs` | `Controllers/SpaBookingController.cs` | ✅ có |
| `Controllers/PetsController.cs` | `Controllers/PetsController.cs` | ✅ có |
| `Controllers/PayOSController.cs` | `Controllers/PayOSController.cs` | ✅ có |
| `Controllers/PayrollController.cs` | `Controllers/PayrollController.cs` | ✅ có |
| `Controllers/AttendanceController.cs` | `Controllers/AttendanceController.cs` | ✅ có |
| `Controllers/ShiftExchangeController.cs` | `Controllers/ShiftExchangeController.cs` (+ `.Requests.cs`, `.Processing.cs`) | ✅ có, đã tách partial |
| `Controllers/NotificationController.cs` | `Controllers/NotificationController.cs` | ✅ có |
| `Controllers/BreedPricingController.cs` | `Controllers/BreedPricingController.cs` | ✅ có |
| `Controllers/PetServicesController.cs` | `Controllers/PetServicesController.cs` | ✅ có |
| `Controllers/ReviewController.cs` | `Controllers/ReviewController.cs` | ✅ có |
| `Controllers/CartController.cs` | `Controllers/CartController.cs` | ✅ có |
| `Controllers/ConfigController.cs` | `Controllers/ConfigController.cs` | ✅ có |
| `Controllers/ProductsPageController.cs` | *(không map — redirect cứng `localhost:5173`)* | ⚠️ route `/mvc/...` trong `Program.cs` trỏ tới controller không tồn tại → cần xoá mapping hoặc tạo controller |
| `Services/AuthService.cs` | `Services/AuthService.cs` (+ `AuthService.Google.cs`) | ✅ có, đã tách partial |
| `Services/SpaBookingService.cs` | `Services/SpaBookingService.cs` (+ `.Booking.cs`) | ✅ có, đã tách partial |
| `Services/PayOSService.cs` | `Services/PayOSService.cs` (+ `.Fulfillment.cs`) | ✅ có, đã tách partial |
| `Services/EmailService.cs` | `Services/EmailService.cs` | ✅ có |
| `Services/PayrollService.cs` | `Services/PayrollService.cs` | ✅ có |
| `Services/AttendanceService.cs` | `Services/AttendanceService.cs` | ✅ có (+ 7 unit test) |
| `Services/WorkScheduleDedup` (Helpers/) | `Services/WorkScheduleDedup.cs` | ✅ có (+ 7 unit test) |
| `Repositories/*` | `Repositories/*.cs` | ✅ có 7 repo |
| `Interfaces/*` | `Interfaces/*.cs` | ✅ có 30 interface |
| `Models/*` (entity + DTO) | `Models/*.cs` | ✅ có 52 file |
| `Data/ShopPetDatabaseContext.cs` | `Data/ShopPetDatabaseContext.cs` | ✅ có |
| `EmailTemplates/*.hbs` | `EmailTemplates/*.hbs` | ✅ có |
| `Helpers/WorkScheduleDedup.cs` | `Services/WorkScheduleDedup.cs` | ✅ đã map |

#### (b) Backend — CẦN TẠO MỚI (chưa có ở đích)

| Nguồn `Projectojt/BackEnd/` | Đích cần tạo | Lát cắt | Slide |
|---|---|---|---|
| `Controllers/ChatbotController.cs` | `Controllers/ChatbotController.cs` | LS-08 | Ch00 Slide 5 (giới thiệu AI) |
| `Interfaces/IChatbotService.cs` | `Interfaces/IChatbotService.cs` | LS-08 | — |
| `Interfaces/IAiProviderService.cs` | `Interfaces/IAiProviderService.cs` | LS-08 | — |
| `Services/ChatbotService.cs` | `Services/ChatbotService.cs` (+ `.Prompt.cs` vì > 500 dòng) | LS-08 | — |
| `Services/GeminiProviderService.cs` | `Services/AI/GeminiProviderService.cs` | LS-08 | — |
| `Services/OllamaProviderService.cs` | `Services/AI/OllamaProviderService.cs` | LS-08 | — |
| `Models/ChatbotDtos.cs` | `Models/ChatbotDtos.cs` | LS-08 | — |
| `Models/ChatbotOptions.cs` | `Models/ChatbotOptions.cs` | LS-08 | — |
| `Models/ChatMessage.cs` | `Models/ChatMessage.cs` | ✅ **đã có** | tái dùng |
| `Services/PayOSService.cs` (fallback baseUrl) | `Services/PayOSService.cs` (sửa `_lastError` → DTO) | LS-07 | — |
| *(mới)* | `Protos/pets.proto` | LS-09 | Ch09 Slide 35 |
| *(mới)* | `Protos/bookings.proto` | LS-09 | Ch09 Slide 16-18 |
| *(mới)* | `Services/Grpc/PetCatalogService.cs` | LS-09 | Ch09 Slide 38-41 |
| *(mới)* | `Services/Grpc/BookingWatchService.cs` (server-streaming) | LS-09 | Ch09 Slide 17 |
| *(mới)* | `Controllers/GrpcDemoController.cs` (client demo) | LS-09 | Ch09 Slide 44 |
| *(mới)* | `Controllers/ApiDemoController.cs` (MVC) | LS-11 | Ch07 Slide 16-24 |
| *(mới)* | `Views/ApiDemo/Index.cshtml` | LS-11 | Ch07 Slide 25-36 |
| *(mới)* | `wwwroot/lib/jquery/jquery.min.js` | LS-11 | Ch07 Slide 8 |
| *(mới)* | `wwwroot/lib/jquery/jquery.validate.min.js` + `jquery.validate.unobtrusive.min.js` | LS-11 | Ch05 Slide 30 |
| *(mới)* | `Dockerfile` | LS-14 | Ch10 Slide 28 |
| *(mới)* | `.dockerignore` | LS-14 | Ch10 Slide 28-29 |
| *(mới)* | `docker-compose.yml` | LS-14 | (khuyến nghị) |
| *(mới)* | `Services/CsvInputFormatter.cs` (input formatter) | LS-13 | Ch03 Slide 17, 29 |
| *(mới)* | `Controllers/ProductImportController.cs` (demo CSV import) | LS-13 | Ch03 Slide 29 |

#### (c) Frontend React → Blazor (KHÔNG migrate React)

| Nguồn `Projectojt/frontend/src/` | Đích | Ghi chú |
|---|---|---|
| `pages/HomePage.jsx` | `Components/Pages/Home.razor` + `Components/Home/*.razor` | ✅ đã port |
| `pages/ProductsPage.jsx` | `Components/Pages/Products.razor` + `ProductList.razor` | ✅ |
| `pages/ProductDetailPage.jsx` | `Components/Pages/ProductDetail.razor` | ✅ |
| `pages/SearchProductsPage.jsx` | `Components/Pages/Search.razor` | ✅ |
| `pages/CartPage.jsx` | `Components/Pages/Cart.razor` + `CartStateService` | ✅ |
| `pages/CheckoutPage.jsx` | `Components/Pages/Checkout.razor` (+ `.razor.cs`) | ✅ |
| `pages/CodConfirmationPage.jsx` | `Components/Pages/CodConfirmation.razor` | ✅ |
| `pages/LoginPage.jsx` | `Components/Pages/Login.razor` | ✅ |
| `pages/RegisterPage.jsx` | `Components/Pages/Register.razor` | ✅ |
| `pages/ForgotPasswordPage.jsx` | `Components/Pages/ForgotPassword.razor` | ✅ |
| `pages/ProfilePage.jsx` + `ProfileEditPage.jsx` | `Components/Pages/Account.razor` | ✅ (2 route trong 1 file) |
| `pages/PetInfoPage.jsx` | `Components/Pages/PetInfo.razor` | ✅ |
| `pages/OrderHistoryPage.jsx` + `OrderHistoryDetailPage.jsx` | `Components/Pages/Orders.razor` | ✅ |
| `pages/ServicePage.jsx` + `ServiceDetailPage.jsx` | `Components/Pages/Services.razor` + `ServiceDetail.razor` | ✅ |
| `pages/SpaCartPage.jsx` | `Components/Pages/SpaCart.razor` (+ `.razor.cs`) | ✅ |
| `pages/SpaBookingHistoryPage.jsx` + `...DetailPage.jsx` | `Components/Pages/SpaBookingHistory.razor` + `SpaBookingHistoryDetail.razor` | ✅ |
| `pages/SpaInvoicePage.jsx` | `Components/Pages/SpaInvoice.razor` | ✅ |
| `pages/OrdersStaffPage.jsx` | `Components/Pages/StaffOrders.razor` | ✅ |
| `pages/SystemPage.jsx` | `Components/Pages/StaffDashboard.razor` + `StaffBookings.razor` + `StaffSchedule.razor` | ✅ tách 3 file |
| `pages/CreateProductPage.jsx` | `Components/Pages/ProductCreate.razor` + `ProductEdit.razor` | ✅ |
| `pages/AboutPage.jsx` | *(chưa có)* | ⚠️ GAP LS-12 |
| `pages/SpaBookingDetailPage.jsx` (orphan) | `Components/Pages/SpaBookingHistoryDetail.razor` | ✅ đã gộp |
| `pages/AccountPage.jsx` (re-export thừa) | *(xoá)* | ✅ không map |
| `components/home/*.jsx` (12 file) | `Components/Shared/*.razor` + `Components/Home/*.razor` | ✅ 1:1 |
| `components/products/ProductsSidebar.jsx` | `Components/Products/ProductsSidebar.razor` | ✅ |
| `components/products/ClearSearchButton.jsx` | *(chưa có)* | ⚠️ GAP LS-12 (nhỏ) |
| `components/staff/OrdersStaffPanel.jsx` | `Components/Pages/StaffOrders.razor` (inline) | ✅ |
| `components/staff/NotificationBell.jsx` | *(chưa có — API có, UI chưa)* | ⚠️ GAP LS-12 |
| `components/checkout/AddressModal.jsx` | *(kiểm tra trong `Checkout.razor`)* | ⚠️ cần verify |
| `components/checkout/daNangData.js` | *(chưa có)* | ⚠️ GAP LS-12 |
| `api/*.js` (10 file, 70 fn) | `Services/*Client.cs` (10 client) + `Controllers/*` | ✅ đã chuyển sang C# typed client |
| `api/chatbotApi.js` | `Services/ChatbotClient.cs` (nếu có UI chatbot) | ⚠️ LS-08 |
| `utils/userProfileStore.js` | `Services/UserSessionService.cs` | ✅ |
| `styles/*.css` (14 file) | `wwwroot/css/template/*.css` (16 file) | ✅ đã copy + tách |
| `styles/home.css` (2788 dòng) | `wwwroot/css/template/home.css` | ⚠️ > 500 dòng — CSS không bắt buộc tách nhưng nên xem xét |
| `styles/service.css` (3681 dòng) | `wwwroot/css/template/spa.css` + `service-react.css` | ⚠️ > 500 dòng |
| `styles/systemPage.css` (1426 dòng) | `wwwroot/css/template/*.css` (đã chia) | ✅ |
| `src/index.css` + `src/App.css` | `wwwroot/css/template/index.css` + `App.css` | ✅ (đã loại nền tối Vite) |
| `src/images/*` (144 file) | `wwwroot/images/*` | ✅ xem PHẦN E |
| `public/*` (2 file) | `wwwroot/images/*` hoặc `wwwroot/*` | ⚠️ cần quyết định (xem E.4) |
| `App.jsx` (router + guard) | `Components/Routes.razor` + `Components/App.razor` + `AuthGuard.razor` + `CustomerOnlyGuard.razor` | ✅ |
| `main.jsx` | `Program.cs` (`MapRazorComponents<App>().AddInteractiveServerRenderMode()`) | ✅ |

---

# PHẦN C — QUY ƯỚC ĐẶT TÊN

## C.1 Quy ước lấy từ slide

| # | Quy ước | Nguồn slide | Áp dụng ở đích |
|---|---|---|---|
| C.1.1 | **Namespace theo tầng**: `Project.Controllers`, `Project.Services`, `Project.Repositories`, `Project.Interfaces`, `Project.Models`, `Project.Data` | Ch02 Slide 5 (3-layer) | ✅ Đang dùng. Xem C.3.1 về việc đổi namespace. |
| C.1.2 | **Repository pattern**: interface `I<Entity>Repository` ở `Interfaces/`, implementation `<Entity>Repository` ở `Repositories/` | **Ch05 Slide 32-40** | ✅ `IProductRepository` / `ProductRepository` … (7 cặp) |
| C.1.3 | **Service pattern**: interface `I<Entity>Service` ở `Interfaces/`, implementation `<Entity>Service` ở `Services/` | Ch02 Slide 18, 24; Ch05 Slide 34 | ✅ `IAuthService`/`AuthService` … |
| C.1.4 | **DTO suffix `Dto`**, record/response dùng suffix `Response`/`Request` | Ch02 Slide 20-22; Ch05 Slide 41-42 | ✅ `RegisterRequestDto`, `AuthResponseDto`, `TokenResponseDto` |
| C.1.5 | **Entity tên số nhiều** (`Customer`, `Product`, `Order`) — bảng DB là số nhiều (`Customers`) | Ch02 Slide 7 | ✅ Đúng quy ước đích. |
| C.1.6 | **Action method**: `Get` / `GetById` / `Add` / `Update` / `Delete` hoặc `GetAllAsync` | Ch01 Slide 38; Ch05 Slide 33-36 | ✅ Cả 2 style đều dùng (repo dùng `GetAllAsync`, service dùng `GetAsync`). |
| C.1.7 | **Controller kết thúc bằng `Controller`** | Ch02 Slide 25; Ch05 Slide 44-50 | ✅ 22 controller đều theo. |
| C.1.8 | **`DbContext` kết thúc bằng `Context`** | Ch02 Slide 16; Ch04 Slide 45 | ✅ `ShopPetDatabaseContext` |
| C.1.9 | **EDM model** dùng CLR type trực tiếp, khai báo key tường minh | **Ch04 Slide 46** | ✅ `PetShopEdmModel.Build()` |
| C.1.10 | **`.proto` file** trong `Protos/`, `package` + `service` + `rpc` | **Ch09 Slide 12, 35** | ✅ sẽ tạo ở LS-09 |
| C.1.11 | **Attribute route template**: `api/<resource>` (lowercase, phân tách bằng `-`) | Ch01 Slide 27-28; Ch05 Slide 6 | ✅ `api/spa-booking`, `api/shift-exchange`, `api/pet-services` |
| C.1.12 | **HTTP verb attribute** theo action: `[HttpGet]` `[HttpPost]` `[HttpPut]` `[HttpPatch]` `[HttpDelete]` | Ch01 Slide 28, 32-33 | ✅ |
| C.1.13 | **Route constraint**: `{id:int}` | Ch05 Slide 6 | ✅ `api/pets/{petId:int}` |

## C.2 Quy ước bổ sung từ `AGENTS.md` đích (BẮT BUỘC)

| # | Quy ước | Nguồn |
|---|---|---|
| C.2.1 | **Giao tiến với chủ dự án: TIẾNG VIỆT.** Code tiếng Anh, thông báo nghiệp vụ tiếng Việt. | `AGENTS.md` §2.4 |
| C.2.2 | **KHÔNG `catch { }`** / `catch { return false; }` — phải log qua `ILogger<T>` rồi trả lỗi có thông tin. | `AGENTS.md` §2.2 |
| C.2.3 | **KHÔNG `Console.WriteLine`** trong code ứng dụng — dùng `ILogger<T>` + extension `LoggerExtensions`. Không log mật khẩu / token / `ChecksumKey`. | `AGENTS.md` §2.2 |
| C.2.4 | `Nullable = enable` → **không warning CS86xx**. Xử lý null bằng `?.` / `?? string.Empty` / `required`. | `AGENTS.md` §2.3 |
| C.2.5 | `async`/`await` bắt buộc cho I/O — **không** `.Result` / `.Wait()`. | `AGENTS.md` §2.3 |
| C.2.6 | EF read-only → `AsNoTracking()`; dùng `.Select(...)` projection thay vì load cả entity. | `AGENTS.md` §2.3 |
| C.2.7 | SQL thô chỉ `ExecuteSqlRawAsync` với tham số `@p0`; **cấm `FromSqlRaw`**. | `AGENTS.md` §2.3 |
| C.2.8 | **File > 500 dòng hoặc component > 25KB → tách.** C#: `partial class` + `<Tên>.<Phần>.cs`. Razor: tách `@code` → `<Tên>.razor.cs`. | `AGENTS.md` §2.3 |
| C.2.9 | **KHÔNG copy-paste logic** — business logic trùng lặp gom về `Services/` dùng chung. | `AGENTS.md` §2.3 |
| C.2.10 | Sửa **đúng phạm vi** yêu cầu; không tự ý đổi schema DB / seed data / `SHOP_PET_Database.sql`. | `AGENTS.md` §2.5 |
| C.2.11 | **KHÔNG** push / commit / force-push nếu chưa được yêu cầu. | `AGENTS.md` §2.5 |
| C.2.12 | **KHÔNG** tự ý gắn `[Authorize]` vào endpoint đang chạy tốt (phá luồng Blazor self-HTTP). | `AGENTS.md` §5 |
| C.2.13 | **KHÔNG hardcode `JwtSettings:Key`** — nằm ở `appsettings.Development.json` / User Secrets, ≥ 32 ký tự. | `AGENTS.md` §5 |
| C.2.14 | Đọc `.editorconfig` trước khi sửa (nullability warning → error). | `AGENTS.md` §2.3 |
| C.2.15 | Đặt tên file/class **bỏ hậu tố `_temp`** (giữ tương thích bằng `[Obsolete]` nếu cần chuyển tiếp). | `AGENTS.md` §2.3 |

## C.3 Quy ước đặc biệt cần user duyệt

| # | Điểm | Vấn đề | Khuyến nghị |
|---|---|---|---|
| C.3.1 | **Namespace gốc** | Đích đang dùng namespace root (vd `PetShop.Models`?) — cần xác minh. Slide không quy định namespace cụ thể. | Giữ nguyên namespace hiện tại (đổi = churn lớn, không lợi ích học thuật). |
| C.3.2 | **Tên file theo tầng** | Đích dùng `<Tên>Controller.cs`, `<Tên>Service.cs`, `<Tên>Repository.cs` — khớp slide. | Giữ. |
| C.3.3 | **Phân tầng thư mục con trong `Services/`** | Đang có `Services/AI/` (mới), `Services/Grpc/` (mới). Slide không quy định. | Chấp nhận — dễ tra cứu, không vi phạm 3-layer. |

---

# PHẦN D — LỘ TRÌNH CẮT LÁT TÍNH NĂNG (VERTICAL SLICE)

> **Thứ tự:** nền tảng → auth → nghiệp vụ lõi → thanh toán → chatbot/realtime → phần còn lại.
> **Mỗi lát:** tên lát cắt → file nguồn → file đích → API endpoint → asset → rủi ro → tiêu chí "done".

---

## LS-01 — Nền tảng: xác nhận baseline build + test

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | *(không có — lát xác nhận)* |
| **API endpoint** | *(không có)* |
| **Asset** | *(không có)* |
| **Rủi ro** | Baseline xanh không được đảm bảo → mọi lát sau đều khó chẩn đoán. Ngoài ra: `MapControllerRoute("default", "mvc/{controller=ProductsPage}/...")` trỏ tới controller không tồn tại (TARGET_EXISTING §8.4) → có thể 404 nếu ai truy cập `/mvc/...`. |
| **Việc làm** | 1) `dotnet build PetShop_PRN222.sln` → 0 error 0 warning. 2) `dotnet test` → 30/30. 3) Smoke test 8 endpoint lõi (xem bảng bên dưới). 4) Quyết định: xoá `MapControllerRoute` default **hoặc** tạo `ProductsPageController` → đề xuất **xoá mapping** vì Blazor đã có `/products`. |
| **Done khi** | ☐ Build 0 error / 0 warning ☐ Test 30/30 pass ☐ 8 endpoint trả 200 ☐ `/mvc/...` không còn 404 hoặc đã chốt giữ ☐ Ghi `dotnet build` output vào `docs/_inventory/build-baseline.txt` |

**Smoke test:**

| Endpoint | Kỳ vọng |
|---|---|
| `GET /api/products` | 200 |
| `GET /api/products/categories` | 200 |
| `GET /api/orders/history` | 200 |
| `GET /api/pet-services/grouped` | 200 |
| `GET /api/pets/meta/species` | 200 |
| `GET /api/boarding-rooms/availability` | 200 |
| `GET /PetShop/$metadata` | 200 (XML EDM) |
| `GET /PetShop/Products?$top=2&$count=true` | 200 + `@odata.count` |

---

## LS-02 — OData: mở rộng EDM + kiểm thử đầy đủ query options

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/BackEnd/Models/*` (entity) → `PRN232_PetCareProject/Models/*.cs` (**giữ nguyên**) → `Models/PetShopEdmModel.cs` (**mở rộng**) |
| **API endpoint** | `GET /PetShop/$metadata`, `GET /PetShop/Products`, `GET /PetShop/Products({key})`, `GET /PetShop/Products/$count`, `GET /PetShop/PetServices`, `GET /PetShop/Pets` (+ `$filter`, `$select`, `$orderby`, `$top`, `$skip`, `$expand`, `$count`, `$inlinecount`) |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) `$expand` + `Include` thủ công xung đột → phải trả `IQueryable` **không** `Include`. (b) OData v9 không tự xử lý path segment `$count` → phải khai báo route thủ công. (c) `Pet` key là `Id` không phải `PetId` → bắt buộc `HasKey`. (d) `$expand` trên collection navigation gây subquery nặng → `Ignore()`. |
| **Việc làm** | (1) Mở rộng `PetShopEdmModel` thêm `Breed`, `Species`, `Supplier`, `ProductCategory`, `Admin` nếu chưa đủ. (2) Viết bộ test script Postman/curl cho 8 query option. (3) Xác minh `$inlinecount=allpages`. (4) Xác minh `SkipToken` (server-driven paging). |
| **Done khi** | ☐ `/$metadata` trả XML đúng 8 entity set ☐ `$filter` lọc đúng ☐ `$select` chỉ trả field được chọn ☐ `$orderby` sort đúng ☐ `$top`/`$skip` phân trang đúng ☐ `$expand` kèm navigation ☐ `$count` path segment trả số thuần ☐ `$inlinecount=allpages` có `@odata.count` ☐ Build 0 warning ☐ Tất cả endpoint REST cũ vẫn 200 |

---

## LS-03 — Content Negotiation: XML/CSV formatter + input formatter

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | *(không có nguồn — theo slide)* → `Services/CsvOutputFormatter.cs` ✅ có, `Services/XmlOutputFormatter.cs` ✅ có, `Services/MvcBuilderExtensions.cs` ✅ có → **MỚI**: `Services/CsvInputFormatter.cs` |
| **API endpoint** | `GET /api/products` với `Accept: application/json` / `application/xml` / `text/csv`; `POST /api/products/import` (CSV) — **mới** |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) `Accept: application/xml;q=0.9, application/json;q=0.8` phải trả XML — kiểm chứng thứ tự ưu tiên. (b) Không khớp media type → **406** (không phải 500). (c) `ContentResult`/`JsonResult` **không** negotiate — phải dùng `Ok(obj)`. (d) `PropertyNamingPolicy` phải giữ `CamelCase` (không theo slide Ch03 Slide 20 — xem A.4.6). |
| **Việc làm** | (1) Viết `CsvInputFormatter` kế thừa `TextInputFormatter`, override `CanReadType` + `ReadRequestBodyAsync`. (2) Đăng ký vào `MvcOptions.InputFormatters`. (3) Thêm `POST /api/products/import` (multipart file CSV) để demo. (4) Test 4 case Accept header + 406. |
| **Done khi** | ☐ `Accept: application/json` → JSON ☐ `Accept: application/xml` → XML ☐ `Accept: text/csv` → CSV ☐ `Accept: application/xml;q=0.9,application/json;q=0.8` → XML ☐ `Accept: application/yaml` → 406 ☐ `POST /api/products/import` nhận CSV hợp lệ → 200 ☐ CSV sai format → 400 ProblemDetails ☐ Build 0 warning |

---

## LS-04 — Validation: DataAnnotations + ProblemDetails + custom attribute

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/BackEnd/Models/AuthDtos.cs` (không có attribute) → `PRN232_PetCareProject/Models/AuthDtos.cs` (+ attribute) |
| **API endpoint** | Tất cả endpoint có body: `POST /api/auth/register`, `/api/auth/login`, `/api/pets`, `PUT /api/pets/{id}`, `POST /api/breed-pricing`, `POST /api/spa-booking`, `POST /api/payroll/generate-all`, `POST /api/checkout` |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) Thêm `[Required]` vào DTO → Blazor `EditForm` có thể double-validate, nhưng chấp nhận. (b) `ResetPasswordRequestDto` cần `[Compare("NewPassword")]`. (c) `ApiExceptionMiddleware` phải trả `application/problem+json` đúng chuẩn, không trả `{ message }` lẫn lộn (SOURCE_API_ENDPOINTS §3). (d) Rủi ro phá luồng Blazor nếu validation chặn request hiện đang chạy (vd `OrderId` optional). |
| **Việc làm** | (1) Rà soát 6 file DTO: thêm `[Required] [EmailAddress] [StringLength] [Range] [RegularExpression] [Compare]`. (2) Tạo 1 custom attribute (VD `[PriceAdjustPercent]`) kế thừa `ValidationAttribute` override `IsValid` cho `PriceAdjustPercent` 1–500%. (3) Chuẩn hoá envelope lỗi: mọi exception → `ProblemDetails`. (4) Bổ sung `[ProducesResponseType]` cho Swagger. |
| **Done khi** | ☐ `POST /api/auth/register` email sai → 400 + `ValidationProblemDetails` ☐ `POST /api/auth/register` thiếu tên → 400 ☐ `POST /api/auth/reset-password` mismatch → 400 với lỗi `Compare` ☐ `POST /api/breed-pricing` `PriceAdjustPercent = 0` → 400 (custom attribute) ☐ Mọi lỗi trả `Content-Type: application/problem+json` ☐ Không còn endpoint trả `{ message }` lẫn lộn ☐ Test 30/30 vẫn pass ☐ Build 0 warning |

---

## LS-05 — Routing: attribute routing + route conventions + Swagger metadata

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/BackEnd/Controllers/*.cs` → `PRN232_PetCareProject/Controllers/*.cs` (giữ route đã chuẩn hoá) |
| **API endpoint** | Toàn bộ 22 controller |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) Đổi route = phá client cũ. Phải giữ **alias** cho route cũ (vd `api/spa-booking/all-bookings` ↔ `api/spa-booking/history/all` đã có 2 route trên 1 action — nên chuẩn hoá còn 1 + giữ alias). (b) `api/products-temp` → `api/products` đã đổi — nếu còn client React cũ thì hỏng. |
| **Việc làm** | (1) Rà soát 100% controller có `[Route]` attribute + `[HttpX]` — không dùng convention routing. (2) Tạo `Constants/ApiRoutes.cs` để không magic string (khuyến nghị). (3) Bổ sung `[ProducesResponseType]` ở mọi action. (4) Xoá `MapControllerRoute` trỏ `ProductsPageController` không tồn tại (hoặc tạo controller). (5) Kiểm tra Swagger hiển thị đúng nhóm endpoint. |
| **Done khi** | ☐ 100% controller dùng attribute routing ☐ 0 route convention trỏ controller không tồn tại ☐ Swagger liệt kê đúng status code từng action ☐ 30 test pass ☐ Build 0 warning ☐ Route cũ (`-temp`) không còn nhưng client đích đã dùng route mới |

---

## LS-06 — JWT Bearer: hoàn thiện demo Ch06 (token + refresh token + protected endpoint)

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/BackEnd/Services/AuthService.cs` (không có JWT) → `PRN232_PetCareProject/Services/JwtTokenService.cs` ✅ có → `Controllers/AuthController.cs` (`api/auth/token` ✅ có) |
| **API endpoint** | `POST /api/auth/token` (đã có), `GET /api/auth/me` (đã có, `[Authorize]`), **MỚI**: `POST /api/auth/refresh-token` |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) **`[Authorize]` phá luồng Blazor self-HTTP** — AGENTS.md §5 cấm tự ý gắn. Chỉ gắn ở endpoint demo. (b) `JwtSettings:Key` phải ≥ 32 ký tự, không hardcode. (c) Token lưu ở `localStorage` → XSS risk (chấp nhận, dự án local). (d) `ClockSkew` 30 giây trong plan vs 1 phút trong code đang chạy — phải giữ cái đang chạy. |
| **Việc làm** | (1) Thêm `POST /api/auth/refresh-token` nếu user duyệt (khuyến nghị: **CÓ** vì slide nêu rõ ở Slide 40/42 — giữ đúng "100% bản gốc"). (2) Viết `docs/_inventory/postman-jwt-collection.json` mô tả đúng 3 request Postman slide (login → private API → refresh). (3) Ghi tài liệu cách gắn Bearer header. (4) Demo `GET /api/auth/me` với token sai → 401. |
| **Done khi** | ☐ `POST /api/auth/token` sai mật khẩu → 401 ☐ Đúng → 200 + accessToken có claim `sub/email/name/role` ☐ `GET /api/auth/me` không token → 401 ☐ `GET /api/auth/me` token sai → 401 ☐ `GET /api/auth/me` token hợp lệ → 200 đúng user ☐ `POST /api/auth/refresh-token` → accessToken mới (nếu user duyệt) ☐ Postman collection chạy được từ file ☐ 30 test pass |

---

## LS-07 — Thanh toán PayOS: webhoook idempotent + fix rủi ro đã phát hiện

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/BackEnd/Services/PayOSService.cs` → `PRN232_PetCareProject/Services/PayOSService.cs` (+ `.Fulfillment.cs`) |
| **API endpoint** | `POST /api/payos/spa/create`, `POST /api/payos/order/create`, `POST /api/payos/webhook`, `GET /api/payos/status/{orderCode}`, `POST /api/payos/send-invoice` |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) **`Payment` record insert TRƯỚC khi tạo link PayOS** → nếu PayOS fail còn record orphan `pending` (SOURCE_COMPLEX_FLOWS §2.4). (b) **`_lastError`/`_lastResponse` là field instance của scoped service** → race khi concurrent (SOURCE_COMPLEX_FLOWS §7). (c) **Thiếu idempotency + replay protection** cho webhook (SOURCE_API_ENDPOINTS §9). (d) Rò rỉ `error` + `response` raw của PayOS ra client. (e) `GenerateOrderCode` dùng timestamp → không đảm bảo unique tuyệt đối khi gọi song song. (f) **2 luồng thanh toán song song**: `api/payos/order/create` vs `api/checkout` tạo link trực tiếp (SOURCE_API_ENDPOINTS §4). |
| **Việc làm** | (1) Chuyển `_lastError`/`_lastResponse` → trả DTO lỗi (`PayOSErrorDto`). (2) Thêm idempotency: check `Payment.PaymentStatus == "paid"` → trả idempotent 200. (3) Thêm `ProcessedWebhook` table hoặc cột `PayosEventId` để chặn replay. (4) Gỡ rò rỉ `error`/`response` ra response. (5) Gộp 2 luồng thanh toán: `api/checkout` **không** tạo link trực tiếp, mà delegate sang `IPayOSService.CreatePaymentLinkAsync`. (6) `GenerateOrderCode` → dùng `Interlocked` + timestamp nano hoặc GUID-derived để unique tuyệt đối. (7) Transaction bao quanh insert Payment + tạo link. |
| **Done khi** | ☐ Webhook gọi 2 lần cùng payload → chỉ 1 lần cập nhật DB ☐ Webhook sai signature → 401 "Invalid signature" ☐ Webhook `orderCode` không tồn tại → 400 ☐ PayOS API fail → không còn record `pending` orphan ☐ Response không còn field `error`/`response` raw ☐ 2 request `order/create` song song → 2 `orderCode` khác nhau ☐ `api/checkout` và `api/payos/order/create` dùng chung 1 code path ☐ `POST /api/payos/send-invoice` gửi được cả order + spa invoice ☐ Build 0 warning ☐ 30 test pass |

---

## LS-08 — Chatbot AI: provider + orchestration + intent detection

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/BackEnd/Controllers/ChatbotController.cs` → **MỚI** `PRN232_PetCareProject/Controllers/ChatbotController.cs`<br>`Projectojt/BackEnd/Interfaces/IChatbotService.cs` → **MỚI** `Interfaces/IChatbotService.cs`<br>`Projectojt/BackEnd/Interfaces/IAiProviderService.cs` → **MỚI** `Interfaces/IAiProviderService.cs`<br>`Projectojt/BackEnd/Services/ChatbotService.cs` → **MỚI** `Services/ChatbotService.cs` (+ `.Prompt.cs`)<br>`Projectojt/BackEnd/Services/GeminiProviderService.cs` → **MỚI** `Services/AI/GeminiProviderService.cs`<br>`Projectojt/BackEnd/Services/OllamaProviderService.cs` → **MỚI** `Services/AI/OllamaProviderService.cs`<br>`Projectojt/BackEnd/Models/ChatbotDtos.cs` → **MỚI** `Models/ChatbotDtos.cs`<br>`Projectojt/BackEnd/Models/ChatbotOptions.cs` → **MỚI** `Models/ChatbotOptions.cs`<br>`Projectojt/BackEnd/Models/ChatMessage.cs` → `Models/ChatMessage.cs` ✅ **đã có** (tái dùng)<br>`Projectojt/frontend/src/api/chatbotApi.js` → **MỚI** `Services/ChatbotClient.cs` |
| **API endpoint** | `POST /api/chatbot/message`, `POST /api/chatbot/intent`, `GET /api/chatbot/context/{customerId}`, `GET /api/chatbot/health` |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) **Bug DI nguồn**: `DefaultProvider = "groq"` nhưng không có provider Groq; `OllamaProviderService` không được đăng ký → phải dùng `IEnumerable<IAiProviderService>` + `Options` pattern (TARGET_EXISTING §8.1). (b) **Message lỗi tiếng Anh** trong khi dự án dùng tiếng Việt → phải đổi sang tiếng Việt (AGENTS.md §2.4). (c) **`SystemPromptPath` khai báo nhưng không dùng** → prompt hard-code 200+ dòng trong service → **tách file `.Prompt.cs`** (AGENTS.md §2.3 > 500 dòng). (d) **Message lỗi "Ollama is not available"** cứng trong `CallAiProviderAsync` dù provider đang chạy là Gemini → sửa. (e) **`GetHealth` luôn trả 200** → client không phân biệt được degraded → sửa thành 503 khi provider chưa cấu hình. (f) **Chatbot không lưu lịch sử vào DB** dù bảng `ChatMessages` có sẵn → cân nhắc ghi lịch sử. (g) `DetectIntentRequestDto` nằm trong file controller → phải chuyển sang `Models/`. (h) API key Gemini hard-code trong source → đưa vào `appsettings.Development.json` (không commit). (i) `TryHandleProductQueryDirectlyAsync` là dead code → hoặc gọi, hoặc xoá. |
| **Việc làm** | (1) Tạo `Interfaces/IAiProviderService.cs` + 2 implementation. (2) Tạo `Models/ChatbotOptions.cs` (bỏ `DefaultProvider = "groq"`, dùng `DefaultProviderName` có giá trị thật). (3) Tạo `Services/ChatbotService.cs` — tách prompt sang `Services/ChatbotService.Prompt.cs`. (4) Copy **nguyên văn** system prompt từ SOURCE_COMPLEX_FLOWS §1.5.2 (kể cả danh sách thương hiệu bị cấm) — giữ đúng hành vi bản gốc. (5) Đăng ký DI: `AddHttpClient` cho từng provider + `IEnumerable<IAiProviderService>`. (6) Đổi toàn bộ message sang tiếng Việt. (7) Sửa `GetHealth` → 503 khi degraded. (8) Chuyển `DetectIntentRequestDto` → `Models/ChatbotDtos.cs`. (9) Tạo `Services/ChatbotClient.cs` (typed client self-HTTP). (10) Tạo UI chat: `Components/Pages/Chat.razor` + `Components/Shared/ChatWidget.razor` (route `/chat`). |
| **Done khi** | ☐ `GET /api/chatbot/health` → 200 khi provider OK, 503 khi chưa cấu hình ☐ `POST /api/chatbot/message` "xin chào" → trả lời tiếng Việt có emoji 🐾 ☐ `POST /api/chatbot/intent` "tôi muốn mua đồ chơi cho chó" → `intent = "product_search"`, `parameters.product_type = "đồ chơi"` ☐ `POST /api/chatbot/message` với `CustomerId` → context có pet + booking thật từ DB ☐ `POST /api/chatbot/message` message rỗng → 400 (tiếng Việt) ☐ `POST /api/chatbot/message` message > 2000 ký tự → 400 ☐ Provider lỗi mạng → 502 ☐ Provider chưa cấu hình → 503 ☐ Prompt **không** còn nằm trong file service chính (đã tách `.Prompt.cs`) ☐ Cả 2 provider (Gemini + Ollama) đều resolve được từ DI ☐ Chat UI tại `/chat` gửi/nhận được ☐ Build 0 warning ☐ 30 test pass |

---

## LS-09 — gRPC: proto3 service + 4 loại RPC + client demo

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | *(không có nguồn OJT — theo slide)* → **MỚI** `Protos/pets.proto`, `Protos/bookings.proto`, `Services/Grpc/PetCatalogService.cs`, `Services/Grpc/BookingWatchService.cs`, `Controllers/GrpcDemoController.cs` |
| **API endpoint** | gRPC (HTTP/2 + protobuf): `PetCatalog/GetPets`, `PetCatalog/GetPetById`, `BookingWatch/WatchBookings` (server streaming), `BookingWatch/StreamBookings` (client streaming), `BookingWatch/SyncBookings` (bidirectional). REST demo: `GET /api/grpc-demo/pets` (proxy sang gRPC) |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) **HTTP/2 + TLS**: browser không gọi được gRPC trực tiếp → demo qua REST proxy hoặc `grpcurl`. (b) `Grpc.Tools` cần `Protobuf Include="Protos\*.proto"` trong `.csproj`. (c) `Grpc.AspNetCore` 2.85.0 phải tương thích `net8.0`. (d) `Google.Protobuf` xung đột version nếu trùng package khác. (e) Server-streaming cần `IAsyncStreamWriter` + `CancellationToken` — rủi ro rò rỉ connection. (f) Package `Grpc.AspNetCore` có thể kéo thêm `Grpc.Net.Client` — kiểm tra transitive. |
| **Việc làm** | (1) Thêm 3 package: `Grpc.AspNetCore` 2.85.0, `Grpc.Tools` 2.85.0 (PrivateAssets=All), `Grpc.Net.Client` 2.85.0. (2) Thêm `<Protobuf Include="Protos\*.proto" GrpcServices="Server" />`. (3) Viết `Protos/pets.proto` (proto3) — `service PetCatalog { rpc GetPets; rpc GetPetById; }`. (4) Viết `Protos/bookings.proto` — `rpc WatchBookings (stream ...)`, `rpc StreamBookings`, `rpc SyncBookings` (bi-directional). (5) `AddGrpc()` + `MapGrpcService<PetCatalogService>()` + `MapGrpcService<BookingWatchService>()`. (6) Implement `PetCatalogService` — inject `ILogger<PetCatalogService>` (slide Ch09 Slide 31) + `ShopPetDatabaseContext` (`AsNoTracking`). (7) Implement `BookingWatchService` — server-streaming dùng `IAsyncStreamWriter`. (8) Tạo `Controllers/GrpcDemoController.cs` — `GrpcChannel.ForAddress("http://localhost:5286")` gọi `GetPetsAsync` + `WatchBookingsAsync` → trả JSON cho browser xem. (9) Tạo trang demo `Components/Pages/GrpcDemo.razor` (route `/grpc-demo`). |
| **Done khi** | ☐ `dotnet build` sinh file `PetCatalogGrpc.cs` từ `.proto` (kiểm tra `obj/Debug/net8.0/`) ☐ `GrpcChannel.ForAddress` + `GetPetsAsync` → nhận `PetList` ☐ `GetPetById` với id không tồn tại → `NotFound` ☐ `WatchBookingsAsync` nhận ≥ 1 message rồi stream kết thúc ☐ `StreamBookingsAsync` (client streaming) nhận nhiều message ☐ `SyncBookingsAsync` (bi-directional) hoạt động ☐ `/grpc-demo` hiển thị dữ liệu ☐ `ILogger` ghi log trong service (không `Console.WriteLine`) ☐ Build 0 warning ☐ 30 test pass |

---

## LS-10 — JavaScript client demo (jQuery AJAX + XHR) trên MVC view

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/frontend/src/pages/*` (React) → **KHÔNG** port React; theo slide Ch07 dùng jQuery thuần → **MỚI** `Controllers/ApiDemoController.cs`, `Views/ApiDemo/Index.cshtml`, `wwwroot/lib/jquery/jquery.min.js`, `wwwroot/js/api-demo.js` |
| **API endpoint** | `GET /api/products`, `GET /api/products/{id}`, `POST /api/products` (**mới**), `PUT /api/products/{id}` (**mới**), `DELETE /api/products/{id}` (**mới**), `GET /api/products/categories` |
| **Asset** | `wwwroot/lib/jquery/jquery.min.js` (copy từ CDN hoặc jQuery.com), `wwwroot/js/api-demo.js` |
| **Rủi ro** | (a) **`ProductsController` hiện CHỈ có GET** — phải thêm POST/PUT/DELETE (có thể ảnh hưởng dữ liệu thật → nên dùng bảng riêng hoặc cờ `IsDeleted`). (b) **`[Authorize]` trên write endpoint sẽ phá demo** → dùng session header hoặc không gắn. (c) jQuery CDN → mất mạng = demo fail → **copy local**. (d) Slide demo cả `XMLHttpRequest` (4 slide) → làm cả 2 để khớp 100%. (e) CORS nếu test từ file khác origin. (f) `dotnet-svcutil` không liên quan. |
| **Việc làm** | (1) Tạo `Views/` + `Views/Shared/_Layout.cshtml` tối giản. (2) `Controllers/ApiDemoController.cs` — MVC controller, action `Index()` trả `View()`. (3) `Views/ApiDemo/Index.cshtml` — HTML table + 4 nút (Load all / Load by ID / Create / Update) + 2 tab (jQuery AJAX / XMLHttpRequest). (4) `wwwroot/js/api-demo.js` — implement `$.ajax()` + `XMLHttpRequest` cho 4 thao tác. (5) Copy `jquery.min.js` vào `wwwroot/lib/jquery/`. (6) Thêm CRUD write cho `ProductsController`. (7) `MapControllerRoute` cần map `ApiDemo` → hoặc dùng `[Route]` attribute trên controller. (8) Copy `jquery.validate.min.js` + `jquery-validation-unobtrusive.min.js` cho client-side validation (Ch05 Slide 30). |
| **Done khi** | ☐ `GET /api-demo` trả 200 + render table ☐ Bấm "Load" → bảng có dữ liệu sản phẩm thật ☐ Bấm "Load by ID" → 1 dòng ☐ Bấm "Create" → `POST` 201 → bảng cập nhật ☐ Bấm "Update" → `PUT` 200 ☐ Bấm "Delete" → `DELETE` 204 ☐ Tab "XMLHttpRequest" hoạt động y hệt tab jQuery ☐ Form có client-side validation (jquery.validate) ☐ Trang **không** cần mạng ngoài (mọi asset local) ☐ Build 0 warning ☐ 30 test pass |

---

## LS-11 — Realtime: notification pull + SignalR (khuyến nghị)

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/frontend/src/components/staff/NotificationBell.jsx` → **MỚI** `Components/Shared/NotificationBell.razor` (hoặc đưa vào `TopBar.razor`)<br>`Projectojt/BackEnd/Controllers/NotificationController.cs` → `Controllers/NotificationController.cs` ✅ đã có |
| **API endpoint** | Đã có: `GET /api/notifications/{staffId}`, `GET /api/notifications/{staffId}/unread-count`, `PUT /api/notifications/{requestId}/read`, `PUT /api/notifications/{staffId}/read-all`, `DELETE /api/notifications/{requestId}`<br>**MỚI (khuyến nghị)**: SignalR hub `/hubs/notifications` |
| **Asset** | `wwwroot/js/notifications.js` (nếu dùng SignalR client JS) |
| **Rủi ro** | (a) **`NotificationController` dùng bảng `ShiftRequest`, không dùng bảng `Notifications`** → không có bảng notification thật. (b) **`DELETE /api/notifications/{id}` là stub** — luôn trả 200 "Không hỗ trợ xóa" mà không xoá → client hiểu nhầm. (c) **Pull-based chỉ** — không có push → độ trễ. (d) SignalR + Blazor Server: phải cẩn thận với circuit và DI scope (hub là singleton, service phải scoped → dùng `IServiceScopeFactory`). (e) `ChatbotService` lịch sử do client gửi → nếu thêm chat UI nên lưu vào `ChatMessages`. |
| **Việc làm** | (1) Tạo `Components/Shared/NotificationBell.razor` — dùng `INotificationClient` (mới) để poll badge số chưa đọc. (2) **Khuyến nghị**: thêm SignalR `NotificationHub` + `NotificationService` để push khi có `ShiftRequest` mới. (3) Sửa `DELETE /api/notifications/{requestId}` → thật sự xoá (hoặc trả 405 Method Not Allowed thay vì 200 giả). (4) Ghi lịch sử chatbot vào bảng `ChatMessages` (dùng `ChatMessage` entity đã có sẵn). |
| **Done khi** | ☐ `NotificationBell.razor` hiển thị badge số chưa đọc ☐ Bấm chuông → panel danh sách yêu cầu đổi ca ☐ Đánh dấu đã đọc → badge giảm ngay ☐ (nếu làm SignalR) Yêu cầu đổi ca mới → nhận push không cần reload ☐ `DELETE /api/notifications/{id}` không còn trả 200 giả ☐ Lịch sử chatbot lưu vào bảng `ChatMessages` và đọc lại được ☐ Build 0 warning ☐ 30 test pass |

---

## LS-12 — Đóng gói: Docker + dọn gap UI + route `/about`

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/frontend/public/logo-pet-toy.png` → `wwwroot/images/logo-pet-toy.png` (hoặc `wwwroot/`)<br>`Projectojt/frontend/public/ziggy-hero-reference.png` → `wwwroot/images/ziggy-hero-reference.png`<br>`Projectojt/frontend/src/pages/AboutPage.jsx` → **MỚI** `Components/Pages/About.razor` (route `/about`)<br>`Projectojt/frontend/src/components/checkout/daNangData.js` → **MỚI** `Models/daNangData.cs` hoặc `wwwroot/js/daNangData.js`<br>`Projectojt/frontend/src/components/staff/NotificationBell.jsx` → LS-11 |
| **API endpoint** | *(không có — trang tĩnh)* |
| **Asset** | `wwwroot/images/logo-pet-toy.png`, `wwwroot/images/ziggy-hero-reference.png`, `Dockerfile`, `.dockerignore`, `docker-compose.yml` |
| **Rủi ro** | (a) **`docker-compose.yml` cần SQL Server** → compose phải có service `sqlserver` (mcr.microsoft.com/mssql/server:2022-latest) + `SHOP_PET_Database` import script. (b) `docker-compose` không dùng được `sa/1234` nếu máy chạy Windows Auth → dùng `Integrated Security` không chạy trong Linux container → **bắt buộc** dùng SQL auth. (c) `/about` cần viết mới hoàn toàn (nội dung tĩnh từ React). (d) `default-service-placeholder.png` **thiếu** trong nguồn (`SOURCE_FRONTEND` §5.3) → phải tạo hoặc dùng `placeholder.jpg`. (e) Port mapping 5286→80 trùng khi app chạy local. |
| **Việc làm** | (1) Tạo `Dockerfile` multi-stage theo slide Ch10 Slide 28 — `sdk:8.0` build → `aspnet:8.0` runtime. (2) Tạo `.dockerignore`. (3) Tạo `docker-compose.yml` với service `petshop` + `sqlserver`. (4) `docker build -t petshop .` + `docker run -it --rm -p 5286:80`. (5) Tạo `Components/Pages/About.razor` route `/about`. (6) Copy `daNangData.js` → `wwwroot/js/daNangData.js` + wire vào `Checkout.razor` nếu cần. (7) Tạo `default-service-placeholder.png` từ `placeholder.jpg` hoặc thiết kế mới. (8) Dọn `Components/Pages/ProductDeleted.razor` nếu còn. |
| **Done khi** | ☐ `docker build -t petshop .` thành công ☐ `docker run` → `http://localhost:5286` trả 200 ☐ `docker-compose up` → app + SQL Server kết nối được ☐ `/about` trả 200 với nội dung đầy đủ ☐ `daNangData` hoạt động trong checkout ☐ `default-service-placeholder.png` tồn tại ☐ 0 lỗi 404 asset (kiểm tra tất cả `<img src>` trong wwwroot) ☐ Build 0 warning ☐ 30 test pass |

---

## LS-13 — Robustness: input formatter + fix rủi ro nghiệp vụ đã phát hiện

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | `Projectojt/BackEnd/Controllers/PetsController.cs` (`basePrice` override) → `PRN232_PetCareProject/Controllers/PetsController.cs` (bỏ override)<br>`Projectojt/BackEnd/Controllers/CheckoutController_temp.cs` (giá do client gửi) → `Controllers/CheckoutController.cs` (tính giá server-side)<br>`Projectojt/BackEnd/Services/SpaBookingService.cs` (string status tự do) → `Services/SpaBookingService.cs` (enum)<br>`Projectojt/BackEnd/Services/AuthService.cs` (OTP dùng `System.Random`) → `Services/AuthService.cs` (`RandomNumberGenerator`)<br>`Projectojt/BackEnd/Services/EmailService.cs` (OTP 5 phút vs email 10 phút) → `Services/EmailService.cs` |
| **API endpoint** | `GET /api/pets/customer/{cid}/service/{sid}/pricing`, `POST /api/checkout`, `PATCH /api/spa-booking/{id}/status`, `POST /api/auth/send-email-otp` |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) Đổi status string → enum sẽ **phá** các client gửi `"Chờ thanh toán PayOS"`, `"Đã thanh toán"` → cần map tương thích ngược. (b) Bỏ `basePrice` override có thể phá test cũ. (c) Tính giá server-side có thể lệch với UI hiện tại đang tính giá ở client. (d) Đổi hành vi OTP là thay đổi luồng xác thực — cần thông báo user. (e) Thêm rate-limit OTP = thêm package (`AspNetCoreRateLimit`) → thêm dependency. |
| **Việc làm** | (1) Bỏ `basePrice` query override → luôn lấy giá từ DB. (2) `POST /api/checkout` tính `TotalAmount` từ `Products.Price` × `Quantity` (bỏ trust `UnitPrice` client). (3) `SpaBookingStatus` enum + map string ↔ enum tương thích ngược (đã có file `Models/SpaBookingStatus.cs` — kiểm tra xem đã dùng chưa). (4) `AuthService.GenerateOtp` → `RandomNumberGenerator.GetInt32(100000, 1000000)`. (5) Đồng bộ TTL OTP giữa code và email (chọn 10 phút cả 2). (6) Kiểm tra `IsPasswordValid` — đã migrate-on-login ✅. (7) Kiểm tra `UploadImage` — thêm kiểm magic bytes / extension thật. (8) Bỏ `Console.WriteLine` còn sót trong `.razor` (AGENTS.md §3.3 M3). |
| **Done khi** | ☐ `?basePrice=` không còn override giá ☐ `POST /api/checkout` với `UnitPrice` giả → tổng tiền vẫn đúng theo DB ☐ `PATCH /api/spa-booking/{id}/status` nhận cả string cũ lẫn enum mới ☐ OTP dùng CSPRNG ☐ TTL OTP khớp giữa code và email ☐ Upload ảnh từ chối file không phải ảnh (kiểm magic bytes) ☐ 0 `Console.WriteLine` còn trong `Components/` ☐ 30 test pass ☐ Build 0 warning |

---

## LS-14 — Hoàn thiện: tài liệu + báo cáo + đóng gói bài nộp

| Hạng mục | Nội dung |
|---|---|
| **File nguồn → đích** | *(không có)* → `docs/ARCHITECTURE_REQUIREMENTS.md` ✅ (file này), `docs/_inventory/ARCHITECT_DESIGN.md` ✅ (file này), `docs/_inventory/postman-jwt-collection.json`, `docs/_inventory/build-baseline.txt`, `README.md` (cập nhật) |
| **API endpoint** | *(không có)* |
| **Asset** | *(không có)* |
| **Rủi ro** | (a) Tài liệu lệch code nếu không cập nhật đồng bộ. (b) Báo cáo lab/assignment theo yêu cầu môn (Chapter_00 Slide 8-10). (c) Nếu có user yêu cầu `git commit` → phải được yêu cầu rõ (AGENTS.md §2.5). |
| **Việc làm** | (1) Cập nhật `README.md`: kiến trúc, cách chạy, danh sách endpoint, cách demo từng chương slide. (2) Bổ sung `docs/ARCHITECTURE_REQUIREMENTS.md` (bản user duyệt). (3) Viết checklist mapping slide → hiện thực. (4) Ghi lại screenshot Postman cho JWT/OData. (5) Nếu cần: `git add` + `commit` (chỉ khi được yêu cầu). |
| **Done khi** | ☐ `README.md` mô tả đúng trạng thái hiện tại ☐ `docs/ARCHITECTURE_REQUIREMENTS.md` được user duyệt ☐ Checklist mapping slide → code đầy đủ ☐ Screenshot Postman đính kèm ☐ Build 0 error 0 warning ☐ Test 30/30 (hoặc nhiều hơn nếu đã thêm) ☐ `dotnet run` khởi động OK, DI resolve OK |

---

## D.1 Bảng tổng hợp thứ tự lát cắt

| # | Lát cắt | Nhóm | Slide chính | Ước lượng | Rủi ro |
|---|---|---|---|---|---|
| LS-01 | Baseline build + test | Nền tảng | — | 0.5h | 🟢 Thấp |
| LS-02 | OData mở rộng + test | Nền tảng | Ch04 | 2-3h | 🟡 TB |
| LS-03 | Content Negotiation + input formatter | Nền tảng | Ch03 | 2h | 🟢 Thấp |
| LS-04 | Validation + ProblemDetails | Nền tảng | Ch05 | 2-3h | 🟢 Thấp |
| LS-05 | Routing + Swagger metadata | Nền tảng | Ch05, Ch01 | 1-2h | 🟢 Thấp |
| LS-06 | JWT demo hoàn chỉnh | Auth | Ch06 | 2-3h | 🟡 TB |
| LS-07 | PayOS webhook idempotent | Thanh toán | — (nghiệp vụ) | 3-4h | 🔴 Cao |
| LS-08 | Chatbot AI | Chatbot/realtime | Ch00 (AI) | 6-8h | 🔴 Cao |
| LS-09 | gRPC service | Realtime | Ch09 | 3-4h | 🟡 TB |
| LS-10 | JS client demo | Phần còn lại | Ch07 | 3-4h | 🟡 TB |
| LS-11 | Notification + chat UI | Chatbot/realtime | — | 3-4h | 🟡 TB |
| LS-12 | Docker + dọn gap UI | Phần còn lại | Ch10 | 2-3h | 🟢 Thấp |
| LS-13 | Robustness (giá, status, OTP) | Phần còn lại | — | 3-4h | 🟡 TB |
| LS-14 | Tài liệu + báo cáo | Phần còn lại | — | 2h | 🟢 Thấp |

**Tổng ước lượng: ~34–46 giờ.**

---

# PHẦN E — CHIẾN LƯỢC ASSET

## E.1 Yêu cầu của user (nguyên văn)

> "Copy nguyên vẹn asset vào wwwroot, giữ đúng đường dẫn tham chiếu"

## E.2 Nguyên tắc

1. **Copy nguyên vẹn** — không nén lại, không đổi tên, không resize ảnh.
2. **Vào `wwwroot/`** — ASP.NET Core `StaticFileMiddleware` phục vụ trực tiếp từ `wwwroot/`.
3. **Giữ đúng đường dẫn tham chiếu** — mọi URL trong DB (`Product.ImagePath`, `Pet.ImagePath`, `PetService.ImageUrl`) và mọi `src` trong Razor phải trỏ đúng file đã copy.
4. **Không sửa DB** — nếu DB đang lưu `/images/toy_1.jpg` thì file phải nằm ở `wwwroot/images/toy_1.jpg` (AGENTS.md §2.5 cấm đổi schema/data).

## E.3 Bảng mapping nguồn → đích

### (a) Ảnh — `frontend/src/images/` → `wwwroot/images/`

| Nhóm nguồn | Số lượng | Đích | Đã có ở đích? |
|---|---|---|---|
| Dịch vụ tắm (`bath1.png`, `bath2.jpg`, `bath3.jpg`) | 3 | `wwwroot/images/` | ✅ có |
| Dịch vụ cắt tỉa (`cut1.jpg`…`cut3.jpg`) | 3 | `wwwroot/images/` | ✅ có |
| Dịch vụ spa cao cấp (`spa-caocap1.jpg`…`spa-caocap3.jpg`) | 3 | `wwwroot/images/` | ✅ có |
| Dịch vụ răng miệng (`teeth1.jpg`…`teeth3.jpg`) | 3 | `wwwroot/images/` | ✅ có |
| Placeholder (`placeholder.jpg`) | 1 | `wwwroot/images/placeholder.jpg` | ✅ có |
| Phòng mèo (`room-cat-normal1..3`, `room-cat-vip1..3`) | 6 | `wwwroot/images/` | ✅ có |
| Phòng chó (`room-dog-large1..3`, `room-dog-small1..3`) | 6 | `wwwroot/images/` | ✅ có |
| Slider (`slide_1.jpg`…`slide_4.jpg`) | 4 | `wwwroot/images/` | ✅ có |
| Tin tức pet (`petnews1..6.jpg`) | 6 | `wwwroot/images/` | ✅ có |
| Đánh giá (`meovat1..6.jpg`) | 6 | `wwwroot/images/` | ✅ có |
| Thành viên (`member1.jpg`, `member3.jpg`) | 2 | `wwwroot/images/` | ✅ có |
| Đồ chơi (`toy_1..35.jpg`, `toy_156..216.jpg`) | 96 | `wwwroot/images/` | ✅ có (đủ 96) |
| Khác (`gt.jpg`, `gt.png`, `nember_9.png`) | 3 | `wwwroot/images/` | ✅ có |
| Video (`video.mp4`, `video.MOV`) | 2 | `wwwroot/images/` | ✅ có |
| **Tổng `src/images/`** | **144** | **`wwwroot/images/`** | ✅ **144/144 đã copy** |

### (b) Ảnh — `frontend/public/` → `wwwroot/`

| Nguồn | Đích đề xuất | Lý do | Trạng thái |
|---|---|---|---|
| `public/logo-pet-toy.png` | `wwwroot/images/logo-pet-toy.png` | Nguồn React dùng `/logo-pet-toy.png` (root, không phải `/images/`). Đích quy ước 1 thư mục `images/` → đặt vào `images/` và **cập nhật `src` trong Razor**. | ⚠️ **Chưa có ở đích** → cần copy (LS-12) |
| `public/ziggy-hero-reference.png` | `wwwroot/images/ziggy-hero-reference.png` | Tương tự. | ⚠️ **Chưa có** → LS-12 |
| `public/default-service-placeholder.png` | `wwwroot/images/default-service-placeholder.png` | **KHÔNG TỒN TẠI trong nguồn** (SOURCE_FRONTEND §5.3) → phải tạo mới hoặc trỏ về `placeholder.jpg`. | 🔴 **Thiếu** → LS-12 |

### (c) CSS

| Nguồn `frontend/src/styles/` | Đích `wwwroot/css/template/` | Dòng | Ghi chú |
|---|---|---|---|
| `home.css` | `home.css` | 2788 | ⚠️ > 500 dòng → nên tách thành `home-layout.css` / `home-products.css` / `home-footer.css` |
| `service.css` | `spa.css` + `css/spa/service-react.css` | 3681 | ⚠️ > 500 dòng → đã tách 1 phần, nên tách tiếp |
| `systemPage.css` | (đã chia vào nhiều file `css/template/`) | 1426 | ✅ |
| `profile.css` | `profile.css` | 917 | ⚠️ > 500 dòng → tách `profile.css` + `profile-pets.css` |
| `checkout.css` | `checkout.css` | 864 | ⚠️ > 500 dòng → tách `checkout.css` + `checkout-address.css` |
| `productDetails.css` | `productDetails.css` | 678 | ⚠️ > 500 dòng → tách `productDetails.css` + `productReviews.css` |
| `cart.css` | `cart.css` | 611 | ⚠️ > 500 dòng → tách `cart.css` + `cartSummary.css` |
| `ordersStaff.css` | `ordersStaff.css` | 312 | ✅ (đã có) |
| `codConfirmation.css` | `codConfirmation.css` | 277 | ✅ |
| `cartDrawer.css` | `cartDrawer.css` | 273 | ✅ |
| `searchProducts.css` | `searchProducts.css` | 267 | ✅ |
| `auth.css` | `auth.css` | 186 | ✅ |
| `createProduct.css` | `createProduct.css` | 158 | ⚠️ đích chưa có file này — kiểm tra |
| `pageContextBar.css` | `pageContextBar.css` | 113 | ✅ |
| `src/index.css` | `css/template/index.css` | 66 | ✅ đã bỏ nền tối Vite |
| `src/App.css` | `css/template/App.css` | — | ✅ |

**Quy ước đặt tên CSS đích:** `wwwroot/css/template/<tên>.css` cho style dùng chung toàn trang; `wwwroot/css/spa/<tên>.css` cho style riêng luồng spa. Nguồn có `service.css` (3681) → đích tách thành `css/template/spa.css` + `css/spa/service-react.css`.

### (d) JS

| Nguồn | Đích | Ghi chú |
|---|---|---|
| `frontend/src/utils/userProfileStore.js` | `Services/UserSessionService.cs` | ✅ đã chuyển sang C# |
| `frontend/src/api/*.js` (10 file) | `Services/*Client.cs` (10 typed client) | ✅ đã chuyển sang C# |
| `frontend/src/api/chatbotApi.js` | `Services/ChatbotClient.cs` (LS-08) | ⚠️ chưa có |
| *(không có)* | `wwwroot/js/blazorAuth.js` | ✅ có sẵn ở đích |
| *(không có)* | `wwwroot/js/cartStorage.js` | ✅ có sẵn |
| *(không có)* | `wwwroot/js/spaCart.js` | ✅ có sẵn |
| *(không có)* | `wwwroot/js/api-demo.js` | 🆕 LS-10 (Ch07 demo) |
| *(không có)* | `wwwroot/js/daNangData.js` | 🆕 LS-12 |
| *(không có)* | `wwwroot/lib/jquery/jquery.min.js` | 🆕 LS-10 (Ch07) |
| *(không có)* | `wwwroot/lib/jquery/jquery.validate.min.js` | 🆕 LS-10 (Ch05 Slide 30) |
| *(không có)* | `wwwroot/lib/jquery/jquery.validate.unobtrusive.min.js` | 🆕 LS-10 |

### (e) Font

| Nguồn | Đích | Ghi chú |
|---|---|---|
| *(không có file font trong `src/`)* | *(không có)* | **Slide không đề cập** — nguồn dùng font hệ thống / Google Fonts qua CSS `@import`? Cần kiểm tra `home.css` xem có `@import url(...)` không. Nếu có → nên vendored font vào `wwwroot/fonts/` để demo offline. |

> ⚠️ **Cần verify**: grep `@import` và `fonts.googleapis` trong `frontend/src/styles/*.css` để xác nhận có phụ thuộc font ngoài không.

## E.4 Cơ chế xử lý khác biệt React import ↔ Blazor

Đây là điểm dễ sai nhất khi migrate asset.

### E.4.1 Nguyên tắc

| Môi trường | Cú pháp | Đường dẫn | Điểm cần chú ý |
|---|---|---|---|
| **React + Vite (nguồn)** | `import heroImg from '../../images/slide_1.jpg'` | **Tương đối tới file source** | Vite build → hash file → `/assets/slide_1-<hash>.jpg`. Ở dev mode (`vite dev`) URL là `/src/images/slide_1.jpg`. **URL thay đổi theo build** → không thể hard-code. |
| **Blazor Server (đích)** | `<img src="/images/slide_1.jpg" />` | **Tuyệt đối từ root** | `StaticFileMiddleware` phục vụ `wwwroot/` tại `/`. URL **cố định** → DB lưu được, CDN được, cache được. |

### E.4.2 Quy tắc quy đổi (mapping 1:1)

```
React:  import x from '../../images/toy_1.jpg'
   ↓
Blazor: <img src="/images/toy_1.jpg" />

React:  import x from '../images/slide_1.jpg'        (1 cấp)
   ↓
Blazor: <img src="/images/slide_1.jpg" />            (KHÔNG giữ dấu ..)

React:  import x from '/logo-pet-toy.png'            (từ public/)
   ↓
Blazor: <img src="/images/logo-pet-toy.png" />        (chuyển vào images/ cho nhất quán)
   hoặc <img src="/logo-pet-toy.png" />               (nếu copy thẳng vào wwwroot/)
```

**Quy tắc vàng:**
- ❌ **KHÔNG** giữ `../../` trong URL Blazor — browser sẽ resolve theo URL hiện tại (vd trang `/products` → `../../images/x.jpg` → `/images/x.jpg` ngẫu nhiên, trang `/account/orders/5` → hỏng).
- ❌ **KHÔNG** dùng đường dẫn tương đối (`images/x.jpg`) — phụ thuộc URL trang.
- ✅ **LUÔN** dùng đường dẫn tuyệt đối bắt đầu bằng `/`.

### E.4.3 Ba nguồn đường dẫn ảnh trong hệ thống đích

| Nguồn đường dẫn | Ví dụ | Nơi lưu | Cách xử lý |
|---|---|---|---|
| **(1) URL tuyệt đối tĩnh** | `/images/toy_1.jpg` | Hard-code trong `.razor` | ✅ Đã làm ở đích. Giữ nguyên. |
| **(2) URL trong DB** | `Product.ImagePath = "/images/toy_1.jpg"` | Cột DB | ✅ Đã đúng. **Không sửa DB** (AGENTS.md §2.5). |
| **(3) URL tương đối từ upload** | `Pet.ImagePath = "/uploads/pets/pet_<guid>.jpg"` | Cột DB | ✅ `POST /api/pets/upload-image` lưu vào `wwwroot/uploads/pets/`. Giữ nguyên. |

### E.4.4 Quy trình copy asset (thực thi)

```
Nguồn:  Projectojt/frontend/src/images/*.jpg
                    ↓  copy nguyên vẹn (không nén, không đổi tên)
Đích:   PRN232_PetCareProject/wwwroot/images/*.jpg
                    ↓  URL tuyệt đối
Dùng:   <img src="/images/toy_1.jpg" />     (Razor)
        <img src="/images/toy_1.jpg" />     (DB Product.ImagePath)
```

**Checklist sau khi copy:**
- ☐ `Get-ChildItem wwwroot/images | Measure-Object` = số file nguồn
- ☐ Grep toàn bộ `.razor`/`.cs`/`.cshtml` cho `../../images` → **0 kết quả**
- ☐ Grep toàn bộ cho `src="images/` (thiếu dấu `/`) → **0 kết quả**
- ☐ Verify từng `ImagePath` trong DB có file tương ứng trong `wwwroot/images/`
- ☐ Mở mỗi trang (34 page) → không có ảnh vỡ (icon broken)

### E.4.5 Cảnh báo đặc biệt

| # | Cảnh báo | Nguồn |
|---|---|---|
| 1 | **`default-service-placeholder.png` không tồn tại trong nguồn** nhưng `ServicePage.jsx` tham chiếu `/default-service-placeholder.png` | SOURCE_FRONTEND §5.3 |
| 2 | **`logo-pet-toy.png` + `ziggy-hero-reference.png`** ở `public/` — đích chưa có trong `wwwroot/images/` | SOURCE_FRONTEND §5.1 |
| 3 | **`src/index.css` còn nền tối Vite `#242424`** — đích đã loại bỏ | SOURCE_FRONTEND §7.3 |
| 4 | Nhiều ảnh từ **Unsplash / Freepik CDN** (không nằm trong repo) → nếu demo offline sẽ hỏng. Cân nhắc copy về `wwwroot/images/` hoặc chấp nhận phụ thuộc mạng. | SOURCE_FRONTEND §5.3 |
| 5 | `wwwroot/uploads/pets/` là thư mục **runtime** — phải có trong `.gitignore` / `.dockerignore`, không copy từ nguồn. | SOURCE_COMPLEX_FLOWS §5 |

---

# PHẦN F — MA TRẬN QUYẾT ĐỊNH KHÓ (CẦN USER DUYỆT)

> ≤ 12 câu hỏi, mỗi câu kèm 2-3 lựa chọn + khuyến nghị.

---

### ❓ Q1 — `Microsoft.AspNetCore.OData`: giữ 9.5.0 hay hạ về 9.2.1 theo slide?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Giữ 9.5.0** (đang chạy, P3 xong) | ✅ Khớp API `AddRouteComponents`; không rủi ro regression. **Khuyến nghị.** |
| B. Hạ về 9.2.1 (đúng slide) | ⚠️ Rủi ro regression OData đang chạy tốt; lợi ích = "khớp slide 100%". |
| C. Nâng EF Core 8.0.8 → 8.0.15 (đúng slide Ch04 Slide 32) | ⚠️ Cần restore mạng; đồng bộ cả 3 package EF. Lợi ích học thuật rất nhỏ. |

**Khuyến nghị: A.** Ghi rõ trong báo cáo: "Slide dẫn 9.2.1 / EF 8.0.15; dự án dùng 9.5.0 / EF 8.0.8 — cùng API, bản vá mới hơn."

---

### ❓ Q2 — Refresh token: có làm theo slide Ch06 không?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Không làm** (giữ hiện trạng access token 120 phút) | ✅ Đơn giản, bài chấm là local. PRN232_PLAN đã chốt ❌. |
| B. Làm `POST /api/auth/refresh-token` (access + refresh, lưu DB) | ⚠️ Khớp slide 100%; thêm bảng + logic thu hồi. |

**Khuyến nghị: B nhưng tối giản** — access token 60 phút + refresh token 7 ngày lưu bảng `RefreshTokens`, chỉ 1 endpoint. Nếu user muốn "giống 100% bản gốc" (slide nêu rõ ở Slide 40 + 42) → chọn B. Nếu ưu tiên tốc độ → A.

---

### ❓ Q3 — AutoMapper: có dùng không? (slide Ch02 Slide 23, Ch05 Slide 43)

| Lựa chọn | Đánh giá |
|---|---|
| **A. Không dùng** — map tay như hiện tại | ✅ 63 file Model đã map tay, build sạch, 30 test pass. PRN232_PLAN đã chốt ❌. **Khuyến nghị.** |
| B. Dùng AutoMapper + `Profile` class cho ~22 DTO | ⚠️ Thêm package + 22 mapping profile; phải test lại từng DTO. |

**Khuyến nghị: A.** Slide chỉ nói "one of the best libraries" — không phải yêu cầu bắt buộc. Nếu muốn "100% bản gốc" về mặt công nghệ → B.

---

### ❓ Q4 — Chatbot: dùng provider nào, có multi-provider không?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Gemini + Ollama, `IEnumerable<IAiProviderService>` + fallback chain** | ✅ Sửa bug DI của nguồn; hỗ trợ cả local (Ollama, không cần key) lẫn cloud. **Khuyến nghị.** |
| B. Chỉ Gemini (giống nguồn) | ⚠️ Không demo được offline; nguồn có bug `DefaultProvider = "groq"` không tồn tại. |
| C. Chỉ Ollama (local, không cần API key) | ✅ Demo offline 100%. ⚠️ Cần cài Ollama trên máy; mô hình `llama3.2` (~2GB). |

**Khuyến nghị: A** (đăng ký cả 2, `DefaultProviderName` cấu hình được). Nếu trình bày offline không có mạng → **C**.

---

### ❓ Q5 — Chatbot lưu lịch sử hội thoại vào DB hay không?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Có** — ghi vào bảng `ChatMessages` (đã có sẵn entity + DbSet) | ✅ Bảng đã có, chỉ thiếu code ghi. Cho lịch sử bền vững. **Khuyến nghị.** |
| B. Không — giữ như nguồn (client gửi `History`) | ⚠️ Không bền vững; mở tab mới mất lịch sử. |

**Khuyến nghị: A.** Bảng `ChatMessages` đã tồn tại (`Models/ChatMessage.cs` + `DbSet<ChatMessage>`) — chỉ cần code. **Không đổi schema DB.**

---

### ❓ Q6 — Thanh toán: gộp 2 luồng PayOS hay giữ nguyên?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Gộp** — `POST /api/checkout` không tạo link trực tiếp, delegate sang `IPayOSService` | ✅ 1 code path, hết trùng lặp, dễ bảo trì. **Khuyến nghị.** |
| B. Giữ 2 luồng (như nguồn) | ⚠️ Trùng logic tạo `Payment` → dễ lệch trạng thái. |

**Khuyến nghị: A** (kèm transaction + idempotency trong LS-07).

---

### ❓ Q7 — Thêm CRUD write cho `ProductsController` (cần cho demo Ch07) hay không?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Thêm POST/PUT/DELETE** (khớp slide Ch07 Slide 31-36) | ✅ Bắt buộc để demo JS client đầy đủ. ⚠️ Ghi vào DB thật → nên có cờ `IsDeleted` + dùng bảng riêng cho demo. **Khuyến nghị.** |
| B. Không thêm — demo Ch07 chỉ GET (lệch slide 4 slide demo) | ⚠️ Không đạt "giống 100% bản gốc" về mặt demo. |
| C. Thêm nhưng trỏ sang API demo riêng `/api/demo/products` (InMemory) | ✅ An toàn dữ liệu thật; thêm 1 controller nữa. |

**Khuyến nghị: A** (dùng soft-delete `IsDeleted` đã có sẵn trên `Product`) hoặc **C** nếu user lo ngại dữ liệu thật.

---

### ❓ Q8 — Docker: có cần `docker-compose.yml` với SQL Server không?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Có compose** (app + sqlserver:2022) | ✅ Demo 1 lệnh. ⚠️ SQL trong container dùng SQL auth, không dùng Windows Auth của máy. **Khuyến nghị.** |
| B. Chỉ `Dockerfile` (đúng slide Ch10 Slide 28) | ✅ Đúng slide 100%; nhưng phải trỏ connection string ra SQL Server ngoài container (`host.docker.internal`). |

**Khuyến nghị: Cả hai** — `Dockerfile` (bắt buộc, khớp slide) + `docker-compose.yml` (tiện, PRN232_PLAN P7.3 đã có).

---

### ❓ Q9 — `/about` (trang giới thiệu, React `AboutPage.jsx`) có port sang Blazor không?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Có** — tạo `Components/Pages/About.razor` (route `/about`) | ✅ Đóng góp "giống 100% bản gốc" về mặt tính năng; nội dung tĩnh, dễ làm. **Khuyến nghị.** |
| B. Không — bỏ trang `/about` | ⚠️ Link trong `Navigation`/`Footer` của nguồn dẫn tới `/about` → link chết. |

**Khuyến nghị: A.**

---

### ❓ Q10 — `daNangData.js` (dữ liệu 8 quận + 100+ phường Đà Nẵng) xử lý thế nào?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Copy nguyên file `.js` vào `wwwroot/js/daNangData.js`**, load bằng JSInterop | ✅ Copy nguyên vẹn, giữ đúng hành vi bản gốc; không cần viết lại 100+ phường. **Khuyến nghị.** |
| B. Viết lại thành C# (`Models/daNangData.cs`) | ⚠️ Phải gõ lại ~500 dòng dữ liệu; rủi ro typo. |
| C. Bỏ — checkout chỉ nhập địa chỉ text tự do | ⚠️ Mất tính năng chọn quận/phường. |

**Khuyến nghị: A.**

---

### ❓ Q11 — Trạng thái booking: đổi string tự do → enum hay giữ string?

| Lựa chọn | Đánh giá |
|---|---|
| **A. Giữ string** (khớp bản gốc 100%) | ✅ Đơn giản, không phá client. ⚠️ Rủi ro client tự set `"Đã thanh toán"` (SOURCE_COMPLEX_FLOWS §8.7). |
| B. Đổi enum + map tương thích ngược | ✅ An toàn hơn; file `Models/SpaBookingStatus.cs` đã có sẵn. ⚠️ Phải map cả 2 chiều. |
| C. Giữ string nhưng **cấm client set trạng thái thanh toán** (chỉ webhook được set) | ✅ Giữ string, chặn đúng lỗ hổng. **Khuyến nghị.** |

**Khuyến nghị: C** — ít rủi ro nhất, vẫn giữ 100% hành vi bản gốc.

---

### ❓ Q12 — Namespace gốc của project: đổi hay giữ?

| Lựa cầu | Đánh giá |
|---|---|
| **A. Giữ nguyên namespace hiện tại** | ✅ 0 churn, không rủi ro build. **Khuyến nghị.** |
| B. Đổi thành `PetShop.Models` / `PetShop.Services` / ... (tường minh theo tầng) | ⚠️ Chỉ đổi tên namespace, không đổi folder → sẽ gây lệch giữa cấu trúc thư mục và namespace. |

**Khuyến nghị: A** — trừ khi user muốn namespace khớp 1:1 với tên project hiện tại (`PetShop_PRN222` → `PetShop`).

---

# PHẦN G — CHECKLIST NGHIỆM THU

## G.1 Kiến trúc
- ☐ Solution có đúng 1 project `PetShop_PRN222.csproj` + `tests/PetShop.Tests`
- ☐ 3-layer thể hiện bằng thư mục: `Controllers/` → `Services/` → `Repositories/` + `Data/`
- ☐ Không có `.razor` nào inject `ShopPetDatabaseContext`
- ☐ Dependency direction đúng (Presentation → Business → Data)

## G.2 Slide coverage
- ☐ Ch00 — môi trường .NET 8, SQL Server, JWT, AJAX, gRPC ✅ / WCF ❌ / microservices thật ❌
- ☐ Ch01 — `[ApiController]`, attribute routing, HTTP verbs, status codes, ProblemDetails ✅
- ☐ Ch02 — EF Core code-first + DB-first, DTO, 3-layer ✅ / AutoMapper ❌
- ☐ Ch03 — Content negotiation JSON/XML/CSV, custom formatter, `System.Text.Json` ✅
- ☐ Ch04 — OData EDM + `AddOData` + 8 query option ✅
- ☐ Ch05 — Binding source, DataAnnotations, Repository pattern, routing ✅
- ☐ Ch06 — JWT Bearer dual-mode, `AllowAnonymous`, middleware ✅ / Identity ❌ / autoscaling ❌
- ☐ Ch07 — jQuery AJAX + XHR + MVC demo ✅
- ☐ Ch08 — WCF/CoreWCF ❌ (có lý do ghi rõ)
- ☐ Ch09 — gRPC proto3, 4 loại RPC, DI + Logging ✅
- ☐ Ch10 — Docker ✅ / microservices thật ❌ (có lý do ghi rõ)

## G.3 Asset
- ☐ 144 ảnh `src/images/` → `wwwroot/images/` nguyên vẹn
- ☐ 2 ảnh `public/` → `wwwroot/images/` (LS-12)
- ☐ `default-service-placeholder.png` tồn tại (tạo mới)
- ☐ 0 kết quả grep `../../images` trong `.razor`/`.cs`/`.cshtml`
- ☐ Mọi `ImagePath` trong DB có file tương ứng trong `wwwroot/images/`
- ☐ 34 trang mở lên không có ảnh vỡ

## G.4 Chất lượng
- ☐ `dotnet build` → 0 error, 0 warning
- ☐ `dotnet test` → 30/30 (hoặc nhiều hơn)
- ☐ 0 `catch { }` rác
- ☐ 0 `Console.WriteLine` trong code ứng dụng
- ☐ 0 file > 500 dòng (C#) hoặc component > 25KB (Razor)
- ☐ 0 vi phạm `.editorconfig`

---

*Tài liệu thiết kế kiến trúc — soạn bởi Kiến trúc sư trưởng (Hội đồng khắc nghiệt). Tất cả nội dung đối chiếu trực tiếp với 11 file text trong `_slides_text/` và 4 file trong `docs/_inventory/`. **Không có file nào trong nguồn hay đích bị chỉnh sửa bởi tài liệu này.***
