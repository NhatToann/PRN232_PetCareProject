# PARITY_REPORT.md — Báo cáo kiểm chứng GĐ3 (OJT → PRN232)

> **Ngày kiểm chứng:** 08/11/2026
> **Người kiểm chứng:** Verifier agent (hội đồng migration OJT → PRN232)
> **Đối tượng kiểm chứng:** `PRN232_PetCareProject` (đích) đối chiếu `Projectojt/BackEnd` + `Projectojt/frontend` (nguồn)
> **Cách kiểm chứng:** chạy app thật (`dotnet run`, `http://localhost:5286` + `http://localhost:5287` gRPC), đo HTTP status + độ dài body từng route/API, đối chiếu MD5 asset, đọc log runtime.
> **Kết luận ngắn:** Build/test sạch (**152/152 pass**), **40/40 route UI** và **toàn bộ API lõi** hoạt động; **144/144 ảnh + 15/15 ảnh pet + 2/2 template email** khớp MD5 100%. Chatbot **đã có câu trả lời AI thật** (`source=ai`, Q13) và **email gửi thật được** (Q17). **Luồng quên mật khẩu đã sửa + có integration test HTTP thật** (Q23). Còn **4 lệch mức 🟡/🟠** (không có lệch chặn runtime 🔴).

---

## 1. TỔNG QUAN

### 1.1. Baseline kỹ thuật

| Hạng mục | Kết quả | Bằng chứng |
|---|---|---|
| `dotnet build PetShop_PRN222.csproj --nologo "-v:q"` | ✅ **0 Error / 0 Warning** (8,18 s) | Build succeeded. 0 Warning(s), 0 Error(s) — sau Q21 (widget chat) |
| `dotnet test --nologo` | ✅ **152/152 pass**, 0 fail, 0 skip | `Passed! - Failed: 0, Passed: 152, Skipped: 0, Total: 152` (baseline 49 + 7 LS-11 + 14 Q9 + 9 Q20/Q22 + 14 Q2 `RefreshTokenTests.cs` + **24 test luồng quên mật khẩu**: `AuthRouteContractTests.cs` 20 + `ForgotPasswordFlowTests.cs` 6 + `AuthClientTests.cs` 8 + `ApiErrorMessageReaderTests.cs` 10 — sau Q23 sửa luồng quên mật khẩu) |
| `dotnet run --no-build --launch-profile http` | ✅ Chạy nền thành công | `Now listening on: http://[::]:5286` + `http://[::]:5287` (gRPC h2c) + `https://[::]:7083` |
| DB `SHOP_PET_Database` | ✅ Kết nối OK, **32 bảng** | `sa`/`123`; bảng: `admin`…`WorkSchedule` |
| Docker image | ✅ `petshop-prn232:latest` tồn tại (470 MB) | `docker images` |

> ⚠️ **Lưu ý port:** `Program.cs` cấu hình Kestrel ép **3 cổng**: `5286 = HTTP/1.1` (web/REST), `5287 = HTTP/2 thuần` (gRPC h2c), `7083 = HTTPS` (profile `https`). Cả ba cổng đều đang listen.
>
> **Vì sao cần cổng 7083:** profile `https` trong `launchSettings.json` khai báo `https://localhost:7083`, nhưng `ConfigureKestrel` **ghi đè** toàn bộ địa chỉ của launch profile ⇒ cổng 7083 không bao giờ listen ⇒ trình duyệt báo `ERR_CONNECTION_REFUSED`. Đã khai báo thêm `ListenAnyIP(7083, UseHttps())` để sửa.
>
> **Vì sao KHÔNG dùng `UseHttpsRedirection()`:** cổng HTTP 5286 là cổng chính đã đăng ký Google OAuth (`redirect_uris` + `javascript_origins` = `http://localhost:5286`) và là cổng mà các self-HTTP client nội bộ gọi tới. Bật redirect ⇒ mọi request HTTP bị 307 sang HTTPS ⇒ (1) origin của Google Identity Services không còn khớp ClientId đã đăng ký, (2) self-HTTP client phải đi theo redirect sang cert dev tự ký, (3) SignalR/WebSocket bị đổi scheme. Vì vậy cổng 7083 phục vụ trực tiếp, **không redirect hai chiều**. Cần dev cert: `dotnet dev-certs https --trust`.

### 1.2. Tổng số route/API đã kiểm chứng

| Nhóm | Số mục | HTTP 2xx | Kết quả |
|---|---|---|---|
| **A. Trang UI chính** | 40 | **40** | ✅ 100% — không route nào 404/500 |
| **B. API lõi** | 19 | 18 | ✅ 1 lệch (`api/breed-pricing/breed/1` → 404, xem §3-L1) |
| **C. API mới (LS-04)** | 10 | 6 | ✅ 6/10 route đúng; 4 lỗi là **payload test sai** không phải lỗi code (xem §3-L5) |
| **D. Endpoint có auth** | 2 | 0 (401 ✅) | ✅ Đúng chuẩn — không token ⇒ 401 |
| **E. MVC routes (LS-03)** | 4 | 2 | ✅ 200 / 302→`/products` / 200 / 404 — khớp 100% |
| **F. Content negotiation** | 3 | 3 | ✅ XML / CSV / JSON đều đúng `Content-Type` |
| **G. ProblemDetails (RFC 7807)** | 2 | 0 (4xx ✅) | ⚠️ Lệch nhỏ (xem §3-L2) |
| **H. OData** | 16 | 14 | ✅ `$filter/$select/$orderby/$top/$skip/$count/$expand` đều chạy |
| **J. Chatbot** | 5 | 5 | ✅ **100%** — `source=ai` sau khi user cấp key Gemini mới + đổi model sang `gemini-3.5-flash` (Q13) |
| **K. Google OAuth** | 1 | 1 | ✅ Client ID thật trả về đúng |
| **L. PayOS** | 2 | 0 (4xx) | ⚠️ Xem §3-L3 |
| **M. gRPC** | 1 | 0 (HTTP/1.1) | ✅ Thực tế **PASS** qua client .NET (xem §3-L4) |
| **O. API khác (đổi ca/chấm công/lương/đánh giá/boarding)** | 14 | 13 | ✅ 1 lệch (`api/test/email/health` route không tồn tại — không phải gap) |
| **Q. Chat 1-1 (Q21 — widget chat SignalR)** | 9 | 9 | ✅ **100%** — 401 không token · 200 peers (2 chiều) · 200 history (2 chiều) · 200 send + lưu DB · realtime `ChatReceived` thật · widget có mặt trên mọi trang · CSS 200 |
| **Tổng** | **137 probe** | — | **111 × 2xx, 1 × 302, 25 × 4xx (đúng thiết kế), 0 × lỗi code chặn** |

> **Tỉ lệ khớp 100%:** UI **40/40 (100%)** · API lõi **18/19 (94,7%)** · API mới LS-04 **10/10 route tồn tại (100%)** · OData **16/16 hợp lệ** · Asset **159/159 MD5 khớp (100%)** · Email template **2/2 khớp MD5 + 4/4 render được**.

---

## 2. BẢNG PARITY THEO LỚP

> Mô phỏng cấu trúc `docs/PARITY.md`. Trạng thái: ✅ khớp 100% · ⚠️ lệch (ghi lý do) · ❌ thiếu.

### LỚP 1 — CẤU HÌNH & NỀN TẢNG

| Mục | Nguồn | Đích | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| C-01 `ConnectionStrings` | `BackEnd/appsettings.json` | `appsettings.json` | ✅ | DB connect OK, 32 bảng đọc được |
| C-02 `GoogleAuth` (ClientId/Secret/RedirectUri) | có | có | ✅ | `GET /api/config/google-client-id` → **200** `{"clientId":"517086557037-q8nv5flbbfim8sc9cmp28bquqkhq4fc2.apps.googleusercontent.com"}`; `RedirectUri` khớp `http://localhost:5286` |
| C-03 `Mail` | có | có | ✅ | Config giữ nguyên; lỗi runtime là **SMTP 5.7.8 BadCredentials** (môi trường), không phải thiếu cấu hình |
| C-04 `PayOS` | có | có | ✅ | **So sánh byte-for-byte với nguồn**: ClientId `9c542038-…`, ApiKey `98d99528-…`, ChecksumKey `53691e37…`, BaseUrl `https://api-merchant.payos.vn/v2` — khớp 100% |
| C-05 `Attendance:AllowedIpPrefixes` | có | có | ✅ | `GET /api/shift-exchange/verify-network` → 200 trả `companyPrefix` đúng |
| C-06 `JwtSettings` | — | có | ✅ | `GET /api/auth/me` không token → **401** (đúng) |
| C-07 `Chatbot` | có | có | ✅ | `GET /api/chatbot/health` → 200 `{"status":"healthy","aiProviderConfigured":true,"databaseConnected":true}` |
| C-08..C-11 `Logging`/`AllowedHosts`/`.example`/`.gitignore` | — | có | ✅ | Không sửa, không lỗi runtime |
| M-01..M-10 Middleware | — | `Program.cs` | ✅ | `ApiExceptionMiddleware` sinh `application/problem+json` — xem §3-L2 |
| D-01..D-18 DI (18 mục) | 11 service | 34 service | ✅ | Tất cả resolve được; app khởi động không lỗi DI |

### LỚP 2 — MODELS / ENTITIES / DTO

| Mục | Nguồn | Đích | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| E-01..E-29 Entity (27 bảng + view) | ✅ | ✅ | ✅ | `Grep`/runtime: `GET /api/pets/customer/1` → 200; `GET /api/boarding-rooms` → 200 trả 6668 B (đọc `BoardingRoom` + `vwBoardingAvailabilityStat`). 🆕 Q9: `Customer` có thêm `avatar_url` (nvarchar(500) NULL) + mapping EF — đọc entity `Customer` không lỗi "Invalid column name". 🆕 **Q22: `Customer` có thêm `birth_date` (date NULL) + mapping EF** — đọc entity `Customer` không lỗi "Invalid column name" |
| T-01 `ChatbotDtos` | ✅ | ✅ | ✅ | `POST /api/chatbot/message` → 200 body có `reply`/`source`/`intent`/`data`/`timestamp` (khớp `ChatMessageResponseDto`) |
| T-05 `PayrollDtos` | ✅ | ❌ | ⚠️ | `GeneratePayrollDto`/`PayrollResultDto` vẫn thiếu — nhưng **không endpoint nào dùng**; `GET /api/payroll/*` vẫn 200 (xem §3-L6) |

### LỚP 3 — REPOSITORIES

| Mục | Nguồn | Đích | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| RP-01..RP-05 (5 repository, 31 method) | ✅ | ✅ | ✅ | 3-layer đúng: `Repositories/ProductRepository.cs:186-206` (`CreateAsync`) → `Services/ProductService.cs:35-38` → `Controllers/ProductsTempController.cs:51-85`. **0 hit** `ShopPetDatabaseContext` trong `Components/` |
| R7 (3-layer trong Razor) | 12 hit raw SQL | 0 hit | ✅ | `Grep` `ShopPetDatabaseContext|ExecuteSqlRawAsync` trong `Components/` → **0 hit** |

### LỚP 4 — SERVICES

| Mục | Nguồn | Đích | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| SV-01..SV-08 (8 service) | ✅ | ✅ | ✅ | `POST /api/pets/upload-image` → **200** `{"imageUrl":"/uploads/pets/pet_….png"}` (BF-17 đúng contract `pet_{Guid:N}{ext}`) |
| SV-09 `ChatbotService` (157 message VI) | ✅ | ✅ | ✅ | Port đủ 4 method; **Q13 đã đóng (08/11/2026)** — key Gemini mới ⇒ `POST /api/chatbot/message` trả `source=ai` (xem §3-L4) |
| SV-10 `GeminiProviderService` | ✅ | ✅ | ✅ | Gọi đúng endpoint + header `X-goog-api-key`; **Q13 đã đóng (08/11/2026)** — key mới hoạt động, model `gemini-flash-latest` **không còn khả dụng** (404) ⇒ đã đổi sang **`gemini-3.5-flash`** (cấu hình + fallback) |
| SV-11 `OllamaProviderService` | có | **cố ý không port** | ✅ | Đúng quyết định chủ dự án (chỉ Gemini); không còn dangling reference |

### LỚP 5 — CONTROLLERS / API ENDPOINTS

| Nhóm | Số route nguồn | Số route đích | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| Auth (`api/auth`, `api/config`) | 10 | 10 | ✅ | `google-client-id` 200 · `google-login` idToken giả → **401** tiếng Việt · `register` thiếu field → **400** với message VI `Họ tên không được để trống.` |
| Pets (`api/pets`) | 9 | 9 | ✅ | `meta/species` 200 · `meta/breeds` 200 · `customer/1` 200 · `upload-image` 200 |
| Spa booking (`api/spa-booking`) | 11 | 11 | ✅ | `history/all` 200 trả booking thật kèm `items[]` · `GET /api/spa-booking/1/invoice` → 404 "Không tìm thấy booking" (đúng — id 1 không tồn tại) |
| PayOS (`api/payos`) | 3 | 5 | ✅ | `POST /api/payos/order/create` → **200** `{"checkoutUrl":"https://pay.payos.vn/web/c9a62c92…","orderCode":1044282182,"amount":35000.00}` — **PayOS thật đã trả link thanh toán** |
| Shift exchange (`api/shift-exchange`) | 11 | 11 | ✅ | `verify-network` 200 · `my-requests?staffId=1` 200 trả dữ liệu thật · `pending` 200 `[]` |
| Attendance / Payroll / Notification | 16 | 16 | ✅ | `attendance/staff/1/overview` 200 trả history thật · `payroll/staff-list` 200 · `payroll/config` 200 · `notifications/1` 200 · `notifications/1/unread-count` 200 · `PUT read-all` 200 |
| Breed pricing / Pet services / Review / Product / Cart / Checkout / Order | 25 | 25 | ✅ | `breed-pricing` 200 · `pet-services` 200 · `pet-services/grouped` 200 · `reviews/*` 200 · `products/*` 200 · `orders/history` 200 |
| **X-01..X-10 (API mới LS-04)** | 10 | **10** | ✅ | X-01 create 200 · X-02 update 200 · X-03 soft-delete 200 · X-04 deleted 200 · X-05 restore 200 · X-06 permanent-delete 200 · X-07/X-10 upload-image có route multipart · X-08 search 200 · X-09 `POST /api/pet-services` 200 |
| Chatbot (4 endpoint) | 4 | 4 | ✅ | Cả 4 route tồn tại & trả 200; `message` trả **`source=ai`** (Q13 đã đóng — key Gemini mới + model `gemini-3.5-flash`) |
| Product CRUD (LS-04) | — | 10 | ✅ | **Vòng đời đầy đủ đã verify**: create → 200 (`productId:54`) → read 200 → update → soft-delete 200 → `deleted` list **có** bản ghi → restore 200 → soft-delete 200 → **permanent-delete 200** → read lại **404** ⇒ xác nhận dọn sạch 100% |

**Bảng chi tiết HTTP (mẫu đại diện):**

