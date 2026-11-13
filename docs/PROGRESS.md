# PROGRESS.md — Trạng thái migration OJT → PRN232

> **Cập nhật lần cuối:** 08/11/2026 (sau GĐ3 — Kiểm chứng + đóng Q13/Q17)
> **Giai đoạn hiện tại:** ✅ **GĐ3 — KIỂM CHỨNG XONG** · `docs/PARITY_REPORT.md` đã tạo · **Q13 (Gemini) + Q17 (Gmail) đã đóng** ⇒ **không còn việc credential nào chặn demo cho GVHD** · còn **1 việc ngoài repo** (rotate key Gemini cũ) + 2 việc ở bước đóng gói
> **Ngôn ngữ:** Tiếng Việt (theo `AGENTS.md`)

---

## 1. Tóm tắt 1 dòng

> **Đích `PRN232_PetCareProject` đã là bản Blazor Server hoàn chỉnh của hệ thống OJT.** Migration = **vá 10 gap chặn runtime** + **port stack Chatbot/AI** + **bổ sung 3 phase slide chưa làm** (gRPC Ch09, JS client Ch07, Docker Ch10). **KHÔNG phải** viết lại từ đầu.

---

## 2. Đã hoàn thành

### 2.1. GĐ0 — Đọc & hiểu ✅

| Hạng mục | Kết quả |
|---|---|
| Đọc slide | ✅ 11 chapter PPTX (422 slide) → trích text vào `_slides_text/` → phân tích đầy đủ |
| Quét nguồn | ✅ `Projectojt/BackEnd` (18 controller, **92 endpoint**, 11 service, 5 repository, 60 model) + `Projectojt/frontend` (React 19 + Vite 7, **23 route**, 27 page, 17 component, **70 hàm API**, 14 CSS / 12.551 dòng, **144 ảnh**) |
| Quét đích | ✅ 22 controller, 32 service, 7 repository, 30 interface, 64 model, **39 DbSet**, 68 Razor, 45+ DI registration |
| Xác định vai trò Node.js | ✅ **Node chỉ là Vite + React SPA (build tool + UI). Backend nguồn 100% là C#** — chatbot, realtime, upload, middleware, PayOS đều đã là C#. Không có Node server cần viết lại. |

### 2.2. Kiểm chứng môi trường (đã chạy thật)

| Hạng mục | Kết quả |
|---|---|
| `dotnet build` | ✅ **0 error, 0 warning** (SDK 10.0.402, target `net8.0`) |
| `dotnet test` | ✅ 30/30 pass |
| DB connect | ✅ `sa` / `123` → `SHOP_PET_Database`, **32 bảng** (mật khẩu `1234` trong file cũ **sai** — đã sửa) |
| `dotnet run` | ✅ `Now listening on: http://localhost:5286` |
| `GET /` | ✅ HTTP 200 (36.614 bytes) |
| `appsettings.json` + `appsettings.Development.json` | ⚠️ **bị xoá khỏi đĩa** → **đã khôi phục** từ git history, giữ nguyên `GoogleAuth`/`Mail`/`PayOS`/`Attendance` |

### 2.3. Hội đồng khắc nghiệt (7 agent)

| Vai trò | Kết quả | File |
|---|---|---|
| Agent kiểm kê API | 92 endpoint, 18 controller, 14 nhóm bất thường | `_inventory/SOURCE_API_ENDPOINTS.md` |
| Agent kiểm kê Frontend | 23 route, 27 page, 17 component, 70 API, 144 asset, 12.551 dòng CSS | `_inventory/SOURCE_FRONTEND.md` |
| Agent kiểm kê đích | 39 DbSet, 22 controller, 68 Razor, 45+ DI | `_inventory/TARGET_EXISTING.md` |
| Agent luồng phức tạp | Chatbot / PayOS / OAuth / Email / Upload / Notification / Đổi ca / Spa — prompt + URL + header + thuật toán ký **nguyên văn** | `_inventory/SOURCE_COMPLEX_FLOWS.md` |
| **Red team** | 🔴 10 gap chặn runtime + TOP 10 rủi ro + chiến lược 5 giai đoạn | `_inventory/RED_TEAM_REVIEW.md` |
| **Kiến trúc sư trưởng** | Bảng slide→quyết định Ch00–10, sơ đồ solution, 14 lát cắt, 12 câu hỏi | `_inventory/ARCHITECT_DESIGN.md` |
| **Thẩm định** | **187 gap** đối chiếu từng cặp file/method/message/template/asset | `_inventory/GAP_AUDIT.md` |

