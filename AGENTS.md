# AGENTS.md — PetShop_PRN222 (PRN232)

> Tài liệu này là "bản ghi chú" của agent cho dự án: kiến trúc, quy ước bắt buộc, và kết quả kiểm tra sức khoẻ codebase (codebase health check).
> Ngôn ngữ giao tiếp chuẛn của agent trong dự án này: **Tiếng Việt**.

---

## 0. Bối cảnh dự án — ĐỌC TRƯỚC

| | |
|---|---|
| Mục đích | **Bài tập môn PRN232** (Lập trình .NET) — dự án quản lý cửa hàng thú cưng |
| Môi trường | **Chạy hoàn toàn trên LOCAL** (máy cá nhân của sinh viên) |
| Người dùng | Nhóm sinh viên, nội bộ |
| Mức độ ưu tiên bảo mật | **THẤP — không phải mối quan tâm chính** |

### ⚠️ Hệ quả trực tiếp của bối cảnh trên

Vì dự án **không deploy ra môi trường thật**, agent **KHÔNG** bị phép:

- ❌ Đề xuất hoặc triển khai xoay vòng (rotate) secret, dọn Git history, `git filter-repo`, force-push
- ❌ Bắt buộc thêm `[Authorize]` / ASP.NET Core Identity cho **toàn bộ** endpoint
- ❌ Bắt buộc dọn `appsettings.json` khỏi Git hoặc chuyển hết secret sang User Secrets
- ❌ Bắt buộc dùng `UseForwardedHeaders` / `KnownProxies`
- ❌ Bắt buộc thay `new HttpClient()` chỉ vì lý do socket exhaustion

> **Ngoại lệ đã có sẵn (02/11/2026):** JWT Bearer đã được cài đặt theo **dual-mode**
> (xem §6) — nhưng **không** bắt buộc gắn `[Authorize]` vào endpoint nào.
> Agent được phép dùng JWT để demo/làm bài, nhưng **không** được tự ý khoá API
> khiến luồng Blazor nội bộ hỏng.

**Các secret trong `appsettings.json` (PayOS, Gmail, SQL `sa`) được coi là chấp nhận được (acceptable) trong bối cảnh local.**
Nếu chủ dự án muốn dọn, đó là việc tùy chọn — không phải ưu tiên của agent.

> **Tuy nhiên:** từ 02/11/2026, `appsettings.json` + `appsettings.Development.json` đã được
> **gỡ khỏi git index** (`git rm --cached`) và thay bằng `appsettings.example.json`.
> Đây là thay đổi **tích cực** (không mất secret khỏi máy, chỉ ngừng theo dõi), giữ nguyên
> nguyên tắc "không bắt buộc xoá lịch sử git".

**Tập trung của agent vào:** chức năng chạy đúng, dữ liệu đúng, code dễ đọc/dễ bảo trì, build sạch, logic nghiệp vụ nhất quán với đặc tả môn học.

---

## 1. Tổng quan dự án

| Mục | Chi tiết |
|---|---|
| Loại | ASP.NET Core Web App — .NET 8 (`net8.0`) |
| Kiến trúc | Blazor Server (Interactive Server) + Razor Pages/MVC + API Controllers trong cùng một project |
| DB | SQL Server — `SHOP_PET_Database`, EF Core 8 (Code First scaffold từ DB có sẵn) |
| Thanh toán | PayOS (Việt Nam) — webhook + chữ ký HMAC |
| Email | MailKit / Gmail SMTP + Handlebars template |
| Xác thực | Blazor `ProtectedLocalStorage` + Google OAuth 2.0 (cơ chế header nội bộ, chấp nhận được) |
| Port | `http://localhost:5286` (http profile), `https://localhost:7083` (https profile) |
| Frontend dev server | `http://localhost:5173` (CORS policy `FrontendDev`) |
| Script DB | `SHOP_PET_Database.sql`, `database_script.txt` (ở thư mục gốc) |

### Cấu trúc thư mục

```
Components/     68 file .razor (Pages, Layout, Shared) — UI Blazor Server
Controllers/    API endpoints (api/auth, api/payroll, api/payos, api/pets, ...)
Services/       Business logic + HTTP clients gọi chính API của app (self-HTTP)
Repositories/   Truy vấn EF Core trực tiếp
Interfaces/     Abstractions (repository/service/client)
Models/         Entity + DTO + record
Data/           ShopPetDatabaseContext (DbContext)
```

### Luồng kiến trúc đặc biệt cần hiểu

App gọi **chính nó** qua HTTP: Blazor page → `Service` → `HttpClient` (BaseAddress = origin hiện tại) → `Controller` → `Repository`/`Service` → DB.
Đây là lý do các HTTP client tự trỏ về origin hiện tại — xem `Services/SelfHttpClientExtensions.cs`
(`AddSelfHttpClient<T>()` / `AddSelfHttpClientWithAuth<T>()`).