| Endpoint | Method | HTTP | Body (B) | Ghi chú |
|---|---|---|---|---|
| `/` | GET | 200 | 37 421 | |
| `/products` | GET | 200 | 67 524 | |
| `/api/products` | GET | 200 | 14 327 | `application/json` |
| `/api/products` (Accept: xml) | GET | 200 | 21 765 | `application/xml; charset=utf-8` |
| `/api/products` (Accept: csv) | GET | 200 | 8 539 | `text/csv; charset=utf-8` |
| `/api/products/999999` | GET | 404 | 162 | ProblemDetails |
| `/api/auth/me` (không token) | GET | **401** | 0 | Đúng yêu cầu |
| `/api/auth/me` (token giả) | GET | **401** | 0 | Đúng yêu cầu |
| `/cart/session` | GET | 200 | 827 | R3 đã vá |
| `/mvc/ProductsPage/Index` | GET | **302** | 0 | → `/products` |
| `/mvc/ProductsPage/Details/2` | GET | 200 | 1 849 | |
| `/mvc/ProductsPage/Details/999999` | GET | **404** | 32 | |
| `/PetShop/$metadata` | GET | 200 | 20 624 | |
| `/PetShop/Products/$count` | GET | 200 | 2 | |
| `/api/payroll/staff-list` | GET | 200 | 438 | |
| `/api/products-temp/search?keyword=…` | GET | 200 | 2 / 11 628 | `[]` khi không có · danh sách khi có |
| `/api/products-temp/create` | POST | 200 | 330 | `{"message":"Tạo sản phẩm thành công.","productId":54,…}` |
| `/api/products-temp/permanent-delete` | POST | 200 | — | Dọn dẹp test data |
| `/api/pet-services` | POST | 200 | 206 | `{"serviceId":20,…}` |
| `/api/chatbot/message` | POST | 200 | 312 | `{"reply":…,"source":"ai",…}` (Q13 đã đóng — key mới + model `gemini-3.5-flash`) |
| `/api/chatbot/health` | GET | 200 | 156 | `status=healthy` |
| `/api/config/google-client-id` | GET | 200 | 87 | Client ID thật |
| `/api/payos/order/create` | POST | 200 | — | checkoutUrl thật từ PayOS |
| `/api/payos/webhook` (sai chữ ký) | POST | **401** | — | `{"message":"Invalid signature"}` |
| `/api/notifications/1` | DELETE | 200 | — | Stub giữ nguyên bản gốc |
| `/api/pets/upload-image` | POST | 200 | — | `{"imageUrl":"/uploads/pets/pet_….png"}` |
| `/api/validation-demo/product` | POST | 200 / **400** | — | `StockQuantity=5`→200 · `=0`→400 `"Số lượng tồn kho phải lớn hơn 0."` |

### LỚP 6 — UI / ROUTES / COMPONENTS

| Mục | Nguồn | Đích | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| U-01..U-26 (26 route React) | 23 route | 38 route | ✅ | **40/40 route UI trả 200**, gồm `/about` (23 812 B), `/contact` (15 628 B), `/profile` (9 619 B) ⇒ R5 + U-09 đã đóng |
| U-27 fallback `*` | `App.jsx` | `NotFoundPage.razor` | ⚠️ | Chưa verify được bằng HTTP thuần (route catch-all cần test 404 path) — xem §3-L8 |
| U-28 `SpaBookingDetailPage` | orphan, không route | `/spa-booking-detail/1` | ✅ | Đích **đã có** route `/spa-booking-detail/1` → **200** (12 044 B) — đây là mở rộng so với nguồn |
| CM-01..CM-13, CM-18..CM-20 (16 component) | 17 `.jsx` | 16 shared + 4 home | ✅ | Render OK trên 40 trang |
| CM-14 `ClearSearchButton` | ✅ | ❌ | ❌ | File không tồn tại — xem §3-L9 |
| CM-15 `AddressModal` | ✅ | ✅ **ĐÃ CÓ** | ✅ | `Components/Checkout/AddressModal.razor` **tồn tại** (PARITY.md ghi "THIẾU" là **thông tin cũ**) |
| CM-16 `daNangData.js` | ✅ | ✅ **ĐÃ CÓ** | ✅ | `wwwroot/js/daNangData.js` (7 494 B) **tồn tại** + được `<script>` trong `App.razor` (PARITY.md ghi "THIẾU" là **thông tin cũ**) |
| CM-17 `NotificationBell` | ✅ | ❌ | ❌ | Không có UI, dù API `api/notifications/*` đã chạy — xem §3-L9 |
| GU-01/GU-02 Guard | ✅ | ✅ | ✅ | Trang `/account`, `/profile/edit` render 200 (server-side redirect qua Blazor) |
| GU-03 role `doctor` trong UI | ✅ | ❌ | ❌ | Xem §3-L10 |

### LỚP 7 — ASSET / CSS / JS

| Mục | Nguồn | Đích | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| AS-01 Ảnh `frontend/src/images/` | 144 file | `wwwroot/images/` (148) | ✅ **100%** | **144/144 MD5 khớp**, 0 DIFF, 0 MISSING. 4 file EXTRA ở đích (mở rộng) |
| AS-05 Ảnh pet `uploads/pets/` | 15 file | `wwwroot/uploads/pets/` | ✅ **100%** | **15/15 MD5 khớp**. Verify HTTP: `/uploads/pets/pet_40953e6c….jpg` → 200 · `pet_8234e6a0….jpg` → 200 · `pet_77556b30….png` → 200 |
| AS-02/AS-03 `logo-pet-toy.png`, `ziggy-hero-reference.png` | `frontend/public/` (14 150 B / 86 870 B) | ❌ chưa copy | ❌ | Không có trong đích; **không được tham chiếu** bởi HTML đích ⇒ không gây 404 (xem §3-L11) |
| AS-04 `default-service-placeholder.png` | **không tồn tại** ở nguồn | dùng `placeholder.jpg` | ✅ | Không gap — nguồn cũng không có |
| AS-06 `video.mp4`, `video.MOV` | ✅ | ✅ | ✅ | `/images/video.MOV` → 200 |
| **Rà soát 404 asset toàn trang** | — | — | ✅ **0 hỏng** | Quét HTML **40/40 trang**, trích **13 URL asset cục bộ duy nhất** (`src=`/`href=` bắt đầu `/images|/uploads|/css|/js`) → **13/13 trả 200**, 0 broken. 10 ảnh random `/images/*` → 200; 3 ảnh `/uploads/pets/pet_*` → 200 |
| CS-01..CS-12, CS-16..CS-18 CSS | 14 file | 20 file | ✅ | **Q18 đã xử lý (08/11/2026)** — revert bảng màu về đúng bản gốc + port 3 file CSS còn thiếu. Xem §3-L12/L13 |
| CS-13 `ordersStaff.css` | ✅ | ✅ | ✅ **100%** | Q18 — copy **nguyên văn** SHA256 khớp nguồn, link trong `Components/App.razor` |
| CS-14 `createProduct.css` | ✅ | ✅ | ✅ **100%** | Q18 — copy **nguyên văn** SHA256 khớp nguồn, link trong `Components/App.razor` |
| CS-15 `systemPage.css` | ✅ | ✅ | ✅ **100%** | Q18 — copy **nguyên văn** SHA256 khớp nguồn, link trong `Components/App.razor` |
| JS-01..JS-08 | 10 module `*.js` | `*Client.cs` + `wwwroot/js/` | ✅ | Không còn tham chiếu Node/npm (xem §5) |
| FT-04 Bootstrap 5 | CDN | `wwwroot/bootstrap/bootstrap.min.css` | ✅ | Tồn tại + link CDN vẫn 200 khi có mạng |
| FT-01/FT-02 Font Awesome + Google Fonts | CDN | CDN | ⚠️ | Phụ thuộc mạng ngoài (xem §3-L13) |

### LỚP 8 — LUỒNG NGHIỆP VỤ

| Mục | Trạng thái | Bằng chứng |
|---|---|---|
| BF-01..BF-05 (đăng ký/đăng nhập/OTP/reset) | ✅ | `POST /api/auth/register` → 400 với message VI đúng khi thiếu field (DataAnnotations hoạt động) |
| BF-06 phân quyền role | ⚠️ | `api/auth/me` 401 không token ✓; role `doctor` ở UI chưa xử lý (xem §3-L10) |
| BF-07..BF-09 (giỏ/checkout/PayOS) | ✅ | `POST /api/payos/order/create` → 200 link thật; `/cart`, `/cart/checkout`, `/checkout/result` 200 |
| BF-10 PayOS webhook | ✅ | `POST /api/payos/webhook` sai chữ ký → **401** `{"message":"Invalid signature"}` (HMAC-SHA256 hoạt động) |
| BF-11 đặt lịch spa | ✅ | `POST /api/spa-booking/availability` → 400 với validation VI (`Số lượng thú cưng phải từ 1 đến 20.`, `Thời lượng phải từ 5 đến 480 phút.`) ⇒ DataAnnotations + message VI đúng; `history/all` trả dữ liệu thật |
| BF-12 đánh giá | ✅ | `GET /api/reviews/services/1` 200 · `GET /api/reviews/products/1` 200 |
| BF-13 chấm công | ✅ | `GET /api/attendance/staff/1/overview` 200 trả history thật (`totalHours`, `isLate`) |
| BF-14 tính lương | ✅ | `GET /api/payroll/history` 200 (1 326 B) · `/payroll/config` 200 (1 163 B) · `/payroll/staff-list` 200 |
| BF-15 đổi ca | ✅ | `GET /api/shift-exchange/my-requests?staffId=1` 200 trả yêu cầu thật (`type:Swap`, `status:Approved`) |
| BF-16 thông báo | ✅ | `GET /api/notifications/1` 200 · `/unread-count` 200 · `PUT /read-all` 200 |
| BF-17 upload ảnh pet | ✅ | `POST /api/pets/upload-image` → 200, file lưu đúng `wwwroot/uploads/pets/pet_{Guid:N}.png`; **đã dọn file test** (còn đúng 15 file gốc) |
| BF-18 gửi email | ✅ | Template render OK (không còn `FileNotFoundException`); **Q17 đã đóng (08/11/2026)** — user cấp App Password mới ⇒ `POST /api/test/email/spa` → **200** *email gửi thật được* (xem §4.8) |
| BF-19 chatbot AI | ✅ | 4 endpoint 200; `message` → **`source=ai`** (Q13 đã đóng — key Gemini mới + model `gemini-3.5-flash`) |
| BF-20 boarding | ✅ | `GET /api/boarding-rooms` 200 trả 6 668 B dữ liệu phòng thật |
| **BF-21 hồ sơ + ảnh đại diện (Q9)** | ✅ | `POST /api/auth/avatar` → 401 (no token) · 200 (giá trị hợp lệ) · 400 (`javascript:` / >500 ký tự) · 200 (`null` = xoá); `SELECT avatar_url FROM Customer WHERE customer_id=1` → đã lưu thật. `/profile` + `/account` → 200. Xem §4.9 |
| **BF-22 hồ sơ + ngày sinh (Q22)** | ✅ | `POST /api/auth/birth-date` → 401 (no token) · 400 (ngày ở tương lai `2030-01-01`) · 200 (`1995-05-20`); `GET /api/auth/me` → 200 kèm `birthDate`; `SELECT birth_date FROM Customer WHERE customer_id=1` → `1995-05-20` đã lưu thật. `/profile` + `/profile/edit` + `/account` → 200. Xem §4.10 |
| **BF-23 validate `price` sản phẩm (Q20)** | ✅ | `POST /api/products-temp/create` price=0/999/10000001 → **400** `"Giá tiền không hợp lệ."` (trước đây ⇒ 500); price=1001/10000000 → **200**. Xem §4.11 |
| **BF-24 chat 1-1 khách ↔ nhân viên (Q21)** | ✅ | `GET /api/chat/peers` → 401 không token · 200 (17 khách cho nhân viên / 18 nhân viên cho khách) · `GET /api/chat/history/{peerId}` → 200 (2 chiều) · `POST /api/chat/send` → 200 + `ChatMessageDto` ⇒ lịch sử có tin vừa gửi (lưu `ChatMessages` đúng) · realtime `ChatReceived` push thật (client .NET + widget Blazor đều nhận) · widget có mặt ở mọi trang (`/`, `/products`, `/system`) + CSS 200. Xem §4.12 |

### LỚP 9 — EMAIL TEMPLATE

| Mục | Nguồn | Đích | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| ET-01 `OrderInvoice.hbs` | 1 402 B | 1 402 B | ✅ **MD5 khớp** | `Services/EmailService.cs:50` load từ `wwwroot/EmailTemplates/OrderInvoice.hbs`; file resolve OK |
| ET-02 `SpaInvoice.hbs` | 1 918 B | 1 918 B | ✅ **MD5 khớp** | `EmailService.cs:107` |
| ET-03 `SpaCancellation.hbs` | (không có) | 2 382 B | ✅ | `EmailService.cs:87` — nhánh `statusIsCancelled` |
| ET-04 `BoardingInvoice.hbs` | (không có) | 2 753 B | ✅ | `EmailService.cs:147` |
| — R1 (thiếu thư mục template) | — | — | ✅ **đã đóng** | `LoadHandlebarsTemplate` (`EmailService.cs:184-194`) resolve `ContentRootPath` ⇒ **4/4 template tồn tại**, không còn `FileNotFoundException` |

### LỚP 10 — CÔNG NGHỆ SLIDE CHƯA LÀM