### 2.4. GĐ1 — Tài liệu ✅ (đã tạo, chờ duyệt)

| File | Nội dung |
|---|---|
| `docs/ARCHITECTURE_REQUIREMENTS.md` | 73 mục yêu cầu, **227 trích dẫn `(ChXX, Slide YY)`**, 52 ✅ / 6 🟡 / 15 🔴 / 32 ❌ có lý do |
| `docs/PARITY.md` | **~250 dòng checkbox** — 11 lớp, 15 lát cắt, 12 câu hỏi chặn |
| `docs/_inventory/*.md` | 7 file kiểm kê thô (493 KB) |
| `docs/PROGRESS.md` | file này |
| `docs/agent.md` | quy trình + phân vai agent + quy tắc tối ưu RAM |

---

## 2.5. GĐ3 — KIỂM CHỨNG ✅ (verifier agent, 08/11/2026)

> **Báo cáo đầy đủ:** `docs/PARITY_REPORT.md` (8 mục). Bằng chứng gốc: 128 probe HTTP (`verify_results*.csv`) + script `verify_*.ps1`.

### Baseline kỹ thuật

| Hạng mục | Kết quả |
|---|---|
| `dotnet build --nologo "-v:q"` | ✅ **0 error / 0 warning** (9,72 s) |
| `dotnet test --nologo` | ✅ **93/93 pass**, 0 fail, 0 skip (baseline 49 + 7 LS-11 + 14 Q9 + 9 Q20/Q22 + 14 Q2) |
| `dotnet run --no-build --launch-profile http` | ✅ `Now listening on: http://[::]:5286` (HTTP/1.1) + `http://[::]:5287` (HTTP/2 h2c, gRPC) + `https://[::]:7083` (HTTPS) |
| DB `SHOP_PET_Database` | ✅ 32 bảng, `sa`/`123` |
| Docker | ✅ image `petshop-prn232:latest` tồn tại (470 MB) |

### Kết quả kiểm chứng runtime

| Nhóm | Kết quả |
|---|---|
| **A. Trang UI** | ✅ **40/40 route trả 200** (gồm `/about`, `/contact`, `/profile`, `/spa-booking-detail/1`) |
| **B. API lõi** | ✅ **18/19** nhóm 200 (`api/breed-pricing/breed/1` → 404 vì `breedId=1` không có trong DB) |
| **C. API mới LS-04** | ✅ **10/10 route tồn tại**; **vòng đời CRUD đầy đủ** create→read→update→soft-delete→deleted-list→restore→soft-delete→permanent-delete→404 ⇒ **dọn sạch DB 100%** |
| **D. Auth** | ✅ `api/auth/me` không token → **401**, token sai → **401** (đúng, không 200) |
| **E. MVC (LS-03)** | ✅ `/cart/session` 200 · `/mvc/ProductsPage/Index` **302**→`/products` · `Details/2` 200 · `Details/999999` **404** |
| **F. Content negotiation** | ✅ XML / CSV / JSON — đúng `Content-Type` |
| **G. ProblemDetails** | ⚠️ middleware có đủ `type`/`title`/`status`/`detail`; `NotFound()` của controller thiếu `detail` (🟡) |
| **H. OData** | ✅ **16/16** — `$filter`/`$select`/`$orderby`/`$top`/`$skip`/`$count`/`$expand`/`$metadata` |
| **I. Asset** | ✅ **0 đường dẫn hỏng** (13/13 URL cục bộ ở 40/40 trang); MD5: **144/144 ảnh + 15/15 ảnh pet + 2/2 template `.hbs` khớp 100%** |
| **J. Chatbot** | ✅ 4 endpoint 200, shape khớp 100%; **`source=ai`** (Q13 đã đóng — key Gemini mới + model `gemini-3.5-flash`) |
| **K. Google OAuth** | ✅ **3/3 tầng** — config 200 (Client ID thật) · UI `#google-login-btn` + GSI · token giả → 401 |
| **L. PayOS** | ✅ `POST order/create` → **200 link PayOS thật** · webhook sai chữ ký → **401**; config khớp 100% nguồn; **không thanh toán thật** |
| **M. gRPC** | ✅ verify bằng client .NET thật: unary `total_count=42` + server-streaming 5 item |
| **N. Docker** | ✅ multi-stage Dockerfile + compose + image tồn tại; `/js-client-demo` 200 (`$.ajax()`/`XMLHttpRequest`/jQuery local) |