> **Lịch sử:** trước đây các controller/service cũ mang hậu tố `_temp` (do đặt nhầm khi viết song song 2 nhánh giao diện).
> Nhóm này **đã được đổi tên sạch** ở Phase P1/P2 — xem §4.2.

---

## 2. Quy ước bắt buộc (BẮT BUỘC áp dụng khi sửa code)

### 2.1 Về "bảo mật" — giữ đúng mức độ của một dự án local

**Được phép (không cần sửa):**
- Secret nằm trực tiếp trong `appsettings.json` / `appsettings.Development.json`
- Connection string hardcode trong `Data/ShopPetDatabaseContext.cs::OnConfiguring` (dùng cho scaffold offline)
- Xác thực bằng header `X-User-Id` / `X-User-Role` / `X-Customer-Id` do Blazor tự gắn
- `EmailTestController` mở rộng
- Whitelist IP đọc từ `X-Forwarded-For` / `X-Real-IP`

**Nếu agent tình cờ đụng tới (ví dụ đang refactor) thì nên làm cho sạch, nhưng KHÔNG được biến thành việc phải làm:**
- Xoá `#warning` trong `OnConfiguring` → dùng `Name=` / `GetConnectionString` (nice-to-have)
- `new HttpClient()` trong `AuthService.cs:237` → nên dùng `IHttpClientFactory` (sửa vì lỗi kỹ thuật, không phải bảo mật)
- Nhánh fallback so sánh mật khẩu plaintext trong `AuthService.IsPasswordValid` → nên giữ BCrypt là chính (sửa vì logic bị lệch, không phải bảo mật)

> **Nguyên tắc:** đừng biến task thành cuộc diệt tập bảo mật. Sửa cái nào **gây lỗi chức năng hoặc gây khó hiểu**; bỏ qua cái nào chỉ "không đúng chuẩn production".

### 2.2 Xử lý lỗi — đây là điểm agent nên tập trung

- **KHÔNG** dùng `catch { }` hay `catch { return false; }` để nuốt lỗi.
  - Bắt buộc log qua `ILogger` (inject `ILogger<T>`), rồi trả lỗi có thông tin cho client.
  - Đây là **mục tiêu code quality hàng đầu** của dự án — lỗi bị nuốt khiến nhóm không debug được.
- **KHÔNG** dùng `Console.WriteLine` trong code ứng dụng.
  - Chuyển sang `ILogger<T>.LogInformation/LogWarning/LogError`.
  - Không log mật khẩu / token / `ChecksumKey`.
- `Console.WriteLine` chỉ được phép trong script/helper tạm (không commit).

### 2.3 Code quality

- `Nullable` = `enable` → **không được để warning CS86xx**. Xử lý null bằng `?.`, `?? string.Empty`, hoặc `required`/`init`.
- `async`/`await` bắt buộc cho I/O; **không** `.Result`/`.Wait()`.
- Truy vấn EF: dùng `AsNoTracking()` cho read-only, `.Select(...)` projection thay vì load cả entity rồi map tay.
- **SQL thô**: `ExecuteSqlRawAsync` chỉ dùng với tham số (`@p0`), tuyệt đối không nối chuỗi. `FromSqlRaw` bị cấm.
- Xóa các `catch { }` rác trong `StaffDashboardService`, `SpaBookingClient`, `PetsClient`...
- Tên file/class: bỏ hậu tố `_temp` khi dọn (giữ tương thích bằng `[Obsolete]` nếu cần giai đoạn chuyển tiếp).
- **File > 500 dòng hoặc component > 25KB → tách.** Cách tách đã chuẩn hoá (02/11/2026):
  - C# class → **`partial class`** trên nhiều file, đặt tên `<Tên>.<Phần>.cs`.
  - Blazor `.razor` → tách khối `@code` sang file **`<Tên>.razor.cs`** (partial class cùng namespace).
  - Không được copy-paste logic giữa các file — dùng method private dùng chung.
- Business logic trùng lặp giữa nhiều file → gom về `Services/` dùng chung.
- `.editorconfig` ở thư mục gốc quy định format + biến nullability warning thành **error**. Đọc file này trước khi sửa code.

### 2.4 Ngôn ngữ & thông báo

- **Giao tiếp với người dùng (agent ↔ chủ dự án)**: **TIẾNG VIỆT — BẮT BUỘC.**
  Mọi câu trả lời, báo cáo kết quả, giải thích nguyên nhân lỗi đều viết bằng tiếng Việt.
  Không trả lời bằng tiếng Anh hay tiếng Trung.
  (Xem thêm rule `.cursor/rules/ngon-luong-tieng-viet.mdc`.)