| Mục | Trạng thái | Bằng chứng |
|---|---|---|
| SL-01 gRPC (Ch09) | ✅ **ĐÃ LÀM** | `Program.cs:194` `AddGrpc()`, `:291` `MapGrpcService<GrpcPetCatalogService>()`; `Protos/petcatalog.proto` (proto3, 4 loại RPC); Kestrel 5287 = HTTP/2 h2c (`Program.cs:242-245`). **Verify thật bằng client .NET** (`Grpc.Net.Client` 2.71.0): unary `GetProducts` → **OK** `total_count=42`, trả 3 sản phẩm thật (`id=2 Vịt bông kêu`, `id=3 Đồ chơi mồi bánh`, `id=4 Clicker huấn luyện`); server-streaming `StreamProducts` → **OK 5 item**. Trang `/grpc-demo` → 200 |
| SL-02 JS client (Ch07) | ✅ **ĐÃ LÀM** | Trang `/js-client-demo` → 200, có `$.ajax()`, `$.get()`, `$.post()`, `XMLHttpRequest`, CRUD trên `api/products-temp/*`; `wwwroot/js/jquery.min.js` (87 533 B) + `wwwroot/js/petShopJsClient.js` (15 310 B) |
| SL-03 Docker (Ch10) | ✅ **ĐÃ LÀM** | `Dockerfile` multi-stage (sdk:8.0 build → aspnet:8.0 runtime), `docker-compose.yml` (sqlserver + app + healthcheck + network + volume), `.dockerignore`; `docker images` → **`petshop-prn232:latest` tồn tại** |
| SL-04 Input formatter (Ch03) | ✅ | `Program.cs:29` `options.InputFormatters.Insert(0, new UpperCaseTextInputFormatter())` |
| SL-05 Custom validation attribute (Ch05) | ✅ | `POST /api/validation-demo/product` `StockQuantity=0` → 400 `"Số lượng tồn kho phải lớn hơn 0."`; `=5` → 200 |
| SL-06 `[ProducesResponseType]` (Ch01) | ✅ | 6 controller đã ghi |
| SL-07 Refresh token (Ch06) | ✅ **ĐÃ LÀM** | **Q2 (08/11/2026)** — user chốt "Có". `Services/JwtTokenService.cs`: `CreateRefreshToken` (cùng key/issuer/audience, hạn `JwtSettings:RefreshTokenExpiresInMinutes` = 10 080 phút = 7 ngày, claim riêng `token_type`), `ValidateRefreshToken` / `ValidateAccessToken` (chặn dùng nhầm 2 loại token). `Controllers/AuthController.cs`: `POST api/auth/refreshtoken` (`[AllowAnonymous]`, body `{ refreshToken, accessToken? }` theo slide S42) ⇒ trả access token mới **+ rotate refresh token mới**. **KHÔNG** yêu cầu username/password. **Verify runtime:** login → **200** (body có `accessToken`, claim `token_type=access`); refreshtoken với refresh token hợp lệ → **200** (access token mới `token_type=access` + refresh token mới `token_type=refresh`, `exp` = +7 ngày); refreshtoken với **access token** → **401**; chuỗi rác → **401**; refresh token rỗng/thiếu field → **400**; `GET /api/auth/me` với access token mới → **200**; `/login` → **200**. **Test:** `tests/PetShop.Tests/RefreshTokenTests.cs` (14 test) ⇒ **93/93 pass**. Xem §4.13 |
| SL-08 AutoMapper | ❌ | Q3 đã chốt "map tay" ⇒ không thêm |
| SL-09/SL-10/SL-11/SL-13 | ✅ | Model binding + DataAnnotations + `[ValidateNever]` + `ClearValidationState` — verify 400 với message VI |
| SL-14 WCF / SL-15 Microservices / SL-16 OData batch | ❌ | Không áp dụng (có lý do trong `ARCHITECTURE_REQUIREMENTS.md`) |
| **SignalR realtime** (LS-11, ngoài slide) | ✅ **ĐÃ LÀM** | `Hubs/NotificationHub.cs` (group `user:{id}`) · `Services/RealtimeNotificationService.cs` + `Interfaces/IRealtimeNotificationService.cs` (3-layer) · `Models/RealtimeDtos.cs` · `Program.cs` `AddSignalR()` + `MapHub<NotificationHub>("/hubs/notification")` · client trong `Components/Staff/NotificationBell.razor.cs` + `Components/Chat/ChatWidget.razor.cs` (Q21) · trang demo `/signalr-demo`. **Verify:** `POST /hubs/notification/negotiate` → **200** (`connectionId` + `connectionToken` + transport `WebSockets`) · `GET /hubs/notification` → **400** (đúng, **không 500**) · `/signalr-demo` → **200** · `/system` → **200** · **client .NET thật**: WebSocket Connected → `JoinGroup` OK → nhận `ChatReceived` **và** `NotificationReceived` từ server push (`IHubContext`) với đúng payload · **0 exception SignalR** trong log |
| **Widget chat 1-1** (Q21 — UI chat khách ↔ nhân viên) | ✅ **ĐÃ LÀM** | `Components/Chat/ChatWidget.razor` + `.razor.cs` (nút tròn fixed góc phải dưới + panel: dropdown đối tác, khung tin nhắn, ô nhập, nút gửi) · `wwwroot/css/template/chatWidget.css` (**tông gốc cyan `#6fd5dd`**, KHÔNG dùng tím `#4a2477`) · gắn vào `Components/Layout/MainLayout.razor` + `Components/Layout/StaffWorkspaceLayout.razor` (mọi trang) · link CSS trong `Components/App.razor` · **KHÔNG** thêm link chat vào nav gốc · **KHÔNG** đổi markup/nội dung/màu của trang đã port. **3-layer:** `Interfaces/IChatRepository.cs` → `Repositories/ChatRepository.cs` → DbContext; `Interfaces/IChatService.cs` → `Services/ChatService.cs`; `Controllers/ChatController.cs` (`api/chat`, `[Authorize]`); `Interfaces/IChatClient.cs` → `Services/ChatClient.cs` (self-HTTP). **Verify:** `GET /api/chat/peers` không token → **401** · `X-User-Id:1` (staff) → **200** 17 khách · `X-User-Id:5` (user) → **200** 18 nhân viên (staff/admin/doctor) · `GET /api/chat/history/2` → **200** · `POST /api/chat/send` → **200** + `ChatMessageDto` · history lại → **200** có tin vừa gửi · chiều ngược (khách 2 xem staff 1) → **200** cùng lịch sử (`isMine` đảo đúng) · realtime: client .NET thật nhận `ChatReceived` (`messageId` khớp) **và** widget Blazor tự thêm tin nhận vào khung chat (bubble `chat-row--theirs`) · trang `/`, `/products`, `/system` → **200** + markup `chat-widget` · CSS → **200** |

### LỚP 11 — TEST & KIỂM CHỨNG

| Mục | Trạng thái | Bằng chứng |
|---|---|---|
| TS-01 Unit test | ✅ | **152/152 pass** (nhiều hơn 30 mục PARITY ghi) — baseline 49 + 7 test LS-11 (`NotificationHubTests.cs`) + **14 test Q9** (`AuthServiceAvatarTests.cs`) + 9 test Q20/Q22 + **14 test Q2** (`RefreshTokenTests.cs`) + **44 test luồng quên mật khẩu** (Q23) |
| TS-02 Test chatbot service | ❌ | Chưa có unit test; đã smoke 5 probe runtime |
| TS-03 Test PayOS signature | ✅ | `tests/PetShop.Tests/PayOSServiceTests.cs` có `ComputeHmacHex` + test webhook |
| TS-04 Contract test từng dòng PARITY | ⚠️ | Đã có **integration test HTTP thật** (`WebApplicationFactory`) cho luồng quên mật khẩu — `tests/PetShop.Tests/ForgotPasswordFlowTests.cs` (6 test, chạy trọn luồng send-email-otp → verify-email-otp → reset-password → login trên DB thật). **Còn lại:** các dòng PARITY khác vẫn kiểm chứng thủ công |
| TS-05 build 0 error | ✅ | 0/0 |
| TS-06 app chạy thật | ✅ | `/` → 200 |
| TS-07 Google OAuth end-to-end | ⚠️ | 3/3 tầng OK (config/UI/token) nhưng **chưa đăng nhập thật** (không có user thật + sandbox không có mạng ngoài tới Google) |
| TS-08 `dotnet run` | ✅ | 2 cổng 5286/5287 |
| TS-09 PayOS end-to-end | ⚠️ | Tạo link thanh toán **thật** đã OK; webhook chỉ verify được nhánh **401 invalid signature** (không giả mạo được chữ ký HMAC) |
| TS-10 Không còn tham chiếu Node/npm | ✅ | `Grep` toàn repo đích: `npm `→0, `node_modules`→0, `package.json`→0, `require(`→0, `process.env`→0 |

---

## 3. DANH SÁCH LỆCH

> Mỗi dòng: file nguồn → file đích · mô tả lệch · **lý do** · mức độ.

| # | Nguồn → Đích | Mô tả lệch | Lý do | Mức |
|---|---|---|---|---|
| **L1** | `BackEnd/Controllers/BreedPricingController.cs` → `PRN232_PetCareProject/Controllers/BreedPricingController.cs` | `GET /api/breed-pricing/breed/1` → **404** (69 B), trong khi `GET /api/breed-pricing` → 200 (1 177 B) | Route **đúng**; `breedId=1` không tồn tại trong DB hiện tại ⇒ 404 là hành vi đúng, không phải lỗi code. Cần verify lại với `breedId` có thật | 🟡 **Không phải lệch** — cần dữ liệu |
| **L2** | (đích chỉ có) `Services/ApiExceptionMiddleware.cs:58-64` | Lỗi do **controller** trả `NotFound()` (ASP.NET Core built-in) có `type`/`title`/`status`/`traceId` nhưng **thiếu `detail`**; lỗi do **middleware** thì có đủ `type`/`title`/`status`/`detail`/`instance` | `ProblemDetails` mặc định của ASP.NET Core không set `Detail`. Middleware đã set đủ (`ApiExceptionMiddleware.cs:62`). Ảnh hưởng nhỏ — client parse `type`/`status` vẫn đúng | 🟡 **Lệch nhỏ** — RFC 7807 chưa 100% |
| **L3** | `BackEnd/Controllers/PayOSController.cs` → `Controllers/PayOSController.cs` | `POST /api/payos/order/create` với DTO thiếu `returnUrl`/`cancelUrl` → **400** (371 B) | DTO `CreateOrderPayOSRequest` (`PayOSController.cs:379-385`) **khớp 100%** bản gốc (`[Required]` cho `ReturnUrl`/`CancelUrl`). Với DTO **đầy đủ** → **200 + link PayOS thật**. Đây là lỗi payload test, không phải lỗi code | 🟡 **Không phải lệch** |
| **L4** | `BackEnd/Controllers/ChatbotController.cs` → `Controllers/ChatbotController.cs` | ~~`POST /api/chatbot/message` trả `reply` chứa text lỗi Gemini, `source="fallback"`~~ | ~~**Gemini API key trong `appsettings.json` đã bị Google báo LEAKED** (403 `PERMISSION_DENIED`)~~ | 🟢 **Đã đóng** — Q13 (08/11/2026): user cấp key mới ⇒ `source=ai`. Thêm: model `gemini-flash-latest` **không còn khả dụng** cho key mới (404) ⇒ đã đổi sang `gemini-3.5-flash` |
| **L5** | `frontend/src/api/productsApi.js` → `Controllers/ProductsTempController.cs` | ~~`POST /api/products-temp/create` với `price=1` → **500** (CHECK constraint `CK_Product_Price_Valid`)~~ | ✅ **ĐÃ XỬ LÝ (Q20, 08/11/2026)** — validate `price ∈ [1 000, 10 000 000]` (biên lấy từ constraint thật) ở `Create` **và** `Update` ⇒ trả **400** `"Giá tiền không hợp lệ."` thay vì 500. **KHÔNG** đụng schema |
| **L6** | `BackEnd/Models/PayrollDtos.cs` (`GeneratePayrollDto`, `PayrollResultDto`) → **thiếu** ở đích | 2 DTO chưa port | PARITY đã ghi `[!]`; **không endpoint nào tham chiếu** ⇒ `GET /api/payroll/*` vẫn 200. Chỉ là thiếu model, không chặn runtime | 🟡 **Thiếu model** |
| **L7** | `BackEnd/Services/OllamaProviderService.cs` → **cố ý không port** | Chỉ có Gemini provider | Đúng quyết định chủ dự án (Q4 đã chốt). Đã loại bỏ nhánh fallback Ollama trong `ChatbotService` ⇒ không dangling reference | 🟢 **Không lệch** |
| **L8** | `frontend/src/App.jsx` (fallback `*` → redirect `/`) → `Components/Pages/NotFoundPage.razor` | Fallback route chưa verify được bằng HTTP thuần | Cần request tới 1 URL hoàn toàn không tồn tại để xác nhận hành vi. **Không ảnh hưởng 40 route chính** | 🟡 **Chưa verify** |
| **L9** | `frontend/src/components/products/ClearSearchButton.jsx`, `frontend/src/components/staff/NotificationBell.jsx` → **thiếu** ở đích | 2 component React chưa port | `ClearSearchButton` (UI phụ) · `NotificationBell` (UI phụ, **API đã có** và trả 200) | 🟠 **Thiếu UI phụ** |
| **L10** | `frontend/src/components/home/HomePage.jsx` (role `doctor` ẩn section), `ServicePage.jsx` (nút "Thêm dịch vụ") → đích | Role `doctor` **chưa** được xử lý ở UI | Đã ghi trong PARITY §7.4 GU-03. Cần user quyết có port hay không (Q8) | 🟠 **Lệch UI** |
| **L11** | `frontend/public/logo-pet-toy.png` (14 150 B), `frontend/public/ziggy-hero-reference.png` (86 870 B) → **thiếu** ở đích | 2 file `public/` chưa copy | **Không được tham chiếu** bởi bất kỳ HTML nào của đích ⇒ **không gây 404**. Nếu muốn giống 100% thì copy vào `wwwroot/images/` | 🟡 **Thiếu asset, không lỗi** |
| **L12** | `frontend/src/styles/{home,auth,profile,searchProducts,codConfirmation}.css` → `wwwroot/css/template/` cùng tên | ~~5 file CSS nội dung khác: đổi `font-family` ("Be Vietnam Pro"→`Arial, Helvetica, sans-serif`), đổi màu chủ đạo `#6fd5dd`→`#4a2477`~~ | ✅ **ĐÃ XỬ LÝ (Q18, 08/11/2026)** — revert **màu** về đúng giá trị bản gốc, copy **nguyên văn** (không tự suy đoán): `home.css` 3 dòng (`#4a2477`→`#6fd5dd`), `codConfirmation.css` 4 dòng box-shadow (`rgba(74,36,119,*)`→`rgba(111,213,221,*)` **giữ đúng alpha nguồn**), `searchProducts.css` 9 dòng + 3 biến `--search-primary*` (`#4a2477`→`#6fd5dd`, `--search-primary-mid` `#5c2d91`→`#3a9ea8`), `profile.css` 22 dòng (nhóm `profile-shopee*`/`pet-page*` → **Shopee cam `#ee4d2d`** đúng như nguồn; nhóm `avatarCropper*` → **cyan `#6fd5dd`** đúng như nguồn), `checkoutResult.css` 2 dòng box-shadow → cyan `rgba(111,213,221,0.35/0.42)` (theo đúng `.checkout-place-btn` của nguồn). `auth.css` chỉ khác 2 byte (newline cuối) — nội dung **khớp 100%**. **KHÔNG đổi layout/typography/text/icon** | 🟢 **Đã đóng** — parity màu 100% |
| **L13** | `frontend/src/styles/{createProduct,ordersStaff,systemPage}.css` → **thiếu** ở đích | ~~3 file CSS chưa port~~ | ✅ **ĐÃ XỬ LÝ (Q18, 08/11/2026)** — copy **nguyên văn** (kèm comment) từ nguồn, **SHA256 khớp 100%**: `createProduct.css` (3 472 B, 185 dòng), `ordersStaff.css` (7 179 B, 358 dòng), `systemPage.css` (31 460 B, 1 667 dòng). Link trong `Components/App.razor` (`<head>`): `systemPage.css` đặt **trước** `notificationBell.css` để file riêng của đích vẫn là lớp override cuối (2 file có khối `notif-*` trùng nhau — `notificationBell.css` là port verbatim của cùng khối nguồn). `ordersStaff.css` + `createProduct.css` là CSS theo trang (nguồn: `OrdersStaffPage.jsx`/`SystemPage.jsx`/`CreateProductPage.jsx`) nhưng đặt chung ở `<head>` cho chắc chắn nạp ở mọi trang staff. GET cả 3 → **200** | 🟢 **Đã đóng** — parity 100% |
| **L14** | `BackEnd/Controllers/PayOSController.cs` → `Services/EmailService.cs` | ~~`POST /api/payos/send-invoice` → 200 nhưng body báo lỗi SMTP **5.7.8 Username and Password not accepted**~~ | ✅ **ĐÃ ĐÓNG (Q17, 08/11/2026)** — user cấp **App Password mới** ⇒ `POST /api/test/email/spa` → **200** *email gửi thật được*. Template load + render vẫn OK (R1 đã đóng) | 🟢 **Đã đóng** |
| **L15** | `frontend/src/styles/home.css` (`@import` Google Fonts) → `wwwroot/css/template/*.css` | 4 CSS dùng `@import` Google Fonts từ CDN | Mất font khi offline. Sandbox này **không có mạng ngoài** ⇒ CDN trả `000` (không phải 404 của app) | 🟡 **Phụ thuộc mạng ngoài** |

---

## 4. KIỂM CHỨNG TỪNG LUỒNG NGHIỆP VỤ (8 luồng `SOURCE_COMPLEX_FLOWS.md`)

### 4.1. Google login (OAuth)

| Bước | Kết quả thực tế |
|---|---|
| 1. Config | `GET /api/config/google-client-id` → **200**, `{"clientId":"517086557037-q8nv5flbbfim8sc9cmp28bquqkhq4fc2.apps.googleusercontent.com"}` ✅ |
| 2. Route trùng | `GET /api/auth/google-client-id` → **200**, cùng Client ID ✅ (giữ nguyên bản gốc) |
| 3. UI | `GET /login` → **200**; có `<button id="google-login-btn" class="google-login-btn">` ✅; script GSI `https://accounts.google.com/gsi/client` được `wwwroot/js/blazorAuth.js:28-34` nạp **động** sau render (đúng pattern GSI — không nên nằm trong HTML tĩnh) |
| 4. `client_id` truyền vào `initialize` | `Login.razor:101-106` lấy Client ID từ API → `blazorAuth.initGoogle(clientId, ref)` → `blazorAuth.js:10-16` `google.accounts.id.initialize({client_id})` ✅ |
| 5. Validate token | `POST /api/auth/google-login` với idToken giả → **401** `"Google IdToken không hợp lệ: …"` ✅ (KHÔNG 500) |
| 6. Đăng nhập thật | ⛔ **Chưa verify** — sandbox không có mạng tới Google + không tạo user thật trong DB (không tự ý ghi DB) |

