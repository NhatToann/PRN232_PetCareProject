# GAP AUDIT — Đối chiếu chi tiết OJT (Projectojt) → PRN232

> **Nguồn (chỉ đọc):** `Projectojt\BackEnd\` (+ `Projectojt\frontend\src\`)
> **Đích (chỉ đọc):** `PRN232_PetCareProject\`
> **Phương pháp:** đọc CODE THẬT từng file, so khớp từng `public method`, từng `public property`, từng chuỗi string literal tiếng Việt, hash MD5 từng file asset.
> **Ngày thực hiện:** 2026-11-08
> **Trạng thái:** CHỈ ĐỌC — không sửa code nguồn hay đích.

---

## TÓM TẮT ĐIỀU HÀNH

| # | Hạng mục đối chiếu | Kết quả |
|---|---|---|
| 1 | Repository / Service (từng cặp) | 5 file Repo nguồn (không phải 6) + 11 file Service nguồn (không phải 14) → **3 file Service nguồn KHÔNG CÓ file đích** |
| 2 | Entity / DTO | 60 model nguồn vs 64 model đích → **4 model nguồn thiếu**, 8 model chỉ có ở đích |
| 3 | Validation message | 325 chuỗi tiếng Việt ở nguồn → **157 chuỗi thiếu nguyên văn** (toàn bộ thuộc ChatbotService) |
| 4 | Email template | 2 file `.hbs` nguồn → **0 file `.hbs` ở đích** (runtime `FileNotFoundException`) |
| 5 | Upload ảnh pet | Cùng logic + cùng URL `/uploads/pets/{fileName}` → **thư mục `wwwroot/uploads/pets/` và 15 file ảnh đã upload KHÔNG có ở đích** |
| 6 | Asset hình ảnh | 144 file nguồn → **144/144 có ở đích, MD5 khớp 100%** ✅ |

**Tổng số gap phát hiện: 187**
— Ẩn nghiêm trọng (chặn runtime): 6 · Cao: 9 · Trung bình: 8 · Thấp/khác: 164

---

## 1. ĐỐI CHIẾU REPOSITORY / SERVICE (TỪNG CẶP, TỪNG PUBLIC METHOD)

### 1.1 Bảng đối chiếu Repositories

> **Lưu ý về yêu cầu:** đề bài nói "6 file trong `Repositories\`" và "14 file trong `Services\`", nhưng kiểm tra thực tế filesystem cho thấy nguồn chỉ có **5 file Repository** và **11 file Service**. Bảng dưới đây bám theo **thực tế đã đếm**.

| Nguồn (`Projectojt\BackEnd\Repositories\`) | Đích (`PRN232_PetCareProject\Repositories\`) | Số method nguồn | Số method đích | Trạng thái |
|---|---|---|---|---|
| `AuthRepository.cs` | `AuthRepository.cs` | 7 | 8 | ✅ **Đầy đủ** — đích có **thêm** `UpgradePasswordHashAsync(email, newHash, ct)` (migrate-on-login plaintext→BCrypt). Không mất method nào. |
| `PetServiceRepository.cs` | `PetServiceRepository.cs` | 2 | 2 | ✅ **Đầy đủ** — thân hàm thức giống từng dòng (`GetAllAsync`, `GetDetailAsync`). |
| `ProductRepository_temp.cs` | `ProductRepository.cs` | 3 | 3 | ✅ **Đầy đủ** — đổi tên class bỏ hậu tố `_temp` (`ProductRepository_temp`→`ProductRepository`, DTO tương ứng `_temp`→thường). **Hành vi KHÁC:** đích `GetAllAsync` và `GetByIdAsync` **bổ sung** tính `AvgRating` + `ReviewCount` từ bảng `Reviews` (nguồn trả `0`/null). Nguồn `ProductDto_temp` có **10 field**, đích `ProductDto` có **12 field**. |
| `ReviewRepository.cs` | `ReviewRepository.cs` | 3 | 3 | ✅ **Đầy đủ** — thân hàm giống hệt, **kể cả 5 chuỗi validation tiếng Việt** (`"Không tìm thấy đơn hàng hợp lệ."`, `"Chỉ có thể đánh giá khi đơn hàng đã hoàn thành."`, `"Sản phẩm không thuộc đơn hàng này."`, `"Số sao không hợp lệ (1-5)."`, `"Đã ghi nhận đánh giá."`). |
| `SpaBookingRepository.cs` | `SpaBookingRepository.cs` | 16 | 16 | ✅ **Đầy đủ** — 16/16 method khớp tên + hành vi. |

**Repository đích có thêm (không có ở nguồn):**

| File đích | Ghi chú |
|---|---|
| `Repositories\BoardingRepository.cs` | Feature **Boarding** (đặt phòng) — mới, không có ở nguồn. Hợp lệ vì đích bổ sung nghiệp vụ. |

**⇒ Không có Repository nào "chưa có file" ở đích. 5/5 file nguồn đều có file tương đương.**

### 1.2 Bảng đối chiếu Services

| Nguồn (`Projectojt\BackEnd\Services\`) | Đích (`PRN232_PetCareProject\Services\`) | Method nguồn | Method đích | Trạng thái |
|---|---|---|---|---|
| `AttendanceService.cs` | `AttendanceService.cs` | 6 | 6 | ✅ **Đầy đủ** — thân hàm **giống từng dòng**, kể cả 4 message (`"Hôm nay bạn không có ca làm."`, `"Bạn đến quá sớm..."`, `"Bạn đã hoàn thành ca hôm nay."`, `"Check-in thành công!"`). Chỉ khác: `using PetShop.Helpers` → `using PetShop.Services` (helper `WorkScheduleDedup` được tách ra file riêng ở đích). |
| `AuthService.cs` | `AuthService.cs` + `AuthService.Google.cs` | 6 | 5+1 | ✅ **Đầy đủ (split partial)** — `LoginWithGoogleAsync` được tách sang `AuthService.Google.cs`; **6 message Google giữ nguyên văn**. ⚠️ **KHÁC HÀNH VI:** đích thay `IsPasswordValid()` bằng `VerifyAndUpgradeAsync()` — khi mật khẩu lưu plaintext sẽ **tự động hash BCrypt và ghi đè DB** (migrate-on-login). Đích cũng bỏ `GetGoogleProfileFromAccessTokenAsync` dùng `new HttpClient()` → dùng `IHttpClientFactory`. |
| `ChatbotService.cs` | **— KHÔNG CÓ —** | 4 | 0 | ❌ **CHƯA CÓ FILE** — mất 4 method: `GetResponseAsync`, `BuildSystemPromptAsync`, `GetContextAsync`, `AnalyzeIntentWithAi`. Xem GAP-01. |
| `EmailService.cs` | `EmailService.cs` | 4 | 5 | ✅ **Đầy đủ + mở rộng** — 4 method nguồn giữ nguyên message. Đích thêm `SendBoardingInvoiceEmailAsync`. ⚠️ **KHÁC HÀNH VI:** đích đổi đường dẫn template từ `EmailTemplates/...` → `wwwroot/EmailTemplates/...` (**thư mục không tồn tại** → Xem GAP-03). `SendSpaInvoiceEmailAsync` đổi signature: thêm `string paymentStatusLabel`. |
| `GeminiProviderService.cs` | **— KHÔNG CÓ —** | 1 | 0 | ❌ **CHƯA CÓ FILE** — mất `GenerateTextAsync` + toàn bộ provider Gemini. Xem GAP-01. |
| `OllamaProviderService.cs` | **— KHÔNG CÓ —** | 1 | 0 | ❌ **CHƯA CÓ FILE** — mất `GenerateTextAsync` + toàn bộ provider Ollama. Xem GAP-01. |
| `PayOSService.cs` | `PayOSService.cs` + `PayOSService.Fulfillment.cs` | 6 | 8 | ⚠️ **KHÁC HÀNH VI (quan trọng)** — 6 method đều có mặt, đích thêm `GetPaymentStatusAsync` + `HandleWebhookForStatusAsync`. **NHƯNG:** nguồn `CreatePaymentLinkAsync` **có fallback URL** `https://api.payos.vn/v2` khi `HttpRequestException`; đích **ĐÃ XÓA fallback** → trả `null` ngay. Xem GAP-06. |
| `PayrollService.cs` | `PayrollService.cs` | 7 | 7 | ✅ **Đầy đủ** — 7/7 method khớp, message giữ nguyên (`"Khong tim thay nhan vien."`, `"Luong thang toi thieu la 3.000.000 VND."`, `"So ca chuan phai tu 10 den 30 ca/thang."`, …). Chỉ khác `Console.WriteLine` → `ILogger`. |
| `PetServiceService.cs` | `PetServiceService.cs` | 2 | 2 | ✅ **Đầy đủ** — thân hàm giống hệt (chỉ delegate 2 lần). |
| `ProductService_temp.cs` | `ProductService.cs` | 3 | 3 | ✅ **Đầy đủ** — đổi tên bỏ `_temp`. Thân hàm giống hệt. |
| `SpaBookingService.cs` | `SpaBookingService.cs` + `SpaBookingService.Booking.cs` | 11 | 5+6 | ✅ **Đầy đủ (split partial)** — 11/11 method khớp tên, **logic giống từng dòng** (semaphore `SchedulingLock`, rollback `DeleteBookingsAsync`, wave 2-pet, `AlignToSlot` 30 phút, `maxChecks = 96`). 5 message (`"Khung giờ đã đủ 2 slot."`, `"Rating phải từ 1 đến 5."`, `"Bạn chưa hoàn thành dịch vụ này."`, `"Trạng thái không hợp lệ."`, `"Không tìm thấy booking để cập nhật trạng thái."`) giữ **nguyên văn** (chuyển sang `SpaBookingService.Booking.cs`). |

