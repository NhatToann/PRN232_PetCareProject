# PARITY.md — Bảng kiểm kê parity OJT → PRN232

> **Mục đích:** checklist đầy đủ mọi thứ cần chuyển từ dự án OJT (`Projectojt/`) sang PRN232 (`PRN232_PetCareProject/`), theo đúng kiến trúc slide yêu cầu.
> **Ngày lập:** 08/11/2026 · **Trạng thái:** ⏳ CHỜ DUYỆT
> **Nguồn kiểm kê thô:** `docs/_inventory/` — `SOURCE_API_ENDPOINTS.md`, `SOURCE_FRONTEND.md`, `SOURCE_COMPLEX_FLOWS.md`, `TARGET_EXISTING.md`, `GAP_AUDIT.md`, `RED_TEAM_REVIEW.md`, `ARCHITECT_DESIGN.md`

---

## 0. Quy ước

| Ký hiệu | Ý nghĩa |
|---|---|
| `[ ]` | Chưa làm |
| `[~]` | Đang làm |
| `[x]` | Xong — build pass **và** đã tick |
| `[!]` | **Chặn** — cần user quyết định (xem `docs/ARCHITECTURE_REQUIREMENTS.md` §6) |
| `🆕` | Chưa có ở nguồn, đích đã có (mở rộng) |
| `➕` | Cần thêm mới ở đích |

**Quy tắc tick:** chỉ tick `[x]` khi (1) `dotnet build` pass 0 error, (2) `dotnet test` pass, (3) hành vi đã đối chiếu với bản gốc.

**Nguồn → Đích:** `Projectojt/BackEnd/` = backend ASP.NET Core 8; `Projectojt/frontend/` = React 19 + Vite 7 SPA.

---

## 1. TỔNG QUAN — Bức tranh lớn

| Hạng mục | Số lượng | Nguồn | Đích | Ghi chú |
|---|---|---|---|---|
| API endpoint | 92 action-route | 18 controller | 22 controller | ~90/92 đã có; thiếu chatbot (4) + 10 endpoint `*-temp` mà FE gọi |
| Razor page (route) | 23 unique URL (React) | — | 38 route | Đích có nhiều hơn nhưng **thiếu 4 route nguồn** |
| Component | 17 `.jsx` | — | 16 shared + 4 home | Đã quy đổi sang Razor |
| CSS | 14 file / 12.551 dòng | — | 20 file | Cần đối chiếu từng rule |
| Asset ảnh | 144 file | `frontend/src/images/` | `wwwroot/images/` (148) | ✅ **144/144 MD5 khớp 100%** |
| Service | 11 file | — | 34 file | ✅ Đủ (thêm `ChatbotService`, `GeminiProviderService`) — **không port** `OllamaProviderService` (chỉ Gemini) |
| Repository | 5 file | — | 8 file | ✅ 5/5 đầy đủ + `ChatbotRepository` (mới, cho chatbot) |
| Model | 60 file | — | 66 file | ✅ Đủ (`ChatbotDtos`, `ChatbotOptions`, `ChatbotRepositoryDtos`) |
| Validation message (VI) | 325 chuỗi | — | +157 chuỗi chatbot | ✅ **157 chuỗi chatbot đã port nguyên văn, 0 đổi nội dung** (đối chiếu tự động: khớp 100%, chỉ khác 1 chuỗi do đổi Ollama→Gemini có chủ đích) |

### 1.1. Kết luận kiến trúc (sau vòng hội đồng khắc nghiệt)

> **Đích đã là bản Blazor Server hoàn chỉnh của hệ thống OJT.** Migration **KHÔNG phải** viết lại từ đầu — mà là **vá lỗ hổng parity** còn lại + **bổ sung 3 phase slide chưa làm** (gRPC Ch09, JS client Ch07, Docker Ch10).

> **Node.js trong nguồn thực chất chỉ là Vite + React SPA (build tool + UI).** Backend OJT **100% là C#** — chatbot, realtime, upload, middleware, thanh toán **đều đã là C#**. Không có Node server nào cần viết lại. (Xem `RED_TEAM_REVIEW.md` §4.)

### 1.2. 10 khoảng trống chặn runtime (🔴) — phải xử lý trước tiên

| # | Vấn đề | Bằng chứng | Lát cắt |
|---|---|---|---|
| R1 | `wwwroot/EmailTemplates/` **không tồn tại** → `EmailService` ném `FileNotFoundException` khi gửi hoá đơn | `Services/EmailService.cs:50,87,107,147` | LS-01 — ✅ **đã xử lý** (template đặt ở `EmailTemplates/` ContentRoot, khớp bản gốc) |
| R2 | `wwwroot/uploads/pets/` **không tồn tại** → 15 file ảnh pet đã upload thiếu → ảnh trong DB **404** | `GAP_AUDIT.md` §5 | LS-01 — ✅ **đã xử lý** (copy 15 file từ nguồn, MD5 khớp) |
| R3 | `CartController` đích **thiếu action `Index`** + không có `Views/` → `RedirectToAction(nameof(Index))` ⇒ **HTTP 500** | `Controllers/CartController.cs:30,44,81` | LS-03 — ✅ **đã xử lý** |
| R4 | `Program.cs` map `mvc/{controller=ProductsPage}` nhưng **không có `ProductsPageController`** ⇒ route `/mvc/*` 404 | `Program.cs` cuối file | LS-03 — ✅ **đã xử lý** |
| R5 | `Footer.razor` link `/about` và `/contact` nhưng **không có page** ⇒ link chết ở mọi trang | `Components/Shared/Footer.razor` | LS-12 |
| R6 | Google OAuth: `RedirectUri=http://localhost:5173` nhưng app chạy `:5286`; `ClientId` **rỗng** ở cả 2 bên ⇒ **login Google không hoạt động** | `appsettings.json`, `ConfigController.cs` | LS-06 — ✅ **đã xử lý** (user cấp Client ID/Secret thật; `RedirectUri` → `http://localhost:5286` khớp `redirect_uris` trong OAuth client) |
| R7 | 10 Razor component gọi thẳng `ShopPetDatabaseContext` + raw SQL ⇒ **vi phạm slide Ch02 3-layer** | 12 hit trong `Components/` | LS-13 |
| R8 | Toàn bộ stack Chatbot/AI mất (3 service + 1 controller + 2 model + 2 interface) ⇒ **157 message tiếng Việt mất** | `GAP_AUDIT.md` §1.2 | LS-08 — ✅ **đã xử lý** (xem §5.2, §6.9) |
| R9 | 10 endpoint mà `frontend/src/api/*.js` gọi **không tồn tại ở cả nguồn lẫn đích** ⇒ port FE nguyên văn chết ngay | `products-temp/{create,update,delete,deleted,restore,permanent-delete,upload-image,search}`, `pet-services` POST | LS-04 |
| R10 | `PayOSService.CreatePaymentLinkAsync` đích **đã xoá fallback** `https://api.payos.vn/v2` | `GAP_AUDIT.md` §1.2` | LS-07 |

---

## 2. LỚP 1 — CẤU HÌNH & NỀN TẢNG

### 2.1. Cấu hình & biến môi trường

| # | Hạng mục | Nguồn | Đích dự kiến | Slide | Trạng thái |
|---|---|---|---|---|---|
| C-01 | `ConnectionStrings:DefaultConnection` / `DBDefault` | `BackEnd/appsettings.json` | `appsettings.json` + `appsettings.Development.json` | Ch02 S19 | `[x]` — đã khôi phục, đã test connect OK (`sa`/`123`, 32 bảng) |
| C-02 | `GoogleAuth:{ClientId,ClientSecret,RedirectUri}` | `BackEnd/appsettings.json` | `appsettings.json` (giữ nguyên) | Ch06 | `[x]` — ✅ user đã cấp Client ID/Secret thật (`517086557037-...apps.googleusercontent.com`); `RedirectUri` đổi `http://localhost:5173` → `http://localhost:5286` (khớp `redirect_uris` + `javascript_origins` trong file OAuth client) |
| C-03 | `Mail:{SmtpHost,SmtpPort,SecureSocketOptions,Username,Password,From}` | `BackEnd/appsettings.json` | `appsettings.json` (giữ nguyên) | — | `[x]` — đã khôi phục nguyên văn |
| C-04 | `PayOS:{ClientId,ApiKey,ChecksumKey,BaseUrl}` | `BackEnd/appsettings.json` | `appsettings.json` (giữ nguyên) | — | `[x]` — đã khôi phục nguyên văn, **không sửa** |
| C-05 | `Attendance:AllowedIpPrefixes` | `BackEnd/appsettings.json` | `appsettings.json` (giữ nguyên) | — | `[x]` — đã khôi phục nguyên văn |
| C-06 | `JwtSettings:{Issuer,Audience,Key,ExpiresInMinutes}` | — (chỉ có ở đích) | `appsettings.Development.json` | Ch06 S40 | `[x]` — đã có sẵn, giữ nguyên |
| C-07 | `Chatbot:{DefaultModel,Temperature,MaxTokens,Providers{gemini}}` | `BackEnd/appsettings.json` | `appsettings.json` (khối `Chatbot`) | LS-08 | `[x]` — khối `Chatbot` đã có sẵn ở đích, **không sửa**. Chỉ dùng provider `gemini` (bỏ `ollama` theo quyết định chủ dự án). ⚠️ Key Gemini đã bị Google báo **leaked (403 PERMISSION_DENIED)** ⇒ **cần user rotate key** (xem Q13) |
| C-08 | `Logging:LogLevel` | cả hai | `appsettings.json` | — | `[x]` |
| C-09 | `AllowedHosts` | `BackEnd/appsettings.json` | `appsettings.json` | — | `[x]` |
| C-10 | `appsettings.example.json` (template, không secret) | — | đã có sẵn | LS-08 | `[x]` — đã bổ sung khối `Chatbot` (kèm `DefaultProviderName`, `EnableDatabaseQuery`, `SystemPromptPath`, `MaxTokens`/`Temperature`/`Enabled` cho provider) |
| C-11 | `.gitignore` (appsettings.*.json không track) | — | đã có sẵn | — | `[x]` |

### 2.2. Middleware & pipeline