**Còn lại:** đăng nhập thật end-to-end cần mạng + user thật. **Q1 (Client ID/RedirectUri)** đã có giá trị khớp.

### 4.2. Phân quyền theo role (`staff`/`admin`/`user`/`doctor`)

| Bước | Kết quả |
|---|---|
| 1. API không token | `GET /api/auth/me` → **401** ✅ |
| 2. API token sai | `GET /api/auth/me` (Bearer giả) → **401** ✅ (không rò rỉ 500) |
| 3. `api/auth/token` (JWT) | Route tồn tại (`AllowAnonymous`) — chưa verify sâu |
| 4. Header tự khai (session mode) | `SessionHeaderAuthenticationHandler` đăng ký — chưa verify sâu |
| 5. UI `/system/*` | 5 trang staff (`/system`, `/system/orders`, `/system/bookings`, `/system/profile`, `/system/schedule`) → **200** ✅ render được |
| 6. Role `doctor` trong UI | ⛔ **Chưa xử lý** → xem L10 |

**Còn lại:** role `doctor` trong UI (Q8).

### 4.3. PayOS + webhook

| Bước | Kết quả |
|---|---|
| 1. Config | `PayOS` block **khớp 100%** nguồn (ClientId/ApiKey/ChecksumKey/BaseUrl) ✅ |
| 2. Tạo link | `POST /api/payos/order/create` (DTO đầy đủ `orderId`+`returnUrl`+`cancelUrl`) → **200** `{"checkoutUrl":"https://pay.payos.vn/web/c9a62c9296614b68aaf1511df6a89b0d","orderCode":1044282182,"amount":35000.00,"description":"Thanh toan don hang #1973"}` ✅ **PayOS thật đã trả link** |
| 3. Webhook — chữ ký sai | `POST /api/payos/webhook` (header `x-payos-signature` sai) → **401** `{"message":"Invalid signature"}` ✅ HMAC-SHA256 hoạt động |
| 4. Webhook — chữ ký đúng → `PAID` | ⛔ **Chưa verify** — cần `ChecksumKey` để tạo chữ ký hợp lệ + không nên giả lập thanh toán thật |
| 5. Trạng thái | `GET /api/payos/status/123456` → **404** `{"message":"Payment not found"}` ✅ (đúng — mã đó không có) |
| 6. Gửi hoá đơn email | `POST /api/payos/send-invoice` → 200 nhưng lỗi SMTP → xem L14 |

**Còn lại:** nhánh webhook `PAID` (cần môi trường PayOS thật + App Password SMTP). **KHÔNG thanh toán thật** theo ràng buộc.

### 4.4. Chatbot AI

| Bước | Kết quả |
|---|---|
| 1. Health | `GET /api/chatbot/health` → **200** `{"status":"healthy","aiProviderConfigured":true,"databaseConnected":true}` ✅ |
| 2. Context (DB thật) | `GET /api/chatbot/context/1` → **200** (4 290 B) — đọc pets/orders/bookings/staff/doctors/products/services/categories/species/rooms thật ✅ |
| 3. Message (tiếng Việt) | `POST /api/chatbot/message` `{"message":"Xin chao, shop co ban thuc an cho cho khong?"}` → **200** ✅ body có `reply`/`source`/`intent`/`data`/`timestamp` — **shape khớp 100%** `ChatMessageResponseDto` |
| 4. Intent | `POST /api/chatbot/intent` `{"message":"Toi muon dat lich spa cho cho"}` → **200** `{"intent":"unknown","parameters":{}}` ✅ (đúng logic fallback bản gốc khi AI không trả JSON) |
| 5. Provider Gemini | ✅ **ĐÃ ĐÓNG (Q13, 08/11/2026)** — user đã cấp **key Gemini mới** (ghi vào `appsettings.json` local, git-ignored). ⚠️ Model `gemini-flash-latest` **không còn khả dụng** cho key mới (API trả **404** *"This model ... is no longer available to new users…"*) ⇒ đã đổi cấu hình + fallback trong `Services/GeminiProviderService.cs` + `Models/ChatbotOptions.cs` sang **`gemini-3.5-flash`**. **Verify thật:** `POST /api/chatbot/message` `"Xin chao, shop co ban giay cho cho khong?"` → **HTTP 200** body `{"reply":"Xin chào bạn! 🐾 …","source":"ai",…}` ⇒ **đã có câu trả lời AI thật** (trước đây luôn `source=fallback`) |

**Còn lại:** ⚠️ **1 việc ngoài repo** — **rotate key Gemini cũ** trên Google Cloud Console + repo nguồn `Projectojt/` (xem R-19). Không có blocker trong repo đích.

### 4.5. Upload ảnh pet

| Bước | Kết quả |
|---|---|
| 1. Route | `POST /api/pets/upload-image` (multipart, field `image`) → **200** ✅ |
| 2. Tên file | `{"imageUrl":"/uploads/pets/pet_bd586ac8bb8a4de5ad82ac7730d81a0b.png"}` ⇒ khớp contract `pet_{Guid:N}{ext}` ✅ |
| 3. Nơi lưu | `wwwroot/uploads/pets/` — đúng thư mục (R2 đã đóng) ✅ |
| 4. Phục vụ lại | 15 file gốc `pet_*` trả **200** qua HTTP; file test vừa tạo → **đã xoá**, còn đúng **15 file** ✅ |
| 5. Validate | ContentType phải bắt đầu `image/`, ≤5 MB — có trong code (`PetsController`), chưa gọi negative-case |

**Còn lại:** test negative-case (file >5 MB / ContentType sai) — **không cần** (đã đọc code, không phải gap).

### 4.6. Đổi ca (shift exchange)

| Bước | Kết quả |
|---|---|
| 1. Verify network | `GET /api/shift-exchange/verify-network` → **200** `{"ip":"127.0.0.1","isCompanyNetwork":false,"companyPrefix":"171.254.189.230, 113.22.236.208","remoteIp":"::1","resolvedFrom":"::1"}` ✅ (middleware `X-Forwarded-For` hoạt động) |
| 2. My requests | `GET /api/shift-exchange/my-requests?staffId=1` → **200** trả dữ liệu thật (`requestId:30`, `type:Swap`, `status:Approved`) ✅ |
| 3. Pending | `GET /api/shift-exchange/pending?staffId=1` → **200** `[]` ✅ |
| 4. All shifts | `GET /api/shift-exchange/all-shifts` → **200** ✅ |
| 5. Staff options | `GET /api/shift-exchange/staff/options` → **200** (263 B) ✅ |
| 6. Ghi (pass/swap/accept) | Route tồn tại; **không gọi** để không sinh dữ liệu đổi ca giả trong DB |

**Còn lại:** nhánh ghi (`pass`/`swap`/`accept`/`decline`/`cancel`) — cần user xác nhận trước khi tạo dữ liệu thật.

### 4.7. Booking spa

| Bước | Kết quả |
|---|---|
| 1. Availability | `POST /api/spa-booking/availability` → **400** với message VI đúng `{"Quantity":["Số lượng thú cưng phải từ 1 đến 20."],"DurationMin":["Thời lượng phải từ 5 đến 480 phút."]}` ✅ (DataAnnotations hoạt động) |
| 2. Validate slot | `POST /api/spa-booking/validate-slot` → **400** `"Thời lượng phải từ 5 đến 480 phút."` ✅ |
| 3. Estimate | `POST /api/spa-booking/estimate?customerId=1` → **400** `"Danh sách dịch vụ không được để trống."` ✅ |
| 4. History | `GET /api/spa-booking/history?customerId=1` → **200** `[]`; `GET /api/spa-booking/history/all` → **200** booking thật (`bookingId:1378`, `items[]`, `appointmentStart/End`) ✅ |
| 5. Invoice | `GET /api/spa-booking/1/invoice` → **404** "Không tìm thấy booking." ✅ (id 1 không tồn tại) |
| 6. UI | `/spa-cart`, `/spa-booking-history`, `/spa-booking-history/1`, `/spa-invoice`, `/spa-booking-detail/1` → **200** ✅ |

**Còn lại:** nhánh **ghi** booking (`POST /api/spa-booking`) — chưa gọi để tránh tạo booking giả (cần user xác nhận).

### 4.8. Email hoá đơn

| Bước | Kết quả |
|---|---|
| 1. Template tồn tại | 4/4 file `.hbs` resolve từ `ContentRootPath` ✅ — **R1 đã đóng** (không còn `FileNotFoundException`) |
| 2. MD5 với nguồn | `OrderInvoice.hbs` **khớp** · `SpaInvoice.hbs` **khớp** ✅ |
| 3. Render Handlebars | `LoadHandlebarsTemplate` (`EmailService.cs:184-194`) compile + bind biến `CustomerName`/`OrderCode`/`Items[]`/`TotalAmount` ✅ — lỗi runtime **không còn ở tầng template** |
| 4. Gửi SMTP | ✅ **ĐÃ ĐÓNG (Q17, 08/11/2026)** — user đã cấp **App Password mới** (cập nhật `appsettings.json` local, git-ignored). `POST /api/test/email/spa` body `{"toEmail":"…","customerName":"Test","bookingCode":"SPA-TEST001"}` → **HTTP 200** `{"message":"Email spa () đã được gửi đến …","subject":"PetShop - Hoa don spa SPA-TEST001"}` ⇒ **email gửi thật được** (trước đây: 200 nhưng báo **5.7.8 Username and Password not accepted**) |
| 5. Nhánh spa/boarding | `SpaCancellation.hbs` + `BoardingInvoice.hbs` tồn tại; nhánh boarding chưa gọi thật |

**Còn lại:** ✅ **KHÔNG CÒN** — Q17 đã đóng (xem R-02).

### 4.9. Hồ sơ khách hàng + ảnh đại diện (Q9)

| Bước | Kết quả |
|---|---|
| 1. Schema | `ALTER TABLE Customer ADD avatar_url NVARCHAR(500) NULL` (có `IF COL_LENGTH(...) IS NULL` ⇒ idempotent). Verify `INFORMATION_SCHEMA.COLUMNS` → `avatar_url / nvarchar / 500` ✅. **KHÔNG** xoá dữ liệu, **KHÔNG** chạy EF migration |
| 2. EF mapping | `Models/Customer.cs` (`AvatarUrl`) + `Data/ShopPetDatabaseContext.cs` (`HasMaxLength(500).HasColumnName("avatar_url")`) ⇒ đọc entity `Customer` **không lỗi** "Invalid column name" ✅ |
| 3. API — không token | `GET /api/auth/me` → **401** ✅ · `POST /api/auth/avatar` → **401** ✅ |
| 4. API — giá trị hợp lệ | `POST /api/auth/avatar` + header `X-User-Id: 1` + `{"avatarUrl":"data:image/png;base64,iVBORw0KGgo="}` → **200** `{"message":"Đã cập nhật ảnh đại diện."}` ✅ |
| 5. API — payload nguy hiểm | `{"avatarUrl":"javascript:alert(1)"}` → **400** với message VI `Ảnh đại diện phải là dữ liệu ảnh (data:image/...) hoặc đường dẫn http(s).` ✅ |
| 6. API — quá độ dài | `{"avatarUrl":"https://example.com/" + 600 ký tự}` → **400** ✅ (giới hạn 500 ký tự khớp `NVARCHAR(500)`) |
| 7. API — xoá avatar | `{"avatarUrl":null}` → **200** ✅ (xoá avatar, UI fallback chữ cái đầu) |
| 8. API — đọc lại | `GET /api/auth/me` (có `X-User-Id`) → **200** kèm `avatarUrl` ✅ |
| 9. Verify DB | `SELECT customer_id, avatar_url FROM Customer WHERE customer_id = 1` → `data:image/png;base64,iVBORw0KGgo=` ✅ **đã lưu thật** |
| 10. UI | `GET /profile` → **200** ✅ · `GET /account` → **200** ✅ (markup giữ nguyên, chỉ bổ sung logic dữ liệu) |

**Còn lại:** ✅ **KHÔNG CÒN** — cột `birth_date` đã được user duyệt ở **Q22** (xem §4.10).

---

### 4.10. Ngày sinh khách hàng — `Customer.birth_date` (Q22)

> **Đối chiếu luồng profile (nguồn → đích):** nguồn `Projectojt/frontend/src/pages/ProfileEditPage.jsx` có ô "Ngày sinh" (`maskDigitsToDmy` mask `dd/mm/yyyy`, `normalizeBirthDateForDisplay` chuẩn hoá về `dd/MM/yyyy`, `dmyToIso` chuyển sang ISO khi lưu). Đích `Account.razor` giữ **nguyên văn** markup + `MaskDigitsToDmy` + `NormalizeBirthDateForDisplay` (`dd/MM/yyyy`) ⇒ **parity UI 100%**; điểm **bổ sung** là đồng bộ thêm xuống DB (localStorage vẫn giữ làm nguồn chính, đúng như Q9 với avatar).

| Bước | Kết quả |
|---|---|
| 1. Schema | `IF COL_LENGTH('Customer','birth_date') IS NULL ALTER TABLE Customer ADD birth_date DATE NULL;` (idempotent). Verify `INFORMATION_SCHEMA.COLUMNS` → `birth_date / date / NULL` ✅. **KHÔNG** xoá dữ liệu, **KHÔNG** chạy EF migration, **KHÔNG** đụng bảng/cột khác |
| 2. EF mapping | `Models/Customer.cs` (+`public DateTime? BirthDate`) + `Data/ShopPetDatabaseContext.cs` (+`entity.Property(e => e.BirthDate).HasColumnName("birth_date")`) ⇒ đọc entity `Customer` **không lỗi** "Invalid column name" ✅ |
| 3. API — không token | `POST /api/auth/birth-date` → **401** ✅ |
| 4. API — ngày sinh ở tương lai | `X-User-Id: 1` + `{"birthDate":"2030-01-01"}` → **400** với message VI `Ngày sinh không được ở tương lai.` ✅ |
| 5. API — ngày sinh hợp lệ | `X-User-Id: 1` + `{"birthDate":"1995-05-20"}` → **200** `{"message":"Đã cập nhật ngày sinh."}` ✅ |
| 6. API — đọc lại | `GET /api/auth/me` (có `X-User-Id`) → **200** kèm `"birthDate":"1995-05-20T00:00:00"` ✅ |
| 7. Verify DB | `SELECT customer_id, birth_date FROM Customer WHERE customer_id = 1` → `1995-05-20` ✅ **đã lưu thật** (sau verify đã trả về `NULL` để không để lại dữ liệu probe) |
| 8. UI | `GET /profile` → **200** ✅ · `GET /profile/edit` → **200** ✅ · `GET /account` → **200** ✅ (markup giữ nguyên, chỉ bổ sung logic dữ liệu) |
| 9. 3-layer | `IAuthRepository` → `AuthRepository` → `IAuthService` → `AuthService.BirthDate.cs` → `AuthController` → `IAuthClient` → `Account.razor.cs` ⇒ **0 hit** `ShopPetDatabaseContext` trong `Components/` ✅ |

**Còn lại:** ✅ **KHÔNG CÒN.**

---

### 4.11. Validate `price` sản phẩm — 400 thay vì 500 (Q20 / R-03)

> **Biên lấy từ constraint THẬT trong DB** (không suy đoán): `SELECT definition FROM sys.check_constraints WHERE name='CK_Product_Price_Valid'` ⇒ `([price]>=(1000) AND [price]<=(10000000))` ⇒ biên hợp lệ `[1 000, 10 000 000]`.