- **Code**: tiếng Anh (tên biến, tên hàm, tên class, tên file).
- **Thông báo người dùng / log nghiệp vụ**: tiếng Việt (dự án dùng thông báo tiếng Việt trong UI).
- **Comment** giải thích *tại sao*, không *cái gì* — viết bằng tiếng Việt.
- **Chuỗi kỹ thuật không dịch được** giữ nguyên tiếng Anh: `ProblemDetails`,
  `ValidationProblemDetails`, `ILogger`, `IQueryable`, `SingleResult.Create`, `$filter`,
  `OData`, `JWT`, `BCrypt`, `PayOS`…

### 2.5 Phạm vi thay đổi

- Sửa **đúng phạm vi yêu cầu**, không refactor lan man.
- Không tự ý đổi schema DB, không xoá seed data, không sửa `SHOP_PET_Database.sql` nếu không được yêu cầu.
- Không push / commit / force-push nếu chưa được yêu cầu.

---

## 3. Kết quả kiểm tra sức khoẻ codebase

> Chạy ngày 01/11/2026 — commit `bf22856`.

### 3.1 Trạng thái build: ✅ THÀNH CÔNG (sạch)

> Sau đợt sửa P0 (01/11/2026): **2 Warning(s) | 0 Error(s)** — chỉ còn NU1902 của NuGet.

| Mã | Vị trí | Vấn đề | Trạng thái |
|---|---|---|---|
| NU1902 | `MailKit 4.15.1` | Cảnh báo CVE từ NuGet | ✅ Đã sửa — nâng lên `MailKit 4.16.0` (+ `MimeKit 4.16.0`) |
| CS1030 | `Data/ShopPetDatabaseContext.cs:100` | `#warning` do connection string hardcode | ✅ Đã sửa → dùng `Name=DefaultConnection` |
| CS8604 | `Components/Pages/StaffProfile.razor:69` | null → tham số `ExecuteSqlRawAsync` | ✅ Đã sửa (bỏ SQL thô) |
| CS8604 | `Controllers/PayOSController.cs:213` | null → `PayOSPaymentStatusResult.Status` | ✅ Đã sửa (`?? "unknown"`) |
| CS8601 | `Services/StaffDashboardService.cs:83` | gán null | ✅ Đã sửa (`?.`) |
| CS8602 | `Components/Pages/Revenue.razor:203` | dereference null | ✅ Đã sửa (bỏ raw SQL → EF query) |
| CS8605 | `Components/Pages/ProductEdit.razor:94` | unbox null | ✅ Đã sửa (pattern matching) |

> **Trạng thái build hiện tại (sau P0+P1+P2): `0 Warning(s) | 0 Error(s)` — hoàn toàn sạch.**

### 3.2 🔴 Nghiêm trọng về **chức năng / dữ liệu** (ưu tiên cao)

**R1. Check-in nhân viên ghi sai dòng dữ liệu**

`Services/StaffDashboardService.cs::CheckInAsync` — bỏ qua tham số `scheduleId`, dùng `ExecuteSqlRaw` update dòng cuối cùng trong bảng `WorkSchedule`:

**R1. Check-in nhân viên ghi sai dòng dữ liệu** — ✅ ĐÃ SỬA

`Services/StaffDashboardService.cs::CheckInAsync` — bỏ qua tham số `scheduleId`, dùng `ExecuteSqlRaw` update dòng cuối cùng trong bảng `WorkSchedule`:

```sql
UPDATE WorkSchedule SET status = N'Đã Check-in'
WHERE schedule_id = (SELECT TOP 1 schedule_id FROM WorkSchedule ORDER BY schedule_id DESC)
```

⇒ Check-in của nhân viên A có thể đánh dấu ca của nhân viên B. Sai dữ liệu lương/attendance.

**Đã sửa:** `CheckInAsync` giờ load đúng entity theo `scheduleId` (qua EF, không raw SQL), set `Status = "Đã Check-in"`, `SaveChangesAsync` trong `try/catch` có log. Trả `false` + log cảnh báo nếu `scheduleId` không tồn tại.

**R2. Sửa hồ sơ nhân viên ghi đè người khác** — ✅ ĐÃ SỬA

`Components/Pages/StaffProfile.razor:57,69` — `WHERE customer_id = 2` viết cứng, mọi thao tác lưu đều nhắm vào customer id = 2.