### Ràng buộc GĐ3

| Hạng mục | Kết quả |
|---|---|
| Tham chiếu Node/npm | ✅ **0 hit** `npm `/`node_modules`/`package.json`/`require(`/`process.env` (`vite`/`react` chỉ là comment lịch sử) |
| Secret hard-code trong code C# | ✅ **0 hit** trong code ứng dụng **và test** (Q19 — `PayOSServiceTests.cs` dùng key giả cố định ⇒ CI pass không cần secret thật) |
| Ảnh hỏng | ✅ **0** |

> **Kết luận GĐ3:** **0 lỗi chí mạng** (không build fail / không route 500 hàng loạt / không ảnh 404 hàng loạt) ⇒ **không sửa code**. Toàn bộ mục còn lại là **quyết định nội dung cần user chốt** hoặc **nợ kỹ thuật mức 🟡**.
>
> **Việc user cần làm trước khi demo cho GVHD:** ✅ **KHÔNG CÒN** — **Q13** (Gemini key mới + model `gemini-3.5-flash` ⇒ `source=ai`) và **Q17** (App Password mới ⇒ email gửi thật) đều đã đóng.
>
> **Còn lại (không chặn demo):**
> 1. ⚠️ **Ngoài repo đích** — **rotate key Gemini cũ** trên Google Cloud Console + repo nguồn `Projectojt/` (xem `docs/PARITY_REPORT.md` R-19).
> 2. ⚠️ **Bước đóng gói** — user-secrets chỉ nạp ở `Development` ⇒ deploy Production phải đặt biến môi trường `PayOS__*` (R-18).
> 3. 🟡 Refresh token **stateless không có blacklist** (token đã rotate vẫn dùng được — đúng mức "tối giản" theo slide Ch06) (R-17).
>
> **Lệch còn lại:** 🔴 **0** · 🟠 2 (2 component UI phụ thiếu, role `doctor` chưa xử lý ở UI) · 🟡 7 (chi tiết tại `docs/PARITY_REPORT.md` §3)

---

## 3. 10 GAP CHẶN RUNTIME (🔴) — ưu tiên xử lý trước

> **Trạng thái sau GĐ3:** ✅ **R1–R10 đều đã đóng** (xem `docs/PARITY.md` §1.2). GĐ3 xác nhận bằng runtime thật: R1 (4/4 template resolve) · R2 (15/15 ảnh pet MD5 khớp) · R3 (`/cart/session` 200) · R4 (`/mvc/ProductsPage/*` 200/302/404) · R5 (`/about` + `/contact` 200) · R6 (Client ID thật, 3/3 tầng OAuth OK) · R7 (0 hit raw SQL trong `Components/`) · R8 (4 endpoint chatbot 200) · R9 (10/10 endpoint LS-04 tồn tại) · R10 (PayOS config khớp 100% nguồn, tạo link thật 200).