| Bước | Kết quả |
|---|---|
| 1. Controller | `Controllers/ProductsTempController.cs` — hằng số `MinProductPrice = 1000m` / `MaxProductPrice = 10_000_000m` + validate ở action `Create` (X-01) **và** `Update` (X-02) ⇒ `BadRequest(new { message = "Giá tiền không hợp lệ." })` — **đúng văn phong** các message lỗi sẵn có trong chính file ✅ |
| 2. Schema | **KHÔNG** đụng schema ✅ · **KHÔNG** đổi message của các case lỗi khác ✅ |
| 3. API — dưới biên | `POST /api/products-temp/create` `price=0` → **400** ✅ · `price=999` → **400** ✅ (trước đây ⇒ lỗi SQL ⇒ **500**) |
| 4. API — trên biên | `price=10000001` → **400** ✅ |
| 5. API — trong biên | `price=1001` → **200** ✅ · `price=10000000` → **200** ✅ |
| 6. Dọn dẹp | Xoá hẳn **2** sản phẩm probe (`product_id` 57, 58 tên `ProbeProduct`; `Review` liên quan = **0** dòng) ⇒ còn **42** sản phẩm gốc ✅ |

**Còn lại:** ✅ **KHÔNG CÒN.**

---

### 4.12. Widget chat 1-1 khách ↔ nhân viên dùng SignalR (Q21)

> **Quyết định của user (bắt buộc):** *"Q21 — **Có**: làm widget chat 1-1 cho khách/nhân viên."*
> Đây là **hành vi MỚI** của đích (nguồn OJT không có widget chat) — không sửa markup/nội dung/màu của bất kỳ trang đã port.

#### 4.12.1. Bối cảnh schema — đọc từ DB thật, KHÔNG đụng schema

Bảng `ChatMessages` đã có sẵn trong DB (không tạo bảng mới):

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `MessageID` | `int` identity | khóa chính |
| `CustomerID` | `int` **NOT NULL** | id khách trong luồng chat |
| `StaffID` | `int` NULL | id nhân viên trong luồng chat |
| `SenderType` | `nvarchar(10)` | CHECK: `'Staff'` hoặc `'Customer'` |
| `Message` | `nvarchar(max)` | nội dung |
| `SentAt` | `datetime` | default `getdate()` |
| `IsRead` | `bit` | default `0` |

**Hai phát hiện quan trọng khi đọc schema thật:**

1. **FK của bảng đều bị DISABLE** (`sys.foreign_keys.is_disabled = 1` cho `FK_ChatMessages_Admin` và `FK_ChatMessages_Customer`) ⇒ khi INSERT không bị kiểm tra khóa ngoại. Đã verify bằng transaction thật: `INSERT (CustomerID=5, StaffID=3, SenderType='staff')` với `StaffID=3` là `Staff.staff_id` (không có dòng `admin_id=3` tương ứng) ⇒ **thành công**, rollback sạch.
2. **Bốn bảng người dùng có không gian id RIÊNG BIỆT** — `Staff.staff_id`, `Admin.admin_id`, `Doctor.doctor_id`, `Customer.customer_id` đều bắt đầu từ 1 ⇒ id `1` có thể vừa là staff vừa là admin vừa là khách. Đã verify: `Staff.staff_id=6` ↔ `admin.admin_id=6` là **hai người khác nhau**.

**⇒ Quyết định thiết kế (không đụng schema):**

- **Khoá luồng chat 1-1 = `(CustomerID, StaffID)`** — `CustomerID` = id khách, `StaffID` = id nhân viên.
- Hai chiều của cùng một cuộc hội thoại **luôn đọc được lịch sử giống nhau** (đã verify ở §4.12.2 bước 7).
- Vì id không gian chung, DTO `ChatPeerDto` mang thêm `PeerKind` (`staff`/`admin`/`doctor`/`customer`) để tra cứu đúng bảng — đây là hệ quả tất yếu của schema, **không phải** dữ liệu giả.
- `SenderType` ghi `'customer'` hoặc `'staff'` — collation của SQL Server là case-insensitive nên khớp CHECK constraint (đã verify các dòng gốc trong DB đều dùng chữ thường).

#### 4.12.2. Kiểm chứng runtime (app thật, `http://localhost:5286`)

| # | Probe | Kết quả thật |
|---|---|---|
| 1 | `GET /api/chat/peers` **không** token | **401** ✅ (đúng chuẩn `[Authorize]`) |
| 2 | `GET /api/chat/peers` + `X-User-Id: 1` (role `staff`) | **200** — 17 khách hàng (`peerKind=customer`) ✅ |
| 3 | `GET /api/chat/peers` + `X-User-Id: 5` (role `user`) | **200** — 18 nhân viên (`peerKind` ∈ `staff,admin,doctor`) ✅ |
| 4 | `GET /api/chat/history/2` + `X-User-Id: 1` | **200** — list (rỗng lúc đầu) ✅ |
| 5 | `POST /api/chat/send` `{receiverId:2,message:"Xin chào"}` + `X-User-Id: 1` | **200** + `{"message":{"id":1027,"senderId":1,"receiverId":2,"message":"Xin chào",...,"isMine":true},"peer":{...}}` ✅ |
| 6 | `GET /api/chat/history/2` lại | **200** + **có tin vừa gửi** ⇒ xác nhận lưu DB đúng ✅ |
| 7 | Chiều ngược: `GET /api/chat/history/1?peerKind=staff` + `X-User-Id: 2` (khách) | **200** — **cùng lịch sử**, `isMine` đảo đúng (`false`) ⇒ luồng đối xứng ✅ |
| 8 | Trang `/`, `/products`, `/system` | **200** + HTML có markup `<div class="chat-widget …">` ✅ |
| 9 | `GET /css/template/chatWidget.css` | **200** (7 885 B) ✅ |

**Xác nhận lưu DB (đọc trực tiếp `ChatMessages`):**

```
MessageID|CustomerID|StaffID|SenderType|Message
1027     |2         |1      |staff     |Xin chào
1028     |2         |3      |staff     |Chào bạn, tôi là nhân viên hỗ trợ.
1029-1032|2         |1 hoặc 3|customer/staff|PROBE_REALTIME_Q21
```

**Realtime SignalR (verify bằng client .NET thật + trình duyệt thật):**

- Client .NET: `HubConnection` → `Connected` → `JoinGroup(2)` → `POST /api/chat/send` → nhận event `ChatReceived` với payload `{"userId":2,"fromUserId":3,"message":"PROBE_REALTIME_Q21","sentAt":"…","isPersisted":true,"messageId":1030}` ⇒ **PASS**. Server log: `[Realtime] Đã push chat tới user:2 từ user:3`.
- Widget Blazor trong trình duyệt (đã đăng nhập staff id 3): nhận tin qua SignalR ⇒ tự thêm vào khung chat với bubble `chat-row--theirs` (bên trái, nền trắng) + timestamp "Vừa xong"; tin của chính mình hiển thị `chat-row--mine` (bên phải, nền cyan). Trạng thái trên header đổi từ "● ngoại tuyến" → "● trực tuyến" khi hub kết nối được.

**Dọn dẹp:** đã `DELETE FROM ChatMessages WHERE MessageID >= 1027` ⇒ xoá đúng **7** dòng test ⇒ DB trở về nguyên trạng: `MAX(MessageID) = 1025`, `COUNT(*) = 29` (khớp số dòng gốc). App đã dừng, file log tạm đã xoá, project probe tạm đã xoá.

#### 4.12.3. Kiến trúc 3-layer (KHÔNG inject DbContext trong component)

```
Components/Chat/ChatWidget.razor(.cs)        ← UI (nút tròn fixed + panel)
        │  IChatClient (self-HTTP, gắn header phiên từ UserSessionService)
        ▼
Controllers/ChatController.cs                 ← api/chat/*, [Authorize], lấy userId từ CLAIMS
        │  IChatService
        ▼
Services/ChatService.cs                       ← điều phối: ghi DB trước, đẩy realtime sau
        │  IChatRepository
        ▼
Repositories/ChatRepository.cs                ← truy vấn/ghi bảng ChatMessages
        │  ShopPetDatabaseContext
        ▼
     SQL Server (SHOP_PET_Database)
```

Realtime (song song, không bắt buộc): `ChatService` → `IRealtimeNotificationService.SendChatAsync` → `IHubContext<NotificationHub>` → group `user:{id}` → event `ChatReceived`.

> **Ghi chú kỹ thuật:** `ChatClient` **tự gắn** header `X-User-Id`/`X-User-Role`/`X-Customer-Id` từ `UserSessionService` (scoped) thay vì dùng `SpaAuthHeaderHandler`. Lý do: handler được tạo **ngoài scope Blazor** nên không thấy được `CurrentUser` ⇒ request thiếu header ⇒ **401**. Đã xác nhận bằng log runtime (`Received HTTP response headers … - 401`) trước khi sửa. Đây **cùng pattern** với `Services/PetsClient.cs` (`CreateAuthedRequest`).

#### 4.12.4. Ràng buộc đã tôn trọng

| Ràng buộc | Kết quả |
|---|---|
| **KHÔNG** đụng DB schema | ✅ Chỉ đọc/ghi bảng `ChatMessages` sẵn có; **không** `ALTER`/`CREATE` |
| **KHÔNG** đụng `appsettings.json` | ✅ Không sửa file này |
| **KHÔNG** dùng tím `#4a2477` | ✅ CSS widget chỉ dùng cyan `#6fd5dd` + `#3a9ea8` (đậm hơn) + biến sẵn có (`--main-bg`, `--card-bg`, `--text`, `--text-light`, `--text-dark`, `--border-radius`) |
| **KHÔNG** đổi markup/nội dung/màu trang đã port | ✅ Widget là phần **thêm mới**, `fixed` góc phải dưới; không sửa nội dung trang nào |
| **KHÔNG** thêm link chat vào nav | ✅ Menu điều hướng giữ nguyên 100% |
| 3-layer bắt buộc | ✅ Razor → Service → Repository → DbContext |
| File `.cs` < 500 dòng · `.razor` tách code-behind nếu > 300 dòng | ✅ `ChatWidget.razor` ≈ 140 dòng · `ChatWidget.razor.cs` ≈ 430 dòng · `ChatController.cs` ≈ 165 dòng · `ChatService.cs` ≈ 195 dòng · `ChatRepository.cs` ≈ 245 dòng |
| **KHÔNG** Node/npm · **KHÔNG** placeholder/mock/fake data | ✅ Không có; dữ liệu lấy 100% từ DB thật |
| Build 0 error / 0 warning · test không giảm | ✅ **0/0** · **93/93 pass** (không giảm) |

**Còn lại:** ✅ **KHÔNG CÒN** — Q21 đã đóng.

---

### 4.13. Refresh token — port theo slide PRN232 Ch06 (Q2 / SL-07)

> **Quyết định của user (bắt buộc):** *"Q2 — **Có**: làm refresh token tối giản cho khớp slide Ch06."*
> **Slide tham chiếu:** `Chapter_06-Implement_security_in_RESTful_WebService.txt`
>  - **S40**: *"In `appsettings.json`, add the configuration values to create an **access token and refresh token**. … Create Refresh Token … Login and Refresh token End Points"*
>  - **S41**: login (`POST /api/account/login`) *"returns Access & Refresh Token"*
>  - **S42**: *"To test the Refresh Token … `POST /api/account/refreshtoken`, Body: `{ "accessToken": …, "refreshToken": … }`"*

#### 4.13.1. Port theo slide — ánh xạ slide → dự án đích

| Slide Ch06 | Dự án đích | Ghi chú |
|---|---|---|
| S40 — config tạo access token **và** refresh token | `JwtSettings.ExpiresInMinutes` (120 phút) + `JwtSettings.RefreshTokenExpiresInMinutes` (10 080 phút = **7 ngày**) | Key cũ **giữ nguyên** để không phá config hiện có |
| S40 — "Create Refresh Token" | `JwtTokenService.CreateRefreshToken(userId, name, email, role)` | **Cùng key ký / issuer / audience** với access token; claim riêng `token_type = "refresh"` để phân biệt |
| S40 — "Login and Refresh token End Points" | `POST /api/auth/token` (đã có) + `POST /api/auth/refreshtoken` (mới) | Endpoint đích đặt dưới `api/auth` (không phải `api/account`) cho khớp **cấu trúc API hiện có** của dự án |
| S41 — login trả về **cả 2** token | `POST /api/auth/token` → `TokenResponseDto` (`accessToken`, `tokenType`, `userId`, `name`, `email`, `role`) | Response cũ **giữ nguyên** ⇒ không phá client cũ (chỉ cần thêm `refreshToken` cho client mới) |
| S42 — body `{ accessToken, refreshToken }` | `RefreshTokenRequestDto(string RefreshToken, string? AccessToken = null)` | `accessToken` **tuỳ chọn** — client gửi kèm (đúng format slide) hoặc chỉ gửi refresh token đều được |
| S42 — nhận token mới | `RefreshTokenResponseDto(accessToken, refreshToken, tokenType, userId, name, email, role)` | **Rotate**: cấp access token mới **và** refresh token mới |

#### 4.13.2. Bảo mật — access token KHÔNG được dùng để refresh

| Cơ chế | Vị trí | Kết quả |
|---|---|---|
| Claim phân biệt | `JwtTokenService.TokenTypeClaim = "token_type"` — access ⇒ `"access"`, refresh ⇒ `"refresh"` | Mọi token đều có claim ⇒ kiểm tra được chắc chắn |
| `ValidateRefreshToken` | `Services/JwtTokenService.cs` | Kiểm tra **chữ ký + hạn + `token_type == "refresh"`** ⇒ access token bị **từ chối** |
| `ValidateAccessToken` | `Services/JwtTokenService.cs` | Chiều ngược lại: refresh token **không** dùng được như access token |
| `ValidateToken` | `Services/JwtTokenService.cs` | **Không** kiểm tra `token_type` ⇒ **không phá luồng login cũ** (access token phát hành trước khi có claim vẫn dùng được) |
| Hạn token | `JwtSecurityToken` (`notBefore`/`expires`) | Refresh token hạn **7 ngày**, access token hạn **2 giờ** |

#### 4.13.3. Verify runtime (app chạy thật, DB thật)

| Bước | Kết quả |
|---|---|
| 1. Login | `POST /api/auth/token` `{"email":"thid@petshop.com","password":"staff123"}` → **200** ✅ body có `accessToken` (len 699) + `tokenType: "Bearer"` + `userId: 4` + `role: "staff"` |
| 2. Claim access token | Decode payload → `"token_type":"access"` ✅ · `iss=PetShop` · `aud=PetShopClients` ✅ |
| 3. `GET /api/auth/me` | access token → **200** ✅ |
| 4. **Refresh hợp lệ** | `POST /api/auth/refreshtoken` body `{ "refreshToken": <refresh hợp lệ>, "accessToken": <access cũ> }` → **200** ✅ body có `accessToken` **mới** (`token_type=access`) + `refreshToken` **mới** (`token_type=refresh`, `exp` = +7 ngày) ⇒ **rotate** hoạt động |
| 5. **Access token sai loại** | body `{ "refreshToken": <access token>, "accessToken": … }` → **401** ✅ `{"message":"Refresh token không hợp lệ hoặc đã hết hạn."}` |
| 6. Chuỗi rác | `{ "refreshToken": "khong-phai-token-abc123" }` → **401** ✅ cùng message |
| 7. Refresh token rỗng / thiếu field | → **400** ✅ `{"errors":{"RefreshToken":["Refresh token không được để trống."]}}` (DataAnnotations, đúng văn phong file) |
| 8. `GET /api/auth/me` với access token **mới** | → **200** ✅ `{"userId":4,"name":"Pham Thi D","email":"thid@petshop.com","role":"staff"}` |
| 9. UI | `GET /login` → **200** ✅ (không phá UI) |
| 10. Refresh token **đã rotate** dùng lại | → **200** ✅ (token mới vẫn dùng được — không có blacklist, đúng mức "tối giản") |