| # | Hạng mục | Nguồn | Đích dự kiến | Slide | Trạng thái |
|---|---|---|---|---|---|
| M-01 | `UseSwagger` / `UseSwaggerUI` (Development) | `BackEnd/Program.cs` | `Program.cs` | Ch01 S27 | `[x]` |
| M-02 | Middleware đọc `X-Forwarded-For` → `Items["RealClientIp"]` | `BackEnd/Program.cs` | `Program.cs` | — | `[x]` — đã có, giữ nguyên |
| M-03 | `UseCors("FrontendDev")` — origin `http://localhost:5173` | `BackEnd/Program.cs` | `Program.cs` | — | `[x]` — giữ nguyên (không còn FE React nhưng policy vẫn hợp lệ) |
| M-04 | `UseSession` + `AddDistributedMemoryCache` (IdleTimeout 8h) | `BackEnd/Program.cs` | `Program.cs` | — | `[x]` |
| M-05 | `UseStaticFiles` | `BackEnd/Program.cs` | `Program.cs` | — | `[x]` |
| M-06 | `ApiExceptionMiddleware` → `application/problem+json` | — (chỉ có ở đích) | `Services/ApiExceptionMiddleware.cs` | Ch01 S37 | `[x]` |
| M-07 | `UseAuthentication` + `UseAuthorization` | — (nguồn **thiếu**) | `Program.cs` | Ch06 S25–29 | `[x]` — đã có, dual-mode |
| M-08 | `UseAntiforgery` | — | `Program.cs` | — | `[x]` |
| M-09 | `UseHttpsRedirection` | `BackEnd/Program.cs` | `Program.cs` | — | `[x]` |
| M-10 | `UseODataRouteDebug` (Development) | — | `Program.cs` | Ch04 S34 | `[x]` |

### 2.3. DI registrations

| # | Hạng mục | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|
| D-01 | `IProductRepository` → `ProductRepository` | `Repositories/ProductRepository_temp.cs` | `Repositories/ProductRepository.cs` | `[x]` — đã đổi tên bỏ `_temp` |
| D-02 | `IProductService` → `ProductService` | `Services/ProductService_temp.cs` | `Services/ProductService.cs` | `[x]` |
| D-03 | `IAuthRepository` / `IAuthService` | ✅ | ✅ | `[x]` |
| D-04 | `IEmailService` → `EmailService` | ✅ | ✅ | `[x]` |
| D-05 | `IPetServiceRepository` / `IPetServiceService` | ✅ | ✅ | `[x]` |
| D-06 | `ISpaBookingRepository` / `ISpaBookingService` | ✅ | ✅ | `[x]` |
| D-07 | `IReviewRepository` | ✅ | ✅ | `[x]` |
| D-08 | `IAttendanceService` / `IPayrollService` | ✅ | ✅ | `[x]` |
| D-09 | `IPayOSService` → `PayOSService` (HttpClient + `x-client-id`/`x-api-key`) | ✅ | ✅ | `[x]` — nhưng thiếu fallback URL → xem R10 |
| D-10 | `IChatbotService` → `ChatbotService` | ✅ | ✅ | `[x]` LS-08 — port đủ 4 method, 157 message tiếng Việt giữ nguyên văn. Truy vấn DB đi qua `IChatbotRepository` (3-layer) |
| D-11 | `IAiProviderService` → `GeminiProviderService` / `OllamaProviderService` | ✅ (2 provider) | ✅ (chỉ `GeminiProviderService`) | `[x]` LS-08 — **chỉ Gemini** theo quyết định chủ dự án; `OllamaProviderService` **không port**. Nhánh fallback sang Ollama trong `ChatbotService` đã loại bỏ (không còn dangling reference). Đăng ký bằng `AddHttpClient<IAiProviderService, GeminiProviderService>()` → timeout lấy từ `Chatbot:Providers:gemini:TimeoutSeconds` |
| D-12 | `Options<PayOSOptions>` | ✅ | ✅ | `[x]` |
| D-13 | `Options<ChatbotOptions>` | ✅ | ✅ | `[x]` LS-08 — `Configure<ChatbotOptions>(GetSection("Chatbot"))` |
| D-14 | `Options<JwtSettings>` + `IJwtTokenService` | — | ✅ | `[x]` 🆕 |
| D-15 | `AddSelfHttpClient<...>` (9 client) | — | ✅ | `[x]` 🆕 — cơ chế Blazor gọi chính API qua HTTP |
| D-16 | `IStaffRepository/IStaffService/IStaffDashboardService/IStaffProfileService` | — | ✅ | `[x]` 🆕 |
| D-17 | `UserSessionService` + `CartStateService` | — | ✅ | `[x]` 🆕 |
| D-18 | `IBoardingRepository` / `IBoardingService` | — | ✅ | `[x]` 🆕 |

> **Lưu ý:** `Program.cs` nguồn đăng ký trùng lặp `IPayOSService`, `PayOSOptions`, `IChatbotService` (3 cặp) — đích đã dọn sạch.

---

## 3. LỚP 2 — MODELS / ENTITIES / DTO

### 3.1. Entity bảng gốc (27) — scaffold DB-first, KHÔNG đổi schema

| # | Entity | Nguồn | Đích | Key | Trạng thái |
|---|---|---|---|---|---|
| E-01 | `Admin` | `Models/Admin.cs` | `Models/Admin.cs` | `AdminId` | `[x]` |
| E-02 | `AttendanceRecord` | ✅ | ✅ | `AttendanceId` | `[x]` |
| E-03 | `BoardingBooking` | ✅ | ✅ | `BookingId` | `[x]` 🆕 feature boarding |
| E-04 | `BoardingRoom` | ✅ | ✅ | `RoomId` | `[x]` 🆕 |
| E-05 | `Booking` | ✅ | ✅ | `BookingId` | `[x]` |
| E-06 | `BookingService` | ✅ | ✅ | composite `(BookingId,ServiceId)` | `[x]` |
| E-07 | `Breed` | ✅ | ✅ | `BreedId` | `[x]` |
| E-08 | `BreedPricing` | ✅ | ✅ | `BreedPricingId` | `[x]` |
| E-09 | `ChatMessage` | ✅ | ✅ | `MessageId` | `[x]` — entity có sẵn, **chưa có service ghi** |
| E-10 | `Customer` | ✅ | ✅ | `CustomerId` | `[x]` — ⚠️ nguồn có `birth_date`/`avatar_url`, đích thiếu → xem Q9 |
| E-11 | `Doctor` | ✅ | ✅ | `DoctorId` | `[x]` |
| E-12 | `MedicalRecord` | ✅ | ✅ | `RecordId` | `[x]` |
| E-13 | `Order` | ✅ | ✅ | `OrderId` | `[x]` |
| E-14 | `OrderDetail` | ✅ | ✅ | `DetailId` | `[x]` |
| E-15 | `Payment` | ✅ | ✅ | `PaymentId` | `[x]` |
| E-16 | `PayrollRecord` | ✅ | ✅ | `PayrollId` | `[x]` |
| E-17 | `Pet` | ✅ | ✅ | `Id` (không phải `PetId`) | `[x]` |
| E-18 | `PetService` | ✅ | ✅ | `ServiceId` | `[x]` |
| E-19 | `Product` | ✅ | ✅ | `ProductId` | `[x]` — đích thêm `IsDeleted` |
| E-20 | `ProductCategory` | ✅ | ✅ | `CategoryId` | `[x]` |
| E-21 | `Review` | ✅ | ✅ | `ReviewId` | `[x]` |
| E-22 | `Shift` | ✅ | ✅ | `ShiftId` | `[x]` |
| E-23 | `ShiftRequest` | ✅ | ✅ | `RequestId` | `[x]` |
| E-24 | `Species` | ✅ | ✅ | `SpeciesId` | `[x]` |
| E-25 | `Staff` | ✅ | ✅ | `StaffId` | `[x]` |
| E-26 | `StaffSalary` | ✅ | ✅ | `SalaryId` | `[x]` |
| E-27 | `Supplier` | ✅ | ✅ | `SupplierId` | `[x]` |
| E-28 | `WorkSchedule` | ✅ | ✅ | `ScheduleId` | `[x]` |
| E-29 | `SystemSetting` | ✅ | ✅ (không có DbSet) | — | `[x]` |

### 3.2. Entity view (11) — read-only

| # | Entity | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|
| V-01..V-11 | `VBooking`, `VCart`, `VOrderItemCompact`, `VOrderItemProduct`, `VPayment`, `VProductReview`, `VSalesOrder`, `VServiceReview`, `VwBoardingAvailabilityStat`, `VwBookingStatusCount`, `VwPetWithBreedPricing` | ✅ | ✅ | `[x]` — map `ToView(...)` giữ nguyên |

### 3.3. DTO — thiếu ở đích

| # | DTO | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|
| T-01 | `Models/ChatbotDtos.cs` | ✅ (`ChatMessageRequestDto`, `ChatMessageResponseDto`, `ChatHistoryItemDto`, `DetectIntentRequestDto`, `AiIntentResult`, `ConversationContextDto`, `ChatbotHealthDto`) | ✅ | `[x]` LS-08 — `Models/ChatbotDtos.cs` + `Models/ChatbotRepositoryDtos.cs` (DTO rút gọn cho repository). `DetectIntentRequestDto`/`ChatbotHealthDto` đặt trong `Controllers/ChatbotController.cs` **giữ nguyên vị trí như bản gốc** |
| T-02 | `Models/ChatbotOptions.cs` (`ChatbotOptions`, `AiProviderConfig`) | ✅ | ✅ | `[x]` LS-08 — `DefaultProvider` đổi `"groq"` → `"gemini"` (chỉ 1 provider, khớp DI đích) |
| T-03 | `Models/ProductDto_temp.cs` | ✅ | ✅ đã đổi tên → `Models/ProductDto.cs` (+2 field `AvgRating`,`ReviewCount`) | `[x]` — cần user xác nhận mở rộng (GAP-05) |
| T-04 | `Models/ProductCategoryFilterDto_temp.cs` | ✅ | ✅ đã đổi tên → `Models/ProductCategoryFilterDto.cs` | `[x]` |
| T-05 | `Models/PayrollDtos.cs` — `GeneratePayrollDto`, `PayrollResultDto` | ✅ | ❌ thiếu | `[!]` — cần xác minh không endpoint nào dùng (GAP-06) |
| T-06 | 53 model dùng chung | ✅ | ✅ | `[x]` — property/type khớp 100% |

---

## 4. LỚP 3 — REPOSITORIES

> **Slide Ch05 Slide 32–40:** Repository pattern tạo abstraction layer giữa data access và business logic.

| # | Repository nguồn | Đích | Số method | Trạng thái |
|---|---|---|---|---|
| RP-01 | `Repositories/AuthRepository.cs` | `Repositories/AuthRepository.cs` | 7 → 8 | `[x]` — đầy đủ, đích thêm `UpgradePasswordHashAsync` |
| RP-02 | `Repositories/PetServiceRepository.cs` | ✅ | 2 → 2 | `[x]` — thân hàm giống từng dòng |
| RP-03 | `Repositories/ProductRepository_temp.cs` | `Repositories/ProductRepository.cs` | 3 → 3 | `[x]` — đổi tên bỏ `_temp`; đích bổ sung `AvgRating`/`ReviewCount` |
| RP-04 | `Repositories/ReviewRepository.cs` | ✅ | 3 → 3 | `[x]` — giữ nguyên 5 message tiếng Việt |
| RP-05 | `Repositories/SpaBookingRepository.cs` | ✅ | 16 → 16 | `[x]` — 16/16 khớp |
| RP-06 | — | `Repositories/BoardingRepository.cs` | — | `[x]` 🆕 |