| # | Gap | Bằng chứng | Lát cắt |
|---|---|---|---|
| **R1** | `wwwroot/EmailTemplates/` không tồn tại ⇒ `EmailService` ném `FileNotFoundException` khi gửi hoá đơn | `Services/EmailService.cs:50,87,107,147` | LS-01 |
| **R2** | `wwwroot/uploads/pets/` không tồn tại ⇒ 15 ảnh pet mất ⇒ **5 pet trong DB 404 ảnh** | `GAP_AUDIT.md` §5 | LS-01 |
| **R3** | `CartController` thiếu action `Index` + không có `Views/` ⇒ `RedirectToAction(nameof(Index))` ⇒ **HTTP 500** | `Controllers/CartController.cs:30,44,81` | LS-03 |
| **R4** | `Program.cs` map `mvc/{controller=ProductsPage}` nhưng **không có `ProductsPageController`** ⇒ `/mvc/*` 404 | `Program.cs` (cuối file) | LS-03 |
| **R5** | `Footer.razor` link `/about` + `/contact` nhưng không có page ⇒ **link chết ở mọi trang** | `Components/Shared/Footer.razor` | LS-12 |
| **R6** | Google OAuth: `RedirectUri=:5173` nhưng app chạy `:5286`; `ClientId` **rỗng** ⇒ **login Google không hoạt động** | `appsettings.json`, `ConfigController.cs` | LS-06 |
| **R7** | 12 Razor component gọi thẳng `ShopPetDatabaseContext` + raw SQL ⇒ **vi phạm slide Ch02 3-layer** | 12 hit trong `Components/` | LS-13 |
| **R8** | Toàn bộ stack Chatbot/AI mất ⇒ **157 message tiếng Việt mất** | `GAP_AUDIT.md` §1.2 | LS-08 |
| **R9** | 10 endpoint FE nguồn gọi không tồn tại ở **cả 2 bên** ⇒ port FE nguyên văn chết ngay | X-01..X-10 | LS-04 |
| **R10** | `PayOSService.CreatePaymentLinkAsync` đã **xoá fallback** `https://api.payos.vn/v2` | `GAP_AUDIT.md` §1.2 | LS-07 |

---

## 4. Số liệu parity

> **Cập nhật sau GĐ3 (kiểm chứng runtime thật):**

| Hạng mục | Nguồn | Đích | Trạng thái (GĐ3) |
|---|---|---|---|
| API endpoint | 92 | 90 | ✅ **10/10 endpoint LS-04 tồn tại + 4 chatbot**; verify runtime 18/19 nhóm trả 200 |
| Route UI | 23 | 38 | ✅ **40/40 route trả 200**; `/about` + `/contact` + `/profile` + `/spa-booking-detail/{id}` đã đóng |
| Component | 17 | 16+ | ✅ `AddressModal` + `daNangData` **đã có** (thông tin cũ); ❌ còn thiếu `ClearSearchButton` + `NotificationBell` |
| Service | 11 | 32 | ✅ Chatbot/AI **đã port**; **Q13 đã đóng** — `source=ai` (key Gemini mới + model `gemini-3.5-flash`) |
| Repository | 5 | 7 | ✅ **5/5 đầy đủ, 0 method mất** — verify CRUD đầy đủ (create→restore→permanent-delete) |
| Model | 60 | 64 | ✅ `ChatbotDtos`/`ChatbotOptions` **đã có**; ⚠️ còn thiếu 2 `PayrollDtos` (không endpoint nào dùng) |
| Validation message VI | 325 | 471 | ✅ **0 đổi nội dung** — verify runtime: 400 trả đúng message VI (`Số lượng tồn kho phải lớn hơn 0.`, `Thời lượng phải từ 5 đến 480 phút.`) |
| Email template `.hbs` | 2 | 4 | ✅ **2/2 MD5 khớp** + 2 file dựng mới; **4/4 resolve & render** ⇒ R1 đóng |
| Asset ảnh | 144 | 148 | ✅ **144/144 MD5 khớp 100%** + **15/15 ảnh pet MD5 khớp**; **0 đường dẫn hỏng** |
| CSS | 14 | 17 | ✅ **Q18 đã đóng** — revert màu về tông gốc + port 3 file CSS còn thiếu (SHA256 khớp 100%) |
| Slide Ch07 (JS client) | — | ✅ | ✅ `/js-client-demo` 200 — `$.ajax()`/`$.get()`/`$.post()`/`XMLHttpRequest` + jQuery local |
| Slide Ch09 (gRPC) | — | ✅ | ✅ **verify client .NET thật**: unary `total_count=42` + server-streaming 5 item |
| Slide Ch10 (Docker) | — | ✅ | ✅ multi-stage Dockerfile + compose + **image `petshop-prn232:latest` tồn tại** |

### Số liệu parity (bản gốc, trước GĐ3)

