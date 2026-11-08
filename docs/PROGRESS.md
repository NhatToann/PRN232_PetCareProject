# PROGRESS.md — Trạng thái migration OJT → PRN232

> **Cập nhật lần cuối:** 08/11/2026 (sau GĐ0 + GĐ1)
> **Giai đoạn hiện tại:** ⏸️ **GĐ1 — CHỜ USER DUYỆT** `docs/PARITY.md` + `docs/ARCHITECTURE_REQUIREMENTS.md`
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

## 3. 10 GAP CHẶN RUNTIME (🔴) — ưu tiên xử lý trước

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

## 5. 12 câu hỏi chặn (cần user quyết)

| # | Câu hỏi | Khuyến nghị |
|---|---|---|
| **Q1** | **Google OAuth `ClientId` thật là gì?** Có thể đăng ký thêm `http://localhost:5286`? | **Bắt buộc user cung cấp** — chặn R6 |
| Q2 | Refresh token (Ch06)? | Làm tối giản nếu ưu tiên khớp slide |
| Q3 | AutoMapper? | Map tay |
| Q4 | Chatbot provider? | Gemini + Ollama (fallback chain) |
| Q5 | Chatbot lưu lịch sử DB? | Có (`ChatMessages` đã có sẵn) |
| Q6 | SignalR trong phạm vi? | **Không** — slide không nhắc |
| Q7 | `CartController` MVC có `Views/`? | Chuyển sang Razor `/cart` |
| Q8 | Route/component "chết" ở nguồn — port hay sửa? | Port nguyên văn |
| Q9 | `Customer.birth_date`/`avatar_url` — cho phép đụng schema? | Chấp nhận lệch (ràng buộc 1) |
| Q10 | `daNangData.js` — copy `.js` hay viết lại C#? | Copy nguyên `.js` |
| Q11 | Google Maps embed key? | User cung cấp hoặc bỏ iframe |
| Q12 | Namespace? | Giữ `PetShop.*` |

---

## 6. Việc tiếp theo (sau khi duyệt)

```
GĐ2 — Thực hiện theo lát cắt (14 lát, ~50 giờ)
  LS-01 Nền tảng asset & email ──┐
  LS-02 OData chuẩn hoá          │
  LS-03 Sửa lỗi MVC cart/routing├─► giải quyết R1..R5
  LS-04 API Product CRUD đầy đủ ┘
  LS-05 Asset/CSS/JS đồng bộ
  LS-06 Auth & Google OAuth (cần Q1)
  LS-07 PayOS end-to-end
  LS-08 Chatbot AI (port nguyên văn)
  LS-09 gRPC (Ch09)
  LS-10 JS client jQuery (Ch07)
  LS-11 Realtime (nếu Q6 cho phép)
  LS-12 Docker + dọn gap UI
  LS-13 Robustness 3-layer
  LS-14 Kiểm chứng + PARITY_REPORT.md
```

**Quy tắc commit:** mỗi lát cắt = 1 commit riêng, ví dụ `feat(prn232): migrate email templates & pet uploads (LS-01)`.
**Quy tắc tick:** chỉ tick `[x]` trong `PARITY.md` khi build pass **và** test pass **và** đã đối chiếu bản gốc.

---

## 7. Rủi ro đang theo dõi

| Rủi ro | Mức | Xử lý |
|---|---|---|
| Google OAuth không chạy (R6) | 🔴 | Chờ user Q1 |
| `SpaBookingService` static semaphore + self-HTTP ⇒ **deadlock tiềm năng** | 🟠 | Kiểm tra khi port (LS-07), cân nhắc bỏ static lock |
| 12 Razor gọi raw SQL (R7) | 🟠 | LS-13 |
| Gemini API key còn sống trong repo nguồn | 🟠 | User quyết định dùng lại / rotate |
| 4 CSS `@import` Google Fonts từ CDN ⇒ mất font offline | 🟡 | LS-05 |
| `react-icons` (npm) không có ở Blazor | 🟡 | LS-05 — đổi sang inline SVG / Font Awesome |
| `public/default-service-placeholder.png` được tham chiếu nhưng không tồn tại | 🟡 | Q4 |
| `ProductsController` đích chỉ GET ⇒ không demo được CRUD cho Ch07 | 🟠 | LS-04 |

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