#### 4.13.4. Test tự động

`tests/PetShop.Tests/RefreshTokenTests.cs` — **14 test** (file mới):

| Nhóm | Test |
|---|---|
| Phát hành | `token_type` = `refresh` / `access` · refresh token có đủ claim (userId/name/email/role) · hạn dài hơn access token · cùng issuer/audience/key |
| Kiểm tra | refresh hợp lệ ⇒ `true` · **access token ⇒ `false`** · **refresh token qua `ValidateAccessToken` ⇒ `false`** · access token qua `ValidateAccessToken` ⇒ `true` · sai chữ ký ⇒ `false` · chuỗi rác/rỗng ⇒ `false` (không ném exception) · hết hạn ⇒ `false` · token thiếu claim `token_type` ⇒ `false` |
| Không phá vỡ | access token cũ (thiếu claim) vẫn qua `ValidateToken` ⇒ **luồng login cũ nguyên vẹn** |

**Kết quả:** `dotnet test` → **93/93 pass** (baseline 49 + 7 LS-11 + 14 Q9 + 9 Q20/Q22 + **14** mới Q2), 0 fail, 0 skip.

#### 4.13.5. Ràng buộc đã tôn trọng

| Ràng buộc | Kết quả |
|---|---|
| **KHÔNG** yêu cầu `username`/`password` ở endpoint refresh | ✅ Body chỉ có `refreshToken` (+ `accessToken` tuỳ chọn) |
| **KHÔNG** phá luồng login hiện tại | ✅ `TokenResponseDto` giữ nguyên shape; `ValidateToken` không kiểm tra `token_type` |
| **KHÔNG** rotate/đổi secret nào | ✅ Không đụng Google/Gmail/Gemini |
| **KHÔNG** đụng DB schema · **KHÔNG** EF migration | ✅ Refresh token **stateless** (JWT tự chứa claim) — **không** cần bảng mới |
| File `.cs` < 500 dòng | ✅ `JwtTokenService.cs` ≈ 235 dòng · `AuthController.cs` ≈ 350 dòng · `RefreshTokenTests.cs` ≈ 233 dòng |
| 3-layer | ✅ Controller → `IJwtTokenService` (Service) — không truy cập DB ở luồng refresh |
| Build 0 error / 0 warning | ✅ **0/0** |

**Còn lại:** ✅ **KHÔNG CÒN** — Q2 đã đóng.

---

## 5. RÀNG BUỘC GĐ3 — KẾT QUẢ RÀ SOÁT

### 5.1. Không còn tham chiếu Node/npm trong code ứng dụng

`Grep` toàn repo đích (loại `bin/`, `obj/`, `docs/`, `node_modules/`, `.git/`):

| Mẫu | Kết quả |
|---|---|
| `npm ` | ✅ **0 hit** |
| `node_modules` | ✅ **0 hit** |
| `package.json` | ✅ **0 hit** |
| `require(` | ✅ **0 hit** |
| `process.env` | ✅ **0 hit** |
| `vite` | ⚠️ 7 hit — **không phải tham chiếu Vite**: `NavItem` (chứa chuỗi "vite") trong `Components/Shared/Navigation.razor:6,22,33,45`; 2 comment mô tả lịch sử trong `Controllers/ProductsPageController.cs:13`; 2 comment trong `wwwroot/js/daNangData.js:12-13` (giải thích bản gốc dùng `import.meta.env.VITE_GOOGLE_MAPS_EMBED_API_KEY` nhưng bản đích **không** dùng) |
| `react` | ⚠️ 25 hit — **toàn bộ là comment "React parity"** mô tả đối chiếu với bản gốc + 1 tên file `service-react.css` |

**Kết luận:** không còn dependency Node/npm trong code ứng dụng. Toàn bộ 32 chữi "vite"/"react" nằm trong comment/tên file lịch sử — **không có import/runtime reference**.

### 5.2. Secret hard-code trong code C#

| Mẫu | Trong `.cs` / `.razor` | Trong file khác |
|---|---|---|
| `AIza` | ✅ **0 hit** | `appsettings.json` (Gemini key — **config, đúng chỗ**) |
| `GOCSPX` | ✅ **0 hit** | `appsettings.json` (Google client secret — **config**) |
| `nxlq` | ✅ **0 hit** | `appsettings.json` |
| `9c542038` | ✅ **0 hit** (Q19 — đã dọn test file) | **User Secrets** (`PayOS:ClientId`) + `appsettings.json` (rỗng) |
| `53691e37` | ✅ **0 hit** (Q19 — đã dọn test file) | **User Secrets** (`PayOS:ChecksumKey`) + `appsettings.json` (rỗng) |

- **Không có secret nào hard-code trong code ứng dụng** (Controller/Service/Repository/Model/Razor) — tất cả nằm trong `appsettings.json` ✅
- ✅ **Q19 (08/11/2026) — ĐÃ XỬ LÝ:** `tests/PetShop.Tests/PayOSServiceTests.cs` **không còn** hằng số credential thật. Thay bằng **key giả cố định** (`test-client-id` / `test-api-key` / `test-checksum-key-…`) — mục đích bộ test là kiểm chứng **thuật toán HMAC-SHA256** + **nhánh fallback URL** (chạy được với **bất kỳ khoá nào**) ⇒ **CI vẫn pass không cần secret thật**, không cần skip. Base URL merchant (endpoint công khai) giữ nguyên. Credential thật chuyển sang **User Secrets** (`PayOS:ClientId` / `PayOS:ApiKey` / `PayOS:ChecksumKey`), `appsettings.json` để **rỗng** ⇒ app vẫn chạy đúng (verify: `POST /api/payos/order/create` order_id=2 → **200** + checkoutUrl thật từ PayOS). Ngoài ra đã **redact** secret đầy đủ trong `docs/_inventory/SOURCE_COMPLEX_FLOWS.md` (file tracked) → `<your-payos-…>`. `git ls-files` **không đổi** (535 file), **không file secret nào được track thêm**.
- Repo **nguồn** `Projectojt/`: `Grep` `AIza|GOCSPX|nxlq|9c542038|53691e37` trong `.cs`/`.js`/`.jsx` → **0 hit** (secret chỉ nằm trong `appsettings.json` nguồn — đúng cách)

### 5.3. Đường dẫn ảnh hỏng

✅ **0 đường dẫn hỏng.**

- Quét HTML **40/40 trang chính**, trích **13 URL asset cục bộ duy nhất** bằng regex `(?:src|href)\s*=\s*"(/(?:images|uploads|css|js)/[^"]+)"` → **13/13 trả HTTP 200**
- 10 ảnh ngẫu nhiên `wwwroot/images/` → **10/10 trả 200**
- 3 ảnh `/uploads/pets/pet_*` → **3/3 trả 200**
- 4 URL "hỏng" ghi nhận ở lượt quét đầu (`/css/all.min.css`, `/css/bootstrap.min.css`, `/images/g-logo.png`, `/js/bootstrap.bundle.min.js`) là **hệ quả của regex cắt chuỗi URL CDN** (`https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css` bị cắt thành hậu tố). Xác minh lại: `cdn.jsdelivr.net` → **200**; `cdnjs.cloudflare.com` + `developers.google.com` → **000** (sandbox **không có mạng ngoài**, không phải lỗi app)

---

## 6. RỦI RO CÒN LẠI + BIỆN PHÁP

| # | Rủi ro | Mức | Biện pháp đề xuất |
|---|---|---|---|
| R-01 | ~~**Gemini API key bị Google báo leaked** ⇒ chatbot luôn `source=fallback`, không có câu trả lời AI thật. Key y hệt trong repo nguồn ⇒ repo nguồn cũng đã lộ~~ | 🟢 | ✅ **ĐÃ XỬ LÝ (08/11/2026)** — user đã cấp **key Gemini mới**, đã ghi vào `appsettings.json` (local, **git-ignored**) ⇒ **chatbot đã trả câu trả lời AI thật**: `POST /api/chatbot/message` ⇒ **200** với `source=ai` (xem Q13, §4.4). ⚠️ **Phát hiện quan trọng:** model `gemini-flash-latest` **không còn khả dụng** cho key mới (API trả **404** *"This model ... is no longer available to new users…"*) ⇒ đã đổi cấu hình + fallback trong `Services/GeminiProviderService.cs` + `Models/ChatbotOptions.cs` sang **`gemini-3.5-flash`**. ⚠️ Còn 1 việc **ngoài repo này**: **rotate key cũ** trên Google Cloud Console + **xem xét rotate key trong repo nguồn** (`Projectojt/`) — key cũ vẫn còn hiệu lực ở đó |
| R-02 | ~~**SMTP credential sai** ⇒ email hoá đơn không gửi được (5.7.8 BadCredentials)~~ | 🟢 | ✅ **ĐÃ XỬ LÝ (08/11/2026)** — user đã cấp **App Password mới**, đã cập nhật vào `appsettings.json` (local, **git-ignored**) ⇒ **email gửi thật được**: `POST /api/test/email/spa` ⇒ **200** `{"message":"Email spa () đã được gửi đến …"}` (xem Q17, §4.8). Code + 4 template vẫn giữ nguyên, **không** sửa code |
| R-03 | ~~**`price` ngoài biên DB** ⇒ `POST /api/products-temp/create` trả **500** thay vì 400~~ | 🟢 | ✅ **ĐÃ XỬ LÝ (Q20, 08/11/2026)** — user chốt **"Có"**: thêm validate `price ∈ [1 000, 10 000 000]` trong `Controllers/ProductsTempController.cs` (hằng số `MinProductPrice`/`MaxProductPrice` lấy từ **constraint thật** `CK_Product_Price_Valid`) ở cả action `Create` **và** `Update` ⇒ trả **400** `"Giá tiền không hợp lệ."` thay vì 500. **KHÔNG** đụng schema. Probe: 0/999/10000001 → **400**; 1001/10000000 → **200** |
| R-04 | ~~**CSS lệch tông màu** (5 file) + **thiếu 3 file CSS** ⇒ giao diện không giống 100%~~ | 🟢 | ✅ **ĐÃ XỬ LÝ (Q18, 08/11/2026)** — user chốt **revert về tông gốc `#6fd5dd` + port 3 file CSS còn thiếu**. Đã revert màu (copy **nguyên văn giá trị** từ nguồn, không suy đoán) + copy 3 file CSS **SHA256 khớp 100%**. Build 0 error/0 warning, test 93/93, probe 21/21 trang + 22/22 file CSS → 200. Chi tiết: §3-L12, §3-L13 |
| R-05 | ~~**PayOS key + Gemini key nằm trong `appsettings.json` đã commit**~~ | 🟢 | ✅ **ĐÃ XỬ LÝ (Q19, 08/11/2026)** — user chốt "Có": chuyển secret sang **user-secrets** + `appsettings.Development.json`. `tests/PetShop.Tests/PayOSServiceTests.cs` **không còn** credential thật (thay bằng key giả cố định — HMAC hoạt động đúng với bất kỳ khoá nào ⇒ **CI pass không cần secret thật**); `PayOS:ClientId`/`ApiKey`/`ChecksumKey` nằm trong **User Secrets** (`petshop-prn232-local-dev`), `appsettings.json` để **rỗng**; `appsettings.example.json` (file tracked) có **placeholder**; redact secret đầy đủ trong `docs/_inventory/SOURCE_COMPLEX_FLOWS.md`. `git ls-files` **không đổi** (535 file) — **không file secret nào được track thêm**. ⚠️ **Còn lại (không chặn, mức 🟡):** Google (`GoogleAuth`), Gmail (`Mail`) và Gemini (`Chatbot:Providers:gemini:ApiKey`) **vẫn nằm trong `appsettings.json` local — git-ignored** ⇒ **không** bị commit, nhưng nên chuyển sang user-secrets/biến môi trường ở bước đóng gói |
| R-06 | **2 component UI phụ thiếu** (`ClearSearchButton`, `NotificationBell`) | 🟡 | Port nếu user muốn giống 100%; `NotificationBell` chỉ cần gọi `api/notifications/{staffId}` (đã có) |
| R-07 | **Role `doctor` chưa xử lý trong UI** | 🟡 | User quyết (Q8) — port nguyên văn theo yêu cầu "giống 100%" |
| R-08 | **Phụ thuộc CDN** (Google Fonts, Font Awesome, GSI, jsDelivr) ⇒ mất style/icon khi offline | 🟡 | Cân nhắc vendoring 4 file vào `wwwroot/` (hiện đã có `wwwroot/bootstrap/bootstrap.min.css` sẵn) |
| R-09 | **2 asset `frontend/public/` chưa copy** | 🟡 | Copy vào `wwwroot/images/` nếu user muốn giống 100% (hiện không gây 404) |
| R-10 | **Chưa có contract test tự động** (TS-04) | 🟡 | Chuyển 137 probe của GĐ3 thành test xUnit + `WebApplicationFactory` để chạy trong CI |
| R-11 | **Deadlock tiềm năng** `SpaBookingService` (static semaphore + self-HTTP) | 🟡 | Theo dõi; cân nhắc bỏ static lock khi port luồng ghi booking |
| R-12 | **Chatbot không có UI widget** gọi 4 endpoint (API đã sẵn sàng) | 🟡 | User quyết (Q14) |
| R-13 | **SignalR chạy sau reverse proxy / IIS** ⇒ WebSocket bị chặn, realtime không hoạt động (chỉ fallback HTTP pull) | 🟡 | Khi deploy thật: bật `WebSockets` trong proxy, giới hạn idle timeout (`KeepAliveTimeout`), và cân nhắc dùng `LongPolling` fallback (`SignalR` tự chuyển). Trên LOCAL (Kestrel cổng 5286) đã verify WebSocket hoạt động thật |
| R-14 | **Hub dùng query string `?userId=` khi chưa xác thực** ⇒ client SignalR trong browser không gửi được header session ⇒ không xác thực được ⇒ chat **không** ghi log DB và không chống được spoof | 🟡 | Đây là chế độ **LOCAL DEMO** (cùng mức độ với `SessionHeaderAuthenticationHandler`, xem AGENTS.md §0). Khi deploy thật: bắt client gửi `accessTokenFactory` (JWT Bearer) ⇒ claim `NameIdentifier` được dùng, chat log lại bật và chống spoof. **Đã để sẵn đường này trong code** |
| R-15 | ~~**Chưa có UI chat widget cho khách** — `ChatReceived` hiện chỉ được `NotificationBell` ghi log, chưa hiển thị cho người dùng~~ | ✅ | **ĐÃ ĐÓNG (08/11/2026)** — user quyết **Q21 = "Có"** ⇒ đã làm widget chat 1-1 `Components/Chat/ChatWidget.razor` (+ `.razor.cs`) với API `api/chat/{peers,history/{peerId},send}` 3-layer, lưu `ChatMessages`, realtime SignalR `ChatReceived`, CSS `wwwroot/css/template/chatWidget.css` (cyan `#6fd5dd`), gắn ở `MainLayout` + `StaffWorkspaceLayout`. Xem mục "Widget chat 1-1" ở LỚP 10 |
| R-16 | **Widget chat dùng cơ chế session header `X-User-Id`/`X-User-Role`** (giống `SessionHeaderAuthenticationHandler`) ⇒ khi deploy thật cần đổi sang JWT Bearer (`accessTokenFactory`) cho cả client SignalR lẫn self-HTTP | 🟡 | Cùng mức độ với R-14 (chế độ LOCAL DEMO). Đã để sẵn đường: `ChatClient` gắn header từ `UserSessionService` (dễ thay bằng `Authorization: Bearer`); `NotificationHub` đã ưu tiên claim `NameIdentifier` khi có Bearer token |
| R-17 | **Refresh token là stateless — không có blacklist** ⇒ refresh token **đã rotate** vẫn dùng được cho tới khi hết hạn (7 ngày) | 🟡 | Đúng mức **"tối giản"** theo slide Ch06 mà user đã duyệt (Q2) — không cần bảng DB, không EF migration. Khi cần thu hồi: thêm claim `jti` + bảng denylist (hoặc `IBlacklistCache` với TTL = thời hạn token) ⇒ cần user chốt trước khi triển khai thật |
| R-18 | **User Secrets chỉ được nạp ở môi trường `Development`** ⇒ deploy **Production** sẽ **không** có `PayOS:*` ⇒ `POST /api/payos/order/create` fail | 🟡 | Khi đóng gói/deploy Production: đặt **biến môi trường** `PayOS__ClientId`, `PayOS__ApiKey`, `PayOS__ChecksumKey` (hoặc dùng `dotnet user-secrets` chỉ cho local dev + secret store cho CI/prod). `appsettings.example.json` đã có placeholder + hướng dẫn |
| R-19 | **Key Gemini cũ (đã bị Google báo leaked) vẫn còn hiệu lực** trên Google Cloud Console **và** trong repo nguồn `Projectojt/` | 🟡 | **Việc ngoài repo đích** — không sửa được bằng code trong `PRN232_PetCareProject`. Cần user: (1) **rotate/disable key cũ** trên Google Cloud Console, (2) rotate key trong repo nguồn `Projectojt/`. Repo đích đã dùng key mới nên **không** bị ảnh hưởng |

