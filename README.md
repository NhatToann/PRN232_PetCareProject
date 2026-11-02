# 🐾 Pets4Care — PetShop (PRN232)

> Đồ án môn **PRN232** (C# / .NET 8) — Hệ thống quản lý chuỗi cửa hàng thú cưng.
> Chạy hoàn toàn **local**, không cần triển khai production.

---

## 1. Tổng quan

Ứng dụng web cho cửa hàng thú cưng, gồm 3 phân hệ chính:

| Phân hệ | Mô tả |
|---|---|
| **Cửa hàng (Customer)** | Sản phẩm, giỏ hàng, checkout (COD / PayOS), theo dõi đơn hàng, đặt lịch Spa & gửi đơn, đánh giá dịch vụ |
| **Nhân viên (Staff)** | Dashboard, lịch làm việc, chấm công, duyệt đơn hàng & đặt lịch, đổi ca, hồ sơ cá nhân, bảng lương |
| **Quản trị (Admin)** | Quản lý sản phẩm, danh mục, nhân viên, doanh thu, lương, cấu hình hệ thống |

### Công nghệ

- **.NET 8.0** / C# 12 (`Nullable` bật)
- **ASP.NET Core Web App** — Blazor Server (Interactive Server) + MVC/API Controllers dùng chung
- **EF Core 8** + **SQL Server** (`SHOP_PET_Database`)
- **PayOS** — thanh toán QR (sandbox & production), có webhook
- **MailKit** — gửi email OTP, hoá đơn, thông báo
- **BCrypt.Net** — băm mật khẩu
- **Google.Apis.Auth** — đăng nhập Google (IdToken / AccessToken)
- **xUnit** — unit test (22 test)

---

## 2. Chạy dự án

### Yêu cầu

- .NET SDK **8.0** trở lên
- SQL Server (Express hoặc bản đầy đủ), đang chạy
- Database tên `SHOP_PET_Database`

### Các bước

```bash
# 1. Cài lại package
dotnet restore

# 2. (Tuỳ chọn) Chạy test
dotnet test

# 3. Chạy web app
dotnet run --project PetShop_PRN222.csproj
```

Mở trình duyệt tại: **http://localhost:5286**
Swagger UI: **http://localhost:5286/swagger**

### Cấu hình

> ⚠️ **`appsettings.json` và `appsettings.Development.json` KHÔNG còn được git theo dõi**
> (đã gỡ khỏi index bằng `git rm --cached`). File mẫu: **`appsettings.example.json`**.
> Mỗi người trong nhóm tự tạo file riêng cho máy mình:
>
> ```bash
> # 1. Copy file mẫu
> cp appsettings.example.json appsettings.Development.json
>
> # 2. (Khuyến nghị) Lưu secret vào User Secrets — không bao giờ lên git
> dotnet user-secrets set "ConnectionStrings:DBDefault" "Data Source=(local);Database=SHOP_PET_Database;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
> dotnet user-secrets set "Mail:Password" "<app-password>"
> dotnet user-secrets set "PayOS:ChecksumKey" "<checksum-key>"
> ```
>
> `UserSecretsId` đã khai báo trong `.csproj` (`petshop-prn232-local-dev`).

Cấu hình trong `appsettings.Development.json` (ưu tiên) hoặc `appsettings.json`:

```jsonc
{
  "ConnectionStrings": {
    // Ưu tiên DBDefault, fallback DefaultConnection
    "DBDefault": "Data Source=(local);Database=SHOP_PET_Database;User Id=sa;Password=1234;TrustServerCertificate=true;Encrypt=false"
  },

  "GoogleAuth": {
    "ClientId": "",        // Client ID từ Google Cloud Console (bật "Web application")
    "ClientSecret": "",
    "RedirectUri": "http://localhost:5286"
  },

  "Mail": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "587",
    "SecureSocketOptions": "StartTls",
    "Username": "your@gmail.com",
    "Password": "<app-password>",   // Mật khẩu ứng dụng của Gmail
    "From": "your@gmail.com"
  },

  "PayOS": {
    "ClientId": "...",
    "ApiKey": "...",
    "ChecksumKey": "...",
    "BaseUrl": "https://api-merchant.payos.vn/v2"
  },

  "Attendance": {
    "AllowedIpPrefixes": ["127.0.0.1", "192.168.1."]   // IP được phép chấm công
  }
}
```

> **Lưu ý:** Nếu dùng SQL Server Express với Windows Authentication, đổi chuỗi kết nối thành
> `Data Source=(local);Database=SHOP_PET_Database;Integrated Security=true;TrustServerCertificate=true;Encrypt=false`.

Ghi đè từ dòng lệnh (không sửa file):

```bash
# PowerShell
$env:ConnectionStrings__DBDefault="Data Source=(local);Database=SHOP_PET_Database;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
dotnet run --project PetShop_PRN222.csproj
```

---

## 3. Kiến trúc

```
PetShop_PRN222/
├── Program.cs              # DI registration + HTTP pipeline
├── Data/
│   └── ShopPetDatabaseContext.cs    # EF Core DbContext
├── Models/                 # Entity (EF) + DTO (API contract)
├── Interfaces/             # Interface cho Service / Repository / Client
├── Repositories/           # Truy cập dữ liệu (EF Core)
├── Services/               # Business logic + HTTP client gọi chính API của app
├── Controllers/            # API endpoints (REST)
├── Components/
│   ├── Pages/              # Blazor pages (37 trang)
│   ├── Layout/             # MainLayout, StaffWorkspaceLayout, Header, NavMenu…
│   ├── Shared/             # Component dùng chung (ProductCard, CartDrawer, TopBar…)
│   └── Home/               # Trang dựng trong template
├── wwwroot/                # CSS / JS / ảnh
├── tests/PetShop.Tests/    # Unit test (xUnit)
└── AGENTS.md               # Ghi chú kiến trúc + lộ trình sửa cho agent
```

### Ba tầng

```
Blazor Page (.razor)
   │  injects
   ▼
Service  (Services/*.cs)          ← Business logic, validate, map DTO
   │
   ├── Repository (Repositories/*.cs)  ← EF Core, truy vấn DB
   │
   └── HTTP Client (Services/*Client.cs)  ← Gọi API của chính app qua HttpClient
                                              (BaseAddress = httpContext hiện tại)
   ▼
Controller (Controllers/*.cs)      ← HTTP endpoints, model binding, trả JSON
```

> **Vì sao có cả Repository lẫn HTTP Client?**
> Đây là đặc thù của đồ án: các trang Blazor dùng **client** gọi API thật (mô phỏng kiến trúc
> frontend ↔ backend tách rời, giống hệ thống SPA/React đang được phát triển song song ở
> `wwwroot/css/spa/`). Vì vậy `Program.cs` đăng ký `HttpClient` với `BaseAddress` trỏ về chính
> ứng dụng đang chạy. Xem `SpaAuthHeaderHandler` để biết cách truyền session qua header.

### Phiên đăng nhập

`UserSessionService` lưu `AuthResponseDto` vào `ProtectedLocalStorage` (mã hoá phía client).
Mỗi request HTTP từ Blazor đi qua `SpaAuthHeaderHandler` để gắn header:

| Header | Ý nghĩa |
|---|---|
| `X-User-Id` | Id người dùng trong bảng `Staff` / `Customer` |
| `X-Customer-Id` | Id khách hàng |
| `X-User-Role` | `admin` / `staff` / `doctor` / `user` |

> ⚠️ `Customer` và `Staff` có thể trùng `id` trong DB. `StaffProfileService.ResolveStaffIdAsync`
> phân giải bằng cách **khớp email trước**, rồi mới fallback sang `id`.

---

## 4. API chính

| Nhóm | Endpoint |
|---|---|
| Xác thực | `POST /api/auth/register` · `/login` · `/token` · `/google-login` · `/send-email-otp` · `/verify-email-otp` · `/reset-password` · `GET /api/auth/me` |
| Sản phẩm | `GET /api/products` · `/api/products/categories` · `/api/products/{id}` |
| Giỏ & thanh toán | `POST /api/checkout` |
| Đơn hàng | `GET /api/orders/history` · `/api/orders/{id}` · `PATCH /api/orders/{id}/delivered` |
| Spa | `GET /api/spa-booking/services` · `/availability` · `/estimate` · `PATCH /api/spa-booking/{id}/status` |
| Lưu trú | `GET /api/boarding-rooms` · `/availability` · `POST /api/boarding-rooms/checkout` |
| Nhân viên | `GET /api/attendance/staff/{id}/overview` · `POST /api/attendance/staff/{id}/toggle` |
| Ca làm | `GET /api/shift-exchange/all-shifts` · `POST /api/shift-exchange/swap` |
| Lương | `GET /api/payroll/history` · `POST /api/payroll/generate-all` |
| PayOS | `POST /api/payos/order/create` · `/spa/create` · `/webhook` · `GET /api/payos/status/{orderCode}` |

---

## 4b. OData endpoint

Ngoài REST API, app còn expose **OData v4** (prefix `PetShop`) cho phép client truy vấn động
mà không cần viết endpoint riêng cho từng bộ lọc.

| URL | Mô tả |
|---|---|
| `GET /PetShop/$metadata` | XML mô tả EDM (schema) |
| `GET /PetShop/Products` | Danh sách sản phẩm |
| `GET /PetShop/Products({key})` | Một sản phẩm theo id |
| `GET /PetShop/Products/$count` | Tổng số sản phẩm (dạng số thuần) |
| `GET /PetShop/PetServices` · `/PetShop/Pets` | Các entity set khác |

**Query options hỗ trợ:** `$filter` · `$select` · `$orderby` · `$top` · `$skip` · `$expand` · `$count=true` · `$skiptoken`

```bash
# Sản phẩm giá > 40k, đắt nhất, 5 bản ghi, chỉ lấy 2 field
curl "http://localhost:5286/PetShop/Products?$filter=Price gt 40000&$orderby=Price desc&$top=5&$select=Name,Price"

# Kèm danh mục + nhà cung cấp, kèm tổng số
curl "http://localhost:5286/PetShop/Products?$expand=Category,Supplier&$count=true&$top=2"
```

> Ở môi trường **Development** có trang `http://localhost:5286/$odata`
> (OData Endpoint Routing Debugger) liệt kê toàn bộ route hợp lệ.
> Mã nguồn: `Controllers/PetShopODataController.cs` + `Models/PetShopEdmModel.cs`.

---

## 4c. Xác thực JWT Bearer (cho client API bên ngoài)

Hệ thống xác thực là **dual-mode** — cùng một endpoint chấp nhận cả hai nguồn:

| Nguồn | Dùng cho | Gửi theo |
|---|---|---|
| **Session** (mặc định) | Blazor Server gọi chính nó (self-HTTP) | Header `X-User-Id` + `X-User-Role` (tự động) |
| **Bearer** | React / Postman / mobile app | Header `Authorization: Bearer <token>` |

Cơ chế chuyển tiếp nằm ở `Program.cs` (`AddPolicyScheme`): nếu request có
`Authorization: Bearer ...` thì dùng `JwtBearer`, ngược lại dùng scheme `Session`.

### Lấy token

```bash
curl -X POST http://localhost:5286/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"email":"leb@petshop.com","password":"staff123"}'
```

```jsonc
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "tokenType": "Bearer",
  "userId": 2,
  "name": "Lê Thị B",
  "email": "leb@petshop.com",
  "role": "staff"
}
```

### Dùng token

```bash
curl http://localhost:5286/api/auth/me -H "Authorization: Bearer <accessToken>"
```

### Cấu hình

Trong `appsettings.Development.json` (hoặc User Secrets):

```jsonc
{
  "JwtSettings": {
    "Issuer": "PetShop",
    "Audience": "PetShopClients",
    "Key": "petshop-prn232-local-dev-signing-key-32bytes-min",  // >= 32 ký tự
    "ExpiresInMinutes": 120
  }
}
```

> Token ký bằng **HMAC-SHA256** (HS256), kiểm tra cả issuer, audience, chữ ký và hạn.
> Mã nguồn: `Services/JwtTokenService.cs` + `Services/SessionHeaderAuthenticationHandler.cs`.

---

## 5. Mật khẩu & dữ liệu seed

Mật khẩu được băm bằng **BCrypt**. Một số tài khoản seed cũ trong DB còn lưu **plaintext**
(vd `staff123`, `123`) — hệ thống xử lý bằng cơ chế **migrate-on-login**:

1. Lần đăng nhập đầu tiên với plaintext → khớp → **ghi lại bằng hash BCrypt**.
2. Từ lần sau trở đi → kiểm tra qua BCrypt.

Nhánh so sánh plaintext **không còn tồn tại vĩnh viễn** trong code. Hành vi này được bảo vệ bởi
`tests/PetShop.Tests/AuthServiceTests.cs`.

---

## 6. Test

```bash
dotnet test
```

| File | Phạm vi |
|---|---|
| `AttendanceServiceTests.cs` | Luật tính công (checkout sớm/muộn/quá giờ, thiếu checkout, làm tròn) |
| `WorkScheduleDedupTests.cs` | Gom dòng lịch trùng, giữ `schedule_id` nhỏ nhất, sắp xếp |
| `AuthServiceTests.cs` | Xác thực BCrypt, migrate-on-login, tài khoản bị khoá, sai mật khẩu |
| `JwtTokenServiceTests.cs` | Phát hành/kiểm tra JWT: claim, chữ ký sai, hết hạn, khoá quá ngắn |

---

## 7. Ghi chú cho nhóm phát triển

Chi tiết về quy ước code, kết quả kiểm tra sức khoẻ codebase và lộ trình sửa nằm trong
[`AGENTS.md`](./AGENTS.md). Tóm tắt:

- **Luật tính công**: xem `Services/AttendanceService.CalculateWorkHours`
- **Gom lịch trùng**: `Services/WorkScheduleDedup.ByStaffDateTimeSlot`
- **Ghi log**: dùng `ILogger<T>` + extension `Services/LoggerExtensions.cs` (`_logger.Line(...)`)
- **Không hardcode `customer_id`**: luôn resolve từ `UserSessionService`
- **EF thay vì SQL thô**: ưu tiên LINQ để có type-safety
- **File lớn**: tách bằng `partial class` (C#) hoặc file `.razor.cs` (Blazor) — xem §2.3 của `AGENTS.md`
- **`.editorconfig`**: quy ước format + nullability warnings là `error`

---

## 8. Giấy phép & công nghệ

Dự án dùng các thư viện mã nguồn mở: EF Core, BCrypt.Net, MailKit, Google.Apis.Auth,
Swashbuckle, xUnit, Handlebars.Net, Microsoft.AspNetCore.OData,
Microsoft.AspNetCore.Authentication.JwtBearer.