| Hạng mục | Nguồn | Đích | Trạng thái |
|---|---|---|---|
| API endpoint | 92 | 90 | 🔴 4 chatbot + 🟠 10 `*-temp` thiếu |
| Route UI | 23 | 38 | 🔴 thiếu `/about`, `/profile` |
| Component | 17 | 16+ | 🔴 thiếu 4 (`AddressModal`, `ClearSearchButton`, `NotificationBell`, `daNangData`) |
| Service | 11 | 32 | 🔴 thiếu 3 (Chatbot/AI) |
| Repository | 5 | 7 | ✅ **5/5 đầy đủ, 0 method mất** |
| Model | 60 | 64 | 🔴 thiếu 2 (ChatbotDtos, ChatbotOptions) |
| Validation message VI | 325 | 471 | 🔴 thiếu 157 (toàn bộ Chatbot) — **0 đổi nội dung** ✅ |
| Email template `.hbs` | 2 | **0** | 🔴 **cả 2 thiếu** |
| Asset ảnh | 144 | 148 | ✅ **144/144 MD5 khớp 100%** |

---

## 5. Câu hỏi chặn (cần user quyết) — cập nhật sau GĐ3

> **Ưu tiên cao (chặn demo cho GVHD): ✅ KHÔNG CÒN — Q13 + Q17 đều đã đóng (08/11/2026).**

| # | Câu hỏi | Khuyến nghị / Trạng thái |
|---|---|---|
| **Q13** ✅ | **Gemini API key đã bị Google báo LEAKED** (403 `PERMISSION_DENIED`) ⇒ chatbot luôn `source=fallback`. Cấp key mới? | ✅ **ĐÃ ĐÓNG (08/11/2026)** — user đã cấp **key Gemini mới** (`appsettings.json` local, **git-ignored** ⇒ không ghi key đầy đủ vào tài liệu tracked). ⚠️ Model `gemini-flash-latest` **không còn khả dụng** cho key mới (404) ⇒ đã đổi cấu hình + fallback sang **`gemini-3.5-flash`**. **Verify thật:** `POST /api/chatbot/message` ⇒ **200** với **`source=ai`** ⇒ chatbot **đã có câu trả lời AI thật** |
| **Q17** ✅ | **App Password Gmail sai** (5.7.8 BadCredentials) ⇒ email hoá đơn không gửi được | ✅ **ĐÃ ĐÓNG (08/11/2026)** — user đã cấp **App Password mới** (`appsettings.json` local, **git-ignored** ⇒ không ghi mật khẩu vào tài liệu tracked). **Verify thật:** `POST /api/test/email/spa` ⇒ **200** ⇒ **email gửi thật được** |
| **Q18** ✅ | **Tông màu & font CSS**: giữ tím `#4a2477` hay revert gốc `#6fd5dd`? Có port 3 CSS còn thiếu? | ✅ **ĐÃ LÀM (08/11/2026)** — revert màu về tông gốc + port nguyên văn 3 file CSS còn thiếu (SHA256 khớp 100%) + link trong `App.razor` |
| **Q20** ✅ | `POST /api/products-temp/create` trả **500** khi `price` ngoài biên DB ⇒ có sửa thành 400? | ✅ **ĐÃ LÀM (08/11/2026)** — validate `price ∈ [1 000, 10 000 000]` (biên từ constraint thật `CK_Product_Price_Valid`) ở `Create` + `Update` ⇒ **400** `"Giá tiền không hợp lệ."` |
| **Q19** ✅ | Secret trong `tests/PetShop.Tests/PayOSServiceTests.cs:24,26` ⇒ chuyển sang user-secrets? | ✅ **ĐÃ LÀM (08/11/2026)** — test dùng **key giả cố định** (CI pass không cần secret thật) + credential thật → **User Secrets** (`PayOS:*`), `appsettings.json` để rỗng |

> **Còn lại (không chặn runtime):**