---

## 5. LỚP 4 — SERVICES

### 5.1. Service đã có đủ ở đích

| # | Service nguồn | Đích | Số method | Trạng thái |
|---|---|---|---|---|
| SV-01 | `Services/AttendanceService.cs` | `Services/AttendanceService.cs` | 6 → 6 | `[x]` — giống từng dòng, 4 message giữ nguyên |
| SV-02 | `Services/AuthService.cs` | `Services/AuthService.cs` + `AuthService.Google.cs` | 6 → 5+1 | `[x]` — split partial, 6 message Google giữ nguyên |
| SV-03 | `Services/EmailService.cs` | `Services/EmailService.cs` | 4 → 5 | `[x]` — ⚠️ đổi path template sang `wwwroot/EmailTemplates/` (**thư mục thiếu → R1**) |
| SV-04 | `Services/PayOSService.cs` | `Services/PayOSService.cs` + `.Fulfillment.cs` | 6 → 8 | `[x]` — ⚠️ mất fallback URL (R10) |
| SV-05 | `Services/PayrollService.cs` | `Services/PayrollService.cs` | 7 → 7 | `[x]` — giữ nguyên message không dấu |
| SV-06 | `Services/PetServiceService.cs` | `Services/PetServiceService.cs` | 2 → 2 | `[x]` |
| SV-07 | `Services/ProductService_temp.cs` | `Services/ProductService.cs` | 3 → 3 | `[x]` |
| SV-08 | `Services/SpaBookingService.cs` | `Services/SpaBookingService.cs` + `.Booking.cs` | 11 → 5+6 | `[x]` — logic giống từng dòng (semaphore, rollback, wave 2-pet) |

### 5.2. Service THIẾU ở đích — phải port nguyên văn

| # | Service nguồn | Nội dung | Trạng thái |
|---|---|---|---|
| SV-09 | `Services/ChatbotService.cs` (30.201 B) | 4 method: `GetResponseAsync`, `BuildSystemPromptAsync`, `GetContextAsync`, `AnalyzeIntentWithAi`. **157 message tiếng Việt** (system prompt ~35, định dạng context ~30, stop-words intent ~70, tin nhắn lỗi ~12) | `[x]` LS-08 — port nguyên văn, 0 đổi nội dung prompt. Thay `ShopPetDatabaseContext` bằng `IChatbotRepository` (3-layer). Loại bỏ nhánh fallback Ollama (message lỗi đổi "Ollama" → "Gemini") |
| SV-10 | `Services/GeminiProviderService.cs` | `GenerateTextAsync` — POST `{baseUrl}/{model}:generateContent`, header `X-goog-api-key`, body `system_instruction`/`contents`/`generationConfig` | `[x]` LS-08 — port nguyên văn, đặt tại `Services/GeminiProviderService.cs` (namespace `PetShop.Services`, đích không có sub-folder `AI/`) |
| SV-11 | `Services/OllamaProviderService.cs` | `GenerateTextAsync` — provider local tại `http://localhost:11434`, model `llama3.2` | `[x]` LS-08 — **cố ý KHÔNG port** (quyết định của chủ dự án: chỉ Gemini). Đã loại bỏ nhánh fallback trong `ChatbotService` để không còn dangling reference |

> **Yêu cầu bắt buộc:** port **NGUYÊN VĂN** prompt, tham số, định dạng response. Chi tiết tại `docs/_inventory/SOURCE_COMPLEX_FLOWS.md` §1.

### 5.3. Service đích có thêm (mở rộng, không phải gap)

`ApiExceptionMiddleware`, `AuthClient`, `BoardingService`, `CartStateService`, `CheckoutClient`, `CsvOutputFormatter`, `JwtTokenService`, `LoggerExtensions`, `MvcBuilderExtensions`, `OrderClient`, `PaymentClient`, `PayOSService.Fulfillment`, `PetsClient`, `PetServiceClient`, `ProductCatalogClient`, `ReviewClient`, `SelfHttpClientExtensions`, `SessionHeaderAuthenticationHandler`, `SpaAuthHeaderHandler`, `SpaBookingClient`, `StaffDashboardService`, `StaffProfileService`, `StaffService`, `UserSessionService`, `WorkScheduleDedup`, `XmlOutputFormatter` — `[x]` 🆕

---

## 6. LỚP 5 — CONTROLLERS / API ENDPOINTS

> **Slide Ch01:** `[ApiController]`, attribute routing bắt buộc, HTTP verbs, status codes, `ProblemDetails` cho ≥400.

### 6.1. Auth — `api/auth`

| # | METHOD | Route | Nguồn | Đích | Auth | Trạng thái |
|---|---|---|---|---|---|---|
| A-01 | POST | `api/auth/register` | ✅ | ✅ | — | `[x]` |
| A-02 | POST | `api/auth/login` | ✅ | ✅ | — | `[x]` |
| A-03 | POST | `api/auth/google-login` | ✅ | ✅ | — | `[x]` — Google IdToken + AccessToken |
| A-04 | POST | `api/auth/send-email-otp` | ✅ | ✅ | — | `[x]` |
| A-05 | POST | `api/auth/verify-email-otp` | ✅ | ✅ | — | `[x]` |
| A-06 | POST | `api/auth/reset-password` | ✅ | ✅ | — | `[x]` |
| A-07 | GET | `api/auth/google-client-id` | ✅ | ✅ | — | `[x]` |
| A-08 | POST | `api/auth/token` | — | ✅ | AllowAnonymous | `[x]` 🆕 JWT (Ch06) |
| A-09 | GET | `api/auth/me` | — | ✅ | `[Authorize]` | `[x]` 🆕 |
| A-10 | GET | `api/config/google-client-id` | ✅ | ✅ | — | `[x]` — trùng lặp A-07, giữ nguyên theo bản gốc |

### 6.2. Pets — `api/pets`

| # | METHOD | Route | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|---|
| P-01 | GET | `api/pets/customer/{customerId:int}` | ✅ | ✅ | `[x]` |
| P-02 | GET | `api/pets/customer/{customerId:int}/manage` | ✅ | ✅ | `[x]` |
| P-03 | POST | `api/pets/upload-image` | ✅ | ✅ | `[x]` — ⚠️ thư mục `wwwroot/uploads/pets/` thiếu (R2) |
| P-04 | GET | `api/pets/customer/{customerId:int}/service/{serviceId:int}/pricing` | ✅ | ✅ | `[x]` |
| P-05 | GET | `api/pets/meta/species` | ✅ | ✅ | `[x]` |
| P-06 | GET | `api/pets/meta/breeds` | ✅ | ✅ | `[x]` |
| P-07 | POST | `api/pets` | ✅ | ✅ | `[x]` — 201 Created |
| P-08 | PUT | `api/pets/{petId:int}` | ✅ | ✅ | `[x]` |
| P-09 | DELETE | `api/pets/{petId:int}?customerId=` | ✅ | ✅ | `[x]` — 204, soft-delete |

### 6.3. Spa booking — `api/spa-booking`

| # | METHOD | Route | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|---|
| SP-01 | GET | `api/spa-booking/pets` | ✅ | ✅ | `[x]` |
| SP-02 | GET | `api/spa-booking/services` | ✅ | ✅ | `[x]` |
| SP-03 | POST | `api/spa-booking/availability` | ✅ | ✅ | `[x]` |
| SP-04 | POST | `api/spa-booking/validate-slot` | ✅ | ✅ | `[x]` |
| SP-05 | POST | `api/spa-booking/estimate` | ✅ | ✅ | `[x]` |
| SP-06 | POST | `api/spa-booking` | ✅ | ✅ | `[x]` — 200 (không phải 201, giữ nguyên bản gốc) |
| SP-07 | GET | `api/spa-booking/history` | ✅ | ✅ | `[x]` |
| SP-08 | GET | `api/spa-booking/history/all` **+** `all-bookings` | ✅ | ✅ | `[x]` — giữ cả 2 route alias |
| SP-09 | GET | `api/spa-booking/{bookingId:int}/invoice` | ✅ | ✅ | `[x]` |
| SP-10 | PATCH | `api/spa-booking/{bookingId:int}/status` | ✅ | ✅ | `[x]` |
| SP-11 | POST | `api/spa-booking/review` | ✅ | ✅ | `[x]` |

### 6.4. PayOS — `api/payos`

| # | METHOD | Route | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|---|
| PO-01 | POST | `api/payos/spa/create` | ✅ | ✅ | `[x]` |
| PO-02 | POST | `api/payos/order/create` | ✅ | ✅ | `[x]` |
| PO-03 | POST | `api/payos/webhook` | ✅ | ✅ | `[x]` — HMAC-SHA256 trên raw body, header `x-payos-signature` |
| PO-04 | GET | `api/payos/status/{orderCode}` | — | ✅ | `[x]` 🆕 |
| PO-05 | POST | `api/payos/send-invoice` | — | ✅ | `[x]` 🆕 |

### 6.5. Shift exchange — `api/shift-exchange`

| # | METHOD | Route | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|---|
| SH-01 | GET | `api/shift-exchange/verify-network` | ✅ | ✅ | `[x]` |
| SH-02 | GET | `api/shift-exchange/staff/{staffId:int}/my-shifts` | ✅ | ✅ | `[x]` |
| SH-03 | GET | `api/shift-exchange/all-shifts` | ✅ | ✅ | `[x]` |
| SH-04 | GET | `api/shift-exchange/staff/options` | ✅ | ✅ | `[x]` |
| SH-05 | POST | `api/shift-exchange/pass` | ✅ | ✅ | `[x]` |
| SH-06 | POST | `api/shift-exchange/swap` | ✅ | ✅ | `[x]` |
| SH-07 | GET | `api/shift-exchange/pending` | ✅ | ✅ | `[x]` |
| SH-08 | GET | `api/shift-exchange/my-requests` | ✅ | ✅ | `[x]` |
| SH-09 | POST | `api/shift-exchange/accept/{requestId:int}` | ✅ | ✅ | `[x]` |
| SH-10 | POST | `api/shift-exchange/decline/{requestId:int}` | ✅ | ✅ | `[x]` |
| SH-11 | POST | `api/shift-exchange/cancel/{requestId:int}` | ✅ | ✅ | `[x]` |