**Đã sửa:** tạo mới `Services/StaffProfileService.cs` + `Interfaces/IStaffProfileService.cs`.
- `GetProfileAsync(userId, email)` — định vị bản ghi bằng **email** (duy nhất toàn hệ thống) rồi mới tới id → không bao giờ ghi nhầm người.
- `ResolveStaffIdAsync(userId, email)` — trả `staff_id` thật (bảng `Staff`), vì `UserSession.UserId` của tài khoản role "staff" có thể là `CustomerId`.
- `UpdateProfileAsync` — cập nhật đúng bảng theo `Source` ("staff"/"customer").
- Trang `StaffProfile.razor` dùng session + service, có báo lỗi/thành công, đồng bộ lại session sau khi lưu.
- `Components/Layout/StaffWorkspaceLayout.razor` cũng bỏ `customer_id = 2` (tên/avatar topbar).

**R3. Dữ liệu giả trong API lịch làm việc** — ✅ ĐÃ SỬA

`Services/StaffDashboardService.cs::GetStaffSchedulesAsync` trả về `ScheduleId = 9999` và chỉ một bản ghi "Ca Trực Hiện Tại" với giờ `DateTime.Now` → `+8h`, bỏ qua toàn bộ lịch thật trong DB.

**Đã sửa:** query `WorkSchedules` theo `StaffId`, lấy ca hôm nay + các ca sắp tới, khử trùng bằng `WorkScheduleDedup.ByStaffDateTimeSlot`. Tên ca lấy từ `Note` nếu có, không thì phân loại theo khung giờ (sáng/chiều/tối). Thêm `GetCurrentShiftAsync` cho `CurrentShiftName`/`CurrentShiftTime` trên dashboard.

**R4. `StaffDashboardService.UpdateBookingStatusAsync` nuốt lỗi** — ✅ ĐÃ SỬA

`try { ... } catch { return false; }` bao trọn cả phần set property `UpdatedAt`/`CreatedAt` lẫn `SaveChangesAsync`. Khi DB lỗi, người dùng chỉ thấy "thất bại" mà không có dấu vết.

**Đã sửa:** `SaveChangesAsync` nằm trong `try/catch` riêng có `ILogger`; phần set `UpdatedAt`/`CreatedAt` bọc `try` riêng bắt `InvalidOperationException` (entity không có cột đó) — không làm hỏng cả lần cập nhật. `StaffDashboardService` nay inject `ILogger<StaffDashboardService>`.

### 3.3 đŸŸ  Trung bĂ¬nh â€” cháº¥t lÆ°á»£ng & báº£o trĂ¬ â€” âœ… **ÄĂƒ Xá»¬ LĂ á» P1/P2**

**M1. Nuá»‘t lá»—i im láº·ng** â€” âœ… ÄĂƒ Sá»¬A

Má»i `catch` trong cĂ¡c file cá»‘t lĂµi (`SpaBookingClient`, `PetsClient`, `OrderClient`, `ReviewClient`, `AuthClient`, `CheckoutClient`, `PaymentClient`, `PetServiceClient`, `ProductCatalogClient`, `CartStateService`, `UserSessionService`, `SpaAuthHeaderHandler`, `BoardingController`, `PayOSController`, `PayOSService`, `ShiftExchangeController`, `PayrollService`, `SpaBookingController`) nay Ä‘á»u ghi log qua `ILogger`.

**M2. File `_temp` cĂ²n tá»“n táº¡i vĂ  Ä‘Æ°á»£c compile** â€” âœ… ÄĂƒ Sá»¬A

Xem Â§4.2 â€” nhĂ³m 9 file + 7 route API Ä‘Ă£ Ä‘Æ°á»£c Ä‘á»•i tĂªn sáº¡ch.

**M3. `Console.WriteLine` thay cho `ILogger`** â€” âœ… ÄĂƒ Sá»¬A

69+ chá»— Ä‘Ă£ chuyá»ƒn sang `ILogger<T>` qua extension `Services/LoggerExtensions.cs` (`_logger.Line(...)`, `_logger.LineWarning(...)`, `_logger.LineError(ex, ...)`).
`Debug.WriteLine` trong `Services/` cÅ©ng Ä‘Ă£ chuyá»ƒn. (CĂ¡c `Debug.WriteLine` cĂ²n láº¡i náº±m trong `.razor` â€” xem Â§4.3.)

**M4. `new HttpClient()` trong `AuthService.cs:237`** â€” âœ… ÄĂƒ Sá»¬A

`GetGoogleProfileFromAccessTokenAsync` nay dĂ¹ng `_httpClientFactory.CreateClient(nameof(AuthService))`.

**M5. Logic xĂ¡c thá»±c lá»‡ch** â€” âœ… ÄĂƒ Sá»¬A

`IsPasswordValid` Ä‘Ă£ bá»‹ thay báº±ng `VerifyAndUpgradeAsync` â€” xem Â§4.1.

### 3.4 đŸŸ¡ Nhá» / ká»¹ thuáº­t