| # | Câu hỏi | Khuyến nghị |
|---|---|---|
| **Q1** | **Google OAuth `ClientId` thật là gì?** Có thể đăng ký thêm `http://localhost:5286`? | ✅ **ĐÃ CÓ** — `517086557037-q8nv5flbbfim8sc9cmp28bquqkhq4fc2.apps.googleusercontent.com`, `RedirectUri` khớp; verify 3/3 tầng OK |
| Q2 | Refresh token (Ch06)? | ✅ **ĐÃ LÀM (08/11/2026)** — user chốt "Có": port tối giản theo slide Ch06 S40–S42 (`CreateRefreshToken` + claim `token_type` + `POST api/auth/refreshtoken` **rotate** token, hạn 7 ngày). Probe: login 200 · refresh hợp lệ 200 · access token sai loại **401** · rác 401 · rỗng 400 · `/api/auth/me` 200. Test: 14 mới ⇒ **93/93 pass** |
| Q3 | AutoMapper? | ✅ **Đã chốt** — map tay |
| Q4 | Chatbot provider? | ✅ **Đã chốt** — chỉ Gemini |
| Q5 | Chatbot lưu lịch sử DB? | Có (`ChatMessages` đã có sẵn) |
| Q6 | SignalR trong phạm vi? | ✅ **ĐÃ LÀM (LS-11)** — ngoài slide, theo yêu cầu chủ dự án: realtime notification/chat (hub + service 3-layer + client Blazor + gắn vào luồng đổi ca + `/signalr-demo`) |
| Q7 | `CartController` MVC có `Views/`? | ✅ `/cart/session` → 200 (R3 đóng) |
| Q8 | Route/component "chết" ở nguồn — port hay sửa? | Port nguyên văn (còn: `ClearSearchButton`, `NotificationBell`, role `doctor`) |
| Q9 | `Customer.birth_date`/`avatar_url` — cho phép đụng schema? | ✅ **ĐÃ LÀM TRỌN (08/11/2026)** — user duyệt cả 2 cột: `Customer.avatar_url NVARCHAR(500) NULL` (Q9) + `Customer.birth_date DATE NULL` (Q22) — ALTER idempotent, **không** xoá dữ liệu, **không** EF migration + EF mapping + API `POST/PUT api/auth/avatar` + `POST/PUT api/auth/birth-date` + 3-layer + UI đọc localStorage → DB → rỗng |
| Q10 | `daNangData.js` — copy `.js` hay viết lại C#? | Copy nguyên `.js` |
| Q11 | Google Maps embed key? | User cung cấp hoặc bỏ iframe |
| Q12 | Namespace? | Giữ `PetShop.*` |

---

## 6. Việc tiếp theo (sau GĐ3)

```
GĐ3 — KIỂM CHỨNG ✅ (08/11/2026, verifier agent)
  → docs/PARITY_REPORT.md: 8 mục, 137 probe, 0 lỗi chí mạng
  → 0 sửa code (không có build fail / 500 / 404 cascade)
  → PARITY.md §15 + PROGRESS.md §2.5 cập nhật

  → ĐÃ LÀM THÊM (08/11/2026): Q13 (Gemini key mới + model gemini-3.5-flash ⇒ source=ai)
                          Q17 (App Password mới ⇒ email gửi thật)
                          Q18 (revert CSS + port 3 file) · Q19 (dọn secret → user-secrets)
                          Q20 (validate price ⇒ 400) · Q21 (widget chat 1-1) · Q22 (birth_date)
                          Q2 (refresh token Ch06) · Q9 (avatar_url) · LS-11 (SignalR)
  → dotnet test: 93/93 pass

GĐ4 — Bàn giao & đóng gói
  1. ⚠️ Rotate key Gemini cũ trên Google Cloud Console + repo nguồn Projectojt/ (ngoài repo đích)
  2. ⚠️ Deploy Production: đặt biến môi trường PayOS__ClientId / PayOS__ApiKey / PayOS__ChecksumKey
     (user-secrets chỉ nạp ở Development)
  3. Cân nhắc thêm blacklist cho refresh token (stateless hiện tại — token đã rotate vẫn dùng được)
  4. Re-run: dotnet build + dotnet test + smoke 40 route + dựng Docker
```

**Quy tắc commit:** mỗi lát cắt = 1 commit riêng, ví dụ `feat(prn232): migrate email templates & pet uploads (LS-01)`.
**Quy tắc tick:** chỉ tick `[x]` trong `PARITY.md` khi build pass **và** test pass **và** đã đối chiếu bản gốc.

---

## 7. Rủi ro đang theo dõi (cập nhật sau GĐ3)