### 6.6. Attendance / Payroll / Notification

| # | METHOD | Route | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|---|
| N-01 | GET | `api/attendance/unclosed` | ✅ | ✅ | `[x]` |
| N-02 | GET | `api/attendance/staff/{staffId:int}/overview` | ✅ | ✅ | `[x]` |
| N-03 | POST | `api/attendance/staff/{staffId:int}/toggle` | ✅ | ✅ | `[x]` |
| N-04 | GET | `api/payroll/history` | ✅ | ✅ | `[x]` |
| N-05 | GET | `api/payroll/staff/{staffId:int}/detail` | ✅ | ✅ | `[x]` |
| N-06 | GET | `api/payroll/staff/{staffId:int}/generate` | ✅ | ✅ | `[x]` — GET có side-effect (giữ nguyên bản gốc) |
| N-07 | POST | `api/payroll/generate-all` | ✅ | ✅ | `[x]` |
| N-08 | GET | `api/payroll/config` | ✅ | ✅ | `[x]` |
| N-09 | GET | `api/payroll/config/staff/{staffId:int}` | ✅ | ✅ | `[x]` |
| N-10 | PUT | `api/payroll/config/staff` | ✅ | ✅ | `[x]` |
| N-11 | GET | `api/payroll/staff-list` | ✅ | ✅ | `[x]` |
| N-12 | GET | `api/notifications/{staffId:int}` | ✅ | ✅ | `[x]` |
| N-13 | GET | `api/notifications/{staffId:int}/unread-count` | ✅ | ✅ | `[x]` |
| N-14 | PUT | `api/notifications/{requestId:int}/read` | ✅ | ✅ | `[x]` |
| N-15 | PUT | `api/notifications/{staffId:int}/read-all` | ✅ | ✅ | `[x]` |
| N-16 | DELETE | `api/notifications/{requestId:int}` | ✅ | ✅ | `[x]` — stub luôn trả 200, giữ nguyên bản gốc |

### 6.7. Breed pricing / Pet services / Review / Product / Cart / Checkout / Order

| # | METHOD | Route | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|---|
| B-01 | GET | `api/breed-pricing` | ✅ | ✅ | `[x]` |
| B-02 | GET | `api/breed-pricing/breed/{breedId:int}` | ✅ | ✅ | `[x]` |
| B-03 | POST | `api/breed-pricing` | ✅ | ✅ | `[x]` — 201 |
| B-04 | PUT | `api/breed-pricing/{id:int}` | ✅ | ✅ | `[x]` |
| B-05 | DELETE | `api/breed-pricing/{id:int}` | ✅ | ✅ | `[x]` — 204 |
| B-06 | POST | `api/breed-pricing/seed-all` | ✅ | ✅ | `[x]` |
| PS-01 | GET | `api/pet-services` | ✅ | ✅ | `[x]` |
| PS-02 | GET | `api/pet-services/grouped` | ✅ | ✅ | `[x]` |
| PS-03 | GET | `api/pet-services/{serviceId:int}` | ✅ | ✅ | `[x]` |
| RV-01 | GET | `api/reviews/services/{serviceId:int}` | ✅ | ✅ | `[x]` |
| RV-02 | GET | `api/reviews/products/{productId:int}` | ✅ | ✅ | `[x]` |
| RV-03 | POST | `api/reviews/products/{productId:int}` | ✅ | ✅ | `[x]` |
| PR-01 | GET | `api/products` | ✅ | ✅ | `[x]` — đích bỏ hậu tố `-temp` |
| PR-02 | GET | `api/products/categories` | ✅ | ✅ | `[x]` |
| PR-03 | GET | `api/products/{id:int}` | ✅ | ✅ | `[x]` |
| CT-01 | POST | `api/checkout` | ✅ | ✅ | `[x]` |
| OR-01..OR-06 | `api/orders/*` (history, detail, staff/pending, staff, staff/confirm, delivered) | ✅ | ✅ | `[x]` |

### 6.8. ⚠️ Endpoint FE nguồn gọi nhưng KHÔNG tồn tại ở cả 2 bên (R9)

| # | METHOD | Route | Gọi từ | Trạng thái |
|---|---|---|---|---|
| X-01 | POST | `api/products-temp/create` | `api/productsApi.js:createProduct` | `[x]` LS-04 — `Controllers/ProductsTempController.cs` (route mới, `ProductsController` cũ giữ nguyên). Body `{name,categoryId,price,stockQuantity,supplierName,description,imageUrl}` |
| X-02 | POST | `api/products-temp/update` | `api/productsApi.js:updateProduct` | `[x]` LS-04 — body `{productId,name,categoryId,price,stockQuantity,supplierName,description,imageUrl}` |
| X-03 | POST | `api/products-temp/delete` | `api/productsApi.js:deleteProduct` | `[x]` LS-04 — **soft delete** qua cột `Products.is_deleted` (cột đã có sẵn trong schema, không sửa DB) |
| X-04 | GET | `api/products-temp/deleted` | `api/productsApi.js:fetchDeletedProducts` | `[x]` LS-04 — trả `[]` khi không có sản phẩm đã xóa (khớp `fetchDeletedProducts` xử lý 404 ⇒ `[]`) |
| X-05 | POST | `api/products-temp/restore` | `api/productsApi.js:restoreProduct` | `[x]` LS-04 — body `{productId}` |
| X-06 | POST | `api/products-temp/permanent-delete` | `api/productsApi.js:permanentDeleteProduct` | `[x]` LS-04 — body `{productId}` |
| X-07 | POST | `api/products-temp/upload-image` | `api/productsApi.js:uploadProductImage` | `[x]` LS-04 — multipart field `image`; lưu `wwwroot/uploads/products/`; trả `{ imageUrl: "/uploads/products/..." }` (khớp `payload?.imageUrl`) |
| X-08 | GET | `api/products-temp/search` | `api/productsApi.js:searchProducts` | `[x]` LS-04 — query `?keyword=&categoryId=` |
| X-09 | POST | `api/pet-services` | `api/petServicesApi.js:createPetService` | `[x]` LS-04 — `Controllers/PetServicesController.cs`; body `{name,description,price,duration,serviceType,status,imageUrl}` |
| X-10 | POST | `api/pet-services/upload-image` | `api/petServicesApi.js:uploadPetServiceImage` | `[x]` LS-04 — multipart field `image`; lưu `wwwroot/uploads/pet-services/`; trả `{ imageUrl, url }` (khớp `data.url \|\| data.imageUrl`) |

### 6.9. Endpoint chatbot (đã port ở LS-08)

| # | METHOD | Route | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|---|
| CH-01 | POST | `api/chatbot/message` | `Controllers/ChatbotController.cs` | ✅ | `[x]` LS-08 — route/status code/response shape giữ nguyên. Smoke test: 200, `source=fallback` do key Gemini bị Google báo leaked (xem Q13) |
| CH-02 | POST | `api/chatbot/intent` | ✅ | ✅ | `[x]` LS-08 — 200, `{"intent":"unknown","parameters":{}}` (AI không trả JSON ⇒ fallback `unknown`, đúng logic bản gốc) |
| CH-03 | GET | `api/chatbot/context/{customerId:int}` | ✅ | ✅ | `[x]` LS-08 — 200, trả đủ context DB thật (pets/orders/bookings/staff/doctors/products/services/categories/species/rooms) |
| CH-04 | GET | `api/chatbot/health` | ✅ | ✅ | `[x]` LS-08 — 200, `status=healthy`, `aiProviderConfigured=true`, `databaseConnected=true` |

### 6.10. Controller MVC (session cart) — đã vá xong (R3, R4)

| # | METHOD | Route | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|---|
| MC-01 | GET | `/cart/session` | ✅ `CartController.Index` + `Views/` (nguồn **không có** thư mục `Views` ⇒ route này luôn lỗi "No view found") | ✅ `CartController.Index` + `Views/Cart/Index.cshtml` | `[x]` LS-03 — đổi route sang `/cart/session` để không trùng Blazor `/cart` (AmbiguousMatch). Verify: HTTP 200 |
| MC-02 | POST | `/cart/dec/{id:int}` | ✅ | ✅ | `[x]` LS-03 — `RedirectToAction(nameof(Index))` → `Redirect("/cart/session")`. **ĐÃ vá lỗi HTTP 500** |
| MC-03 | POST | `/cart/remove/{id:int}` | ✅ | ✅ | `[x]` LS-03 — như trên. **ĐÃ vá lỗi HTTP 500** |
| MC-04 | POST | `/cart/add/{id:int}` | ✅ | ✅ | `[x]` LS-03 — như trên. **ĐÃ vá lỗi HTTP 500** |
| MC-05 | GET | `/mvc/ProductsPage/Index` | ✅ `ProductsPageController.Index` → redirect `http://localhost:5173/products` | ✅ `Controllers/ProductsPageController.cs` | `[x]` LS-03 — redirect tới `/products` (Blazor, không còn SPA Vite). Verify: HTTP 302 → `/products` |
| MC-06 | GET | `/mvc/ProductsPage/Details/{id:int}` | ✅ `ProductsPageController.Details` + View | ✅ `Views/ProductsPage/Details.cshtml` | `[x]` LS-03 — projection port nguyên văn (Category/Supplier). Verify: HTTP 200 với id tồn tại, 404 với id không tồn tại |

### 6.11. OData — `PetShop/*` (slide Ch04)

| # | METHOD | Route | Đích | Trạng thái |
|---|---|---|---|---|
| OD-01 | GET | `PetShop/Products` | ✅ | `[x]` |
| OD-02 | GET | `PetShop/Products({key})` | ✅ | `[x]` |
| OD-03 | GET | `PetShop/Products/$count` | ✅ | `[x]` |
| OD-04..06 | `PetShop/PetServices` (+`{key}`, `/$count`) | ✅ | `[x]` |
| OD-07..09 | `PetShop/Pets` (+`{key}`, `/$count`) | ✅ | `[x]` |
| OD-10 | GET | `PetShop/$metadata` | middleware tự sinh | `[x]` |

### 6.12. 🆕 Endpoint chỉ có ở đích (mở rộng, không phải gap)

| # | METHOD | Route | Mô tả |
|---|---|---|---|
| BD-01..07 | `api/boarding-rooms/*` | Feature boarding (7 endpoint) |
| ET-01..03 | `api/test/email/*` | Test email (3 endpoint) |

---

## 7. LỚP 6 — UI / ROUTES / COMPONENTS

> **Yêu cầu cứng:** giữ nguyên giao diện, màu sắc, bố cục, text, thứ tự, validation message của bản gốc.

### 7.1. Route bản gốc (React) → route đích (Razor)