| # | Váº¥n Ä‘á» | Tráº¡ng thĂ¡i |
|---|---|---|
| L1 | **KhĂ´ng cĂ³ test project nĂ o** â€” 0 test. `AttendanceService.CalculateWorkHours` lĂ  hĂ m thuáº§n, ráº¥t há»£p Ä‘á»ƒ unit test. | âœ… ÄĂ£ táº¡o `tests/PetShop.Tests` â€” 22 test, xem Â§4.3 |
| L2 | **KhĂ´ng cĂ³ CI** â€” khĂ´ng cĂ³ `.github/workflows`, khĂ´ng lint/format gate. | âœ… ÄĂ£ thĂªm `.github/workflows/ci.yml` (build + test) |
| L3 | `README.md` gáº§n nhÆ° trá»‘ng (1 dĂ²ng) â€” thiáº¿u hÆ°á»›ng dáº«n cháº¡y, khá»Ÿi táº¡o DB, kiáº¿n trĂºc. | âœ… ÄĂ£ viáº¿t láº¡i Ä‘áº§y Ä‘á»§ |
| L4 | `AllowedHosts: "*"` â€” cĂ³ thá»ƒ giá»›i háº¡n náº¿u muá»‘n gá»n. | â¬œ KhĂ´ng lĂ m (khĂ´ng áº£nh hÆ°á»Ÿng local) |
| L5 | `app.UseCors("FrontendDev")` Ä‘áº·t trÆ°á»›c `UseSession`/`UseAuthorization`; origin `localhost:5173` hardcode trong code. | â¬œ Äá»ƒ nguyĂªn (cáº§n cho React dev server) |
| L6 | `AddDistributedMemoryCache` + `AddSession` + `ProtectedLocalStorage` â€” 3 cÆ¡ cháº¿ session song song, khĂ¡i niá»‡m láº«n lá»™n. | â¬œ ChÆ°a xá»­ lĂ½ (thay Ä‘á»•i kiáº¿n trĂºc lá»›n) |
| L7 | `Program.cs` láº·p 12 láº§n khá»‘i `AddHttpClient` + logic BaseAddress â†’ nĂªn tĂ¡ch extension `AddSelfHttpClient<T>()`. | âœ… ÄĂ£ tĂ¡ch `Services/SelfHttpClientExtensions.cs` |
| L8 | PhiĂªn báº£n EF Core khĂ´ng Ä‘á»“ng nháº¥t: `Microsoft.EntityFrameworkCore` 8.0.8 vs `.SqlServer` 8.0.0. | âœ… ÄĂ£ thá»‘ng nháº¥t 8.0.8 (kĂ¨m `.Design`, `.Tools`) |
| L9 | File quĂ¡ lá»›n: `SpaCart.razor` (38KB), `Orders.razor` (36KB), `ShiftExchangeController.cs` (31KB), `SpaBookingService.cs` (26KB). | â¬œ ChÆ°a tĂ¡ch (cáº§n nhiá»u Ä‘á»£t, rá»§i ro regression) |
| L10 | ThÆ° má»¥c rĂ¡c `PetShop (ASP WEB CORE API) (This is a template, do not edit here)/Projectojt` â€” Ä‘Ă£ exclude trong csproj, nĂªn xoĂ¡. | âœ… ÄĂ£ xoĂ¡ (gitlink há»ng + thÆ° má»¥c rá»—ng) |
| L11 | `app.UseAntiforgery()` â€” form Blazor pháº£i cĂ³ `<AntiforgeryToken />`, náº¿u khĂ´ng sáº½ 400. | â¬œ Äá»ƒ nguyĂªn (app Ä‘ang cháº¡y bĂ¬nh thÆ°á»ng) |
| L12 | `SpaAuthHeaderHandler` dĂ¹ng `IServiceProvider` Ä‘á»ƒ láº¥y scoped service tá»« handler â€” cĂ¡ch lĂ m khĂ³ báº£o trĂ¬, nĂªn chuyá»ƒn sang typed client + `HttpContextAccessor` rĂµ rĂ ng. | â¬œ ChÆ°a xá»­ lĂ½ (Ä‘Ă£ cĂ³ comment giáº£i thĂ­ch lĂ½ do) |
| L13 | `MailKit 4.15.1` cĂ³ cáº£nh bĂ¡o NU1902 â€” nĂ¢ng lĂªn 4.16+ náº¿u muá»‘n build khĂ´ng cáº£nh bĂ¡o. | âœ… ÄĂ£ nĂ¢ng `MailKit 4.16.0` + `MimeKit 4.16.0` |

---

## 4. Lộ trình sửa (đề xuất thứ tự — đã điều chỉnh cho bối cảnh local)