| Rủi ro | Mức | Trạng thái sau GĐ3 |
|---|---|---|
| Gemini API key bị Google báo **leaked** ⇒ chatbot luôn fallback | 🟢 | **ĐÃ ĐÓNG (08/11/2026)** — user cấp key mới + đổi model `gemini-3.5-flash` ⇒ `source=ai`. ⚠️ Còn 1 việc **ngoài repo**: **rotate key cũ** trên Google Cloud Console + repo nguồn `Projectojt/` (R-19) |
| App Password Gmail sai ⇒ email hoá đơn fail | 🟢 | **ĐÃ ĐÓNG (08/11/2026)** — user cấp App Password mới ⇒ `POST /api/test/email/spa` → **200**, email **gửi thật được** (Q17) |
| Google OAuth `ClientId` | 🟢 | **ĐÃ ĐÓNG** — client id lấy được từ config, `/login` có `#google-login-btn` + GSI script |
| `SpaBookingService` static semaphore + self-HTTP ⇒ deadlock tiềm năng | 🟠 | **CHƯA xác minh** — `/spa-booking-history` 200 nhưng chưa tạo booking thật |
| 12 Razor gọi raw SQL (R7) | 🟠 | **CÒN** — chưa refactor (không chặn runtime) |
| 4 CSS `@import` Google Fonts từ CDN ⇒ mất font offline | 🟡 | **CÒN** — ưu tiên thấp |
| `react-icons` (npm) không có ở Blazor | 🟢 | **ĐÃ ĐÓNG** — 0 tham chiếu npm/node trong code đích (verify bằng Grep) |
| `public/default-service-placeholder.png` không tồn tại | 🟡 | **ĐÃ ĐÓNG** — 0 đường dẫn asset hỏng (13/13 local asset 200) |
| `ProductsController` đích chỉ GET ⇒ không demo được CRUD cho Ch07 | 🟢 | **ĐÃ ĐÓNG** — LS-04 `products-temp/*` CRUD đầy đủ (search→create→update→delete→restore→permanent-delete) |
| `price` vi phạm CHECK constraint ⇒ 500 thay vì 400 | 🟢 | **ĐÃ ĐÓNG (08/11/2026)** — validate `price ∈ [1 000, 10 000 000]` ở `Create` + `Update` ⇒ **400** (Q20) |
| Refresh token **stateless không có blacklist** ⇒ token đã rotate vẫn dùng được tới khi hết hạn (7 ngày) | 🟡 | **CHẤP NHẬN** — đúng mức "tối giản" theo slide Ch06 mà user đã duyệt (Q2). Khi cần thu hồi: thêm claim `jti` + bảng denylist (R-17) |
| **User-secrets chỉ nạp ở `Development`** ⇒ deploy **Production** không có `PayOS:*` | 🟡 | **CẦN LÀM Ở BƯỚC ĐÓNG GÓI** — đặt biến môi trường `PayOS__ClientId` / `PayOS__ApiKey` / `PayOS__ChecksumKey` (R-18) |
| Secret Google/Gmail/Gemini vẫn nằm trong `appsettings.json` local | 🟡 | **KHÔNG CHẶN** — file này **git-ignored** ⇒ không bị commit. Nên chuyển sang user-secrets/biến môi trường ở bước đóng gói (R-05) |

---

## 8. Cấu trúc repo docs

```
PRN232_PetCareProject/docs/
├── ARCHITECTURE_REQUIREMENTS.md   ← BẢN CHÍNH THỨC user duyệt (73 mục, 227 trích dẫn slide)
├── PARITY.md                       ← checklist đầy đủ (~250 dòng checkbox)
├── PROGRESS.md                     ← file này
├── agent.md                        ← quy trình + phân vai agent + tối ưu RAM
└── _inventory/                     ← kiểm kê thô (493 KB, 7 file)
    ├── SOURCE_API_ENDPOINTS.md     (51 KB)  92 endpoint nguồn
    ├── SOURCE_FRONTEND.md          (64 KB)  23 route / 27 page / 144 asset
    ├── SOURCE_COMPLEX_FLOWS.md     (89 KB)  8 luồng phức tạp, prompt nguyên văn
    ├── TARGET_EXISTING.md          (44 KB)  code đích đã có
    ├── GAP_AUDIT.md                (38 KB)  187 gap đối chiếu
    ├── RED_TEAM_REVIEW.md          (91 KB)  phản biện + TOP 10 rủi ro
    └── ARCHITECT_DESIGN.md         (116 KB) thiết kế kiến trúc + 14 lát cắt
```