| # | Route nguồn | Page nguồn | Route đích | Razor đích | Layout đích | Trạng thái |
|---|---|---|---|---|---|---|
| U-01 | `/` | `HomePage.jsx` | `/` | `Home.razor` | `MainLayout` | `[x]` — dùng lại `HeroBanner`/`FeaturedCategories`/`ProductList`/`CustomerReviews` |
| U-02 | `/products` | `ProductsPage.jsx` | `/products` | `Products.razor` | `MainLayout` | `[x]` |
| U-03 | `/search` | `SearchProductsPage.jsx` | `/search` | `Search.razor` | `MainLayout` | `[x]` |
| U-04 | `/products/:id` | `ProductDetailPage.jsx` | `/products/{ProductId:int}` | `ProductDetail.razor` | `MainLayout` | `[x]` |
| U-05 | `/cart` | `CartPage.jsx` | `/cart` | `Cart.razor` | `MainLayout` | `[x]` — ⚠️ giỏ hàng: nguồn localStorage, đích session server |
| U-06 | `/cart/checkout` | `CheckoutPage.jsx` | `/cart/checkout` | `Checkout.razor` | `MainLayout` | `[x]` — ⚠️ thiếu `daNangData` + `AddressModal` (Q10) |
| U-07 | `/checkout/cod-success` | `CodConfirmationPage.jsx` | `/checkout/cod-success` | `CodConfirmation.razor` | `MainLayout` | `[x]` |
| U-08 | `/account` | `ProfilePage.jsx` | `/account` | `Account.razor` | `MainLayout` | `[x]` |
| U-09 | `/profile` | `ProfilePage.jsx` | `/profile` | `Account.razor` | `MainLayout` | `[x]` LS-12 — thêm `@page "/profile"` vào `Account.razor` (nguồn `/profile` và `/account` cùng render `ProfilePage` ⇒ không đổi UI) |
| U-10 | `/profile/edit` | `ProfileEditPage.jsx` | `/profile/edit` | `Account.razor` | `MainLayout` | `[x]` |
| U-11 | `/account/pets` | `PetInfoPage.jsx` | `/account/pets` | `PetInfo.razor` | `MainLayout` | `[x]` |
| U-12 | `/account/orders` | `OrderHistoryPage.jsx` | `/account/orders` | `Orders.razor` | `MainLayout` | `[x]` |
| U-13 | `/account/orders/:orderId` | `OrderHistoryDetailPage.jsx` | `/account/orders/{OrderId:int}` | `Orders.razor` | `MainLayout` | `[x]` |
| U-14 | `/login` | `LoginPage.jsx` | `/login` | `Login.razor` | `EmptyLayout` | `[x]` |
| U-15 | `/register` | `RegisterPage.jsx` | `/register` | `Register.razor` | `EmptyLayout` | `[x]` |
| U-16 | `/forgot-password` | `ForgotPasswordPage.jsx` | `/forgot-password` | `ForgotPassword.razor` | `EmptyLayout` | `[x]` |
| U-17 | `/orders` | `OrdersStaffPage.jsx` | `/system/orders` | `StaffOrders.razor` | `StaffWorkspaceLayout` | `[x]` — route khác nhưng chức năng có |
| U-18 | `/system` | `SystemPage.jsx` | `/system` | `StaffDashboard.razor` | `StaffWorkspaceLayout` | `[x]` — ⚠️ tab `breed-pricing` **không truy cập được** ở nguồn (thiếu key trong `MENU_ITEMS`) → giữ nguyên hay sửa? xem Q8 |
| U-19 | `/services` | `ServicePage.jsx` | `/services` | `Services.razor` | `MainLayout` | `[x]` |
| U-20 | `/services/:id` | `ServiceDetailPage.jsx` | `/services/{Id:int}` | `ServiceDetail.razor` | `MainLayout` | `[x]` |
| U-21 | `/spa-cart` | `SpaCartPage.jsx` | `/spa-cart` | `SpaCart.razor` | `MainLayout` | `[x]` |
| U-22 | `/spa-booking-history` | `SpaBookingHistoryPage.jsx` | `/spa-booking-history` | `SpaBookingHistory.razor` | `MainLayout` | `[x]` |
| U-23 | `/spa-booking-history/:historyId` | `SpaBookingHistoryDetailPage.jsx` | `/spa-booking-history/{HistoryId:int}` | `SpaBookingHistoryDetail.razor` | `MainLayout` | `[x]` |
| U-24 | `/spa-invoice` | `SpaInvoicePage.jsx` | `/spa-invoice` | `SpaInvoice.razor` | `MainLayout` | `[x]` |
| U-25 | `/products/create` | `CreateProductPage.jsx` | `/products/create` | `ProductCreate.razor` | `MainLayout` | `[x]` — ⚠️ gọi raw SQL (R7) |
| U-26 | `/about` | `AboutPage.jsx` | `/about` | `About.razor` | `MainLayout` | `[x]` LS-12 — nội dung tiếng Việt port **nguyên văn** (HIGHLIGHTS/SERVICE_POINTS/STEPS + hero/values/services/steps/faq/cta); CSS `about-*` **không có ở nguồn lẫn đích** ⇒ viết mới `wwwroot/css/template/about.css`, link trong `Components/App.razor`. `Footer.razor` link `/about` hết chết (R5) |
| U-26b | `/contact` | — (nguồn **không có** page, chỉ có link trong Footer) | `/contact` | `Contact.razor` | `MainLayout` | `[x]` LS-12 — dựng mới từ thông tin liên hệ trong `Footer.razor` (`123 Đường ABC, Quận 1, TP.HCM` / `0123 456 789` / `contact@pettoy.com`). ⚠️ **không có bản gốc để port nguyên văn** — bố cục tự dựng (xem Q15) |
| U-27 | `*` (fallback) | `App.jsx` | — | `NotFoundPage.razor` | — | `[ ]` LS-12 — nguồn redirect `/`; đích có `NotFoundPage` (xem Q8) |
| U-28 | — | `SpaBookingDetailPage.jsx` (**orphan**, không có route) | — | — | — | `[ ]` LS-12 — nguồn implement đầy đủ nhưng không reachable. Port hay bỏ? xem Q8 |

### 7.2. Route chỉ có ở đích (mở rộng)

| # | Route | Razor | Mô tả |
|---|---|---|---|
| U-D1 | `/checkout/invoice`, `/order-invoice` | `OrderInvoice.razor` | Hoá đơn |
| U-D2 | `/checkout/result` | `CheckoutResult.razor` | Kết quả thanh toán |
| U-D3 | `/products/deleted` | `ProductDeleted.razor` | SP sản phẩm đã xoá (⚠️ raw SQL R7) |
| U-D4 | `/products/edit/{Id:int}` | `ProductEdit.razor` | Sửa SP (⚠️ raw SQL R7) |
| U-D5 | `/products/inventory` | `ProductInventory.razor` | Kho (⚠️ raw SQL R7) |
| U-D6 | `/products/list` | `ProductList.razor` | Danh sách SP (⚠️ raw SQL R7) |
| U-D7 | `/revenue` | `Revenue.razor` | Doanh thu (⚠️ raw SQL R7) |
| U-D8 | `/system/bookings` | `StaffBookings.razor` | (⚠️ raw SQL R7) |
| U-D9 | `/system/profile` | `StaffProfile.razor` | Hồ sơ NV |
| U-D10 | `/system/schedule` | `StaffSchedule.razor` | Lịch làm việc |
| U-D11 | `/Error` | `Error.razor` | Trang lỗi |

### 7.3. Component nguồn → đích

| # | Component nguồn | Đích | Trạng thái |
|---|---|---|---|
| CM-01 | `components/home/TopBar.jsx` (23 page) | `Components/Shared/TopBar.razor` | `[x]` |
| CM-02 | `components/home/Header.jsx` (11 page) | `Components/Shared/Header.razor` | `[x]` |
| CM-03 | `components/home/Navigation.jsx` (10 page) | `Components/Shared/Navigation.razor` | `[x]` — ⚠️ 11 link không có route ở nguồn; giữ nguyên |
| CM-04 | `components/home/Footer.jsx` (23 page) | `Components/Shared/Footer.razor` | `[x]` — ✅ LS-12: link `/about` + `/contact` **không còn chết** (R5 đã đóng) |
| CM-05 | `components/home/PageContextBar.jsx` (11 page) | `Components/Shared/PageContextBar.razor` | `[x]` |
| CM-06 | `components/home/CartDrawer.jsx` (4 page) | `Components/Shared/CartDrawer.razor` | `[x]` |
| CM-07 | `components/home/Toast.jsx` (1 page) | `Components/Shared/Toast.razor` | `[x]` |
| CM-08 | `components/home/ProductCard.jsx` | `Components/Shared/ProductCard.razor` | `[x]` |
| CM-09 | `components/home/ProductList_temp.jsx` | `Components/Home/ProductList.razor` | `[x]` |
| CM-10 | `components/home/HeroBanner.jsx` | `Components/Home/HeroBanner.razor` | `[x]` |
| CM-11 | `components/home/FeaturedCategories.jsx` | `Components/Home/FeaturedCategories.razor` | `[x]` |
| CM-12 | `components/home/CustomerReviews.jsx` | `Components/Home/CustomerReviews.razor` | `[x]` |
| CM-13 | `components/products/ProductsSidebar.jsx` | `Components/Products/ProductsSidebar.razor` | `[x]` |
| CM-14 | `components/products/ClearSearchButton.jsx` | ❌ **THIẾU** | `[ ]` LS-12 |
| CM-15 | `components/checkout/AddressModal.jsx` | ❌ **THIẾU** | `[ ]` LS-12 |
| CM-16 | `components/checkout/daNangData.js` | ❌ **THIẾU** | `[ ]` LS-12 (Q10) |
| CM-17 | `components/staff/NotificationBell.jsx` | ❌ **THIẾU** | `[ ]` LS-12 — đích có API notification nhưng không có UI |
| CM-18 | `components/staff/OrdersStaffPanel.jsx` | `Components/Pages/StaffOrders.razor` | `[x]` |
| CM-19 | `utils/userProfileStore.js` | `Services/UserSessionService.cs` | `[x]` 🆕 — localStorage → session server |
| CM-20 | `pages/AboutPage.jsx` (dùng `TopBar`/`Header`/`Navigation`/`Footer`) | `Components/Pages/About.razor` | `[x]` LS-12 — port nguyên văn, dùng lại CM-01..CM-04 |

### 7.4. Guard / phân quyền UI