### P0 — Ưu tiên cao (sửa đúng dữ liệu & chức năng) — ✅ **ĐÃ HOÀN TẤT 01/11/2026**

1. ✅ `StaffDashboardService.CheckInAsync` — update theo đúng `scheduleId` (thay vì `TOP 1`).
2. ✅ `StaffProfile.razor` — bỏ `customer_id = 2` hardcode, dùng `IStaffProfileService` + session.
3. ✅ `StaffDashboardService.GetStaffSchedulesAsync` — trả lịch thật từ DB thay vì bản ghi giả `ScheduleId = 9999`.
4. ✅ `UpdateBookingStatusAsync` — tách try/catch, log lỗi `SaveChangesAsync`.
5. ✅ Xoá 5 warning CS86xx (mục 3.1) → build sạch.

**File mới tạo:**
- `Services/StaffProfileService.cs`
- `Interfaces/IStaffProfileService.cs`

**File đã sửa:** `Services/StaffDashboardService.cs`, `Components/Pages/StaffProfile.razor`, `Components/Pages/StaffSchedule.razor`, `Components/Layout/StaffWorkspaceLayout.razor`, `Components/Pages/Revenue.razor`, `Components/Pages/ProductEdit.razor`, `Controllers/PayOSController.cs`, `Data/ShopPetDatabaseContext.cs`, `Models/StaffDtos.cs`, `Program.cs`

### P1 — Chất lượng code — ✅ **ĐÃ HOÀN TẤT 01/11/2026**

6. ✅ `catch { }` → `ILogger` ở các file cốt lõi (18 file: 9 API client + `CartStateService`, `UserSessionService`, `SpaAuthHeaderHandler`, `BoardingController`, `PayOSController`, `PayOSService`, `ShiftExchangeController`, `PayrollService`, `SpaBookingController`).
7. ✅ `Console.WriteLine` → `ILogger<T>` (69+ chỗ, 7 file) — dùng extension `Services/LoggerExtensions.cs`.
8. ✅ `AuthService.cs:237` → dùng `IHttpClientFactory` (`CreateClient(nameof(AuthService))`).
9. ✅ `AuthService.IsPasswordValid` → thay bằng `VerifyAndUpgradeAsync` (**migrate-on-login**, xem §4.1).
10. ✅ Đổi tên nhóm file `_temp` + route API (xem §4.2).
11. ✅ `ShiftExchangeController` — 27 chỗ `Console.WriteLine` → `_logger.Line(...)`.

### P2 — Hoàn thiện — ✅ **ĐÃ HOÀN TẤT 01/11/2026**

12. ✅ Unit test — `tests/PetShop.Tests` (xUnit), **22 test pass** (xem §4.3).
13. ✅ CI — `.github/workflows/ci.yml` (build + test trên `windows-latest`, .NET 8).
14. ✅ Viết lại `README.md` — kiến trúc, cách chạy, cấu hình `appsettings`, bảng API, migrate-on-login.
15. ✅ Tách `Program.cs` bằng extension `Services/SelfHttpClientExtensions.cs` (`AddSelfHttpClient<T>()` / `AddSelfHttpClientWithAuth<T>()`) — giảm ~40 dòng lặp.
16. ✅ Dọn thư mục rác `PetShop (ASP WEB CORE API) (This is a template, do not edit here)/`, thống nhất EF Core 8.0.8, nâng MailKit 4.16.0 + MimeKit 4.16.0.

### 4.1 Chi tiết: migrate-on-login (`Services/AuthService.cs`)

Dữ liệu seed cũ lưu mật khẩu **plaintext** (`staff123`, `123`). Nếu xoá hẳn nhánh plaintext thì các tài khoản đó **không đăng nhập được** → hỏng demo.

Cơ chế mới — `VerifyAndUpgradeAsync`:

| Trạng thái DB | Hành vi |
|---|---|
| Hash BCrypt (`$2...`) | Kiểm tra bằng `BCrypt.Verify`. Hash sai định dạng → `SaltParseException` → coi như không khớp (không crash) |
| Plaintext (seed cũ) | So khớp 1 lần → nếu đúng, **ghi lại bằng hash BCrypt** qua `IAuthRepository.UpgradePasswordHashAsync` → lần sau đi qua BCrypt |
| Rỗng / null | Từ chối |

> Nhánh so sánh plaintext **không còn tồn tại vĩnh viễn** trong code — chỉ dùng để *migrate* rồi bị thay thế ngay.
> Áp dụng cho cả `Customer`, `Staff`, `Doctor` (Admin dùng chung id-space với `Customer`/`Staff` — xem `AuthRepository.UpgradePasswordHashAsync`).

### 4.2 Chi tiết: đổi tên nhóm `_temp`

**File** (9 file):