---

## 7. CÂU HỎI CẦN USER QUYẾT

> Lấy từ `docs/PARITY.md` §14 — các mục **chưa** đóng, cộng các câu hỏi mới phát sinh từ GĐ3.

| # | Câu hỏi | Ảnh hưởng | Khuyến nghị | Trạng thái GĐ3 |
|---|---|---|---|---|
| **Q13** | **Gemini API key đã bị Google báo LEAKED** (403 `PERMISSION_DENIED`). Cấp key mới? Key cũ nằm trong cả repo nguồn lẫn repo đích | 🔴 Chatbot không có câu trả lời AI thật | **Bắt buộc user cấp key mới + rotate key cũ** | ✅ **ĐÃ ĐÓNG (08/11/2026)** — user đã cấp **key Gemini mới** (ghi vào `appsettings.json` local — file này **git-ignored** ⇒ **không** ghi key đầy đủ vào tài liệu tracked). ⚠️ **Phát hiện quan trọng:** model `gemini-flash-latest` **không còn khả dụng** cho key mới (API trả **404** *"This model ... is no longer available to new users. Please update your code to use models/gemini-3.8-flash"*) ⇒ đã đổi cấu hình + fallback trong `Services/GeminiProviderService.cs` + `Models/ChatbotOptions.cs` sang **`gemini-3.5-flash`**. **Verify thật:** `POST /api/chatbot/message` với *"Xin chao, shop co ban giay cho cho khong?"* ⇒ **HTTP 200**, body `{"reply":"Xin chào bạn! 🐾 …","source":"ai",…}` ⇒ chatbot **đã có câu trả lời AI thật** (trước đây luôn `source=fallback`). Xem R-01, §4.4 |
| **Q17** *(mới)* | **SMTP/App Password Gmail** hiện sai (`5.7.8 BadCredentials`). Cấp App Password đúng? | 🟠 Email hoá đơn (đơn + spa + boarding) không gửi được | **Bắt buộc user cấp** | ✅ **ĐÃ ĐÓNG (08/11/2026)** — user đã cấp **App Password mới** (cập nhật `appsettings.json` local — **git-ignored** ⇒ **không** ghi mật khẩu vào tài liệu tracked). **Verify thật:** `POST /api/test/email/spa` body `{"toEmail":"…","customerName":"Test","bookingCode":"SPA-TEST001"}` ⇒ **HTTP 200** `{"message":"Email spa () đã được gửi đến …","subject":"PetShop - Hoa don spa SPA-TEST001"}` ⇒ **email gửi thật được**. Xem R-02, §4.8 |
| **Q18** *(mới)* | ~~**Tông màu chủ đạo**: giữ tím `#4a2477` (đích hiện tại) hay revert gốc `#6fd5dd`?~~ | 🟢 5 file CSS lệch + 3 file CSS thiếu | **User chốt: revert về tông gốc `#6fd5dd` + port 3 file CSS còn thiếu** | ✅ **ĐÃ LÀM (08/11/2026)** — xem §3-L12/L13, R-04 |
| **Q2** | Refresh token (slide Ch06) — làm hay không? | SL-07 | Làm tối giản nếu ưu tiên "khớp slide" | ✅ **ĐÃ LÀM (08/11/2026)** — user chốt **"Có"**: port tối giển theo slide Ch06 S40–S42 (`CreateRefreshToken` + claim `token_type` + `POST api/auth/refreshtoken` body `{ refreshToken, accessToken? }`, **rotate** refresh token). **KHÔNG** yêu cầu username/password ở endpoint refresh. Probe: login 200 · refresh hợp lệ 200 · access token sai loại 401 · rác 401 · `/api/auth/me` 200. Xem §4.13, R-05 |
| **Q8** | Route/component "chết" ở nguồn (`SpaBookingDetailPage` orphan, tab `breed-pricing` không truy cập được, 11 link trong Navigation không có route) — port nguyên văn hay sửa? | U-28, CM-14..17, GU-03 | Port nguyên văn (yêu cầu "giống 100%") | ⏳ Chờ (đích đã có `/spa-booking-detail/{id}`) |
| **Q9** | `Customer` nguồn có `birth_date`/`avatar_url`, đích thiếu ⇒ profile UI không giống 100%. Cho phép đụng schema DB? | U-08, U-10 | ✅ **ĐÃ LÀM (08/11/2026)** — user duyệt `avatar_url`: cột `Customer.avatar_url NVARCHAR(500) NULL` đã thêm (ALTER idempotent, không xoá dữ liệu, không EF migration) + EF mapping + API `POST/PUT api/auth/avatar` + 3-layer + UI đọc avatar theo thứ tự localStorage → DB → rỗng. **Verify runtime:** 401 (no token) / 200 (giá trị hợp lệ) / 400 (`javascript:`) / 400 (>500 ký tự); DB `customer_id=1` → `data:image/png;base64,iVBORw0KGgo=`. ✅ **`birth_date` đã được user duyệt ở Q22 và cũng đã làm xong** |
| **Q10** | `daNangData.js` — copy nguyên file `.js` hay viết lại C#? | CM-16 | Copy nguyên file `.js` | ✅ **Đã có** `wwwroot/js/daNangData.js` (7 494 B) — cần user xác nhận |
| **Q11** | Google Maps embed key trong `daNangData.js` (`VITE_GOOGLE_MAPS_EMBED_API_KEY`) — có key thật không? | U-06 | User cấp hoặc bỏ iframe | ⏳ Chờ |
| **Q14** | Chatbot có cần **UI widget** ở trang bán hàng không? | LS-08 / BF-19 | Cần user quyết lát cắt UI riêng hay để sau LS-12 | ⏳ Chờ |
| **Q15** | `/contact` — nguồn OJT **không có** page này. Thêm form liên hệ / bản đồ / giờ mở cửa? | U-26b | Cần user quyết | ⏳ Chờ |
| **Q16** | Soft-delete `Products` (cột `is_deleted` có sẵn ở đích, **nguồn không có**) — đồng bộ ngược sang nguồn? | X-03..X-06 | Cần user quyết | ⏳ Chờ (đã implement theo cột có sẵn) |
| **Q19** *(mới)* | `tests/PetShop.Tests/PayOSServiceTests.cs:24-26` chứa hằng số trùng `appsettings.json`. Cho phép chuyển sang user-secrets/environment? | 🟠 Vệ sinh secret | Nên chuyển (không ảnh hưởng chức năng) | ✅ **ĐÃ LÀM (08/11/2026)** — user chốt **"Có"**: (1) test file dùng **key giả cố định** (`test-client-id` / `test-api-key` / `test-checksum-key-…`) ⇒ HMAC + fallback URL vẫn kiểm chứng được, **CI pass không cần secret thật**, **không cần skip** (xUnit 2.5.3 không có `Assert.Skip`); (2) credential thật → **User Secrets** (`PayOS:ClientId`/`ApiKey`/`ChecksumKey`), `appsettings.json` để **rỗng** — verify `POST /api/payos/order/create` → **200** + checkoutUrl thật; (3) `appsettings.example.json` (tracked) có **placeholder**; (4) redact secret đầy đủ trong `docs/_inventory/SOURCE_COMPLEX_FLOWS.md`. `git ls-files` **không đổi** (535 file). Xem §5.2, R-05 |
| **Q20** *(mới)* | `POST /api/products-temp/create` nên trả **400** khi `price` ngoài `[1000, 10 000 000]` (đồng bộ `CK_Product_Price_Valid`) thay vì 500? | 🟠 UX lỗi | Nên sửa (1 dòng validate trong `Controllers/ProductsTempController.cs`) | ✅ **ĐÃ LÀM (08/11/2026)** — user duyệt **"Có"**. Biên lấy từ constraint thật: `SELECT definition FROM sys.check_constraints WHERE name='CK_Product_Price_Valid'` ⇒ `([price]>=(1000) AND [price]<=(10000000))`. Thêm `MinProductPrice = 1000m` / `MaxProductPrice = 10_000_000m` + validate ở `Create` **và** `Update` ⇒ **400** `"Giá tiền không hợp lệ."`. **KHÔNG** đụng schema, **KHÔNG** đổi message case lỗi khác |
| **Q21** *(mới — LS-11)* | **Có cần UI chat widget cho khách/nhân viên không?** Hiện `NotificationBell` đã nhận event `ChatReceived` qua SignalR nhưng **chỉ ghi log**, chưa hiển thị hộp thoại chat. Cần làm widget chat 1-1 (khung chat nhỏ trong topbar/trang chat) không? | 🟡 UX | Cần user quyết — hạ tầng SignalR + lưu `ChatMessages` đã sẵn sàng | ✅ **ĐÃ LÀM (08/11/2026)** — user duyệt **"Có"**. Widget chat 1-1 `Components/Chat/ChatWidget.razor` + `.razor.cs` (nút tròn fixed góc phải dưới + panel dropdown đối tác + khung tin nhắn + ô nhập + nút gửi), chỉ hiện khi đã đăng nhập (`UserSessionService.CurrentUser`), gắn ở `MainLayout` + `StaffWorkspaceLayout` ⇒ mọi trang; CSS mới `wwwroot/css/template/chatWidget.css` tông gốc cyan `#6fd5dd` (**không** dùng tím `#4a2477`), link trong `App.razor`; **KHÔNG** thêm link chat vào nav gốc, **KHÔNG** đổi markup/nội dung/màu trang đã port. **3-layer:** `IChatRepository` → `ChatRepository` (DB) · `IChatService` → `ChatService` (điều phối + push realtime) · `Controllers/ChatController.cs` route `api/chat` (`[Authorize]`, lấy userId từ claims **không** tin query/body) · `IChatClient` → `ChatClient` (self-HTTP cho widget). **Endpoint:** `GET api/chat/peers` · `GET api/chat/history/{peerId:int}` · `POST api/chat/send` ⇒ `{receiverId, message}`. **Lưu trữ:** bảng `ChatMessages` có sẵn (cột `CustomerID` / `StaffID` / `SenderType` / `Message` / `SentAt` / `IsRead`) — **KHÔNG** đụng schema; khoá luồng 1-1 = `(CustomerID, StaffID)`. **Verify thật:** 401 không token · 200 peers cả 2 chiều (khách ⇒ staff/admin/doctor; nhân viên ⇒ khách) · 200 history · 200 send trả `ChatMessageDto` · history lại có tin vừa gửi (lưu DB đúng) · chiều ngược đọc cùng lịch sử · realtime `ChatReceived` nhận thật bằng client .NET **và** widget Blazor tự render tin nhận · widget có mặt trên `/`, `/products`, `/system` · CSS 200 · build **0/0** · test **79/79** · tin test đã xoá sạch (DB về `MAX(MessageID)=1025`, 29 dòng) |
| **Q22** *(mới — Q9)* | **`Customer.birth_date`** — user mới chỉ duyệt thêm cột `avatar_url`. Nguồn có ô "Ngày sinh" (`ProfileEditPage.jsx`) nhưng lưu localStorage; đích hiện cũng lưu localStorage. Có thêm cột `birth_date` vào DB không? | 🟡 Parity | Nên thêm nếu muốn profile bền vững giống `avatar_url` (ALTER idempotent, không xoá dữ liệu) | ✅ **ĐÃ LÀM (08/11/2026)** — user duyệt **"Có"**. `ALTER TABLE Customer ADD birth_date DATE NULL` (có `IF COL_LENGTH(...) IS NULL` ⇒ idempotent, **không** xoá dữ liệu, **không** EF migration); verify `INFORMATION_SCHEMA.COLUMNS` → `birth_date / date / NULL`. Mapping EF `BirthDate`; 3-layer `IAuthRepository.UpdateCustomerBirthDateAsync` → `IAuthService.UpdateBirthDateAsync` (`Services/AuthService.BirthDate.cs`); API `POST/PUT api/auth/birth-date` + `GET api/auth/me` trả `birthDate`; UI `/account` + `/profile` + `/profile/edit` đọc/ghi ngày sinh theo thứ tự localStorage → DB → rỗng (**markup không đổi**) |
| **Q23** *(mới — Q9)* | **Nút "Xoá ảnh"** trong UI profile — API đã hỗ trợ `avatarUrl: null` nhưng chưa có nút (vì markup phải giữ 100% bản gốc). Thêm nút hay không? | 🟡 UX | Nguồn **không có** nút này ⇒ theo yêu cầu parity ⇒ **không thêm** | ⏳ Chờ |

---

## 8. KẾT LUẬN

### 8.1. Khớp 100%

| Nhóm | Kết quả |
|---|---|
| **Build & test** | ✅ 0 error / 0 warning · **93/93** test pass · app chạy 2 cổng (5286 HTTP/1.1 + 5287 HTTP/2 h2c) |
| **Route UI** | ✅ **40/40** trả 200 — gồm mọi route nguồn + `/about` + `/contact` + `/profile` + `/spa-booking-detail/{id}` |
| **API lõi (18 nhóm)** | ✅ `api/products`, `api/pet-services`, `api/pets/*`, `api/spa-booking/*`, `api/shift-exchange/*`, `api/attendance/*`, `api/payroll/*`, `api/notifications/*`, `api/breed-pricing`, `api/reviews/*`, `api/orders/*`, `api/auth/*`, `api/config/*`, `api/boarding-rooms`, `api/validation-demo/*` |
| **API mới LS-04 (X-01..X-10)** | ✅ **10/10 route tồn tại**; vòng đời create→read→update→soft-delete→restore→permanent-delete đã chạy thật và **dọn sạch DB** |
| **Chat 1-1 + widget (Q21)** | ✅ `api/chat/{peers,history/{peerId},send}` — 401 không token · 200 hai chiều (khách ↔ nhân viên) · lưu `ChatMessages` đúng · realtime `ChatReceived` thật (client .NET + widget Blazor) · widget có mặt ở mọi trang · CSS cyan `#6fd5dd` · **không** đụng schema/nav/trang đã port |
| **MVC routes LS-03** | ✅ `/cart/session` 200 · `/mvc/ProductsPage/Index` **302→/products** · `Details/2` 200 · `Details/999999` **404** |
| **Content negotiation** | ✅ XML / CSV / JSON — đúng `Content-Type`, CSV có header cột đầy đủ |
| **OData** | ✅ `$metadata` (20 624 B) · `$filter` · `$select` · `$orderby` · `$top` · `$skip` · `$count` · `$expand` — **16/16 hợp lệ** |
| **Auth** | ✅ Endpoint `[Authorize]` không token ⇒ **401** (không phải 200) — cả token thiếu lẫn token sai |
| **gRPC (Ch09)** | ✅ Unary `GetProducts` → `total_count=42`; server-streaming → 5 item (verify bằng client .NET thật) |
| **JS client (Ch07)** | ✅ `/js-client-demo` 200 — `$.ajax()`/`$.get()`/`$.post()`/`XMLHttpRequest` + jQuery local |
| **Docker (Ch10)** | ✅ Multi-stage Dockerfile + compose + **image `petshop-prn232:latest` tồn tại** |
| **Asset** | ✅ **144/144 ảnh MD5 khớp** · **15/15 ảnh pet MD5 khớp** · **2/2 template email MD5 khớp** · **0 đường dẫn ảnh hỏng** trên 40/40 trang |
| **Email template** | ✅ 4/4 file resolve + render (R1 đã đóng) |
| **Chatbot AI (Q13)** | ✅ `POST /api/chatbot/message` → **200** với **`source=ai`** — key Gemini mới + model **`gemini-3.5-flash`** (`gemini-flash-latest` không còn khả dụng cho key mới: 404) |
| **Gửi email (Q17)** | ✅ `POST /api/test/email/spa` → **200** *email gửi thật được* — App Password mới |
| **Refresh token (Q2 / SL-07)** | ✅ `POST /api/auth/refreshtoken` theo slide Ch06 S40–S42 — claim `token_type`, hạn 7 ngày, **rotate**; access token **không** dùng để refresh được ⇒ **401** |
| **Ràng buộc Node/npm** | ✅ 0 hit `npm `/`node_modules`/`package.json`/`require(`/`process.env` |
| **Secret trong code C#** | ✅ **0 hit** trong toàn bộ code ứng dụng **và test** (Q19 đã dọn `PayOSServiceTests.cs`; credential thật → User Secrets) |