| # | Guard nguồn | Đích | Trạng thái |
|---|---|---|---|
| GU-01 | `ProtectedCustomerRoute` (staff → `/system`) | `Components/Shared/CustomerOnlyGuard.razor` | `[x]` — ⚠️ red team báo trùng lặp với `AuthGuard` |
| GU-02 | `AuthRequiredRoute` (chưa login → `/login`) | `Components/Shared/AuthGuard.razor` | `[x]` — ⚠️ race prerender |
| GU-03 | Role `doctor` được ẩn section ở Home + nút "Thêm dịch vụ" ở `/services` | ⚠️ đích **chưa xử lý role `doctor`** | `[ ]` LS-13 |

---

## 8. LỚP 7 — ASSET / CSS / JS

### 8.1. Hình ảnh

| # | Hạng mục | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|
| AS-01 | 144 file ảnh trong `frontend/src/images/` | `Projectojt/frontend/src/images/` | `wwwroot/images/` | `[x]` ✅ **144/144 MD5 khớp 100%** |
| AS-02 | `frontend/public/logo-pet-toy.png` | `public/` | `wwwroot/images/`? | `[ ]` LS-12 — cần xác nhận đường dẫn |
| AS-03 | `frontend/public/ziggy-hero-reference.png` | `public/` | `wwwroot/images/`? | `[ ]` LS-12 |
| AS-04 | `frontend/public/default-service-placeholder.png` | **KHÔNG TỒN TẠI** | — | `[!]` — nguồn tham chiếu file không có; đích dùng `placeholder.jpg` |
| AS-05 | 15 file upload pet đã có (`wwwroot/uploads/pets/pet_*`) | `Projectojt/BackEnd/wwwroot/uploads/pets/` | `wwwroot/uploads/pets/` | `[x]` LS-01 — **R2 đã xử lý**: copy đủ 15 file (MD5 khớp nguồn). Verify: `/uploads/pets/pet_07e4be7a9ea842d1909f6d1b2cc4849a.jpg` trả 200 |
| AS-06 | `video.mp4`, `video.MOV` | `frontend/src/images/` | `wwwroot/images/` | `[x]` — đã có |

### 8.2. CSS

| # | File nguồn | Đích | Trạng thái |
|---|---|---|---|
| CS-01 | `styles/home.css` | `wwwroot/css/template/home.css` | `[x]` — ⚠️ `@import` Google Fonts từ CDN ⇒ mất font khi offline |
| CS-02 | `styles/cart.css` | `wwwroot/css/template/cart.css` | `[x]` — ⚠️ tương tự CS-01 |
| CS-03 | `styles/cartDrawer.css` | `wwwroot/css/template/cartDrawer.css` | `[x]` |
| CS-04 | `styles/checkout.css` | `wwwroot/css/template/checkout.css` | `[x]` |
| CS-05 | `styles/codConfirmation.css` | `wwwroot/css/template/codConfirmation.css` | `[x]` — ⚠️ tương tự CS-01 |
| CS-06 | `styles/productDetails.css` | `wwwroot/css/template/productDetails.css` | `[x]` |
| CS-07 | `styles/profile.css` | `wwwroot/css/template/profile.css` | `[x]` |
| CS-08 | `styles/searchProducts.css` | `wwwroot/css/template/searchProducts.css` | `[x]` |
| CS-09 | `styles/auth.css` | `wwwroot/css/template/auth.css` | `[x]` |
| CS-10 | `styles/service.css` | `wwwroot/css/template/spa.css` | `[x]` |
| CS-11 | `styles/pageContextBar.css` | `wwwroot/css/template/pageContextBar.css` | `[x]` |
| CS-12 | `styles/invoice.css` | `wwwroot/css/template/invoice.css` | `[x]` |
| CS-13 | `styles/ordersStaff.css` | ❓ cần đối chiếu | `[ ]` LS-12 |
| CS-14 | `styles/createProduct.css` | ❓ cần đối chiếu | `[ ]` LS-12 |
| CS-15 | `styles/systemPage.css` | ❓ cần đối chiếu | `[ ]` LS-12 |
| CS-16 | `src/App.css`, `src/index.css` | `wwwroot/app.css` | `[x]` — ⚠️ `index.css` còn nền tối `#242424` của Vite |
| CS-17 | — | `wwwroot/css/spa/cart-react.css`, `service-react.css` | `[x]` 🆕 |
| CS-18 | — (nguồn **không có** CSS `about-*`) | `wwwroot/css/template/about.css` | `[x]` LS-12 🆕 — file **viết mới** cho các class `about-*` mà `AboutPage.jsx` dùng (nguồn dùng class nhưng không khai báo CSS ⇒ lỗi hiển thị ở nguồn). Link thêm trong `Components/App.razor`. Cũng dùng cho `/contact` |

### 8.3. JS / script

| # | File nguồn | Đích | Trạng thái |
|---|---|---|---|
| JS-01 | `frontend/vite.config.js` (proxy `/api` → `:5286`) | — | `[x]` — **không cần** (không còn Vite) |
| JS-02 | `frontend/src/api/*.js` (10 module, 70 hàm) | `Services/*Client.cs` + `Services/*Service.cs` | `[x]` — đã quy đổi sang C# (xem §6.8 cho 10 endpoint thiếu) |
| JS-03 | `frontend/src/utils/userProfileStore.js` | `Services/UserSessionService.cs` | `[x]` |
| JS-04 | `frontend/src/components/checkout/daNangData.js` | ❌ | `[ ]` LS-12 (Q10) |
| JS-05 | — | `wwwroot/js/blazorAuth.js` (Google Identity Services) | `[x]` 🆕 |
| JS-06 | — | `wwwroot/js/cartStorage.js` | `[x]` 🆕 |
| JS-07 | — | `wwwroot/js/spaCart.js` | `[x]` 🆕 |
| JS-08 | `frontend/public/` static | `wwwroot/` | `[ ]` LS-12 |

### 8.4. Font / icon

| # | Hạng mục | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|
| FT-01 | Font Awesome (`fab fa-facebook-f`…) | CDN | `wwwroot/`? | `[ ]` LS-12 — nguồn dùng CDN |
| FT-02 | Google Fonts | CDN `@import` trong 4 CSS | CDN | `[ ]` LS-12 — ⚠️ offline mất font |
| FT-03 | `react-icons` 5 | npm | ❌ | `[ ]` LS-12 — cần đổi sang inline SVG hoặc Font Awesome |
| FT-04 | Bootstrap 5 | `index.html` CDN | `wwwroot/bootstrap/bootstrap.min.css` | `[x]` 🆕 |

---

## 9. LỚP 8 — LUỒNG NGHIỆP VỤ

> Chi tiết đầy đủ: `docs/_inventory/SOURCE_COMPLEX_FLOWS.md`

| # | Luồng | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|
| BF-01 | **Đăng ký** — BCrypt hash, role `user`, status `inactive`, OTP 5 phút | ✅ | ✅ | `[x]` |
| BF-02 | **Đăng nhập** — chain Staff→Doctor→Admin→Customer | ✅ | ✅ | `[x]` |
| BF-03 | **Đăng nhập Google** — IdToken ưu tiên (`GoogleJsonWebSignature`), fallback accessToken → `oauth2/v3/userinfo`; customer mới `Password=null, Role="user", Status="active"` | ✅ | ✅ | `[x]` — ⚠️ **ClientId rỗng ⇒ không chạy được** (R6) |
| BF-04 | **Verify OTP** → trả reset token | ✅ | ✅ | `[x]` |
| BF-05 | **Reset password** | ✅ | ✅ | `[x]` |
| BF-06 | **Phân quyền theo role** — `staff`/`admin`/`user`/`doctor` | header tự khai | header + JWT | `[x]` — ⚠️ role `doctor` chưa xử lý ở UI |
| BF-07 | **Giỏ hàng** — thêm/tăng/giảm/xoá, session server | ✅ | ✅ | `[x]` — ⚠️ 2 nguồn giỏ (session MVC vs `CartStateService`) |
| BF-08 | **Checkout COD** — tạo Order + OrderDetail | ✅ | ✅ | `[x]` |
| BF-09 | **Checkout PayOS** — tạo link thanh toán, redirect | ✅ | ✅ | `[x]` |
| BF-10 | **PayOS webhook** — HMAC-SHA256 trên raw body, header `x-payos-signature`; `PAID` → update `Payments` + set status + gửi email hoá đơn | ✅ | ✅ | `[x]` |
| BF-11 | **Đặt lịch spa** — capacity 2 slot, bước 30 phút, chặn ca ≥19:00 và ≥22:00, `SemaphoreSlim`, rollback khi lỗi, wave 2-pet, `suggestedStart` | ✅ | ✅ | `[x]` — ⚠️ deadlock tiềm năng với self-HTTP |
| BF-12 | **Đánh giá** — sản phẩm (upsert) + dịch vụ (luôn tạo mới) | ✅ | ✅ | `[x]` |
| BF-13 | **Chấm công** — toggle check-in/out, IP allowlist | ✅ | ✅ | `[x]` |
| BF-14 | **Tính lương** — 7 method, validate `10..30 ca/tháng`, lương tối thiểu 3.000.000 | ✅ | ✅ | `[x]` |
| BF-15 | **Đổi ca / nhờ làm thay** — 11 endpoint, hoán đổi `StaffId`, dedupe slot | ✅ | ✅ | `[x]` |
| BF-16 | **Thông báo** — đọc bảng `ShiftRequest`, `ToNotified` làm cờ đã đọc | ✅ | ✅ | `[x]` |
| BF-17 | **Upload ảnh pet** — ≤5MB, `ContentType` bắt đầu `image/`, tên `pet_{Guid:N}{ext}`, URL `/uploads/pets/{fileName}` | ✅ | ✅ | `[x]` — ⚠️ thiếu thư mục (R2) |
| BF-18 | **Gửi email** — MailKit + SMTP `smtp.gmail.com:587` StartTls, Handlebars template | ✅ | ✅ | `[x]` — ⚠️ thiếu template (R1) |
| BF-19 | **Chatbot AI** — intent → context DB → system prompt → Gemini | ✅ | ✅ | `[x]` LS-08 — port đủ 4 method + 4 endpoint. ⚠️ Key Gemini đã bị Google báo **leaked** ⇒ cần rotate (Q13) |
| BF-20 | **Boarding** — đặt phòng, check-in/out | — | ✅ | `[x]` 🆕 |

---

## 10. LỚP 9 — EMAIL TEMPLATE