**Service đích có thêm (không có ở nguồn) — không phải gap:**

`ApiExceptionMiddleware.cs`, `AuthClient.cs`, `BoardingService.cs`, `CartStateService.cs`, `CheckoutClient.cs`, `CsvOutputFormatter.cs`, `JwtTokenService.cs`, `LoggerExtensions.cs`, `MvcBuilderExtensions.cs`, `OrderClient.cs`, `PaymentClient.cs`, `PayOSService.Fulfillment.cs`, `PetsClient.cs`, `PetServiceClient.cs`, `ProductCatalogClient.cs`, `ReviewClient.cs`, `SelfHttpClientExtensions.cs`, `SessionHeaderAuthenticationHandler.cs`, `SpaAuthHeaderHandler.cs`, `SpaBookingClient.cs`, `StaffDashboardService.cs`, `StaffProfileService.cs`, `StaffService.cs`, `UserSessionService.cs`, `WorkScheduleDedup.cs`, `XmlOutputFormatter.cs`

> **Ghi chú kiến trúc:** đích **mở rộng kiến trúc** — thêm lớp `*Client` (self-HTTP client) để tách nghiệp vụ, thêm feature Boarding / Staff Dashboard / JWT. Đây là **mở rộng**, không phải sai lệch so với yêu cầu "giữ nguyên bản gốc".

### HÀNH ĐỘNG CẦN LÀM — Phần 1

- [ ] **GAP-01** — Port lại toàn bộ feature Chatbot/AI: `ChatbotService.cs` (4 method), `GeminiProviderService.cs` (1 method), `OllamaProviderService.cs` (1 method), `ChatbotController.cs`, `IChatbotService.cs`, `IAiProviderService.cs`, `ChatbotOptions.cs`, `ChatbotDtos.cs`
- [ ] **GAP-02** — Khôi phục fallback URL PayOS trong `PayOSService.CreatePaymentLinkAsync` (nguồn fallback `https://api.payos.vn/v2`)
- [ ] **GAP-03** — Copy `EmailTemplates/*.hbs` sang `wwwroot/EmailTemplates/` và tạo thêm `SpaCancellation.hbs` + `BoardingInvoice.hbs`
- [ ] **GAP-04** — Quyết định giữ `VerifyAndUpgradeAsync` (migrate-on-login) hay đưa về `IsPasswordValid` — ghi rõ trong tài liệu vì đây là **thay đổi hành vi DB**
- [ ] **GAP-05** — Xác nhận `ProductDto` mở rộng 2 field `AvgRating`/`ReviewCount` là chủ ý (không phải gap)