| Cũ | Mới |
|---|---|
| `Controllers/ProductsController_temp.cs` | `Controllers/ProductsController.cs` |
| `Controllers/OrdersController_temp.cs` | `Controllers/OrdersController.cs` |
| `Controllers/CheckoutController_temp.cs` | `Controllers/CheckoutController.cs` |
| `Services/ProductService_temp.cs` | `Services/ProductService.cs` |
| `Repositories/ProductRepository_temp.cs` | `Repositories/ProductRepository.cs` |
| `Interfaces/IProductService_temp.cs` | `Interfaces/IProductService.cs` |
| `Interfaces/IProductRepository_temp.cs` | `Interfaces/IProductRepository.cs` |
| `Models/ProductDto_temp.cs` | `Models/ProductDto.cs` |
| `Models/ProductCategoryFilterDto_temp.cs` | `Models/ProductCategoryFilterDto.cs` |

**Route API** (3 route):

| Cũ | Mới |
|---|---|
| `api/products-temp` | `api/products` |
| `api/orders-temp` | `api/orders` |
| `api/checkout-temp` | `api/checkout` |

**Type/DTO** (6 type): `CheckoutItem_temp`, `CheckoutRequest_temp`, `CheckoutResponse_temp`, `OrderSummaryDto_temp`, `OrderDetailItemDto_temp`, `OrderDetailDto_temp` → bỏ hậu tố `_temp`.

> Không có xung đột route với controller khác — đã kiểm tra toàn bộ `[Route]`/`[Http*]` trong `Controllers/`.

### 4.3 Chi tiết: unit test (`tests/PetShop.Tests`)

| File | Số test | Phạm vi |
|---|---|---|
| `AttendanceServiceTests.cs` | 7 | Luật tính công: checkout sớm/muộn/quá giờ, thiếu checkout, check-in muộn, làm tròn 2 chữ số, không trả số giờ âm |
| `WorkScheduleDedupTests.cs` | 7 | Gom dòng lịch trùng, giữ `schedule_id` nhỏ nhất, tách theo staff / theo khung giờ, `StaffId = null` → key 0, sắp xếp |
| `AuthServiceTests.cs` | 8 | BCrypt đúng/sai, migrate-on-login (customer + staff), không migrate khi sai mật khẩu, tài khoản bị khoá, tài khoản không tồn tại, mật khẩu rỗng |
| `JwtTokenServiceTests.cs` | 8 | Phát hành/kiểm tra JWT: claim đầy đủ, chữ ký sai, token rỗng/rác, token hết hạn, khoá rỗng hoặc < 32 ký tự |

Chạy: `dotnet test` → **30 passed, 0 failed**.

> Test project được thêm vào `PetShop_PRN222.sln`. `PetShop_PRN222.csproj` đã `Compile Remove="tests\**\*.cs"`
> để web app không biên dịch nhầm file test.

### 4.4 Kết quả verify cuối (01/11/2026)

| Kiểm tra | Kết quả |
|---|---|
| `dotnet build PetShop_PRN222.sln` | ✅ **0 Error, 0 Warning** (đã hết NU1902 sau khi nâng MailKit) |
| `dotnet test` | ✅ **30/30 passed** (22 cũ + 8 test JWT) |
| App chạy (`dotnet run`) | ✅ Khởi động OK, DI resolve OK |
| Smoke test API | ✅ `api/products`, `api/products/categories`, `api/orders/history`, `api/pet-services/grouped`, `api/pets/meta/species`, `api/boarding-rooms/availability` → 200 |
| `POST /api/auth/login` (seed plaintext `staff123`) | ✅ 200 → role `staff`; DB xác nhận đã được hash lại thành `$2a$11$...` |

> **Lưu ý môi trường:** tài khoản SQL `sa` / mật khẩu `1234` trong `appsettings.json` **không đăng nhập được**
> trên máy này (SQL Server chạy bằng Windows Authentication). Dùng override:
> `Integrated Security=true;TrustServerCertificate=true;Encrypt=false`.

### ❌ KHÔNG nằm trong lộ trình (theo yêu cầu chủ dự án)

- Xoay vòng secret / dọn Git history
- Triển khai ASP.NET Core Identity hoặc bắt buộc `[Authorize]` cho **toàn bộ** endpoint
- `UseForwardedHeaders` / `KnownProxies`
- Bất kỳ thay đổi nào nhằm mục đích "chuẩn hoá production" nói chung

---

## 5. Xác thực dual-mode (JWT Bearer) — 02/11/2026

JWT đã được cài đặt theo **Phase 4 / slide Ch06**, nhưng theo thiết kế **dual-mode** để
không phá vỡ luồng Blazor nội bộ (app gọi chính nó qua HTTP).