| # | Template nguồn | Đích | Trạng thái |
|---|---|---|---|
| ET-01 | `BackEnd/EmailTemplates/OrderInvoice.hbs` (1.402 B) | `wwwroot/EmailTemplates/OrderInvoice.hbs` | `[x]` LS-01 — **R1 đã xử lý**. Copy nguyên văn từ git nguồn (MD5 khớp) |
| ET-02 | `BackEnd/EmailTemplates/SpaInvoice.hbs` (1.918 B) | `wwwroot/EmailTemplates/SpaInvoice.hbs` | `[x]` LS-01 — **R1 đã xử lý**. Copy nguyên văn từ git nguồn (MD5 khớp) |
| ET-03 | — (đích gọi nhưng nguồn không có) | `wwwroot/EmailTemplates/SpaCancellation.hbs` | `[x]` LS-01 — dựng mới theo đúng contract của `EmailService.SendSpaInvoiceEmailAsync` (nhánh `statusIsCancelled`): biến `{{CustomerName}}`, `{{BookingCode}}`, `{{StatusLabel}}`, `{{StatusClass}}`, `{{#each Items}}` (`{{ItemName}}`,`{{Quantity}}`,`{{UnitPrice}}`,`{{LineTotal}}`), `{{TotalAmount}}` |
| ET-04 | — (đích gọi nhưng nguồn không có) | `wwwroot/EmailTemplates/BoardingInvoice.hbs` | `[x]` LS-01 — dựng mới theo đúng contract của `EmailService.SendBoardingInvoiceEmailAsync`: `{{CustomerName}}`, `{{BookingCode}}`, `{{RoomDisplay}}`, `{{CheckIn}}`, `{{CheckOut}}`, `{{Days}}`, `{{Pets}}`, `{{Notes}}`, `{{PricePerDay}}`, `{{TotalPrice}}`, `{{PaymentMethodLabel}}`, `{{PaymentStatusLabel}}`, `{{PaymentStatusClass}}` |

---

## 11. LỚP 10 — CÔNG NGHỆ SLIDE CHƯA LÀM

| # | Yêu cầu slide | Slide | Đích | Trạng thái |
|---|---|---|---|---|
| SL-01 | **gRPC** — `Grpc.AspNetCore` + `Grpc.Tools`, file `.proto` proto3, service + client, 4 loại RPC (unary/server-stream/client-stream/bidi) | Ch09 (toàn bộ) | ❌ | `[ ]` LS-09 |
| SL-02 | **JavaScript client** — jQuery `$.ajax()`, `$.get()`, `$.post()`, `XMLHttpRequest`, HTML demo CRUD (Create/Read/Update/Delete) | Ch07 | ❌ | `[ ]` LS-10 — cần thêm POST/PUT/DELETE cho `ProductsController` (X-01..X-08) |
| SL-03 | **Docker** — `Dockerfile` multi-stage, `docker build`, `docker run`, `docker images` | Ch10 S20–30 | ❌ | `[ ]` LS-12 |
| SL-04 | **Input formatter** (custom) — `TextInputFormatter`, override `CanReadType`/`ReadRequestBodyAsync` | Ch03 S17 | ❌ | `[ ]` LS-10 — chung với SL-02 |
| SL-05 | **Custom validation attribute** — kế thừa `ValidationAttribute`, override `IsValid(value, ValidationContext)` | Ch05 S28 | ❌ | `[ ]` LS-13 |
| SL-06 | **`[ProducesResponseType]`** trên mọi action (mô tả status code trong Swagger) | Ch01 S27 | ❌ | `[ ]` LS-13 |
| SL-07 | **Refresh token** — `POST /api/account/refreshtoken` | Ch06 S40, S42 | ❌ | `[!]` Q2 |
| SL-08 | **AutoMapper** — `Profile` class | Ch02 S23, Ch05 S43 | ❌ (map tay) | `[!]` Q3 |
| SL-09 | **Model binding source attributes** — `[FromBody]`, `[FromForm]`, `[FromHeader]`, `[FromQuery]`, `[FromRoute]` | Ch05 S17–19 | ✅ phần lớn | `[x]` |
| SL-10 | **DataAnnotations** — `[Required]`, `[EmailAddress]`, `[StringLength]`, `[Range]`, `[Compare]`, `[RegularExpression]` | Ch05 S26–27 | ✅ | `[x]` |
| SL-11 | **`[ValidateNever]`** | Ch05 S27 | ❌ | `[ ]` LS-13 |
| SL-12 | **Client-side validation** (jQuery Validate) | Ch05 S30 | ⚠️ Blazor `EditForm` server-side | `[x]` — tương đương chức năng |
| SL-13 | **`ModelState.ClearValidationState()` + `TryValidateModel`** (rerun validation) | Ch05 S25 | ❌ | `[ ]` LS-13 |
| SL-14 | **WCF / CoreWCF** | Ch08 | ❌ | ❌ **không áp dụng** — có lý do ghi rõ trong `ARCHITECTURE_REQUIREMENTS.md` |
| SL-15 | **Microservices tách thật / event bus / Azure Service Fabric** | Ch10 | ❌ | ❌ **không áp dụng** — có lý do ghi rõ |
| SL-16 | **OData batch / actions / functions** | Ch04 S25, S8 | ❌ | ❌ không có trong OData 9 ASP.NET Core |

---

## 12. LỚP 11 — TEST & KIỂM CHỨNG

| # | Hạng mục | Nguồn | Đích | Trạng thái |
|---|---|---|---|---|
| TS-01 | Unit test — Attendance, Auth, JwtToken, WorkScheduleDedup | — | `tests/PetShop.Tests/` (30 test) | `[x]` — 30/30 pass |
| TS-02 | Test chatbot service | — | ❌ | `[ ]` LS-08 — **chưa viết unit test** cho `ChatbotService`. Đã smoke test thủ công 4 endpoint (xem §6.9) |
| TS-03 | Test PayOS signature verify | — | ❌ | `[ ]` LS-07 |
| TS-04 | Contract test — đối chiếu từng endpoint PARITY với bản gốc | — | ❌ | `[ ]` LS-14 |
| TS-05 | `dotnet build` 0 error | — | ✅ | `[x]` — build pass |
| TS-06 | Chạy app thật, `localhost:5286` trả 200 | — | ✅ | `[x]` — verified |
| TS-07 | Google OAuth chạy được trên localhost | — | ❌ | `[!]` **R6** — cần `ClientId` thật + `RedirectUri` khớp port |
| TS-08 | `dotnet run` thành công | — | ✅ | `[x]` — `Now listening on: http://localhost:5286` |
| TS-09 | PayOS flow end-to-end (tạo link → webhook → email) | — | ⚠️ | `[ ]` LS-07 — cần môi trường PayOS thật |
| TS-10 | Rà soát: không còn tham chiếu Node/npm | — | ✅ | `[x]` — không còn `package.json` trong đích |

---

## 13. LỘ TRÌNH LÁT CẮT (VERTICAL SLICE)

> Thứ tự đề xuất: **nền tảng → user/auth → nghiệp vụ lõi → thanh toán → chatbot/realtime → phần còn lại**

| Lát cắt | Tên | Nội dung | Phụ thuộc | Ước lượng |
|---|---|---|---|---|
| **LS-01** | Nền tảng asset & email | Copy `EmailTemplates/*.hbs` → `wwwroot/EmailTemplates/`; copy `wwwroot/uploads/pets/*` (15 file); tạo `SpaCancellation.hbs` + `BoardingInvoice.hbs`; cập nhật `appsettings.example.json` | — | 1h |
| **LS-02** | OData chuẩn hoá | Rà soát EDM, `SetMaxTop`, `$count` path segment, `$metadata` | — | 1h |
| **LS-03** | Sửa lỗi MVC cart & routing | Thêm `CartController.Index` + `Views/Cart/Index.cshtml`; bỏ route `mvc/{controller=ProductsPage}` hoặc thêm `ProductsPageController` | — | 2h |
| **LS-04** | API Product/PetService đầy đủ CRUD | Thêm POST/PUT/DELETE + upload + search cho `ProductsController`; POST + upload cho `PetServicesController` (X-01..X-10) | LS-03 | ✅ **XONG** — 10/10 endpoint, 3-layer, build 0 error/0 warning, 30/30 test, smoke 10/10 200 |
| **LS-05** | Asset/CSS/JS đồng bộ | Copy 144 ảnh đã có ✅; thêm `logo-pet-toy.png`, `ziggy-hero-reference.png`; đối chiếu 14 CSS; xử lý Font Awesome + `react-icons`; bỏ nền tối Vite | LS-01 | 4h |
| **LS-06** | Auth & Google OAuth | Hoàn thiện `ClientId` + `RedirectUri` khớp `:5286`; kiểm tra flow đăng nhập/OTP/reset; role `doctor` | — | 3h |
| **LS-07** | PayOS end-to-end | Khôi phục fallback URL; verify webhook; `GetPaymentStatusAsync`; chống client tự PATCH status | LS-06 | 3h |
| **LS-08** | **Chatbot AI** | ✅ **ĐÃ XONG** — Port `ChatbotService` (4 method, 157 message nguyên văn) + `GeminiProviderService` + `ChatbotController` (4 endpoint) + 2 model + 2 interface + `ChatbotRepository`/`IChatbotRepository` (3-layer); khối `Chatbot` trong config. **KHÔNG port `OllamaProviderService`** (chỉ Gemini theo quyết định chủ dự án) — đã loại nhánh fallback Ollama | LS-06 | ✅ 0 error/0 warning, 30/30 test pass, 4 endpoint smoke test OK |
| **LS-09** | **gRPC** (Ch09) | `Grpc.AspNetCore` + `Grpc.Tools`; `Protos/pets.proto` (proto3); `PetCatalogService` (unary + server-stream); client demo | LS-02 | 5h |
| **LS-10** | **JS client demo** (Ch07) | MVC view demo + `wwwroot/lib/jquery/`; CRUD qua `$.ajax()` + `XMLHttpRequest`; custom input formatter | LS-04 | 4h |
| **LS-11** | Realtime | SignalR hub cho notification/chat (nếu slide yêu cầu — **chưa xác nhận**) | LS-08 | 3h |
| **LS-12** | **Docker** + dọn gap UI | `Dockerfile` + `.dockerignore` + `docker-compose.yml`; thêm route `/about`, `/profile`; component `ClearSearchButton`, `AddressModal`, `NotificationBell`; `daNangData`; `SpaBookingDetailPage` | LS-05 | ⚠️ **1/2 XONG** — đã có `/about` + `/contact` + `/profile` (+ CSS `about.css`). **Còn lại:** Docker, `ClearSearchButton`, `AddressModal`, `NotificationBell`, `daNangData`, `SpaBookingDetailPage` |
| **LS-13** | Robustness 3-layer | 12 Razor component gọi raw SQL → chuyển qua Service/Repository; thêm `[ProducesResponseType]`; custom validation attribute; `[ValidateNever]`; rerun validation | LS-04 | 6h |
| **LS-14** | Kiểm chứng & tài liệu | Contract test từng dòng PARITY; `docs/PARITY_REPORT.md`; rà soát secret; dọn `docs/_inventory/` | Tất cả | 4h |