---

## 2. ĐỐI CHIẾU ENTITY / DTO

**Số liệu:** nguồn **60** file model, đích **64** file model.

### 2.1 Model nguồn THIẾU ở đích (4 file)

| Model nguồn | Loại nội dung | Trạng thái ở đích | Ghi chú |
|---|---|---|---|
| `Models\ChatbotDtos.cs` | DTO chatbot (chat history, intent, context) | ❌ **THIẾU** | Phụ thuộc `IChatbotService` — mất luôn |
| `Models\ChatbotOptions.cs` | Options cấu hình AI (`Providers.gemini`, `Providers.ollama`) | ❌ **THIẾU** | Bị bind bởi `IOptions<ChatbotOptions>` |
| `Models\ProductDto_temp.cs` | `record ProductDto_temp(10 field)` | ✅ **ĐÃ ĐỔI TÊN** → `Models\ProductDto.cs` | Đích thêm 2 field `AvgRating`, `ReviewCount`. Không mất field nào của nguồn. |
| `Models\ProductCategoryFilterDto_temp.cs` | `record ProductCategoryFilterDto_temp(3 field)` | ✅ **ĐÃ ĐỔI TÊN** → `Models\ProductCategoryFilterDto.cs` | Giữ nguyên 3 field. |

> ⇒ **2 model thực sự mất** (đều thuộc Chatbot), **2 model chỉ đổi tên** (đã resolve).

### 2.2 Model CHỈ CÓ ở đích (8 file — tính năng mới, không phải gap)

| Model đích | Nội dung |
|---|---|
| `Models\BoardingDtos.cs` | DTO đặt phòng boarding |
| `Models\FrontendDtos.cs` | `FrontendCartItem`, `FrontendOrderDetail`, `FrontendApiResult<T>` — thay cho các DTO `_temp` |
| `Models\PetShopEdmModel.cs` | Model EDM để xuất CSV/XML |
| `Models\ProductCatalogResult.cs` | Kết quả phân trang catalog sản phẩm |
| `Models\ProductCategoryFilterDto.cs` | Bản đổi tên của `..._temp` |
| `Models\ProductDto.cs` | Bản đổi tên + mở rộng của `ProductDto_temp` |
| `Models\SpaBookingStatus.cs` | Hằng số trạng thái booking spa |
| `Models\StaffDtos.cs` | DTO quản lý nhân viên / dashboard |

### 2.3 So khớp chi tiết 56 model dùng chung

| Kiểm tra | Kết quả |
|---|---|
| Khác biệt khai báo type | Chỉ 3 file: `AuthDtos.cs`, `PayOSDtos.cs`, `PayrollDtos.cs` (xem bên dưới) |
| Khác biệt public property | Chỉ 1 file: `Models\Product.cs` — đích **thêm** `IsDeleted` (`[Column("is_deleted")]`) |
| 53 model còn lại | ✅ Property/type khớp 100% (kích thước file lớn hơn ở đích chỉ vì namespace dùng block-scoped `{}` + `using` + format convention) |

**Chi tiết 3 file khác type:**

| File | Nguồn có | Đích có | Đánh giá |
|---|---|---|---|
| `AuthDtos.cs` | `AuthResponseDto, GoogleLoginRequestDto, LoginRequestDto, RegisterRequestDto, ResetPasswordRequestDto, SendEmailOtpRequestDto, SimpleMessageDto, VerifyEmailOtpRequestDto` | + `CurrentUserDto`, `TokenResponseDto` | ✅ Mở rộng — không mất |
| `PayOSDtos.cs` | `CreatePayOSPaymentRequest, PayOSCheckoutResult, PayOSWebhookData, PayOSWebhookPayload, PayOSWebhookResult` | + `PayOSPaymentStatusResult` | ✅ Mở rộng — không mất |
| `PayrollDtos.cs` | `... GeneratePayrollDto, PayrollResultDto, ...` | **thiếu `GeneratePayrollDto`**, **thiếu `PayrollResultDto`** | ⚠️ **Cần xác nhận** — 2 record này có thể đã được hợp nhất vào `PayrollListResultDto`/`PayrollConfigResultDto`. Kiểm tra tại `PayrollController` xem có endpoint nào còn trả về `GeneratePayrollDto`/`PayrollResultDto` không. |

### HÀNH ĐỘNG CẦN LÀM — Phần 2

- [ ] **GAP-06** — Xác minh `GeneratePayrollDto` và `PayrollResultDto` đã bị thay thế hợp lệ, không còn endpoint nào trả về 2 type này (đối chiếu `PayrollController.cs` ở đích)
- [ ] **GAP-07** — Xác nhận `Product.IsDeleted` là chủ ý (không phải mất property) — đối chiếu với logic soft-delete trong `ProductRepository`/`ShopPetDatabaseContext`
- [ ] Port `ChatbotDtos.cs` + `ChatbotOptions.cs` cùng GAP-01

---

## 3. ĐỐI CHIẾU VALIDATION MESSAGE (YÊU CẦU CỨNG: "KHÔNG tự đổi")