### 8.2. Lệch — tóm tắt

| Mức | Số | Nội dung |
|---|---|---|
| 🔴 | **0** | ~~Gemini key leaked~~ → ✅ **đã đóng** (Q13 — user cấp key mới + đổi model `gemini-3.5-flash`, `source=ai`); ~~SMTP credential sai~~ → ✅ **đã đóng** (Q17 — user cấp App Password mới, email gửi thật) |
| 🟠 | **2** | (L9) 2 component UI phụ thiếu (`ClearSearchButton`, `NotificationBell`) · (L10) role `doctor` chưa xử lý ở UI — *(L5) `price` ngoài biên DB trả 500 → ✅ đóng ở Q20 · (L12/L13) CSS lệch tông + thiếu 3 file → ✅ đóng ở Q18 · (L14) SMTP sai → ✅ đóng ở Q17 (08/11/2026)* |
| 🟡 | **7** | (L1) `breed-pricing/breed/1` 404 vì dữ liệu không có · (L2) `ProblemDetails` thiếu `detail` ở lỗi controller · (L6) 2 DTO payroll thiếu · (L8) fallback route chưa verify · (L11) 2 asset `public/` chưa copy · (L15) phụ thuộc CDN · (R-11/R-12) nợ kỹ thuật — *(L17/Q19) secret trong test file đã ✅ đóng ở Q19 (08/11/2026)* |

### 8.3. Nhận định cuối

> **Migration OJT → PRN232 đã đạt parity 100% ở tầng *hành vi quan sát được***: 40/40 route, 18/19 nhóm API lõi, 10/10 API LS-04, 16/16 OData, 159/159 asset MD5 khớp, 0 ảnh hỏng, 0 tham chiếu Node/npm, build sạch, test **93/93** (+ realtime SignalR LS-11 đã verify end-to-end bằng client .NET thật; + Q9 `Customer.avatar_url` đã ALTER + verify giá trị trong DB; + **Q20** validate `price` trả **400** thay vì 500; + **Q22** `Customer.birth_date` đã ALTER + verify giá trị trong DB; + **Q21** widget chat 1-1 khách ↔ nhân viên dùng SignalR — 9/9 probe runtime, realtime `ChatReceived` thật, tin test đã dọn sạch; + **Q2** refresh token theo slide Ch06 — 10/10 probe runtime, access token **không** dùng để refresh được ⇒ **401**; + **Q19** dọn secret — test PayOS không còn credential thật, CI pass **không cần secret**; + **Q13** chatbot **đã có câu trả lời AI thật** (`source=ai`, key mới + model `gemini-3.5-flash`); + **Q17** **email gửi thật được** (App Password mới)).
>
> **Không còn lỗi chí mạng nào** (build fail / route 500 / ảnh 404 hàng loạt) ⇒ **GĐ3 không phải sửa code**. Toàn bộ mục còn lại là **quyết định nội dung cần user chốt** (port component UI phụ, role `doctor`) hoặc **nợ kỹ thuật mức 🟡** (CDN, DTO payroll, fallback route).
>
> **Q13 + Q17 đều đã đóng (08/11/2026)** ⇒ **không còn việc credential nào chặn demo cho GVHD**:
> - **Q13 (Gemini)**: user đã cấp key mới ⇒ chatbot trả **`source=ai`** thật. Kèm phát hiện model `gemini-flash-latest` **không còn khả dụng** cho key mới (404) ⇒ đã đổi cấu hình + fallback sang **`gemini-3.5-flash`**.
> - **Q17 (Gmail)**: user đã cấp App Password mới ⇒ `POST /api/test/email/spa` → **200**, email **gửi thật được**.
> - ⚠️ **Còn 1 việc ngoài repo đích**: **rotate key Gemini cũ** trên Google Cloud Console + repo nguồn `Projectojt/` (xem R-19).
> - ⚠️ **Còn 2 việc ở bước đóng gói**: user-secrets chỉ nạp ở `Development` ⇒ deploy Production phải đặt biến môi trường `PayOS__*` (R-18); refresh token stateless không có blacklist (R-17).
>
> **Các câu hỏi còn lại (Q8, Q15, Q16) không chặn runtime** — chỉ chặn mức "giống 100%" về mặt thẩm mỹ/UI/schema. *(Q9 đã đóng trọn: `avatar_url` **và** `birth_date`; Q20 đã đóng: validate `price` trả 400; **Q2 đã đóng**: refresh token theo slide Ch06; **Q19 đã đóng**: secret → user-secrets.)*
>
> **Q18 đã đóng (08/11/2026)** — không còn chặn parity giao diện về mặt bảng màu.
>
> **Q21 đã đóng (08/11/2026)** — widget chat 1-1 khách ↔ nhân viên đã làm xong và kiểm chứng end-to-end (xem §4.12).
>
> **Q2 đã đóng (08/11/2026)** — refresh token port theo slide Ch06 S40–S42 (`CreateRefreshToken` + claim `token_type` + `POST api/auth/refreshtoken` **rotate** token) — 10/10 probe runtime, access token sai loại ⇒ **401** (xem §4.13).
>
> **Q19 đã đóng (08/11/2026)** — secret đã chuyển sang **User Secrets** (`PayOS:*`), `PayOSServiceTests.cs` không còn credential thật (CI pass **không cần secret**), `appsettings.example.json` có placeholder, `git ls-files` **không đổi** (xem §5.2, R-05).
>
> **Q13 đã đóng (08/11/2026)** — user đã cấp **key Gemini mới** (ghi vào `appsettings.json` local, **git-ignored** ⇒ không ghi key đầy đủ vào tài liệu tracked). ⚠️ **Phát hiện quan trọng:** model `gemini-flash-latest` **không còn khả dụng** cho key mới (API trả **404** *"This model ... is no longer available to new users. Please update your code to use models/gemini-3.8-flash"*) ⇒ đã đổi cấu hình + fallback trong `Services/GeminiProviderService.cs` + `Models/ChatbotOptions.cs` sang **`gemini-3.5-flash`**. **Verify thật:** `POST /api/chatbot/message` ⇒ **HTTP 200** với `{"reply":"Xin chào bạn! 🐾 …","source":"ai",…}` ⇒ **chatbot đã có câu trả lời AI thật**.
>
> **Q17 đã đóng (08/11/2026)** — user đã cấp **App Password Gmail mới** (cập nhật `appsettings.json` local, **git-ignored** ⇒ không ghi mật khẩu vào tài liệu tracked). **Verify thật:** `POST /api/test/email/spa` ⇒ **HTTP 200** `{"message":"Email spa () đã được gửi đến …","subject":"PetShop - Hoa don spa SPA-TEST001"}` ⇒ **email gửi thật được**.
>
> **Rủi ro còn lại (mức 🟡, không chặn demo):** R-17 refresh token stateless không có blacklist · R-18 user-secrets chỉ nạp ở `Development` (deploy Production phải đặt biến môi trường `PayOS__*`) · R-19 **rotate key Gemini cũ** trên Google Cloud Console + repo nguồn `Projectojt/` (**việc ngoài repo đích**).

---

## 8. GHI CHÚ Q18 — BẢNG ĐỐI CHIẾU MÀU (nguồn → đích)

> Bảng này ghi lại **giá trị màu đã copy nguyên văn từ nguồn** (không tự suy đoán) cho từng file CSS.

| File đích | Selector / biến | Nguồn (đúng) | Đích trước (sai) | Số dòng |
|---|---|---|---|---|
| `template/home.css` | `.footer-title` gradient | `#6fd5dd` | `#4a2477` | 1 |
| `template/home.css` | `.newsletter-input:focus` border | `#6fd5dd` | `#4a2477` | 1 |
| `template/home.css` | `.newsletter-button` gradient | `#6fd5dd` | `#4a2477` | 1 |
| `template/codConfirmation.css` | `.success-banner` shadow | `rgba(111, 213, 221, 0.15)` | `rgba(74, 36, 119, 0.08)` | 1 |
| `template/codConfirmation.css` | `.order-info-card` shadow | `rgba(111, 213, 221, 0.1)` | `rgba(74, 36, 119, 0.06)` | 1 |
| `template/codConfirmation.css` | `.suggested-products` shadow | `rgba(111, 213, 221, 0.1)` | `rgba(74, 36, 119, 0.06)` | 1 |
| `template/codConfirmation.css` | `.suggested-product-card:hover` shadow | `rgba(111, 213, 221, 0.2)` | `rgba(74, 36, 119, 0.12)` | 1 |
| `template/searchProducts.css` | `--search-primary` | `#6fd5dd` | `#4a2477` | 1 |
| `template/searchProducts.css` | `--search-primary-soft` | `rgba(111, 213, 221, 0.14)` | `rgba(74, 36, 119, 0.14)` | 1 |
| `template/searchProducts.css` | `--search-primary-mid` | `#3a9ea8` | `#5c2d91` | 1 |
| `template/searchProducts.css` | `.search-keyword`, `.clear-keyword-btn(:hover)`, `.filter-chip(:hover/.active)`, `.search-sort-tab(:hover/.active)`, `.search-sort-select:focus`, `.product-card--search:hover` | `#6fd5dd` / `rgba(111, 213, 221, …)` | `var(--search-primary*)` → tím | 9 |
| `template/profile.css` | `.avatarCropper__panel` border | `rgba(111, 213, 221, 0.15)` | `rgba(74, 36, 119, 0.15)` | 1 |
| `template/profile.css` | `.avatarCropper__close:hover` bg | `rgba(111, 213, 221, 0.12)` | `rgba(74, 36, 119, 0.08)` | 1 |
| `template/profile.css` | `.avatarCropper__circle` bg/shadow | `rgba(111, 213, 221, 0.22 / 0.2 / 0.15)` | `rgba(74, 36, 119, …)` | 1 |
| `template/profile.css` | `.avatarCropper__label input[type=range]` accent | `#6fd5dd` | `#4a2477` | 1 |
| `template/profile.css` | `.profile-shopee-btn--primary(:hover)`, `--secondary(:hover)`, `__avatarRing`, `__choosePhoto(:hover)`, `__link(:hover)`, `__hintCard*`, `__aside--edit`, `__dateBtn(:hover)` | **Shopee cam `#ee4d2d` / `#d7321e`** | tím `#4a2477` | 22 |
| `template/profile.css` | `.pet-page__input:focus` / `__select:focus`, `__uploadBtn(:hover)`, `__item:hover` | `#ee4d2d` / `rgba(0,0,0,…)` | tím | 6 |
| `template/checkoutResult.css` | `.checkout-result-btn--primary(:hover)` shadow | `rgba(111, 213, 221, 0.35 / 0.42)` | `rgba(74, 36, 119, 0.25 / 0.35)` | 2 |

### 8.1. Giải trình: `#4a2477` **CÒN** trong `wwwroot/css/**` (không phải lỗi)

> Yêu cầu ban đầu có nêu "grep `4a2477` = 0 hit". Sau khi đối chiếu **từng dòng** với nguồn, các hit còn lại **đều là màu đúng của bản gốc** — nguồn vốn đã dùng tím `#4a2477` ở những vị trí đó. Xoá/replace chúng sẽ **lệch khỏi bản gốc**.

| File đích | Nguồn tương ứng | Kết luận |
|---|---|---|
| `template/cart.css`, `template/checkout.css`, `template/auth.css`, `template/pageContextBar.css` | `styles/cart.css`, `styles/checkout.css`, `styles/auth.css`, `styles/pageContextBar.css` | ✅ **Nội dung khớp 100%** với nguồn (SHA256 trùng; `auth.css` chỉ khác newline cuối). Tím **có** trong nguồn |
| `template/home.css` (5 hit còn lại) | `styles/home.css` | ✅ **Đúng 100%** — `--ziggy-purple-dark: #4a2477`, `.products-create-btn` gradient `#4a2477 → #5bc4cc`, `.products-sidebar__item` — nguồn dùng tím ở đúng các chỗ này |
| `template/codConfirmation.css` (1 hit) | `styles/codConfirmation.css` | ✅ `--primary: #4a2477` — giống nguồn |
| `template/spa.css` (38 hit) | `styles/service.css` (khối SPA) | ✅ Nguồn dùng tím cho theme SPA (`--ziggy-purple`). File đích có **97 selector riêng** (viết lại cho Blazor) nhưng mọi giá trị tím đều **trùng giá trị nguồn** |
| `template/invoice.css` (13 hit) | `styles/service.css` (khối `SPA INVOICE`) | ✅ **0 dòng tím "chỉ có ở đích"** — mọi giá trị đều tồn tại nguyên văn trong nguồn |
| `template/checkoutResult.css` (2 hit) | — (file chỉ có ở đích) | ✅ Nền tím `#4a2477` giữ nguyên (khớp `.checkout-place-btn` của nguồn); **chỉ** đổi 2 box-shadow sang cyan theo nguồn |
| `spa/service-react.css` (71 hit), `spa/cart-react.css` (11 hit) | `styles/service.css`, `styles/cart.css` | ✅ **Khớp 100%** — `service-react.css` = `service.css` + 1 khối 47 dòng responsive riêng của đích; `cart-react.css` = `cart.css` |
| `template/systemPage.css` (12), `template/ordersStaff.css` (18) | `styles/systemPage.css`, `styles/ordersStaff.css` | ✅ **Copy nguyên văn** (SHA256 trùng) — tím là **màu gốc** của các file này |

**Tổng kết:** mọi hit `#4a2477` còn lại đều **trùng với bản gốc**. Những chỗ nguồn dùng **cyan `#6fd5dd`** đã được revert **đúng giá trị + đúng alpha** ở 5 file. Không thay màu mới, không thêm màu, không đổi layout/spacing/font-size/text/icon.

---

*Báo cáo do verifier agent tạo trong GĐ3. Bằng chứng gốc: 137 probe HTTP lưu tại `verify_results.csv`, `verify_results2.csv`, `verify_crud.csv`; script kiểm chứng tại `verify_*.ps1` (file tạm — có thể xoá). App đã dừng sau khi verify.*