**Tổng ước lượng:** ~50 giờ.

---

## 14. CÂU HỎI CẦN USER QUYẾT ĐỊNH

> Xem chi tiết & khuyến nghị tại `docs/ARCHITECTURE_REQUIREMENTS.md` §6 và `docs/_inventory/ARCHITECT_DESIGN.md` phần F.

| # | Câu hỏi | Ảnh hưởng | Khuyến nghị |
|---|---|---|---|
| **Q1** | Google OAuth: `ClientId` thật là gì? Có thể đăng ký thêm `http://localhost:5286` vào OAuth client không? (Google cho tối đa 100 URI, 0 dòng code) | **R6** — chặn toàn bộ luồng Google login | Bắt buộc user cung cấp |
| **Q2** | Refresh token (slide Ch06) — làm hay không? | SL-07 | Làm tối giản nếu ưu tiên "khớp slide" |
| **Q3** | AutoMapper — dùng hay map tay? | SL-08 | Map tay (không slide nói bắt buộc) |
| **Q4** | Chatbot provider: Gemini + Ollama (fallback chain) hay chỉ 1? | SV-09..11 | ✅ **Đã chốt: chỉ Gemini** (chủ dự án). Đã loại nhánh fallback Ollama, không port `OllamaProviderService` |
| **Q5** | Chatbot có lưu lịch sử vào bảng `ChatMessages` không? | BF-19 | Có (bảng đã có sẵn) |
| **Q6** | SignalR có nằm trong phạm vi không? Slide **không** nhắc SignalR | LS-11 | **Không** — slide không đề cập |
| **Q7** | `CartController` MVC có `Views/` không? Nguồn không có thư mục `Views` ⇒ route `/cart` GET trả lỗi gì? | MC-01 | Xem Q8 |
| **Q8** | Các route/component "chết" ở nguồn (`/about` chưa có, `/profile`, `SpaBookingDetailPage` orphan, tab `breed-pricing` không truy cập được, 11 link trong Navigation không có route) — port nguyên văn hay sửa cho đúng? | U-09, U-26, U-28, CM-14..17 | Port nguyên văn (yêu cầu "giống 100%") |
| **Q9** | `Customer` nguồn có `birth_date`/`avatar_url` nhưng đích thiếu ⇒ profile UI không giống 100%. Cho phép đụng schema DB không? (ràng buộc 1 nói KHÔNG) | U-08, U-10 | Chấp nhận lệch, hoặc user cho phép thêm 2 cột |
| **Q10** | `daNangData.js` (8 quận + 100+ phường) — copy nguyên file `.js` vào `wwwroot/js/` hay viết lại C#? | CM-16 | Copy nguyên file `.js` |
| **Q11** | Google Maps embed key trong `daNangData.js` (`VITE_GOOGLE_MAPS_EMBED_API_KEY`) — có key thật không? | U-06 | User cung cấp hoặc bỏ iframe |
| **Q12** | Namespace `PetShop.*` giữ nguyên hay đổi? | Toàn bộ | Giữ nguyên |
| **Q13** | **Gemini API key trong `appsettings.json` đã bị Google báo LEAKED** (`403 PERMISSION_DENIED — "Your API key was reported as leaked"`). Key này y hệt trong repo nguồn OJT. User cấp key Gemini mới (hoặc xác nhận dùng key khác)? | LS-08 / C-07 / BF-19 | **Bắt buộc user cung cấp key mới** — hiện tại chatbot gọi được Gemini nhưng luôn nhận 403 ⇒ `source=fallback`, không có câu trả lời AI thật |
| **Q14** | Chatbot có cần **UI widget** (React/Blazor component) ở trang bán hàng không? Nguồn có widget chatbot gọi `/api/chatbot/message`; đích hiện **chưa có** UI nào gọi 4 endpoint này ⇒ API đã sẵn sàng nhưng chưa ai dùng | LS-08 / BF-19 | Cần user quyết định lát cắt UI riêng hay để sau LS-12 |
| **Q15** | **`/contact`**: nguồn OJT **KHÔNG có** page `/contact` (chỉ có link trong `Footer.jsx`) ⇒ không có bản gốc để port nguyên văn. Đích đã dựng `Contact.razor` từ thông tin liên hệ trong Footer (`123 Đường ABC, Quận 1, TP.HCM` / `0123 456 789` / `contact@pettoy.com`) + CSS `about-*`. Có cần thêm form liên hệ / bản đồ / giờ mở cửa không (nguồn không có)? | U-26b | **Cần user quyết** — hiện giữ đúng nguyên text Footer, không tự thêm nội dung mới |
| **Q16** | **Soft-delete `Products`**: cột `is_deleted` **đã có sẵn** trong schema đích (`SHOP_PET_Database.sql` + `database_script.txt`), nhưng **nguồn OJT không có** cột này (`Projectojt/BackEnd/Models/Product.cs` không có `IsDeleted`) ⇒ 3 endpoint `deleted`/`restore`/`permanent-delete` là **hành vi mới của đích**, không có bản gốc để mirror. Hiện đã implement soft-delete qua cột có sẵn (không sửa schema). Có cần đồng bộ ngược sang nguồn không? | X-03..X-06 | **Cần user quyết** — đã implement theo cột có sẵn, không đụng schema |

---

## 15. NHẬT KÝ TIẾN ĐỘ

| Ngày | Giai đoạn | Nội dung | Commit |
|---|---|---|---|
| 08/11/2026 | GĐ0 | Đọc 11 chapter slide (422 slide), trích text. Quét Projectojt (18 controller / 92 endpoint, 27 page React / 23 route, 144 asset). Kiểm kê đích (22 controller, 39 DbSet, 68 Razor). Khôi phục `appsettings.json` + `appsettings.Development.json` (bị xoá khỏi đĩa) — sửa mật khẩu DB `1234`→`123` sau khi test connect. Verify build 0 error + app chạy `:5286` trả 200. Hội đồng khắc nghiệt: 4 agent kiểm kê + 3 agent phản biện (kiến trúc sư trưởng / red team / thẩm định). | (chưa commit) |
| | GĐ1 | `docs/ARCHITECTURE_REQUIREMENTS.md` + `docs/PARITY.md` + `docs/PROGRESS.md` + `docs/agent.md` — **CHỜ DUYỆT** | — |
| 08/11/2026 | **LS-08** | **Chatbot AI — port stack từ OJT sang đích bằng C# thuần.** Tạo: `Models/ChatbotDtos.cs`, `Models/ChatbotOptions.cs`, `Models/ChatbotRepositoryDtos.cs`, `Interfaces/IAiProviderService.cs`, `Interfaces/IChatbotService.cs`, `Interfaces/IChatbotRepository.cs`, `Repositories/ChatbotRepository.cs`, `Services/GeminiProviderService.cs`, `Services/ChatbotService.cs`, `Controllers/ChatbotController.cs`. Sửa: `Program.cs` (DI), `appsettings.example.json` (khối `Chatbot`), `docs/PARITY.md`. **157 message tiếng Việt port nguyên văn, 0 đổi nội dung.** Truy vấn DB đi qua `IChatbotRepository` (3-layer, không inject DbContext vào service). **KHÔNG port `OllamaProviderService`** (chỉ Gemini) — đã loại nhánh fallback Ollama trong `ChatbotService`. Build **0 error / 0 warning**; `dotnet test` **30/30 pass**; smoke test 4 endpoint (`/api/chatbot/health|context/{id}|intent|message`) đều 200 với DB thật. ⚠️ Gemini key trong `appsettings.json` đã bị Google báo **leaked (403)** ⇒ chatbot trả `source=fallback` ⇒ cần user rotate key (Q13). | (chưa commit) |
| 08/11/2026 | **LS-04** | **Bổ sung 10 API CRUD còn thiếu (X-01..X-10)** — Controller → Service → Repository (3-layer, không inject DbContext vào Controller). Tạo: `Controllers/ProductsTempController.cs` (route `api/products-temp/*`, giữ nguyên `ProductsController` cũ route `api/products`), `Models/ProductDto.cs` (+`ProductCreateRequest`/`ProductUpdateRequest`/`ProductIdRequest`/`ProductActionResponse`), `Models/PetServiceDto.cs` (+`PetServiceCreateRequest`). Sửa: `Interfaces/IProductService.cs`, `IProductRepository.cs`, `IPetServiceService.cs`, `IPetServiceRepository.cs`, `Services/ProductService.cs`, `PetServiceService.cs`, `Repositories/ProductRepository.cs` (+7 method write/search), `Repositories/PetServiceRepository.cs` (+`CreateAsync`), `Controllers/PetServicesController.cs` (+POST + upload-image), `Models/PetService.cs` (+`ImageUrl` map cột `image_url` **đã có sẵn** trong schema), `Data/ShopPetDatabaseContext.cs` (+map `image_url`). Route/method/tên field khớp 100% `frontend/src/api/productsApi.js` + `petServicesApi.js`. Soft-delete dùng cột `Products.is_deleted` **đã có sẵn** ⇒ **không sửa DB schema**. Upload lưu `wwwroot/uploads/products/` + `wwwroot/uploads/pet-services/`, trả `{imageUrl}` / `{imageUrl,url}`. Build **0 error / 0 warning**; `dotnet test` **30/30 pass**; smoke test 10/10 endpoint trả 200 với DB thật (kèm vòng đời delete → deleted → restore → permanent-delete). | (chưa commit) |
| 08/11/2026 | **LS-12 (1/2)** | **Đóng gap UI R5 + U-09** — tạo `Components/Pages/About.razor` (`@page "/about"`, port **nguyên văn** nội dung `AboutPage.jsx`: HIGHLIGHTS/SERVICE_POINTS/STEPS + section hero/values/services/steps/faq/cta; dùng `TopBar`/`Header`/`Navigation`/`Footer`), `Components/Pages/Contact.razor` (`@page "/contact"`, lấy thông tin từ `Footer.razor` — nguồn **không có** page `/contact`), `wwwroot/css/template/about.css` (CSS `about-*` **không tồn tại ở cả nguồn lẫn đích** ⇒ viết mới). Sửa: `Components/App.razor` (+`<link ... href="css/template/about.css" />`), `Components/Pages/Account.razor` (+`@page "/profile"` ⇒ U-09), `docs/PARITY.md` (§6.8 X-01..X-10, §7.1 U-09/U-26, §7.3 CM-04/CM-20, §8.2 CS-18, §15). Build **0 error / 0 warning**; `dotnet test` **30/30 pass**; smoke test `/about`, `/contact`, `/profile` đều 200; đối chiếu tự động 18/18 chuỗi tiếng Việt của nguồn **hiện nguyên văn** trong HTML render (không tính HTML entity encode của Razor). | (chưa commit) |