**Phương pháp:** trích xuất **mọi** string literal chứa ký tự tiếng Việt (`\u00C0-\u00D6`, `\u00D8-\u00F6`, `\u00F8-\u1EF9`) trong toàn bộ `.cs` của nguồn (`BackEnd\`) và của đích, so khớp **nguyên văn** (byte-for-byte, phân biệt hoa/thường và khoảng trắng).

**Kết quả tổng:**
- Nguồn: **325** chuỗi tiếng Việt duy nhất
- Đích: **471** chuỗi tiếng Việt duy nhất
- **Thiếu nguyên văn: 157**
- **Đổi nội dung (giữ nguyên ý): 0**
- Chỉ có ở đích: 303 (thuộc feature mới: Boarding, Staff Dashboard, JWT, Razor pages, OData)

### 3.1 Bảng chi tiết theo file

| File nguồn | Số message nguồn | Giữ nguyên văn | Thiếu | Đổi nội dung |
|---|---|---|---|---|
| `Services\ChatbotService.cs` | 157 | 0 | **157** | 0 |
| `Services\ShiftExchangeController.cs` | 32 | 32 ✅ (chuyển sang `ShiftExchangeController.Requests.cs` + `.Processing.cs`) | 0 | 0 |
| `Services\SpaBookingService.cs` | 27 | 27 ✅ (chuyển sang `SpaBookingService.Booking.cs`) | 0 | 0 |
| `Services\AuthService.cs` | 26 | 26 ✅ (chuyển sang `AuthService.Google.cs`) | 0 | 0 |
| `Services\PayOSService.cs` | 3 | 3 ✅ | 0 | 0 |
| `Repositories\ReviewRepository.cs` | 5 | 5 ✅ | 0 | 0 |
| `Services\AttendanceService.cs` | 8 | 8 ✅ | 0 | 0 |
| `Services\PayrollService.cs` | 14 | 14 ✅ | 0 | 0 |
| `Services\EmailService.cs` | 9 | 9 ✅ | 0 | 0 |
| `Controllers\PetsController.cs` | 3 | 3 ✅ | 0 | 0 |
| `Controllers\CartController.cs` | 4 | 4 ✅ | 0 | 0 |
| `Controllers\CheckoutController_temp.cs` | ~10 | 10 ✅ (→ `CheckoutController.cs`) | 0 | 0 |
| `Controllers\OrdersController_temp.cs` | ~5 | 5 ✅ (→ `OrdersController.cs`) | 0 | 0 |
| `Controllers\ProductsController_temp.cs` | ~8 | 8 ✅ (→ `ProductsController.cs`) | 0 | 0 |

### 3.2 157 message bị THIẾU — toàn bộ nằm trong `ChatbotService.cs`

Đây là **hệ quả trực tiếp** của GAP-01 (thiếu cả file service). Nhóm theo mục đích:

| Nhóm | Số lượng | Ví dụ (nguyên văn nguồn) |
|---|---|---|
| System prompt / quy tắc trả lời | ~35 | `"- KHÔNG ĐƯỢC NÓI 'tôi không biết' nếu thông tin đã có trong context"`, `"[QUY TẮC TRẢ LỜI THẲNG MINH:]"` |
| Định dạng dữ liệu context | ~30 | `"[DANH SÁCH SẢN PHẨM (từ database):]"`, `"[PHÒNG LƯU TRŨ (Boarding):]"` |
| Từ khoá nhận diện intent (stop-words) | ~70 | `["bạn"]`, `["giá"]`, `["đặt"]`, `["hàng"]`, `["dịch vụ"]`, `["pet"]`… (mảng điều kiện tiếng Việt) |
| Tin nhắn lỗi thân thiện | ~12 | `"Xin lỗi, hiện tại cửa hàng chưa có sản phẩm phù hợp."`, `"Nếu không biết, thừa nhận và gợi ý liên hệ hotline"` |
| Danh sách ví dụ | ~10 | `"âœ… ĐÚNG: 'Cửa hàng có Thức ăn khô cho chó lớn tuổi - 200.000đ (Còn 60)'"` |

> **Không có message nào bị viết lại/đổi nội dung.** Toàn bộ 157 message đều **bị mất trắng** vì file không tồn tại.

### 3.3 303 message chỉ có ở đích (đều thuộc tính năng mới — không phải gap)

| Nhóm tính năng | File đích | Ví dụ message |
|---|---|---|
| Boarding (đặt phòng) | `Controllers\BoardingController.cs`, `Models\BoardingDtos.cs` | `"Số ngày không hợp lệ."`, `"Không tìm thấy khách hàng với ID: {request.CustomerId}. Vui lòng đăng nhập lại."` |
| Staff Dashboard | `Services\StaffDashboardService.cs` | `"Check-in thất bại: không tìm thấy ca với scheduleId={ScheduleId}."` |
| JWT / Auth mới | `Services\JwtTokenService.cs` | `"Chưa cấu hình JwtSettings:Key. Hãy đặt khóa ký (tối thiểu 32 ký tự)..."` |
| API error middleware | `Services\ApiExceptionMiddleware.cs` | `"Đã xảy ra lỗi không mong muốn. Vui lòng thử lại sau."` |
| Razor pages (Blazor) | `Components\Pages\*.razor.cs` | `"Bạn cần đăng nhập để đặt dịch vụ."`, `"Vui lòng chọn ngày và giờ."` |
| DTO validation mới | `Models\*Dtos.cs` | `"Mã nhân viên không hợp lệ."`, `"Định giá phải từ 1 đến 5 sao."` |
| Email test | `Controllers\EmailTestController.cs` | `"Email spa ({request.PaymentStatus}) đã được gửi đến {request.ToEmail}"` |

### 3.4 ⚠️ Cảnh báo bảo mật phát hiện trong quá trình trích xuất

`Projectojt\BackEnd\appsettings.json` chứa **Gemini API key còn sống dạng plaintext**:

```json
"gemini": {
  "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/models",
  "ApiKey": "AIzaSyBwieKdmFJVUir8JvGqoS_cZs4wFgD3eXo",
  "DefaultModel": "gemini-flash-latest",
  "TimeoutSeconds": 120
}
```

→ Đích **chưa** có section `Chatbot` trong `appsettings.json` (hiện chỉ có: `Logging, ConnectionStrings, AllowedHosts, GoogleAuth, Mail, PayOS, Attendance`). Khi port GAP-01 **không được** copy nguyên key này.

### HÀNH ĐỘNG CẦN LÀM — Phần 3

- [ ] **GAP-08** — Port lại 157 message của `ChatbotService.cs` **NGUYÊN VĂN** (không viết lại, không diễn giải lại) khi khôi phục GAP-01
- [ ] **GAP-09** — Viết regression test assert chuỗi message so sánh với bản gốc (chống hồi quy message) — hiện chưa có test nào guard yêu cầu cứng này
- [ ] **GAP-10** — **Rotate Gemini API key** `AIzaSyBwieKdmFJVUir8JvGqoS_cZs4wFgD3eXo` (đã lộ trong repo) và chuyển sang User Secrets / environment variable

---

## 4. ĐỐI CHIẾU EMAIL TEMPLATE

### 4.1 Bảng đối chiếu

| Template nguồn | Kích thước | Template đích tương đương | Kích thước | Nội dung giống? | Trạng thái |
|---|---|---|---|---|---|
| `Projectojt\BackEnd\EmailTemplates\OrderInvoice.hbs` | 1.402 B | `PRN232_PetCareProject\wwwroot\EmailTemplates\OrderInvoice.hbs` | **KHÔNG TỒN TẠI** | — | ❌ **THIẾU FILE** |
| `Projectojt\BackEnd\EmailTemplates\SpaInvoice.hbs` | 1.918 B | `PRN232_PetCareProject\wwwroot\EmailTemplates\SpaInvoice.hbs` | **KHÔNG TỒN TẠI** | — | ❌ **THIẾU FILE** |
| — | — | `wwwroot\EmailTemplates\SpaCancellation.hbs` (được gọi bởi `EmailService.SendSpaInvoiceEmailAsync` khi status = "Đã hủy") | **KHÔNG TỒN TẠI** | — | ❌ **THIẾU FILE** |
| — | — | `wwwroot\EmailTemplates\BoardingInvoice.hbs` (được gọi bởi `EmailService.SendBoardingInvoiceEmailAsync`) | **KHÔNG TỒN TẠI** | — | ❌ **THIẾU FILE** |

**Tổng: 4 template được code đích `LoadHandlebarsTemplate(...)` yêu cầu — 0 file tồn tại.**

### 4.2 Nội dung 2 template nguồn (tham chiếu để đối chiếu khi copy)

**`OrderInvoice.hbs`** — bảng "San pham/Dich vu", cột "So luong", "Don gia", "Thanh tien", tổng `Tong cong: {{TotalAmount}} VND`, chân trang `Neu can ho tro, vui long lien he bo phan CSKH cua PetShop.`
→ Dùng biến: `CustomerName, OrderCode, Items[] {ItemName, Quantity, UnitPrice, LineTotal}, TotalAmount`

**`SpaInvoice.hbs`** — HTML đầy đủ (`<!DOCTYPE html>` + CSS inline `.card/.total/.footer`), tiêu đề `Hoa don dich vu Spa`, `Ma booking: {{BookingCode}}`, tổng `Tong cong: {{TotalAmount}} đ`
→ Dùng biến: `CustomerName, BookingCode, Items[] {ItemName, Quantity, UnitPrice, LineTotal}, TotalAmount`

### 4.3 Hệ quả runtime

`EmailService.LoadHandlebarsTemplate` ném `FileNotFoundException($"Khong tim thay email template: {relativePath}")` khi file không tồn tại:

```csharp
private HandlebarsTemplate<object, object> LoadHandlebarsTemplate(string relativePath)
{
    var fullPath = Path.Combine(_environment.ContentRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(fullPath))
    {
        throw new FileNotFoundException($"Khong tim thay email template: {relativePath}");
    }
    ...
}
```

⇒ **Gửi email hóa đơn đơn hàng / spa / boarding sẽ CRASH** tại production. Đây là gap chặn runtime, không phải gap hình thức.

### HÀNH ĐỘNG CẦN LÀM — Phần 4

- [ ] **GAP-03** — Tạo thư mục `PRN232_PetCareProject\wwwroot\EmailTemplates\` và copy **nguyên văn** `OrderInvoice.hbs` + `SpaInvoice.hbs` từ nguồn
- [ ] **GAP-11** — Tạo mới `SpaCancellation.hbs` (biến: `CustomerName, BookingCode, StatusLabel, StatusClass, Items[], TotalAmount`)
- [ ] **GAP-12** — Tạo mới `BoardingInvoice.hbs` (biến: `CustomerName, BookingCode, RoomDisplay, CheckIn, CheckOut, Days, Pets, Notes, PricePerDay, TotalPrice, PaymentMethodLabel, PaymentStatusLabel, PaymentStatusClass`)
- [ ] **GAP-13** — Thêm `wwwroot/EmailTemplates/` vào `.csproj` dưới dạng `Content`/`None CopyToOutputDirectory` để file được publish lên `bin/`
- [ ] **GAP-14** — Thêm test: gọi `LoadHandlebarsTemplate` cho cả 4 path và assert không throw

---

## 5. ĐỐI CHIẾU UPLOAD ẢNH PET

### 5.1 Bảng đối chiếu logic upload

| Khía cạnh | Nguồn (`Projectojt\BackEnd\Controllers\PetsController.cs`) | Đích (`PRN232_PetCareProject\Controllers\PetsController.cs`) | Giống? |
|---|---|---|---|
| Endpoint | `[HttpPost("upload-image")]`, `[FromForm] IFormFile image` | `[HttpPost("upload-image")]`, `[FromForm] IFormFile image` | ✅ |
| Guard rỗng | `image is null \|\| image.Length == 0` → `"Vui lòng chọn ảnh hợp lệ."` | **giống hệt** | ✅ |
| Guard dung lượng | `> 5 * 1024 * 1024` → `"Ảnh vượt quá 5MB."` | **giống hệt** | ✅ |
| Guard MIME | `!contentType.StartsWith("image/")` → `"Chỉ cho phép tệp ảnh."` | **giống hệt** | ✅ |
| Thư mục lưu | `Path.Combine(webRoot, "uploads", "pets")` + `Directory.CreateDirectory` | **giống hệt** | ✅ |
| `webRoot` | `Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")` | **giống hệt** | ✅ |
| Tên file | `pet_{Guid.NewGuid():N}{safeExt}` | **giống hệt** | ✅ |
| Sanitize ext | `Regex.Replace(ext, @"[^a-zA-Z0-9\.]", "")`, mặc định `.jpg` | **giống hệt** | ✅ |
| **URL trả về** | `$"/uploads/pets/{fileName}"` | **`$"/uploads/pets/{fileName}"`** | ✅ **GIỐNG 100%** |

### 5.2 Vấn đề: thư mục upload không tồn tại ở đích

| Mục | Nguồn | Đích |
|---|---|---|
| `wwwroot/uploads/` | ✅ tồn tại (có `.gitkeep`) | ❌ **KHÔNG tồn tại** |
| `wwwroot/uploads/pets/` | ✅ tồn tại | ❌ **KHÔNG tồn tại** |
| Số file ảnh đã upload | **15 file** | **0 file** |

Cấu trúc thư mục đích hiện tại: `wwwroot/` chỉ có `bootstrap/`, `css/`, `images/`, `js/`, `app.css`, `favicon.png` — **không có `uploads/`**.

### 5.3 Danh sách 15 file ảnh đã upload ở nguồn (CẦN COPY)

| # | Tên file | Kích thước | Có trong DB đích? |
|---|---|---|---|
| 1 | `pet_07815bf1ae004066b4c1eb7921e0dffb.png` | 124.334 B | — |
| 2 | `pet_07e4be7a9ea842d1909f6d1b2cc4849a.jpg` | 108.959 B | ✅ **pet "abc"** |
| 3 | `pet_36a46fa9bfb14be285d502aecad7115a.png` | 120.039 B | — |
| 4 | `pet_40953e6cf1a94a5f917aba2226a542ff.jpg` | 240.575 B | — |
| 5 | `pet_47e2f670d7df4f8285695169ce2e1ce9.jpg` | 64.870 B | ✅ **pet "123"** |
| 6 | `pet_59c0c31712904c6884c1053be8eef88f.png` | 116.834 B | — |
| 7 | `pet_77556b30f4c948ff90a609613a1c0e2c.png` | 116.834 B | — |
| 8 | `pet_8234e6a0e7684348ba27e664657ec2de.jpg` | 240.575 B | ✅ **pet "thô"** |
| 9 | `pet_90c716419d9345a597e3c139360f48ce.jpg` | 64.870 B | ✅ **pet "bb"** |
| 10 | `pet_931c0d9e53c4468a9daf235af406de16.jpg` | 64.870 B | — |
| 11 | `pet_9e694beb27c2424fbeded24e3c18a1af.jpg` | 64.232 B | — |
| 12 | `pet_a5e71ff720b8471e961f45d442038c1f.png` | 127.979 B | — |
| 13 | `pet_c058588376a74fe8a89c0183be6f7c04.png` | 116.834 B | — |
| 14 | `pet_dd81d89b956040c4b69efa04a7fdeb53.jpg` | 64.232 B | ✅ **pet "cc"** |
| 15 | `pet_fce73b50ea544b7d91ac8e3d8eeebddd.png` | 120.039 B | — |

**5 record trong DB đích (`database_script.txt`) trỏ tới `/uploads/pets/...`** → ảnh hiện tại **404 broken image** trên UI:

| Pet | `image_path` trong DB |
|---|---|
| abc | `/uploads/pets/pet_07e4be7a9ea842d1909f6d1b2cc4849a.jpg` |
| thô | `/uploads/pets/pet_8234e6a0e7684348ba27e664657ec2de.jpg` |
| 123 | `/uploads/pets/pet_47e2f670d7df4f8285695169ce2e1ce9.jpg` |
| cc | `/uploads/pets/pet_dd81d89b956040c4b69efa04a7fdeb53.jpg` |
| bb | `/uploads/pets/pet_90c716419d9345a597e3c139360f48ce.jpg` |

### HÀNH ĐỘNG CẦN LÀM — Phần 5

- [ ] **GAP-15** — Tạo `PRN232_PetCareProject\wwwroot\uploads\pets\` (kèm `.gitkeep` như nguồn)
- [ ] **GAP-16** — Copy **toàn bộ 15 file** `pet_*` từ `Projectojt\BackEnd\wwwroot\uploads\pets\` sang đích (bắt buộc copy cả 10 file không có trong DB hiện tại, vì dữ liệu production có thể chứa chúng)
- [ ] **GAP-17** — Xác minh `PhysicalFile` / static-file middleware phục vụ `/uploads/...` ở đích (kiểm tra `Program.cs` có `app.UseStaticFiles()` và `UseStaticFiles` trả đúng `Content-Type` cho `.png`/`.jpg`)
- [ ] **GAP-18** — Thêm 5 file ảnh vào `.csproj` (`Content` + `CopyToOutputDirectory="PreserveNewest"`) hoặc cấu hình publish để không bị mất khi publish

---

## 6. ĐỐI CHIẾU ASSET HÌNH ẢNH (YÊU CẦU CỨNG: "GIỐNG 100%")

### 6.1 Kết quả

| Chỉ số | Kết quả |
|---|---|
| Nguồn `Projectojt\frontend\src\images\` | **144 file** |
| Đích `PRN232_PetCareProject\wwwroot\images\` | **148 file** |
| **File nguồn THIẾU ở đích (theo đường dẫn)** | **0** ✅ |
| **File nguồn THIẾU ở đích (theo tên file)** | **0** ✅ |
| File cùng đường dẫn nhưng **khác kích thước** | **0** ✅ |
| File cùng đường dẫn nhưng **khác MD5 hash** | **0** ✅ |
| File CHỈ CÓ ở đích | **4** (không phải gap — asset mới) |

### 6.2 Bảng 4 file chỉ có ở đích

| File đích | Kích thước | Ghi chú |
|---|---|---|
| `42c6ae91-a6f4-4863-b05a-f203697990b7_snapedit_1784218735191.jpeg` | 64.580 B | Asset mới (ảnh sửa bằng SnapEdit) |
| `5faa7ad2-13be-4926-8782-809cb1184e3f_images-removebg-preview.png` | 38.348 B | Asset mới (remove.bg) |
| `7f20ed85-8d96-46fa-a737-03371de7b453_2DInventoryIcon_1-removebg-preview.png` | 184.701 B | Asset mới (remove.bg) |
| `a2a3f603-8cbd-4702-a367-d95f420f54c8_images-removebg-preview.png` | 38.348 B | Asset mới (remove.bg) |

### 6.3 Kiểm tra bổ sung: ảnh được tham chiếu trong DB

| Kiểm tra | Kết quả |
|---|---|
| `Product.image_url` trong `database_script.txt` trỏ tới file trong `wwwroot/images/`? | ✅ Có |
| Có file trong `wwwroot/images/` không được DB tham chiếu? | Không ảnh hưởng (asset dự phòng) |
| Ảnh pet (`/uploads/pets/...`) — có trong `wwwroot/images/`? | ❌ **Không** → thuộc GAP-16, tách riêng |

### HÀNH ĐỘNG CẦN LÀM — Phần 6

- [ ] ~~Copy asset hình ảnh~~ — ✅ **KHÔNG CẦN LÀM**, đã giống 100% (144/144, MD5 khớp)
- [ ] Xác nhận 4 file mới ở đích là chủ ý (không phải file rác sinh nhầm)
- [ ] **GAP-19** — Viết script/CI check tự động so `frontend\src\images` vs `wwwroot\images` (theo tên + MD5) để chặn hồi quy asset trong tương lai

---

## 7. BỔ SUNG: GAP NGOÀI 6 MỤC ĐÃ YÊU CẦU (phát hiện khi đọc code)

| ID | Mức độ | Vấn đề |
|---|---|---|
| GAP-20 | 🔴 **Chặn runtime** | `EmailService` gọi 4 template `.hbs` không tồn tại (chi tiết mục 4) |
| GAP-21 | 🔴 **Chặn runtime** | `wwwroot/uploads/pets/` không tồn tại; 5 pet trong DB có `image_path` → 404 (mục 5) |
| GAP-22 | 🟠 **Cao** | `PayOSService.CreatePaymentLinkAsync` mất fallback `https://api.payos.vn/v2` khi `HttpRequestException` → thanh toán PayOS fail nếu `BaseUrl` cấu hình sai |
| GAP-23 | 🟠 **Cao** | Gemini API key còn sống trong `Projectojt\BackEnd\appsettings.json` — cần rotate (mục 3.4) |
| GAP-24 | 🟡 **Trung bình** | Thiếu `Models\GeneratePayrollDto` + `PayrollResultDto` — xác minh không còn endpoint dùng |
| GAP-25 | 🟡 **Trung bình** | `AuthService` thay `IsPasswordValid` bằng `VerifyAndUpgradeAsync` → **ghi đè mật khẩu plaintext trong DB** ở lần đăng nhập đầu. Cần chủ ý + audit log. |
| GAP-26 | 🟡 **Trung bình** | `ChatbotController.cs` (5.264 B) + `IChatbotService.cs` + `IAiProviderService.cs` + 2 model chatbot + 157 message + 3 service AI = **toàn bộ tính năng chatbot mất** |
| GAP-27 | 🟡 **Trung bình** | `ProductsPageController.cs` (nguồn) không có file tương đương ở đích — nhưng `ProductsController.GetById` + Razor `ProductDetail` đã thay thế chức năng → **không cần port**, chỉ cần xác nhận route `/products/{id}` vẫn hoạt động |
| GAP-28 | 🔵 **Thấp** | `Program.cs` đích **không có** section `Chatbot` trong `appsettings.json` → khi port GAP-01 phải bổ sung config (nhưng dùng User Secrets, không copy key cũ) |
| GAP-29 | 🔵 **Thấp** | `Program.cs` đích có `FormFileOperationFilter` (Swagger) — chưa kiểm tra `MultipartBodyLengthLimit` cho upload 5 MB; nếu mặc định ASP.NET là 128 MB thì OK, nhưng nên set rõ |

---

## 8. 10 GAP NGHIÊM TRỌNG NHẤT

| # | Hạng | File đích thiếu / hỏng | Nguồn có gì | Tác động |
|---|---|---|---|---|
| 1 | 🔴 | `Services\ChatbotService.cs` **không tồn tại** | `ChatbotService.cs` (30.201 B) — 4 method `GetResponseAsync`, `BuildSystemPromptAsync`, `GetContextAsync`, `AnalyzeIntentWithAi` + **157 message tiếng Việt** | Mất trọn tính năng chatbot + vi phạm yêu cầu cứng "không tự đổi validation message" |
| 2 | 🔴 | `wwwroot\EmailTemplates\` **không tồn tại** (0 file `.hbs`) | `EmailTemplates\OrderInvoice.hbs` (1.402 B), `EmailTemplates\SpaInvoice.hbs` (1.918 B) | `SendOrderInvoiceEmailAsync` / `SendSpaInvoiceEmailAsync` ném `FileNotFoundException` → **gửi email hóa đơn crash** |
| 3 | 🔴 | `wwwroot\uploads\pets\` **không tồn tại** (0 file) | `wwwroot\uploads\pets\` với **15 file** `pet_*.jpg|png` | 5 pet trong DB có `image_path` → **ảnh broken 404**; mất toàn bộ ảnh pet đã upload |
| 4 | 🔴 | `Services\GeminiProviderService.cs` **không tồn tại** | `GeminiProviderService.cs` (5.840 B) — `GenerateTextAsync`, provider Gemini (`gemini-flash-latest`) | Mất provider AI chính |
| 5 | 🔴 | `Services\OllamaProviderService.cs` **không tồn tại** | `OllamaProviderService.cs` (4.846 B) — `GenerateTextAsync`, provider Ollama (`llama3.2`) | Mất provider AI dự phòng (local) |
| 6 | 🟠 | `Services\PayOSService.cs` — **đã xóa fallback URL** | `PayOSService.cs` dòng 74-106: bắt `HttpRequestException` → thử lại với `https://api.payos.vn/v2` | `BaseUrl` cấu hình sai → thanh toán **fail thay vì tự phục hồi** |
| 7 | 🟠 | `appsettings.json` đích **thiếu section `Chatbot`**; key Gemini còn sống trong `Projectojt\BackEnd\appsettings.json` | `Chatbot` section + `ApiKey` `AIzaSyBwieKdmFJVUir8JvGqoS_cZs4wFgD3eXo` | **Rò rỉ khóa API**; khi port chatbot phải rotate key + dùng User Secrets |
| 8 | 🟠 | `Controllers\ChatbotController.cs` **không tồn tại** | `ChatbotController.cs` (5.264 B) | Mất toàn bộ endpoint chatbot (route + payload contract) |
| 9 | 🟡 | `Models\PayrollDtos.cs` thiếu `GeneratePayrollDto` + `PayrollResultDto` | 2 record này trong `Projectojt\BackEnd\Models\PayrollDtos.cs` | Nếu còn endpoint trả 2 type này → **lỗi compile**; nếu không → OK nhưng cần xác nhận |
| 10 | 🟡 | `Services\AuthService.cs` — `IsPasswordValid` đã bị thay bằng `VerifyAndUpgradeAsync` | `AuthService.cs` nguồn: so sánh plaintext **vĩnh viễn**, không ghi DB | Đích **ghi đè mật khẩu trong DB** ở lần đăng nhập đầu — thay đổi hành vi dữ liệu ngoài ý muốn, cần audit log + xác nhận chủ ý |

---

## 9. HÀNH ĐỘNG CẦN LÀM — TỔNG HỢP (theo thứ tự ưu tiên)

### P0 — Chặn runtime (làm trước tiên)
- [ ] **GAP-20/03** — Tạo `wwwroot/EmailTemplates/` + copy `OrderInvoice.hbs`, `SpaInvoice.hbs` **nguyên văn**; tạo mới `SpaCancellation.hbs`, `BoardingInvoice.hbs`; khai báo trong `.csproj`
- [ ] **GAP-21/15/16** — Tạo `wwwroot/uploads/pets/` + copy **15 file** `pet_*`; xác minh static-file middleware phục vụ `/uploads/`
- [ ] **GAP-23** — **Rotate Gemini API key** ngay (đã lộ trong repo), chuyển cấu hình AI sang User Secrets

### P1 — Khôi phục tính năng bị mất
- [ ] **GAP-26/01/08** — Port `ChatbotService.cs` + `GeminiProviderService.cs` + `OllamaProviderService.cs` + `ChatbotController.cs` + `IChatbotService.cs` + `IAiProviderService.cs` + `ChatbotDtos.cs` + `ChatbotOptions.cs`; **giữ nguyên văn 157 message**
- [ ] **GAP-22** — Khôi phục fallback `https://api.payos.vn/v2` trong `PayOSService.CreatePaymentLinkAsync`
- [ ] **GAP-28** — Thêm section `Chatbot` vào `appsettings.json` đích (giá trị từ User Secrets, **không** copy key cũ)

### P2 — Xác nhận hành vi (không sửa vội)
- [ ] **GAP-24** — Xác minh `GeneratePayrollDto` / `PayrollResultDto` không còn được dùng
- [ ] **GAP-25** — Xác nhận `VerifyAndUpgradeAsync` (migrate-on-login) là chủ ý; thêm audit log nếu giữ
- [ ] **GAP-27** — Xác nhận route `/products/{id}` vẫn phục vụ đúng (thay cho `ProductsPageController`)

### P3 — Chống hồi quy
- [ ] **GAP-09** — Test assert validation message khớp bản gốc (snapshot 325 chuỗi)
- [ ] **GAP-14** — Test assert 4 template `.hbs` load được
- [ ] **GAP-19** — Script/CI check asset `frontend\src\images` ≡ `wwwroot\images` (tên + MD5)
- [ ] **GAP-29** — Set rõ `MultipartBodyLengthLimit` cho upload 5 MB

---

## 10. PHỤ LỤC — BẢNG CHỈ SỐ NHANH

| Loại gap | Số lượng | Chi tiết |
|---|---|---|
| **A. File Service nguồn chưa có file đích** | **3** | `ChatbotService.cs`, `GeminiProviderService.cs`, `OllamaProviderService.cs` |
| **B. Model nguồn thiếu ở đích** | **2** (thực sự) + 2 (đổi tên) | `ChatbotDtos.cs`, `ChatbotOptions.cs` (mất); `ProductDto_temp.cs`, `ProductCategoryFilterDto_temp.cs` (đổi tên) |
| **C. Validation message thiếu nguyên văn** | **157** | Toàn bộ từ `ChatbotService.cs`. **0 message bị đổi nội dung.** |
| **D. Message chỉ có ở đích (tính năng mới)** | **303** | Boarding / Staff Dashboard / JWT / Razor / OData / EmailTest |
| **E. Email template thiếu** | **4** | `OrderInvoice.hbs`, `SpaInvoice.hbs` (copy từ nguồn); `SpaCancellation.hbs`, `BoardingInvoice.hbs` (tạo mới) |
| **F. File upload thiếu** | **15** | Toàn bộ `wwwroot/uploads/pets/pet_*.{jpg,png}` |
| **G. Asset hình ảnh thiếu** | **0** ✅ | 144/144 khớp MD5 |
| **H. Asset hình ảnh chỉ có ở đích** | **4** | Không phải gap |
| **I. Repository thiếu file** | **0** ✅ | 5/5 file nguồn đều có đích |
| **J. Method thiếu trong file đã có** | **0** | Mọi method nguồn đều có ở đích (kể cả sau khi split partial) |
| **K. Hành vi khác** | **3** | `PayOSService` (mất fallback), `AuthService` (migrate-on-login), `EmailService` (đổi path template + thêm param) |
| **L. Model property khác** | **1** | `Product.cs` thêm `IsDeleted` |
| **M. Controller nguồn thiếu ở đích** | **2** | `ChatbotController.cs` (thật sự mất); `ProductsPageController.cs` (đã được thay thế) |
| **N. Khóa API lộ** | **1** | Gemini API key trong `appsettings.json` nguồn |
| **TỔNG** | **187** | 6 chặn runtime · 9 cao · 8 trung bình · 164 thấp/khác |