| Scheme | Nguồn | Cơ chế |
|---|---|---|
| `Session` | Blazor Server (self-HTTP) | Header `X-User-Id` + `X-User-Role` (gắn tự động bởi `SpaAuthHeaderHandler`) |
| `Bearer` | Client API bên ngoài | Header `Authorization: Bearer <token>` |

**Chuyển tiếp:** `Program.cs` dùng `AddPolicyScheme("Default")` với `ForwardDefaultSelector` —
có header `Authorization: Bearer` → `JwtBearer`, ngược lại → `Session`.

**Files chính:**

| File | Vai trò |
|---|---|
| `Services/JwtTokenService.cs` | `JwtSettings` + `IJwtTokenService` (HS256, kiểm tra issuer/audience/signature/lifetime) |
| `Services/SessionHeaderAuthenticationHandler.cs` | `AuthSchemes` + scheme `Session` |
| `Controllers/AuthController.cs` | `POST /api/auth/token` (phát token) + `GET /api/auth/me` (`[Authorize]`) |
| `Models/AuthDtos.cs` | `TokenResponseDto`, `CurrentUserDto` |
| `tests/PetShop.Tests/JwtTokenServiceTests.cs` | 8 unit test |

**Quy tắc cho agent:**

- ✅ Được dùng JWT để demo / làm bài.
- ❌ **KHÔNG** tự ý gắn `[Authorize]` vào endpoint đang chạy tốt — sẽ phá vỡ luồng
  Blazor nội bộ nếu header session không được gắn đúng.
- ❌ **KHÔNG** hardcode `JwtSettings:Key` vào code. Key phải nằm trong
  `appsettings.Development.json` / User Secrets, tối thiểu 32 ký tự.
- ✅ Nếu muốn bảo vệ một endpoint mới, dùng `[Authorize]` **và** chắc chắn mọi đường
  gọi tới nó đều có header session hoặc Bearer token.

---

## 6. Quy trình làm việc của agent trong repo này

1. **Đọc trước khi sửa**: `Program.cs` (DI + pipeline), `Services/UserSessionService.cs`, `Services/SpaAuthHeaderHandler.cs`, và file liên quan trực tiếp.
2. **Sửa**: theo đúng §2 (bắt buộc). Ưu tiên sửa gốc rễ thay vì chặn triệu chứng. **Không mở rộng phạm vi sang refactor không liên quan.**
3. **Build**: `dotnet build PetShop_PRN222.csproj --nologo` — phải **0 error** và **không còn warning mới**.
4. **Báo cáo bằng tiếng Việt**, nêu rõ file + dòng đã thay đổi và phần nào *chưa* làm.
5. **Không** push / commit / force-push nếu chưa được yêu cầu rõ ràng.
6. **Không** đề xuất thêm biện pháp bảo mật trừ khi chủ dự án hỏi.

---

## 7. Tối ưu RAM cho agent (bắt buộc)

Chi tiết đầy đủ ở [`docs/agent.md`](docs/agent.md) §1. Tóm tắt:

1. **Không bao giờ `Read` file lớn toàn bộ vào context.** File > 300 dày ⇒ dùng `Grep` (pattern + `head_limit`) rồi mới `Read` với `offset`/`limit`.
2. File > 1500 dòng hoặc > 60 KB ⇒ **chỉ `Grep`**, không `Read`.
3. Cấm đọc: `node_modules/`, `bin/`, `obj/`, `.git/`, SQL dump 866 KB, `database_script.txt`.
4. Mỗi subagent chỉ đọc đúng phạm vi được giao; trả về **tóm tắt + đường dẫn + số dòng**, không dán lại code dài.
5. Kết quả kiểm kê lớn **ghi ra `docs/_inventory/*.md`** thay vì giữ trong context.
6. Tối đa **3 subagent đọc đồng thời**; agent nặng (quét asset, đọc CSS lớn) chạy **tuần tự**.
7. Build dùng `dotnet build --nologo -v q` để không đổ ngữ cảnh log.
8. `dotnet run` phải chạy **nền** (`Start-Process`), không chặn terminal.
9. Dọn biến tạm sau mỗi lát cắt; không giữ 2 bản sao cùng nội dung trong bộ nhớ.
10. Báo cáo cho user ≤ 40 dòng, không dán lại nội dung file.

## 8. Quy trình báo cáo (bắt buộc)

Mỗi lần báo cáo phải có đủ 4 phần: **1) ĐÃ LÀM** (file/số lượng/commit) · **2) CÒN LẠI** (checklist chưa tick + lý do) · **3) RỦI RO** (🔴/🟠/🟡 + biện pháp) · **4) CẦN USER QUYẾT** (Q1, Q2...).
Báo cáo bằng **tiếng Việt**. Không nói "xong" khi chưa tick đủ checklist.
