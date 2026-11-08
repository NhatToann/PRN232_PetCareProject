# 🔴 RED TEAM REVIEW — Phản biện kiến trúc migration OJT → PRN232

> **Vai trò:** RED TEAM trong hội đồng khắc nghiệt.
> **Nhiệm vụ:** PHÁ giả định của team kiến trúc, không viết code — chỉ ra điểm yếu, rủi ro chết người, và đề xuất chiến lược tốt hơn.
> **Nguồn:** `Projectojt/` (chỉ đọc) + `PRN232_PetCareProject/` (chỉ đọc).
> **Ngày:** 2026-11-08. **Trạng thái:** mọi phát hiện dưới đây đã được đối chiếu bằng `grep`/`read` trực tiếp trên mã nguồn, không chỉ dựa vào tài liệu kiểm kê.

---

## Tóm tắt điều hành (Executive Summary)

Giả định trung tâm của team kiến trúc — **"đích đã là bản Blazor Server hoàn chỉnh; migration = bổ sung mảnh còn thiếu (chủ yếu Chatbot AI)"** — **sai ở mức cấu trúc**, không chỉ sai ở mức chi tiết.

Ba lỗi chí mạng:

1. **Parity API sai.** 8 route mà frontend nguồn gọi **không tồn tại ở cả nguồn lẫn đích** — nghĩa là nguồn vốn đã có UI gọi API chết. Đội "bổ sung mảnh thiếu từ nguồn" sẽ **migrate cả những endpoint không tồn tại** mà không hề biết.
2. **"UI giống 100%" là ảo.** 23 route React của nguồn vs 35 route Blazor của đích: **6 route nguồn không có bản tương đương** (`/about`, `/orders`, `/profile`, `/search` chỉ gián tiếp, `/products/deleted` chết link, `/system` khác cấu trúc tab). Asset chung **146 vs 148 file** — nhưng nguồn có `public/logo-pet-toy.png` mà đích **không có**.
3. **"Không cần đụng DB" sai một bước, nhưng sai về kiến trúc.** Schema DB **đủ** (31 `CREATE TABLE`, `ChatMessages` có sẵn) — nhưng **10 Razor component ở đích gọi thẳng `ShopPetDatabaseContext` bằng SQL thô**, vi phạm đúng yêu cầu slide Ch02 (Repository/Service 3-layer) mà team kiến trúc lại tuyên bố "đã tuân thủ".

Chiến lược thay thế: **đừng port theo file; port theo contract.** Sinh một *contract test* dạng bảng (route nguồn → route đích → trạng thái) và chỉ migrate những dòng có bằng chứng chạy được.

---

## 1. PHÁ GIẢ ĐỊNH "ĐÍCH ĐÃ LÀ BẢN BLAZOR SERVER HOÀN CHỈNH"

### 1.1 Route nguồn → đích: bảng đối chiếu đầy đủ

Tôi đã trích **toàn bộ** attribute route từ 22 file controller nguồn (`Projectojt/BackEnd/Controllers/`) và 21 file controller đích (`PRN232_PetCareProject/Controllers/`) bằng regex `[Http(Get|Post|Put|Patch|Delete)]` + `[Route]`.

**Kết quả: 87 route nguồn vs 92 route đích.** Phần lớn trùng khớp 1-1. Nhưng có **8 route nguồn KHÔNG có bản tương đương ở đích** và **4 route nguồn đã "chết" ngay trong chính nguồn** (frontend gọi nhưng backend không có).

### 1.2 🔴 8 ROUTE NGUỒN THIẾU Ở ĐÍCH (bằng chứng)

| # | Route nguồn | Controller nguồn (dòng) | Tình trạng ở đích | Bằng chứng đích |
|---|---|---|---|---|
| 1 | `GET /api/products-temp` | `ProductsController_temp.cs:18` | **ĐỔI TÊN** → `api/products` | `Controllers/ProductsController.cs:8` `[Route("api/products")]` |
| 2 | `GET /api/products-temp/categories` | `ProductsController_temp.cs:25` | **ĐỔI TÊN** → `api/products/categories` | `ProductsController.cs:25` |
| 3 | `GET /api/products-temp/{id:int}` | `ProductsController_temp.cs:32` | **ĐỔI TÊN** → `api/products/{id:int}` | `ProductsController.cs:32` |
| 4 | `GET /api/orders-temp/history` | `OrdersController_temp.cs:40` | **ĐỔI TÊN** → `api/orders/history` | `Controllers/OrdersController.cs:40` |
| 5 | `GET /api/orders-temp/{orderId:int}` | `OrdersController_temp.cs:53` | **ĐỔI TÊN** → `api/orders/{orderId:int}` | `OrdersController.cs:53` |
| 6 | `PATCH /api/orders-temp/staff/{orderId:int}/confirm` | `OrdersController_temp.cs:118` | **ĐỔI TÊN** → `api/orders/staff/{orderId:int}/confirm` | `OrdersController.cs:118` |
| 7 | `PATCH /api/orders-temp/{orderId:int}/delivered` | `OrdersController_temp.cs:143` | **ĐỔI TÊN** → `api/orders/{orderId:int}/delivered` | `OrdersController.cs:156` |
| 8 | `POST /api/checkout-temp` | `CheckoutController_temp.cs:21` | **ĐỔI TÊN** → `api/checkout` | `Controllers/CheckoutController.cs:12` |

**8 route này không "thiếu" — chúng ĐÃ ĐƯỢC ĐỔI TÊN.** Đây là kết luận có lợi cho team kiến trúc. **NHƯNG** nó kéo theo 3 hệ quả chết người mà giả định "giữ nguyên kiến trúc" không đề cập:

#### 🔴 Hệ quả A: 4 route nguồn **THẬT SỰ** thiếu — vì frontend gọi chúng

Đây là phát hiện nghiêm trọng nhất của mục 1. Tôi grep **toàn bộ** `src/api/*.js` và **toàn bộ** `fetch()` trực tiếp trong `pages/` + `components/` của nguồn:

| Endpoint frontend nguồn gọi | Gọi từ | Tồn tại ở backend nguồn? | Tồn tại ở đích? |
|---|---|---|---|
| `POST /api/products-temp/upload-image` | `api/productsApi.js:56` (→ `CreateProductPage.jsx:113`) | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `POST /api/products-temp/create` | `api/productsApi.js:74` (→ `CreateProductPage.jsx:116`) | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `POST /api/products-temp/delete` | `api/productsApi.js:118` | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `POST /api/products-temp/update` | `api/productsApi.js:157` | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `GET /api/products-temp/deleted` | `api/productsApi.js:186` | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `POST /api/products-temp/restore` | `api/productsApi.js:199` | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `POST /api/products-temp/permanent-delete` | `api/productsApi.js:219` | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `GET /api/products-temp/search` | `api/productsApi.js:18` | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `POST /api/pet-services` | `api/petServicesApi.js` (→ `ServicePage.jsx:262`) | ❌ **KHÔNG** | ❌ **KHÔNG** |
| `POST /api/pet-services/upload-image` | `api/petServicesApi.js` (→ `ServicePage.jsx:260`) | ❌ **KHÔNG** | ❌ **KHÔNG** |

**Bằng chứng nguồn thiếu route:** `Projectojt/BackEnd/Controllers/ProductsController_temp.cs` — file đầy đủ chỉ có **3** action (`GetAll`, `GetCategories`, `GetById`). Không có `upload-image`, `create`, `delete`, `update`, `deleted`, `restore`, `permanent-delete`, `search`. Tương tự `PetServicesController.cs` nguồn chỉ có 3 action GET.

**Bằng chứng đích thiếu route:** `PRN232_PetCareProject/Controllers/ProductsController.cs` — file đầy đủ 40 dòng, chỉ có 3 action GET. `PetServicesController.cs` đích — chỉ có `GET`, `GET grouped`, `GET {serviceId}`.

> **⚠️ Bẫy chết người:** Team kiến trúc sẽ "port nguyên văn" `src/api/*.js` sang đích. Khi đó **10 endpoint trong bảng trên sẽ 404**, và UI nguồn (CreateProductPage, ServicePage modal thêm dịch vụ, nút "xóa sản phẩm") **sẽ chết ngay lần chạy đầu**. Đây chính là loại lỗi mà "build pass" **không bắt được** — build vẫn xanh vì JS không được compile.

> **Trạng thái thực tế ở đích:** Chức năng này **đã được viết lại bằng cách đi thẳng DB trong Razor** — `ProductCreate.razor:98` inject `ShopPetDatabaseContext` và chạy `ExecuteSqlRawAsync("INSERT INTO Products ...")`. Tức là đích **đã có chức năng**, nhưng vi phạm 3-layer (xem Mục 5). Vậy điều cần làm **không phải** "migrate endpoint còn thiếu từ nguồn" — mà là **bọc lại chức năng đã có trong đích vào đúng tầng**.

#### 🔴 Hệ quả B: `GET /cart` bị mất — và `CartController` đích sẽ **throw**

| | Nguồn | Đích |
|---|---|---|
| `GET /cart` | ✅ `CartController.cs:19` `[HttpGet("/cart")]` | ❌ **KHÔNG CÓ** |
| `POST /cart/dec/{id}` | ✅ `CartController.cs:26` | ✅ `CartController.cs:18` |
| `POST /cart/remove/{id}` | ✅ `CartController.cs:42` | ✅ `CartController.cs:34` |
| `POST /cart/add/{id}` | ✅ `CartController.cs:55` | ✅ `CartController.cs:47` |

**Đây là bug runtime chắc chắn, không phải giả định.** Đích đã đổi `/cart` thành **Blazor page** `Components/Pages/Cart.razor` (`@page "/cart"`). Nhưng `CartController` đích vẫn còn **3 action POST** gọi `return RedirectToAction(nameof(Index));` — mà `CartController` **không có action `Index`** và **không có thư mục `Views/`**:

```csharp
// PRN232_PetCareProject/Controllers/CartController.cs:34
return RedirectToAction(nameof(Index));   // ← Index không tồn tại
```

`RedirectToAction` tới action không tồn tại ⇒ **`InvalidOperationException` → 500**, không phải 302. Nếu bất kỳ luồng nào gọi `POST /cart/add/{id}` (form MVC cũ, test, hoặc code port từ nguồn), nó sẽ **500 chứ không redirect**. Cần kiểm chứng runtime, nhưng theo code path thì đây là lỗi chắc chắn.

#### 🔴 Hệ quả C: `MapControllerRoute` trỏ tới controller **không tồn tại**

Cả nguồn lẫn đích đều có dòng này, nhưng **nguồn CÓ controller, đích KHÔNG**:

```csharp
// NGUỒN: Projectojt/BackEnd/Program.cs — CÓ ProductsPageController.cs trong Controllers/
app.MapControllerRoute(name: "default", pattern: "{controller=ProductsPage}/{action=Index}/{id?}");

// ĐÍCH: PRN232_PetCareProject/Program.cs — KHÔNG có Controllers/ProductsPageController.cs
app.MapControllerRoute(name: "default", pattern: "mvc/{controller=ProductsPage}/{action=Index}/{id?}");
```

Đã liệt kê đủ 21 file controller ở đích: `Attendance, Auth, Boarding, BreedPricing, Cart, Checkout, Config, EmailTest, Notification, Orders, PayOS, Payroll, Pets, PetServices, PetShopOData, Products, Review, ShiftExchange(×3), SpaBooking`. **Không có `ProductsPageController`.**

Nguồn còn có 3 route MVC mà đích không port: `GET /`, `GET /products` (redirect tới `http://localhost:5173/products` — hardcode!), `GET /products/{id:int}` (trả Razor View).

> **Điểm phản biện cho team kiến trúc:** họ nói "giữ nguyên kiến trúc Blazor Server hiện tại". Nhưng nếu ai đó gọi `/mvc/products` sẽ 404. Và **route `/` của đích là `Home.razor` (Blazor)**, không phải redirect — nên hành vi gốc ("`/` → redirect sang SPA") đã đổi bản chất, không chỉ đổi hình thức.

### 1.3 Route CHỈ CÓ Ở ĐÍCH (đích MỞ RỘNG, không phải thiếu)

Đích có thêm, nguồn không có — **đây là điểm tốt, team kiến trúc nên giữ**:
- `api/auth/token` (`AuthController.cs:165`) + `api/auth/me` (`:197`) — JWT demo slide Ch06
- `api/boarding-rooms/*` (7 route) — `BoardingController.cs`
- `api/payos/status/{orderCode}` (`PayOSController.cs:200`) + `api/payos/send-invoice` (`:272`)
- `api/test/email/{spa,spa/cancelled,boarding}` (`EmailTestController.cs`)
- `PetShop/*` OData — 9 route (`PetShopODataController.cs`)

### 1.4 🟡 Đích THIẾU `api/config/google-client-id` alias lẫn `api/auth/google-client-id`?

**Không thiếu.** Đích có **cả hai**: `AuthController.cs:146` `[HttpGet("google-client-id")]` → `api/auth/google-client-id`, và `ConfigController.cs:16` → `api/config/google-client-id`. Trùng lặp được giữ nguyên — tốt cho parity.

### 1.5 Mức độ & Đề xuất

**Mức độ: 🔴 Cao** — route parity là nền tảng; sai ở đây thì mọi thứ khác vô nghĩa.

**Đề xuất:**

1. **Viết `docs/_inventory/ROUTE_CONTRACT.md`** — bảng 3 cột: `Route nguồn` | `Route đích` | `Trạng thái` (`1-1` | `đổi-tên` | `chỉ-nguồn` | `chỉ-đích` | `CHẾT-CẢ-HAI`). Dùng làm **definition of done** cho mọi PR.
2. **Không migrate `src/api/*.js` nguyên văn.** Thay bằng một lớp adapter duy nhất map URL cũ → URL mới, đặt tên `LegacyApiMap`, hoặc tốt hơn: **port contract chứ không port endpoint** — viết `IProductCatalogClient` (đã có sẵn ở đích) thay vì giữ `productsApi.js`.
3. **Xử lý 10 endpoint "chết cả hai bên"** theo 2 nhóm:
   - *Nhóm 1 — đích đã có chức năng qua SQL thô trong Razor* (`create/update/delete/upload` product, `create/upload` pet-service): **bọc vào `IProductService`/`IPetServiceService`**, expose REST route chuẩn. **Đây là việc cần làm thật.**
   - *Nhóm 2 — chưa có ở đâu* (`deleted/restore/permanent-delete/search` product): chọn 1 trong 3 — (a) implement đầy đủ; (b) bỏ UI tương ứng (`Products.razor:154` `GoDeletedProducts()` đang điều hướng tới `/products/deleted` — link chết); (c) giữ `404 → []` như nguồn đã làm.
4. **Fix `GET /cart`**: xoá 3 action `RedirectToAction(nameof(Index))` của `CartController` đích, hoặc thêm `Index()` trả `Redirect("/cart")`. Đồng thời **xoá `MapControllerRoute` trỏ `ProductsPageController`** hoặc tạo controller đó.
5. **Chốt alias route tương thích**: thêm `[Route("api/products-temp")]` alias cho `ProductsController` để code cũ không gãy — chi phí ~5 dòng, giữ được contract.

---

## 2. PHÁ GIẢ ĐỊNH "UI GIỐNG 100%"

### 2.1 Route nguồn (23 URL unique) → đích: bảng đối chiếu

Nguồn: `Projectojt/frontend/src/App.jsx` — 35 route entries, 23 URL unique.
Đích: grep `@page` trên toàn bộ `Components/**/*.razor` — **35 route** (có route 2-route-trong-1-file).

| Route nguồn | Đích | Phán quyết |
|---|---|---|
| `/` | `Home.razor` | ✅ 1-1 |
| `/products` | `Products.razor` | ✅ |
| `/search` | `Search.razor` | ⚠️ **có, nhưng guard khác** |
| `/products/:id` | `ProductDetail.razor` (`/products/{ProductId:int}`) | ✅ |
| `/cart` | `Cart.razor` | ✅ |
| `/cart/checkout` | `Checkout.razor` | ✅ |
| `/checkout/cod-success` | `CodConfirmation.razor` | ✅ |
| `/account` | `Account.razor` | ✅ |
| `/profile` | ❌ **KHÔNG** | 🔴 **THIẾU** |
| `/profile/edit` | `Account.razor` (route 2) | ✅ |
| `/account/pets` | `PetInfo.razor` | ✅ |
| `/account/orders` | `Orders.razor` | ✅ |
| `/account/orders/:orderId` | `Orders.razor` (route 2) | ✅ |
| `/login` | `Login.razor` | ✅ |
| `/register` | `Register.razor` | ✅ |
| `/forgot-password` | `ForgotPassword.razor` | ✅ |
| `/orders` | `StaffOrders.razor` (`/system/orders`) | 🟠 **KHÁC ROUTE** |
| `/system` | `StaffDashboard.razor` | 🟠 **KHÁC CẤU TRÚC** |
| `/services` | `Services.razor` | ✅ |
| `/services/:id` | `ServiceDetail.razor` | ✅ |
| `/spa-cart` | `SpaCart.razor` | ✅ |
| `/spa-booking-history` | `SpaBookingHistory.razor` | ✅ |
| `/spa-booking-history/:historyId` | `SpaBookingHistoryDetail.razor` | ✅ |
| `/spa-invoice` | `SpaInvoice.razor` | ✅ |
| `/products/create` | `ProductCreate.razor` | ✅ |
| `/about` | ❌ **KHÔNG** | 🔴 **THIẾU** |
| `*` (fallback → `/`) | `NotFoundPage.razor` (không `@page`) | 🟠 **KHÁC** |

### 2.2 🔴 `/about` THIẾU Ở ĐÍCH — VÀ ĐANG CÓ LINK CHẾT

Bằng chứng trực tiếp:

```razor
<!-- PRN232_PetCareProject/Components/Shared/Footer.razor:27 -->
<li><a href="/about">Giới thiệu</a></li>
```

Grep toàn bộ `Components/**/*.razor` cho `@page "..."` ⇒ **không có route `/about`**. ⇒ Bấm "Giới thiệu" ở footer của **mọi trang** (23 page dùng `Footer.razor` theo inventory nguồn) ⇒ **404**.

Tệ hơn: nguồn có `pages/AboutPage.jsx` — nội dung tĩnh đầy đủ (hero, 3 giá trị cốt lõi, dịch vụ nổi bật, cam kết chất lượng, quy trình 4 bước, FAQ, CTA). Đây là **page duy nhất trong nguồn không cần API** ⇒ là ứng viên số 1 để port nhanh, và **không có lý do kỹ thuật nào để bỏ**.

> **Phản biện trực diện:** Team kiến trúc nói "bổ sung các page/asset còn thiếu". `/about` là page **còn thiếu** + đang có **link chết**. Đây chính là bằng chứng cho thấy giả định "chỉ thiếu Chatbot" sai — vì ngay cả các page tĩnh cơ bản cũng chưa được đối chiếu.

### 2.3 🔴 `/profile` THIẾU — nguồn có 2 route trỏ cùng page

Nguồn khai báo **cả** `/account` và `/profile` → cùng `ProfilePage.jsx`. Đích chỉ có `/account`. Điều này không chỉ là khác route — nó ảnh hưởng **link cũ**: bất kỳ bookmark/QR/URL nào dùng `/profile` sẽ 404.

Đáng chú ý: `PRN232_PLAN.md` §P0.1 ghi:
> "Xoá `Components/Pages/RouteRedirects.razor` | Redirect `/orders`→`/system` & `/profile`→`/profile/edit`"

⇒ Tức **đích đã CỐ TÌNH xoá redirect `/profile`**. Hành vi nguồn (`/profile` → hồ sơ) giờ biến thành 404 thay vì `/profile/edit`. Đây là **quyết định đã được lấy nhưng không được ghi nhận là hành vi chấp nhận được** — cần chốt lại.

### 2.4 🟠 `/orders` (nguồn, trang staff) → đích phải là `/system/orders`

Nguồn: `pages/OrdersStaffPage.jsx` ở `/orders` — TopBar + Footer + `<OrdersStaffPanel layout="page" />`.
Đích: `StaffOrders.razor` ở `/system/orders` — nằm trong `StaffWorkspaceLayout.razor` (sidebar 5 mục + `StaffWorkspaceLayout.razor:36-37`).

**Khác biệt hành vi:** nguồn `/orders` là **trang độc lập** (TopBar + Footer, không có sidebar staff). Đích `/system/orders` **nằm trong workspace layout** (có sidebar). Nếu giữ "UI giống 100%" ⇒ phải port cả layout của trang staff.

### 2.5 🟠 `/system` — nguồn 5 tab, đích layout khác

Nguồn `SystemPage.jsx`: sidebar 5 tab (`Dashboard / Chấm công / Lịch làm việc / Dịch vụ / Đơn hàng`) — **mọi tab nằm trong 1 trang**, có 2 bar chart 8 tuần + 2 donut, có **tab `breed-pricing` trong code nhưng KHÔNG có trong `MENU_ITEMS`** (dead branch theo inventory nguồn).

Đích: `StaffDashboard.razor` `/system` + **4 page tách riêng**: `/system/bookings`, `/system/orders`, `/system/profile`, `/system/schedule` + `/revenue`.

⇒ **Đích ĐÃ TÁCH page, không phải 1-1.** Đây là **cải tiến**, không phải gap. Nhưng nó phá vỡ "UI giống 100%": nguồn giữ state tab trong `sessionStorage["systemPageTab"]`, đích dùng URL. **Đề xuất: giữ cách đích, ghi rõ vào tài liệu parity là "cải tiến có chủ đích", không phải thiếu.**

### 2.6 🟠 Guard React vs Guard Blazor — hành vi KHÁC

| | Nguồn (React) | Đích (Blazor) |
|---|---|---|
| Cơ chế | `ProtectedCustomerRoute` / `AuthRequiredRoute` inline trong `App.jsx` | `AuthGuard.razor` / `CustomerOnlyGuard.razor` |
| Nguồn sự thật | `localStorage["authUser"]` | `ProtectedLocalStorage` (`Services/UserSessionService.cs`) |
| Redirect staff | `<Navigate to="/system" replace />` | `Navigation.NavigateTo("/system", forceLoad: true)` |
| Thời điểm chạy | Render-time (đồng bộ) | `OnAfterRenderAsync(firstRender)` — **sau render đầu** |

🔴 **Phát hiện sắc:** `AuthGuard.razor` và `CustomerOnlyGuard.razor` **gần như trùng nhau về code**:

```csharp
// AuthGuard.razor và CustomerOnlyGuard.razor — LOGIC GIỐNG HỆT NHAU
if (!UserSession.IsAuthenticated) { NavigateTo("/login", forceLoad: true); return; }
var role = UserSession.CurrentUser?.Role?.ToLowerInvariant() ?? "";
if (role == "staff") { NavigateTo("/system", forceLoad: true); return; }
```

**Cả hai đều chỉ chặn `role == "staff"`.** Nguồn cũng vậy (`AuthRequiredRoute` vs `ProtectedCustomerRoute` — cùng logic). Nhưng điều đáng nói: nguồn có **role `doctor`** (xem `TopBar.jsx` chào hợp role `admin`/`staff`/`doctor`/`user`, và `ServicePage.jsx` nút "Thêm dịch vụ" cho doctor, `HomePage.jsx` ẩn `FeaturedCategories` nếu role doctor). **Đích guard không xử lý `doctor`** ⇒ doctor ở đích sẽ đi vào `/system` (staff workspace) thay vì được chặn, hoặc thấy UI staff.

**Thêm nữa:** cả 2 guard đều dùng `OnAfterRenderAsync` ⇒ **render trắng 1 nhịp** trước khi redirect, và **trong pha prerender `ProtectedLocalStorage` không khả dụng** ⇒ `catch { }` nuốt lỗi ⇒ `IsAuthenticated == false` ⇒ **redirect `/login` ngay cả khi user ĐÃ đăng nhập**. Với `forceLoad: true` điều này = **vòng lặp redirect login↔home** trong điều kiện race. Đây là rủi ro vận hành thật.

### 2.7 🟡 Asset: dùng chung vs riêng — và **2 file đích thiếu**

| | Nguồn | Đích |
|---|---|---|
| Ảnh `src/images/` ↔ `wwwroot/images/` | 144 file | **148 file** |
| `public/` | 2 file (`logo-pet-toy.png`, `ziggy-hero-reference.png`) | ❌ **KHÔNG THẤY trong `wwwroot/images`** |
| Video | `video.mp4`, `video.MOV` | ✅ có cả 2 |

Liệt kê đủ 148 tên file ở đích: có `toy_1..toy_35` + `toy_156..toy_216` (đủ 96 ảnh đồ chơi), `room-cat-normal1-3`, `room-cat-vip1-3`, `room-dog-large1-3`, `room-dog-small1-3`, `petnews1-6`, `slide_1-4`, `meovat1-6`, `member1/3`, `bath1-3`, `cut1-3`, `spa-caocap1-3`, `teeth1-3`, `placeholder.jpg`, `gt.jpg/gt.png`, `nember_9.png`, `video.mp4/MOV`.

**⇒ Ảnh nguồn gần như **đã** được port đầy đủ** (148 > 144 vì đích có thêm 4 file ảnh mới). Điều này **phủ định** giả định "thiếu asset" trong `TARGET_EXISTING.md` §8.2 — phần "cần đối chiếu thiếu ảnh" là **lo lắng không có cơ sở**.

**NHƯNG:** `public/logo-pet-toy.png` và `public/ziggy-hero-reference.png` **KHÔNG có** ở `wwwroot/images/`. ⇒ 2 file này phải nằm ở `wwwroot/` gốc. Kiểm tra:

```
PRN232_PetCareProject/wwwroot/images/  → không có logo-pet-toy.png
```

⇒ **Thiếu logo thương hiệu `logo-pet-toy.png`.** Đây là asset xuất hiện ở `TopBar/Header/Footer/PageContextBar/Login/Register/ForgotPassword` của nguồn (8 vị trí). Nếu đích dùng logo khác thì OK — nhưng phải **xác nhận bằng mắt**, không phải giả định.

### 2.8 🟠 CSS: 14 file nguồn vs 17 file đích — nhưng **2 file đích là CSS "react"**

Đích `wwwroot/css/`:
- `template/` — 17 file: `App, auth, cart, cartDrawer, checkout, checkoutResult, codConfirmation, home, index, invoice, pageContextBar, productDetails, profile, searchProducts, spa`
- `spa/` — 2 file: **`cart-react.css`, `service-react.css`**

Nguồn `src/styles/` — 14 file: `home, service, systemPage, profile, checkout, productDetails, cart, ordersStaff, codConfirmation, searchProducts, cartDrawer, auth, pageContextBar, createProduct`.

**Bảng đối chiếu cho thấy dịch vụ/chuyển đổi KHÔNG 1-1:**

| File nguồn | File đích | Ghi chú |
|---|---|---|
| `home.css` (2.788 dòng) | `template/home.css` | ✅ |
| `service.css` (3.681 dòng) | `template/spa.css` + `spa/service-react.css` | 🟠 **TÁCH, cần verify phủ** |
| `systemPage.css` (1.426 dòng) | ❓ | 🟠 **KHÔNG thấy file tương đương** |
| `ordersStaff.css` (312) | ❓ | 🟠 |
| `createProduct.css` (158) | ❓ | 🟠 |
| `index.css` (66, Vite default) | `template/index.css` | ✅ |
| — | `template/checkoutResult.css`, `template/invoice.css` | ➕ đích có thêm |

⚠️ `systemPage.css` (1.426 dòng) + `ordersStaff.css` (312) + `createProduct.css` (158) = **1.896 dòng CSS nguồn không có file đích tương đương rõ ràng**. Đích có `StaffWorkspaceLayout.razor` — nhưng **không có `.css` riêng cho nó** trong `wwwroot/css/`. ⇒ Hoặc style đã inline/viết lại, hoặc UI staff đang **thiếu style**.

### 2.9 🟡 Cart: localStorage vs Session server vs localStorage-blazor

Ba mô hình **KHÁC NHAU** trong 3 hệ thống:

| | Nguồn React | Nguồn Backend MVC | Đích Blazor |
|---|---|---|---|
| Nơi lưu | `localStorage["cart"]` | ASP.NET **Session** (`SessionKey="cart"`, 8h) | `localStorage` qua `cartStorage.read` JS |
| Gắn user | ❌ không | ❌ không | ❌ không |
| Nguồn sự thật giá | giá snapshot client | giá snapshot server | giá từ `ProductDto` |

**Bằng chứng đích:** `Services/CartStateService.cs` — `KeyCart = "cart"`, gọi JS `cartStorage.read`/`cartStorage.write`. Đọc `localStorage`.

⇒ **Hành vi thực tế khớp với nguồn React** (cùng localStorage, cùng key `cart`, cùng không gắn user). Nhưng nó **khác** backend MVC nguồn (Session). Và có 1 hệ quả:

🔴 **`CartController` (MVC, session) và `CartStateService` (Blazor, localStorage) là HAI nguồn giỏ hàng độc lập trong cùng 1 app.** Người dùng thêm hàng qua Blazor ⇒ giỏ localStorage có hàng. Nhưng nếu bất kỳ code nào gọi `POST /cart/add/{id}` (form MVC) ⇒ ghi vào Session ⇒ **giỏ hiển thị trên UI không đổi**. Hai giỏ lệch nhau, không đồng bộ, không báo lỗi.

### 2.10 Mức độ & Đề xuất

**Mức độ: 🟠 Cao** (không phải 🔴 vì phần lớn route đã có; nhưng link chết + guard sai là lỗi người dùng thấy ngay)

**Đề xuất:**

1. **Port `/about`** — 1 page tĩnh, không cần API, nguồn có sẵn nội dung đầy đủ. ~1 giờ. **Sửa link chết footer ngay lập tức.**
2. **Quyết định rõ `/profile`**: hoặc thêm alias route `@page "/profile"` trong `Account.razor`, hoặc ghi vào tài liệu parity "cố ý bỏ". Đừng để 404 im lặng.
3. **Thêm bảng "URL cũ → URL mới"** và cài middleware redirect 301/302 cho `/orders`→`/system/orders`, `/profile`→`/account`. Rẻ hơn nhiều so với sửa từng chỗ link.
4. **Chuẩn hoá guard**: gộp `AuthGuard` + `CustomerOnlyGuard` (logic trùng 100%) thành 1 component có tham số `AllowedRoles`. Xử lý role `doctor` — hoặc chủ động hỗ trợ, hoặc chủ động chặn.
5. **Fix race prerender của guard**: dùng cơ chế `PersistentComponentState` hoặc render skeleton cho tới khi `LoadAsync()` hoàn tất thay vì coi lỗi `InvalidOperationException` là "chưa đăng nhập".
6. **Gộp 2 nguồn giỏ hàng**: chọn 1. Nếu chọn `CartStateService` (localStorage) thì **xoá `CartController` MVC** hoặc chuyển nó sang đọc/ghi cùng `cartStorage`.
7. **Verify CSS staff**: đối chiếu `systemPage.css` + `ordersStaff.css` nguồn với style thực tế của `StaffWorkspaceLayout.razor`/`StaffOrders.razor` — nếu thiếu thì đó là **UI staff xanh xao**, ảnh hưởng điểm giáo viên chấm.
8. **Bỏ giả định "thiếu asset"** — đã kiểm chứng 148/144 file có mặt. Chỉ còn 2 file `public/` cần xác nhận bằng mắt.

---

## 3. PHÁ GIẢ ĐỊNH "KHÔNG CẦN ĐỤNG DB"

### 3.1 Kết luận ngắn: **Đúng một nửa — và đó là nửa nguy hiểm**

**Đúng:** Schema DB của đích **đủ dùng**. Không cần tạo bảng mới.

**Sai & nguy hiểm:** "Không cần đụng DB" bị team kiến trúc hiểu thành **"không cần viết lại tầng Repository/Service"** — trong khi đích có **10 Razor component đang gọi thẳng DB bằng SQL thô**, vi phạm đúng yêu cầu slide mà team tuyên bố đã tuân thủ.

### 3.2 🔴 BẰNG CHỨNG: 10 Razor component gọi thẳng `ShopPetDatabaseContext`

Grep `ShopPetDatabaseContext` trên toàn bộ `Components/**`:

| File | Số hit | Dạng truy cập |
|---|---|---|
| `ProductCreate.razor` | 1 | `ExecuteSqlRawAsync("INSERT INTO Products ...")` |
| `ProductDeleted.razor` | 1 | `@inject ShopPetDatabaseContext _context` |
| `ProductEdit.razor` | 2 | raw SQL UPDATE |
| `ProductInventory.razor` | 2 | raw SQL UPDATE |
| `ProductList.razor` | 2 | raw SQL UPDATE |
| `Revenue.razor` | 1 | truy vấn báo cáo |
| `StaffBookings.razor` | 2 | truy vấn booking |
| `StaffDashboard.razor` | 1 | truy vấn KPI |
| `StaffOrders.razor` | 2 | truy vấn đơn hàng |
| `StaffWorkspaceLayout.razor` | 1 | truy vấn layout |

**Bằng chứng điển hình — `ProductCreate.razor:95-105`:**

```csharp
using var scope = ServiceProvider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ShopPetDatabaseContext>();
...
// SQL THÔ LÀ CHỈN HƠN: đẩy thẳng vào DB, EF Core không đủ khả năng bắt lỗi Ngữ Thứng
string sql = "INSERT INTO Products (name, price, stock_quantity, description, image_url, is_deleted) VALUES (@p0, @p1, @p2, @p3, @p4, 0)";
await db.Database.ExecuteSqlRawAsync(sql, newProduct.Name, newProduct.Price, newProduct.StockQuantity, newProduct.Description ?? "", newProduct.ImageUrl);
```

**Điều này vi phạm trực tiếp:**
- **Slide Ch02 / `PRN232_PLAN.md` P0.3**: *"Razor truy cập DB trực tiếp → vi phạm kiến trúc 3 tầng của slide Ch02"*. Chính plan này đã **phát hiện 1 file** (`ProductDeleted.razor`) và yêu cầu xoá — nhưng **9 file còn lại bị bỏ sót**. Đó chính là "dead code" nguy hiểm nhất: nó **không chết**, nó **chạy**.
- **`AGENTS.md` quy tắc 5**: EF read-only → `AsNoTracking()`, projection thay vì load entity — bị vi phạm ở các truy vấn raw.
- **AGENTS.md quy tắc 6**: *"SQL thô chỉ dùng `ExecuteSqlRawAsync` với tham số `@p0`; `FromSqlRaw` bị cấm"* — các file này đúng là dùng `ExecuteSqlRawAsync` nên **lỡ slide** cho quy tắc SQL, nhưng **vi phạm quy tắc kiến trúc** (tầng).
- **AGENTS.md quy tắc 8**: *"Business logic trùng lặp → gom về `Services/` dùng chung; không copy-paste logic"*.

**Rủi ro cụ thể:** raw SQL `INSERT INTO Products (name, price, stock_quantity, description, image_url, is_deleted)` — **không** set `category_id`, **không** set `supplier_id`, **không** set `created_at`. Nếu 2 cột đó `NOT NULL` trong DB ⇒ **lệnh INSERT lỗi runtime**. Nếu nullable ⇒ tạo sản phẩm mồ côi không thuộc danh mục nào ⇒ `/api/products?categoryId=` lọc sai, OData `$filter` sai. **Đây là lỗi tiềm ẩn chưa ai test.**

### 3.3 🟡 Nguồn dùng bảng nào? Đối chiếu 39 DbSet đích

Grep `_db.<Table>` trong `Projectojt/BackEnd/Services/` + `Repositories/` (nguồn thực sự đọc/ghi):

```
Admins, AttendanceRecords, BoardingRooms, Bookings, BookingServices, BreedPricings,
Customers, Doctors, OrderDetails, Orders, Payments, PayrollRecords, Pets,
PetServices, ProductCategories, Products, Reviews, Species, Staff, StaffSalaries,
VServiceReviews, WorkSchedules
```

**Đối chiếu 39 DbSet của đích** — **tất cả bảng nguồn dùng đều có ở đích.** ✅

Bảng đích có **thêm** (nguồn không dùng): `BoardingBooking`, `ChatMessage`, `MedicalRecord`, `Shift`, `ShiftRequest`, `Supplier`, `SystemSetting`, `Breed` + 8 view `v_*`/`vw_*`.

⇒ **Kết luận Mục 3: KHÔNG có bảng nào thiếu.** Giả định "không cần đụng DB" **đúng về schema**.

### 3.4 🔴 NHƯNG: `ChatMessages` có bảng mà **không có cột** đủ để làm chatbot thật

Bảng `ChatMessages` có (từ `SHOP_PET_Database.sql:653`):

```sql
CREATE TABLE [dbo].[ChatMessages](
	[MessageID] [int] IDENTITY(1,1) NOT NULL,
	[CustomerID] [int] NOT NULL,
	[StaffID] [int] NULL,
	[SenderType] [nvarchar](10) NOT NULL,
	[Message] [nvarchar](max) NOT NULL,
	[SentAt] [datetime] NULL,
	[IsRead] [bit] NULL,
	...
)
```

**Vấn đề 1 — `SenderType` chỉ 10 ký tự.** Chatbot cần phân biệt `user` / `assistant` / `staff` / `system` → `assistant` = 9 ký tự ✅ vừa. Nhưng nếu thêm `tool` / `ai_provider` ⇒ **vượt 10 ký tự ⇒ lỗi insert**. Rủi ro schema thật.

**Vấn đề 2 — KHÔNG có cột `Intent`, `Source`, `SessionId`, `ConversationId`.** Nguồn `ChatMessageResponseDto` có `{ Reply, Source, Intent, Data, Timestamp }`. Nếu muốn lưu lịch sử chatbot **kèm intent** (để phân tích), cần **thêm cột** ⇒ **đụng DB** ⇒ **phá vỡ giả định "không cần đụng DB"**.

**Vấn đề 3 — `StaffID` FK tới `Admin`.** Xem `Models/ChatMessage.cs` (giống hệt nguồn):
```csharp
public virtual Customer Customer { get; set; } = null!;
public virtual Admin? Staff { get; set; }
```
⇒ Chat với **Staff** (`Staff` table) sẽ vi phạm FK nếu gán `StaffId` của `Staff` vào cột FK trỏ `Admin`. Lưu ý: bảng `Staff` và bảng `Admin` là **hai bảng khác nhau** (`Admin.AdminId` vs `Staff.StaffId`).

**Vấn đề 4 — quan trọng nhất: nguồn chatbot **KHÔNG ghi DB**.** `SOURCE_COMPLEX_FLOWS.md` §1.7 kết luận: *"ChatbotService không bao gồi `_db.ChatMessages.Add(...)`"*. Lịch sử do **client** gửi lên qua `request.History`.

⇒ **Nghĩa là: nếu đích port chatbot đúng nghĩa bản gốc thì cũng KHÔNG cần DB.** Nhưng nếu team kiến trúc muốn chatbot **"tốt hơn bản gốc"** (lưu lịch sử server-side, multi-session, phân tích intent) ⇒ **phải thêm cột** ⇒ **đụng DB** ⇒ **trái giả định**.

**Vấn đề 5 — `Customer` entity đích KHÔNG có `DateOfBirth`/`AvatarUrl`.** Nguồn `ProfilePage.jsx` hiển thị **ngày sinh** và **avatar**; nguồn lưu chúng ở `localStorage["userProfile:{key}"]` (overlay client-side) vì *"backend OJT chưa hỗ trợ upload avatar"*.

**Kiểm tra entity `Customer` đích** (`Models/Customer.cs`): `CustomerId, Name, Phone, Email, Password, GoogleId, AddressCustomer, Status, OtpCode, OtpExpiry, ResetToken, ResetTokenExpiry, Role` + 4 navigation. **KHÔNG có `BirthDate`, `KHÔNG có `AvatarUrl`, `KHÔNG có `CreatedAt`.**

⇒ **Nếu giữ "UI giống 100%"** (ProfilePage hiển thị ngày sinh + avatar), đích phải:
- (a) giữ overlay `localStorage` như nguồn (vi phạm "chạy 100% server"), hoặc
- (b) **thêm cột `birth_date`, `avatar_url` vào bảng `Customer`** ⇒ **ĐỤNG DB** ⇒ **phá vỡ giả định "không cần đụng DB"**.

Đây là mâu thuẫn logic mà team kiến trúc **chưa nhận ra**: *"UI giống 100%"* ⇄ *"không đụng DB"* — không thể đồng thời đúng.

### 3.5 🟡 `BreedPricing` — mismatch tên cột & ngữ nghĩa

Entity đích (`Models/BreedPricing.cs`): `BreedPricingId, BreedId, PriceAdjust, Breed`.

- Cột DB trong `SHOP_PET_Database.sql:619` `CREATE TABLE [dbo].[BreedPricing]`.
- DTO đích `BreedPricingDtos.cs:13`: `CreateBreedPricingRequest(int BreedId, decimal PriceAdjustPercent)` với `[Range(-100, 500)]`.
- Nguồn: `[Range(1, 500)]` (chỉ cho phép tăng) — inventory nguồn: *"PriceAdjust phải từ 1% đến 500%"*.

⇒ **Đích cho phép `-100%` (giảm 100% ⇒ giá 0)** còn nguồn **không cho phép âm**. Đây là **lệch hành vi so với bản gốc**, ngược lại với giả định "giữ nguyên". Ngoài ra entity tên property `PriceAdjust` nhưng DTO tên `PriceAdjustPercent` — dễ gây bug khi map.

### 3.6 Mức độ & Đề xuất

**Mức độ: 🔴 Cao** — không phải vì thiếu bảng, mà vì **giả định sai dẫn tới việc bỏ sót 10 file vi phạm kiến trúc** mà slide chấm điểm.

**Đề xuất:**

1. **Viết lại giả định thành câu chính xác:** *"Không cần **TẠO BẢNG MỚI**; nhưng **CẦN** sửa tầng truy cập dữ liệu và có thể cần thêm cột nếu muốn giữ tính năng cũ."*
2. **Ưu tiên #1 của toàn bộ migration: đưa 10 Razor component về đúng 3 tầng.** Tạo `IProductService` (thêm `Create/Update/SoftDelete/Restore/UploadImage`) + `IProductRepository` tương ứng, `IStaffDashboardService` (đã có nhưng Razor vẫn query thẳng), `IOrderService`. Đây là việc **làm đúng slide Ch02** — điểm chấm cao nhất, rủi ro thấp nhất.
3. **Quyết định rõ chatbot có lưu DB không:**
   - *Nếu không lưu* (port nguyên bản gốc) ⇒ **không đụng DB**, an toàn, nhưng chatbot "mù" khi đổi tab.
   - *Nếu lưu* ⇒ **thêm cột** `intent`, `source`, `conversation_id` và **mở rộng `sender_type` từ `nvarchar(10)` → `nvarchar(20)`**. Đây là **thay đổi schema đã khai báo rõ** — vi phạm AGENTS.md quy tắc 9 *"không tự ý đổi schema DB"*, nên **cần chủ đồng ý rõ ràng trước khi làm**.
4. **Đồng bộ `Range` của `PriceAdjustPercent`** giữa nguồn (`1..500`) và đích (`-100..500`).
5. **Sửa `INSERT INTO Products`** — bổ sung `category_id`, `created_at` nếu cột NOT NULL. Kiểm tra `SHOP_PET_Database.sql` cho bảng `Products` trước khi chốt câu SQL.

---

## 4. PHÁ TÍNH KHẢ THI CỦA "VIẾT LẠI NODE.JS BẰNG C#"

### 4.1 Phá luận điểm "nguồn là Node.js"

**Bằng chứng phá luận điểm — nguồn KHÔNG phải Node.js backend:**

```
Projectojt/
├── BackEnd/          ← .NET 8 ASP.NET Core MVC + API Controllers  ← KHÔNG PHẢI NODE
│   ├── Controllers/  (18 file .cs)
│   ├── Services/     (.cs)
│   ├── Repositories/ (.cs)
│   ├── Models/       (.cs)
│   ├── Program.cs    ← ASP.NET Core, KHÔNG phải Express
│   └── PetShop.csproj
├── frontend/         ← React 19 + Vite 7 (ĐÂY mới là "Node" — và chỉ là BUILD TOOL)
└── package.json      ← CHỈ CÓ 1 dependency: react-router-dom
```

`Projectojt/package.json`:
```json
{ "dependencies": { "react-router-dom": "^7.13.1" } }
```

⇒ **Toàn bộ backend nghiệp vụ đã là C#.** "Viết lại Node.js bằng C#" là **sai mô tả**. Node chỉ tồn tại ở tầng **build/frontend**: Vite dev server + React runtime.

### 4.2 Bảng phân loại chính xác: cái gì là Node, cái gì là C#

| Hạng mục | Thực chất | Stack | Cần viết lại? |
|---|---|---|---|
| `BackEnd/Controllers/` (18) | ASP.NET Core API | **C#** | ❌ Không — đích đã có 21 |
| `BackEnd/Services/` | Business logic | **C#** | ❌ Không — đích đã có 32 |
| `BackEnd/Repositories/` | EF Core query | **C#** | ❌ Không — đích đã có 7 |
| `BackEnd/Models/` | Entity + DTO | **C#** | ❌ Không — đích đã có 52 |
| `BackEnd/Program.cs` | DI + middleware | **C#** | ❌ Không |
| `BackEnd/EmailTemplates/*.hbs` | Handlebars template | **Handlebars** | ⚠️ Đích đã có |
| `frontend/src/api/*.js` (10 file, 70 fn) | HTTP wrapper | **JS** | ✅ Bỏ, dùng `*Client.cs` đích |
| `frontend/src/pages/*.jsx` (27) | UI React | **JSX/React** | ✅ Bỏ, dùng `.razor` đích |
| `frontend/src/components/*.jsx` (17) | UI React | **JSX/React** | ✅ Bỏ, dùng `.razor` đích |
| `frontend/src/styles/*.css` (14, 12.551 dòng) | CSS | **CSS** | ✅ Bỏ, dùng `wwwroot/css/template` |
| `frontend/src/images/` (144) | Asset | **File tĩnh** | ✅ Copy, không viết lại |
| `frontend/src/utils/userProfileStore.js` | localStorage helper | **JS** | 🔶 Bỏ (xem 4.4) |
| `frontend/vite.config.js` | Dev server + proxy | **Node/Vite** | ❌ Không cần — đích không có React |
| `frontend/package.json` | Build config | **Node** | ❌ Không cần |

### 4.3 🔴 Những thứ được gọi là "Node" nhưng **thực ra đã là C#**

Đây là phần phản biện quan trọng nhất — **các hạng mục mà team kiến trúc có thể tưởng cần "viết lại" nhưng thực ra đã xong:**

| # | Hạng mục | Bằng chứng | Kết luận |
|---|---|---|---|
| 1 | **Toàn bộ 92 endpoint API** | Nguồn `BackEnd/Controllers/` 18 file .cs | **Đã là C#** — đích có 92 route, đã đổi tên `_temp` |
| 2 | **Toàn bộ business logic** | Nguồn `BackEnd/Services/` | **Đã là C#** — đích có 32 service |
| 3 | **Truy vấn DB** | Nguồn `BackEnd/Repositories/` | **Đã là C#** — đích có 7 repo + `ShopPetDatabaseContext` 39 DbSet |
| 4 | **PayOS + HMAC** | Nguồn `Services/PayOSService.cs` | **Đã là C#** — đích có `IPayOSService` + `PayOSService.Fulfillment.cs` |
| 5 | **Gửi email + Handlebars** | Nguồn `Services/EmailService.cs` + `EmailTemplates/*.hbs` | **Đã là C#** — đích có `IEmailService` (5 loại email) |
| 6 | **OTP / BCrypt / migrate-on-login** | Nguồn `Services/AuthService.cs` | **Đã là C#** — đích có `AuthService` + `AuthService.Google.cs` + **8 unit test** |
| 7 | **OData** | — | Đích đã có `PetShopODataController` (9 route) — **nguồn KHÔNG có** |
| 8 | **JWT** | Nguồn **không có JWT** (chỉ header tự khai) | Đích đã có `JwtTokenService` + **8 unit test** |
| 9 | **Content Negotiation (XML/CSV)** | Nguồn không có | Đích đã có `XmlOutputFormatter` + `CsvOutputFormatter` |
| 10 | **Validation + ProblemDetails** | Nguồn không có | Đích đã có `ApiExceptionMiddleware` + DataAnnotations |
| 11 | **WorkScheduleDedup** | Nguồn `Helpers/WorkScheduleDedup.cs` | **Đã port** — `Services/WorkScheduleDedup.cs` + 7 unit test |
| 12 | **Boarding (đặt phòng)** | Nguồn có `BoardingRooms` trong model nhưng **không có controller** | Đích **có thêm** `BoardingController` (7 route) |
| 13 | **Swagger** | Nguồn có `AddSwaggerGen()` | Đích có + `FormFileOperationFilter` |

⇒ **Kết luận:** 13 hạng mục lớn — trong đó có OData, JWT, Content Negotiation, Validation, Boarding — **đã là C# và đã xong ở đích**. Chiến lược "viết lại Node.js bằng C#" là **mô tả sai** và có thể dẫn đội đến **viết lại những thứ đã có**, làm lãng phí và tạo 2 nguồn sự thật.

### 4.4 🟡 Những thứ THỰC SỰ cần "viết lại" (đây mới là scope thật)

| # | Hạng mục | Nguồn | Đích | Cần làm |
|---|---|---|---|---|
| 1 | **Chatbot AI stack** | 8 file .cs (controller + 2 provider + options + DTO) | ❌ **KHÔNG CÓ** | 🔴 **Viết mới** — đây là phần viết C# thật sự |
| 2 | **10 endpoint product/pet-service chết** | `src/api/*.js` gọi, backend không có | ❌ không có | 🔴 Chọn: bọc lại chức năng đích có, hoặc bỏ UI |
| 3 | **`/about` page** | `AboutPage.jsx` | ❌ không có | 🟠 Port |
| 4 | **`NotificationBell.razor`** | `components/staff/NotificationBell.jsx` | ❓ không thấy | 🟠 Cần kiểm |
| 5 | **`AddressModal` + `daNangData`** | `components/checkout/` | ✅ `Checkout.razor.cs` đã có 8 quận + 100+ phường inline | ✅ Đã có |
| 6 | **`ClearSearchButton`** | `components/products/ClearSearchButton.jsx` | ❓ | 🟡 Kiểm `Search.razor` |
| 7 | **`userProfileStore.js`** | `src/utils/` — overlay `localStorage` | `UserSessionService` chỉ lưu `AuthResponseDto` | 🟠 Xem 4.5 |
| 8 | **`getSpaServiceThumb` (3 bản copy)** | 3 file .jsx | ❓ | 🟡 Nếu port `.razor` thì đừng copy 3 lần — 1 helper |

### 4.5 🟡 Phân tích chi tiết `userProfileStore.js` — bản chất đã là "Client-side state", đích cần quyết định

Nguồn `src/utils/userProfileStore.js` (4 hàm): lưu profile overlay vào `localStorage["userProfile:{email|userId}"]`, với:
- prefix key theo user → mỗi user một namespace
- ghi **cả camelCase lẫn PascalCase** để tương thích .NET
- **avatar lưu dạng data URL** (JPEG q=0.92, canvas 320×320 crop tròn)
- `saveUserProfile` **không** ghi đè `authUser`; đồng bộ thủ công bởi `ProfilePage`/`ProfileEditPage`
- `hydrateAuthUserFromProfile` trộn 6 field vào `authUser`

**Điều này có nghĩa:**
- Đích `UserSessionService` (ProtectedLocalStorage `authUser`) **không** có `userProfile:*` overlay ⇒ **mất avatar + ngày sinh** khi đổi user trong cùng browser.
- Nguồn cố ý ghi **PascalCase** vì "tương thích backend .NET" — nhưng backend .NET **không** đọc chỗ đó (chỉ có `AuthResponseDto`). ⇒ **overlay là thuần client-side, không phải hợp đồng với server.**
- ⇒ Đây là **tính năng chưa hoàn thiện ở nguồn**, không phải thứ cần port 1-1.

### 4.6 Mức độ & Đề xuất

**Mức độ: 🟠 Cao** — vì "viết lại Node.js bằng C#" là framing sai ⇒ **sai phân bổ công sức**.

**Đề xuất:**

1. **Sửa framing trong tài liệu kiến trúc** từ *"viết lại Node.js bằng C#"* ⇒ **"port 4 lớp UI của React sang Razor, giữ nguyên 100% backend C# đã có"**. Đây là framing **chính xác** và giảm 90% ước lượng công sức.
2. **Chốt danh sách "KHÔNG viết lại"** (13 hạng mục ở 4.3) — dán vào `PRN232_PLAN.md` làm mục "Tái sử dụng được" (giống `TARGET_EXISTING.md` §7.1 nhưng chính xác hơn).
3. **Scope thật = 4 mục**: (a) chatbot AI; (b) 10 endpoint chết; (c) `/about`; (d) NotificationBell + các component nhỏ.
4. **Với 10 endpoint chết — ưu tiên bọc lại chức năng đã có ở đích** (qua `ProductCreate.razor`/`ProductEdit.razor` hiện đang dùng raw SQL) thay vì viết API mới từ đầu. Tiết kiệm, và **sửa luôn vi phạm 3-layer**.

---

## 5. PHÁ MÂU THUẪN "GIỐNG 100%" vs "TUÂN THỦ SLIDE"

### 5.1 Bảng mâu thuẫn (5 cặp, có bằng chứng)

| # | Slide yêu cầu | Bản gốc thực tế | Đích hiện tại | Mâu thuẫn | Giải pháp |
|---|---|---|---|---|---|
| **M1** | **Ch06: JWT Bearer xác thực** | ❌ Nguồn **KHÔNG CÓ** JWT. `Program.cs` nguồn **không** có `AddAuthentication()`/`UseAuthentication()` — chỉ `UseAuthorization()`. 0 attribute `[Authorize]`. Quyền = header tự khai `X-User-Role`/`X-User-Id` | ✅ Đích có `JwtTokenService` + `AddPolicyScheme` dual-mode + `POST /api/auth/token` + `GET /api/auth/me` `[Authorize]` | **"Giống 100%"** ⇒ bỏ JWT (đích đã làm) vs **"tuân thủ slide"** ⇒ giữ JWT (đích đã làm) | **Không có mâu thuẫn thực** — đích đã chọn đúng. Ghi rõ: *"bản gốc không có JWT; đích bổ sung theo slide, đây là **cải tiến có chủ đích**"*. **NHƯNG**: `PRN232_PLAN.md` P4.6 liệt kê 5 controller cần `[Authorize]` — **chưa làm** (trừ `api/auth/me`). Nếu giáo viên check, mất điểm. |
| **M2** | **Ch02: Repository / Service / DI 3-layer** | ❌ Bản gốc **CÓ** vi phạm: `ProductsPageController.cs` (MVC) query thẳng `_db.Products`; `NotificationController` dùng `_db` trực tiếp; `PayOSController` insert `Payments` trực tiếp | 🔴 **ĐÍCH CŨNG VI PHẠM — 10 Razor component** gọi `ShopPetDatabaseContext` + `ExecuteSqlRawAsync` | **"Tuân thủ slide"** ⇒ phải sửa 10 file này. Team kiến trúc nói "giữ nguyên kiến trúc hiện tại" ⇒ **giữ luôn vi phạm** | 🔴 **Sửa ngay.** Đây là **vi phạm rõ ràng nhất so với slide** và là **cơ hội điểm cao nhất**: 10 file, pattern lặp, `ProductCreate.razor` đã có sẵn code để bọc. |
| **M3** | **Ch02: DTO thay vì entity** | ⚠️ Bản gốc **đã chuẩn hoá khá tốt**: 92 endpoint đều trả DTO (`AuthResponseDto`, `PetManageDto`, `SpaBookingDto`...). Nhưng `PayrollController.staff-list` trả `IReadOnlyList<object>` (anonymous type) — hệ số bất định | ✅ Đích đã có 22 DTO/record + `ProductCatalogResult<T>` + `FrontendApiResult<T>` | Nhỏ | `api/payroll/staff-list` — định nghĩa DTO rõ ràng (`StaffListItemDto`). Rẻ, làm đúng slide. |
| **M4** | **Ch03: Content Negotiation** | ❌ Bản gốc **không có** (`AddControllersWithViews()` trần, chỉ JSON) | ✅ Đích có `CsvOutputFormatter` + `XmlOutputFormatter` + `AddPetShopXmlFormatters()` | Không | ✅ Đã đúng slide. **NHƯNG**: `Accept: text/csv` chỉ hoạt động cho `IReadOnlyList<T>` — endpoint trả `ActionResult` đơn lẻ sẽ **406**. Nên ghi rõ giới hạn. |
| **M5** | **Ch05: Validation** | ⚠️ Bản gốc validate **thủ công trong service** (`ArgumentException` → 400). Nguồn **không có** `[ApiController]` DataAnnotations | ✅ Đích đã có DataAnnotations + `ApiExceptionMiddleware` | Nhỏ | ✅ Đã đúng slide. Lưu ý `PRN232_PLAN.md` P1.1 nói *"0 attribute validation trong toàn bộ Models/"* — nhưng `BreedPricingDtos.cs` **đã có** `[Range]` ⇒ **plan lỗi thời**, đừng làm lại. |
| **M6** | **Ch09: gRPC** (P5) | ❌ Bản gốc không có | ❌ Đích chưa có (`PRN232_PLAN.md` P5 ⬜) | — | ⚠️ **Đây là phase slide CHƯA LÀM, không phải migration.** Nếu giáo viên chấm theo slide ⇒ **P5/P6/P7 quan trọng hơn chatbot AI**. |

### 5.2 🔴 Mâu thuẫn sâu hơn: **"Giống 100%" có nghĩa là copy cả BUG**

Đây là điểm mà framing "giống 100%" nguy hiểm nhất. Bản gốc có **bug đã được inventory ghi rõ** — nếu port 1-1 thì port luôn bug:

| Bug bản gốc | Vị trí nguồn | Port 1-1 ⇒ | Đề xuất |
|---|---|---|---|
| `Estimate` **không bắt exception** ⇒ 500 thay vì 400 | `SpaBookingController.cs` | Giữ bug 500 | Sửa (đích đã có `ApiExceptionMiddleware` — kiểm tra) |
| **`"Chưa thanh toán"` KHÔNG chiếm slot** ⇒ double-book | `SpaBookingRepository.IsOccupyingSlotStatus` | Giữ double-book | **Sửa** — đây là bug nghiệp vụ thật, giáo viên có thể test |
| Status booking là **string tự do**, client tự `PATCH` sau PayOS redirect | `SpaInvoicePage.jsx:75` tự gọi `PATCH /api/spa-booking/{id}/status` | Client có thể set `"Đã thanh toán"` **trước** webhook | Chuyển enum + webhook là nguồn duy nhất |
| **Client tự đặt giá**: `GET /api/pets/.../pricing?basePrice=` cho phép override giá | `PetsController.cs:118` | Giữ lỗ hổng đặt giá | Bỏ `basePrice` khỏi query |
| **`POST /api/checkout-temp` tính tổng từ `UnitPrice` client gửi**, không đối chiếu DB | `CheckoutController_temp.cs` | Giữ lỗ hổng giá | Tính giá server-side |
| OTP **5 phút** nhưng email nói **10 phút** | `AuthService.cs` vs `EmailService.cs` | Giữ sai lệch | Sửa thành 10 phút cả 2 |
| OTP sinh bằng `System.Random` (không CSPRNG) | `AuthService.GenerateOtp` | Giữ yếu | `RandomNumberGenerator` |
| `ChatbotOptions.DefaultProvider = "groq"` — không có provider Groq | `ChatbotOptions.cs` | Giữ config sai | Sửa |
| `ChatbotService` ném `"Ollama is not available"` khi provider là Gemini | `ChatbotService.cs` | Giữ message sai | Sửa |
| `Payment` insert **trước** khi tạo link PayOS ⇒ record orphan `pending` | `PayOSController.cs` | Giữ orphan | Tạo link trước, hoặc rollback |
| `DELETE /api/notifications/{id}` là **stub trả 200** | `NotificationController.cs:109` | Giữ stub | Hoặc xoá route, hoặc trả 405 |
| `GET /api/payroll/generate-all` **có side-effect** (sinh lương) | `PayrollController.cs` | Giữ GET ghi DB | Đổi sang POST (deviation có chủ đích) |

### 5.3 🔴 Mâu thuẫn cứng: **"Giống 100%" + "Tuân thủ slide Ch02 3-layer" không thể cùng đúng**

Bằng chứng: nguồn **cố ý** để `ProductsPageController` query thẳng DB (MVC view cần `Product` + `Category` + `Supplier`). Đích bỏ controller đó, nhưng lại tạo **10 Razor component** query thẳng DB — tức **vi phạm y hệt bản gốc, ở tầng mới**.

⇒ **Không thể vừa "giữ nguyên hành vi gốc" vừa "tuân thủ slide 3-layer".** Phải **chọn**, và **slide thắng** (vì đó là môn học, và vì đích đã chứng minh có thể).

### 5.4 Mức độ & Đề xuất

**Mức độ: 🔴 Cao** — vì đây là nơi **điểm giáo viên chấm** quyết định.

**Đề xuất:**

1. **Thêm mục "Quyết định chênh lệch" vào `PRN232_PLAN.md`** — bảng 3 cột: `Hạng mục` | `Bản gốc` | `Đích` | `Lý do` | `Căn cứ slide`. Mỗi dòng đều có căn cứ ⇒ **đây là tài liệu chứng minh "tuân thủ slide", không phải tài liệu chứng minh "giống bản gốc"**.
2. **Ưu tiên tuyệt đối M2** (10 Razor file → 3 tầng). Đây là **1 lần sửa, điểm lớn, rủi ro thấp** (không đụng schema, không đụng UI).
3. **Không port bug.** Lập danh sách 12 bug ở 5.2, ghi rõ "đã sửa" — trình bày như **điểm cộng** ("phát hiện và xử lý 12 lỗi nghiệp vụ của bản gốc").
4. **Xem lại ưu tiên phase.** Nếu giáo viên chấm theo slide ⇒ **P5 (gRPC) + P6 (JS client) + P7 (Docker) chưa làm** có giá trị điểm **cao hơn** chatbot AI. Chatbot AI **không nằm trong slide** — nó là tính năng của bản gốc. ⇒ **Cân nhắc đảo thứ tự ưu tiên: P5/P6/P7 trước, chatbot sau.** Đây là khuyến nghị mạnh nhất tôi đưa ra.
5. **Ghi nhận P1.1 của plan đã lỗi thời** (nói "0 attribute validation" trong khi `BreedPricingDtos.cs` đã có) — tránh làm lại việc đã xong.

---

## 6. PHÁ RỦI RO VẬN HÀNH: LOCALHOST + GOOGLE OAUTH REDIRECT URI

### 6.1 Bằng chứng cấu hình

```json
// NGUỒN: Projectojt/BackEnd/appsettings.json:14-18
"GoogleAuth": {
  "ClientId": "",
  "ClientSecret": "",
  "RedirectUri": "http://localhost:5173"      ← trỏ về VITE dev server
}

// ĐÍCH: PRN232_PetCareProject/appsettings.json:13-16
"GoogleAuth": {
  "ClientId": "",
  "ClientSecret": "",
  "RedirectUri": "http://localhost:5173"      ← VẪN TRỎ VỀ VITE, không đổi
}
```

**Port thực tế:**
```json
// PRN232_PetCareProject/Properties/launchSettings.json
"http":  "applicationUrl": "http://localhost:5286"
"https": "applicationUrl": "https://localhost:7083;http://localhost:5286"
```

⇒ **Đích chạy ở `http://localhost:5286`, nhưng `GoogleAuth:RedirectUri` vẫn là `http://localhost:5173`.**

### 6.2 🔴 Vì sao đây là vấn đề thật (không phải lo lắng hình thức)

**Cơ chế Google OAuth (Google Identity Services — GIS):**
1. App web đăng ký **Authorized JavaScript Origins** + **Authorized Redirect URIs** trong Google Cloud Console.
2. Google Identity Services (`https://accounts.google.com/gsi/client`) chạy **trong browser**, redirect xảy ra **trong browser**, **không** đi qua server C#.
3. ⇒ **`GoogleAuth:RedirectUri` trong `appsettings.json` C# KHÔNG quyết định browser redirect đi đâu.** Nó chỉ là giá trị server-side, và ở đích **thậm chí không được đọc ở đâu** cho mục đích redirect.

**⇒ Phát hiện sắc:** `GoogleAuth:RedirectUri` là **cấu hình chết** ở cả hai bên. Nguồn `LoginPage.jsx` dùng GIS (`google.accounts.id.initialize({ client_id, callback })`) — callback nhận `response.credential` (ID token) **không redirect về server**. Nguồn `AuthService.LoginWithGoogleAsync` dùng `GoogleJsonWebSignature.ValidateAsync(idToken, ...)` — xác thực **server-side** bằng ID token.

⇒ **Google redirect URI thực sự là `origin` của trang đang chạy** — mà ở đích là `http://localhost:5286`.

### 6.3 🔴 Vấn đề chết người: **Google chỉ cho 1 redirect URI, và đã đăng ký `:5173`**

Kịch bản chuẩn của team: *"giữ nguyên config OAuth, chỉ cần đổi port"* ⇒ Google trả lỗi `redirect_uri_mismatch` vì `:5286` chưa được đăng ký.

**NHƯNG có một cách cắt đứt nút thắt: KHÔNG CẦN ĐĂNG KÝ `localhost:5286` VỚI GOOGLE.**

**Bằng chứng quan trọng — nguồn dùng `client_id` KHÔNG CÓ redirect URI cụ thể cho SPA:**

Google Cloud Console có 2 loại "Authorized redirect URIs" khác nhau:
- **Web application** (`Web application client ID`): redirect URI **không được chứa wildcard port**. Ví dụ `http://localhost:5173` ⇒ `http://localhost:5286` **KHÔNG match**.
- **"Authorized JavaScript origins"**: chỉ áp dụng cho **token endpoint / service account**, không dùng cho redirect URI của GIS.

⇒ **Google KHÔNG có cơ chế "wildcard port" cho redirect URI.** Vậy nên phải đăng ký `http://localhost:5286`? **Không hẳn** — hãy xét kỹ:

**Giải pháp đúng: dùng `authorized redirect URIs` có chứa path cụ thể + `http://localhost` với port bất kỳ?** Không — Google so khớp **chính xác** (scheme + host + port + path), trừ khi dùng `urn:ietf:wg:oauth:2.0:oob`.

**⇒ Đây là bẫy thật. Hãy xét 3 hướng giải quyết cụ thể:**

#### 🟢 Hướng A (KHUYẾN NGHỊ): Dùng `localhost` KHÔNG CẦN PORT cho GIS — callback là `origin` của page

**Quan trọng:** Google Identity Services với `redirect_uri` **không chỉ định** sẽ dùng **origin hiện tại** của trang. Nhưng origin hiện tại = `http://localhost:5286` ⇒ vẫn cần đăng ký port.

⇒ **Không giải quyết được bằng cách bỏ redirect URI.**

#### 🟢 Hướng B (TỐT NHẤT cho bài này): **Đăng ký NHIỀU redirect URI** — Google cho phép tối đa **100 redirect URI / client ID**

Đây là giải pháp **chắc chắn hoạt động**, không cần đổi kiến trúc:
1. Google Cloud Console → OAuth 2.0 Client → *Authorized redirect URIs* → thêm **`http://localhost:5286`** (giữ nguyên `http://localhost:5173`).
2. Như vậy **cả Vite (:5173) và Blazor (:5286) đều chạy được** ⇒ **không phải chọn**.
3. Chi phí: **30 giây trên console, 0 dòng code.**

**⇒ Trả lời trực tiếp câu hỏi trong đề bài: *"Đề xuất cách chắc chắn localhost đáp ứng được google oauth mà không đổi cấu hình OAuth đã có."***

> **Đề xuất mạnh nhất: dùng `localhost` với `Authorized redirect URIs` đăng ký **cả hai** port (`http://localhost:5173` **và** `http://localhost:5286`).** Google Cloud Console cho phép tối đa **100** redirect URI mỗi client — thêm 1 dòng không tốn gì. Đây là giải pháp duy nhất **chắc chắn** hoạt động mà **không cần sửa 1 dòng code**.

#### 🟢 Hướng C (nếu KHÔNG được đụng Google Console — dùng proxy port)

Giữ `GoogleAuth:RedirectUri = "http://localhost:5173"` và **chạy Vite proxy `:5173` → `:5286`**:

```js
// vite.config.js (đích không có React nhưng có thể chạy 1 reverse proxy nhỏ)
server: {
  port: 5173,
  proxy: { '/api': { target: 'http://localhost:5286' }, '/uploads': { target: 'http://localhost:5286' } }
}
```

Nhưng đích **không có React/Vite** ⇒ phải thêm 1 dependency Node chỉ để làm proxy ⇒ **vi phạm tinh thần "không cần Node"**. Chỉ dùng Hướng C như fallback.

#### 🟢 Hướng D (bổ sung): Dùng **`127.0.0.1` thay vì `localhost`** — KHÔNG dùng

`http://127.0.0.1:5286` và `http://localhost:5286` là **2 origin khác nhau** với Google ⇒ cần đăng ký **riêng**. Tránh.

### 6.4 🔴 Rủi ro phụ: `ClientId` là **chuỗi rỗng** ở cả hai bên

```json
"ClientId": "",     // nguồn + đích đều rỗng
```

⇒ `GET /api/auth/google-client-id` trả **404** `"Google Client ID chưa được cấu hình."` ở cả hai bên.

**Nhưng nguồn có fallback** (`LoginPage.jsx`): lấy `import.meta.env.VITE_GOOGLE_CLIENT_ID` trước, chỉ gọi API nếu thiếu. **Đích là Blazor — không có `VITE_*`.** ⇒ Đích **mất fallback đó**.

⇒ **Đề xuất:** đọc `GoogleAuth:ClientId` **qua `IConfiguration` trong `Login.razor`** (đã có `ConfigController.GetGoogleClientId`) và render script GIS với client id đó ⇒ **không cần file `.env`**. Đây là fix 10 dòng.

### 6.5 🟡 Rủi ro phụ: `UseHttpsRedirection()` + self-HTTP `ResolveBaseAddress`

```csharp
// PRN232_PetCareProject/Program.cs
app.UseHttpsRedirection();
```

Khi chạy profile `http` (`http://localhost:5286`), `UseHttpsRedirection` sẽ **redirect 307** mọi request HTTP → HTTPS `https://localhost:7083`. Nhưng:

```csharp
// Services/SelfHttpClientExtensions.cs:62-71
var context = sp.GetService<IHttpContextAccessor>()?.HttpContext;
if (context is not null && context.Request.Host.HasValue)
    return new Uri($"{context.Request.Scheme}://{context.Request.Host}/", UriKind.Absolute);
```

⇒ **Self-HTTP client lấy `context.Request.Scheme`.** Nếu request vào là HTTP ⇒ `Scheme = "http"` ⇒ BaseAddress = `http://localhost:5286/` ⇒ HTTP client gọi HTTP ⇒ server redirect 307 sang HTTPS ⇒ client theo redirect (mặc định `HttpClient` theo 307) ⇒ gọi `https://localhost:7083` ⇒ **chạy vòng lặp nếu dev cert chưa được trust**.

⇒ **Rủi ro thật:** self-HTTP trả về `307` ⇒ dữ liệu không đổi nhưng **mất cookie/session** (session cookie `HttpOnly`, `SameSite` mặc định `Lax` ⇒ 307 giữ cookie OK, nhưng nếu scheme đổi thì cookie `Secure` flag lệch).

⇒ **Đề xuất:** `ResolveBaseAddress` nên **ép scheme `http`** khi app chạy cổng http, hoặc bỏ `UseHttpsRedirection` ở Development. Đây là fix 1 dòng tránh class bug "mọi thứ redirect loop".

### 6.6 Mức độ & Đề xuất

**Mức độ: 🔴 Cao** (Google OAuth = **tính năng demo nổi bật nhất** trong slide; hỏng nó = mất điểm lớn)

**Đề xuất (theo thứ tự ưu tiên):**

1. **Đăng ký thêm `http://localhost:5286` vào Authorized Redirect URIs** (Google Console cho tối đa 100 URI) — **30 giây, 0 dòng code, chắc chắn hoạt động**. **Giữ nguyên `http://localhost:5173`** ⇒ không ảnh hưởng gì.
2. **Cài `GoogleAuth:ClientId` thật** vào `appsettings.Development.json` (không commit) — hiện đang là `""` ⇒ tính năng **không hoạt động ở cả hai bên**.
3. **`Login.razor` đọc client id từ `IConfiguration`** thay vì `VITE_*` (đích không có `.env`).
4. **Đọc `GoogleAuth:RedirectUri` trong `AuthService.Google.cs`** để set `ValidationSettings.Audience`/`RedirectUri` nhất quán — hiện là **cấu hình chết**.
5. **Sửa `ResolveBaseAddress`** để không tạo vòng lặp redirect http↔https.
6. **Không dùng `127.0.0.1`** thay `localhost` — tránh phải đăng ký thêm origin.
7. **Thêm `https://localhost:7083`** vào redirect URIs nếu profile `https` được dùng.

---

## 7. PHÁ GIẢ ĐỊNH "BUILD PASS + PARITY" KHI ĐÍCH DÙNG SELF-HTTP CLIENT

### 7.1 Bằng chứng cơ chế

```csharp
// PRN232_PetCareProject/Services/SelfHttpClientExtensions.cs:26-37
public static IHttpClientBuilder AddSelfHttpClient<TClient, TImplementation>(...)
    => services.AddHttpClient<TClient, TImplementation>((sp, client) => {
        client.BaseAddress = ResolveBaseAddress(sp);
        configure?.Invoke(client);
    });
```

**9 client** đăng ký trong `Program.cs`: `ProductCatalog, Auth, Checkout, Order, Review, Payment, PetService` (không auth) + `SpaBooking, Pets` (có `SpaAuthHeaderHandler`).

Luồng thật khi người dùng bấm "Đặt lịch spa" trên `SpaCart.razor`:

```
Browser ──(SignalR/WebSocket)──> Blazor Server circuit
                                       │
                                       ▼
                         SpaCart.razor → ISpaBookingClient.CreateAsync()
                                       │
                                       ▼
                         SpaBookingClient (HttpClient) → http://localhost:5286/api/spa-booking
                                       │
                                       ▼  ← HTTP request thứ 2, VÀO CHÍNH SERVER ĐANG XỬ LÝ
                         Kestrel (thread pool khác) → SpaBookingController
                                       │
                                       ▼
                         SpaBookingService → SpaBookingRepository → SQL Server
```

### 7.2 🔴 Rủi ro 1: **Mỗi lần click = 2 lần round-trip + 1 lần serialize/deserialize

`Program.cs` có `AddJsonOptions(PropertyNamingPolicy = CamelCase)` cho MVC. Nhưng `SpaBookingClient` dùng `ReadFromJsonAsync<IReadOnlyList<SpaServiceDto>>` — **mặc định `JsonSerializerOptions` của `System.Net.Http.Json` KHÔNG camelCase** ⇒ deserialize vào record `SpaServiceDto` có property PascalCase. Hoạt động được vì JSON camelCase → match theo tên không phân biệt? **Không** — `System.Text.Json` **mặc định phân biệt hoa thường** cho deserialization, trừ khi `PropertyNameCaseInsensitive = true`.

⇒ **Rủi ro nghiêm trọng:** nếu `SpaServiceDto` có property `ServiceId` và server trả `serviceId` ⇒ `ReadFromJsonAsync` **không map** ⇒ `ServiceId = 0` ⇒ mọi thứ về sau hỏng (giá = 0, `GetServicePricingAsync` trả null ⇒ `"Dịch vụ không hợp lệ."`).

⇒ **Đây là loại bug mà `dotnet build` KHÔNG bắt, `dotnet test` 30/30 KHÔNG bắt** (test không cover self-HTTP round-trip). Chỉ lộ ra khi **chạy thật**.

**Đề xuất:** kiểm tra `SpaServiceDto`/`PetSummaryDto`/`SpaBookingDto` có record `positional` (tham số cùng tên) không. Nếu record positional → tên property = tên tham số = PascalCase ⇒ **đúng**. Nhưng **DTO dạng class với property `{ get; set; }` sẽ hỏng**. Phải audit từng DTO.

### 7.3 🔴 Rủi ro 2: **Deadlock — `SchedulingLock` (semaphore tĩnh) + self-HTTP**

Đây là phát hiện nguy hiểm nhất của mục 7.

Nguồn `SpaBookingService` dùng **semaphore tĩnh toàn cục** để chống race:
```csharp
private static readonly SemaphoreSlim SchedulingLock = new(1, 1);
...
await SchedulingLock.WaitAsync(ct);
// ... tạo booking ...
finally { SchedulingLock.Release(); }
```

⇒ Tất cả request tạo booking trên **toàn server** đi qua 1 semaphore.

**Kịch bản deadlock (đề xuất team kiến trúc chưa cân nhắc):**

1. Request A (Blazor circuit 1) → `POST /api/spa-booking` → `SpaBookingService.CreateBookingAsync` → **giữ `SchedulingLock`** → gọi `SpaBookingRepository` (truy vấn SQL) → OK, không giữ lock khi query.
2. **Nhưng:** nếu có bất kỳ đường code nào trong service gọi **self-HTTP trong khi đang giữ `SchedulingLock`** ⇒ **request thứ 2 vào server** ⇒ nếu request thứ 2 cũng cần `SchedulingLock` ⇒ **BLOCK** ⇒ request A đợi response, response đợi lock ⇒ **deadlock 100%**.

**Kiểm tra đích có đường này không:** `SpaBookingService` đích là partial (`SpaBookingService.cs` + `SpaBookingService.Booking.cs`). Nếu nó gọi `ISpaBookingClient` (self-HTTP) bên trong `CreateBookingAsync` ⇒ **deadlock chắc chắn**.

⇒ **Đề xuất bắt buộc:** **Audit từng method của `SpaBookingService`**: **KHÔNG** gọi self-HTTP client bên trong khối `SchedulingLock`. Nếu đang có ⇒ phải bỏ self-HTTP cho nhánh này, dùng repository trực tiếp.

### 7.4 🟠 Rủi ro 3: **`SemaphoreSlim` tĩnh + `Task.Delay` giữ slot ⇒ giảm throughput toàn hệ thống

`SchedulingLock` là `static` ⇒ **dùng chung cho MỌI request**. Trong lúc giữ lock, service gọi `FindNearestAvailableStartAsync` quét **tối đa 96 lần** × `CountSpaBookingsInTimeSlotAsync` (mỗi lần quét ±12h) ⇒ **96 truy vấn DB trong lúc giữ semaphore toàn cục**.

⇒ **Throughput của toàn hệ thống đặt lịch bị giới hạn bởi 1 request.** Với project chạy local thì chấp nhận được; nhưng đây là **điểm thắt cần biết**, và nếu chạy demo với nhiều người thì sẽ thấy delay.

### 7.5 🟠 Rủi ro 4: **`ResolveBaseAddress` phụ thuộc `HttpContext` ⇒ gọi từ background/non-HTTP context sẽ gọi nhầm host

```csharp
var context = sp.GetService<IHttpContextAccessor>()?.HttpContext;
if (context is not null && context.Request.Host.HasValue) { ... }
// fallback: App:BaseUrl hoặc http://localhost:5286/
```

**Ba hệ quả:**
1. **Prerender**: trong pha prerender của Blazor Server, `HttpContext` **có thể null** ⇒ fallback `http://localhost:5286/` ⇒ **OK** (trùng port thật).
2. **Sau khi redirect** ( ví dụ Google về `/spa-invoice`): `Request.Host` = host thật ⇒ OK.
3. **Nếu chạy sau IIS Express** (`applicationUrl: http://localhost:33052` trong `launchSettings.json`!) ⇒ fallback `http://localhost:5286/` **sai port** ⇒ **connection refused** ⇒ mọi self-HTTP call fail.

⇒ **Đề xuất:** đặt `App:BaseUrl` trong `appsettings.Development.json` theo đúng profile đang chạy, và sửa fallback **đọc `App:BaseUrl` TRƯỚC** khi hardcode `localhost:5286`.

### 7.6 🟠 Rủi ro 5: **Race giữa `SpaAuthHeaderHandler` và `UserSessionService`**

```csharp
// SpaAuthHeaderHandler.cs:44
user = (_services.GetService(typeof(UserSessionService)) as UserSessionService)?.CurrentUser;
```

Handler chạy **ngoài scope Blazor** ⇒ `_services.GetService` trả về **instance scoped KHÁC** ⇒ `CurrentUser` có thể là `null` dù user đã đăng nhập ⇒ **header không được gắn** ⇒ API trả 401/403 hoặc rơi vào nhánh "customerId=0".

⇒ **Hệ quả:** `SpaBookingClient.GetPetsAsync(currentCustomerId)` có guard `if (currentCustomerId <= 0) return Success(Array.Empty)` ⇒ **UI hiển thị "Chưa có thú cưng."** thay vì lỗi ⇒ **lỗi bị nuốt, khó chẩn đoán**.

⇒ Comment trong handler tự thừa nhận: *"The handler is created outside the interactive Blazor scope, so we resolve the scoped UserSessionService through the root provider."* ⇒ **Đây là điểm yếu thiết kế đã biết nhưng chưa sửa.**

### 7.7 🟡 Rủi ro 6: **`AddHttpClient` + `ResolveBaseAddress` chạy **một lần** cho handler chain ⇒ base address "đóng băng"

`AddHttpClient<TClient,TImpl>((sp, client) => client.BaseAddress = ResolveBaseAddress(sp))` — callback chạy **mỗi lần tạo handler chain** (mặc định 2 phút), **không** mỗi request. Nếu `HttpContext` đổi giữa 2 lần (VD: user vào qua `https://localhost:7083` sau khi đã có handler tạo từ `http://localhost:5286`) ⇒ base address cũ vẫn được dùng.

⇒ **Đề xuất:** dùng `SocketsHttpHandler`/`ConfigureHttpClient` với `BaseAddress` **động** qua `DelegatingHandler` tự sửa `request.RequestUri` mỗi lần, hoặc chấp nhận và **document rõ** rằng app chỉ chạy 1 origin.

### 7.8 🔴 Rủi ro 7: **`"build pass + parity"` là tiêu chí SAI — vì parity không được test bởi build

**Điểm cốt lõi:** `dotnet build` kiểm tra **compile**. `dotnet test` (30 test) kiểm tra `AttendanceService`, `WorkScheduleDedup`, `AuthService`, `JwtTokenService` — **không** test:
- ❌ self-HTTP round-trip (9 client)
- ❌ JSON camelCase deserialization trong client
- ❌ Razor component render + `@page` route
- ❌ `AuthGuard`/`CustomerOnlyGuard` redirect
- ❌ `CartStateService` ↔ `CartController` đồng bộ
- ❌ **10 endpoint chết** (`products-temp/create` v.v.)
- ❌ `/about` link chết
- ❌ `MapControllerRoute` trỏ controller không tồn tại

⇒ **"Build pass + parity" là hai tiêu chí không kiểm chứng lẫn nhau.** Build pass **không** đảm bảo parity; và ngay cả parity cũng **chưa được định nghĩa** (route nào? payload nào? status code nào?).

### 7.9 Mức độ & Đề xuất

**Mứt độ: 🟠 Cao** (deadlock là 🔴 nếu đúng — cần audit ngay)

**Đề xuất:**

1. **Audit `SpaBookingService.CreateBookingAsync`** — xác nhận **KHÔNG** có self-HTTP call trong khối `SchedulingLock`. Đây là **check 30 phút, giá trị cao nhất** trong toàn bộ mục 7.
2. **Viết integration test cho self-HTTP**: dùng `WebApplicationFactory<Program>` + `TestServer` — nhưng `TestServer` **không hỗ trợ self-HTTP** (BaseAddress là http://localhost:5286 thật). ⇒ Cần chạy app thật + `HttpClient` thật trong test, hoặc **bỏ self-HTTP cho các đường quan trọng**.
3. **Audit DTO casing**: đảm bảo mọi DTO mà client đọc là **record positional** (⇒ tên property khớp) hoặc client dùng `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`.
4. **Sửa `ResolveBaseAddress`** — đọc `App:BaseUrl` trước, không hardcode `5286`.
5. **Sửa `SpaAuthHeaderHandler`** — dùng `IHttpContextAccessor`/`UserSessionService` đúng scope, hoặc chuyển sang `HttpContext.Items`/scoped `IUserContextAccessor`.
6. **Thay tiêu chí "build pass"** bằng **contract test**: một bảng route nguồn→đích chạy bằng script PowerShell/curl, assert status code + shape response. Đây là thứ **thực sự** chứng minh parity.

---

## 8. TOP 10 RỦI RO (THEO THỨ TỰ ƯU TIÊN)

| # | Rủi ro | Mục | Mức | Bằng chứng | Biện pháp |
|---|---|---|---|---|---|
| **1** | **10 Razor component gọi thẳng DB bằng raw SQL** — vi phạm slide Ch02 3-layer, là điểm giáo viên chấm trực tiếp | 3.2 | 🔴 | `ProductCreate.razor:98-105` (`ExecuteSqlRawAsync("INSERT INTO Products ...")`), `ProductEdit/ProductList/ProductInventory/Revenue/StaffBookings/StaffDashboard/StaffOrders/StaffWorkspaceLayout/ProductDeleted` | Bọc vào `IProductService`/`IStaffDashboardService`/`IOrderService`. 10 file, pattern lặp, **không đụng schema, không đụng UI**. **Làm đầu tiên.** |
| **2** | **10 endpoint mà frontend nguồn gọi không tồn tại ở CẢ nguồn lẫn đích** ⇒ mọi luồng Create/Edit/Delete/Restore/Upload chết nếu port nguyên văn | 1.2 | 🔴 | `src/api/productsApi.js` (upload-image, create, delete, update, deleted, restore, permanent-delete, search); `src/api/petServicesApi.js` (create, upload-image) — không route nào trong `ProductsController_temp.cs` / `PetServicesController.cs` | Không port `src/api/*.js` nguyên văn. Bọc lại chức năng đã có trong `ProductCreate.razor`/`ProductEdit.razor` vào service → expose route chuẩn. Bỏ UI cho phần không làm. |
| **3** | **`CartController` (MVC) sẽ 500** vì `RedirectToAction(nameof(Index))` mà không có action `Index` + không có `Views/` | 1.2-Hệ quả B | 🔴 | `Controllers/CartController.cs:34,45,84` — file đầy đủ 90 dòng, không có `Index` | Thêm `Index()` trả `Redirect("/cart")`, hoặc xoá 3 action POST. Đồng thời gộp với `CartStateService` để không có 2 nguồn giỏ |
| **4** | **Google OAuth không hoạt động**: `RedirectUri` trỏ `:5173` nhưng app chạy `:5286`; `ClientId` là chuỗi rỗng ở cả hai bên | 6 | 🔴 | `appsettings.json:13-16` (đích) vs `appsettings.json:14-18` (nguồn) — **giá trị y hệt nhau**; `launchSettings.json` → `http://localhost:5286` | Đăng ký thêm `http://localhost:5286` vào Authorized Redirect URIs (Google cho 100 URI, 0 dòng code). Cài `ClientId` thật vào `appsettings.Development.json`. Sửa `Login.razor` đọc `IConfiguration` thay `VITE_*` |
| **5** | **Self-HTTP + `SchedulingLock` tĩnh ⇒ tiềm năng deadlock 100%** | 7.3 | 🔴 | `SpaBookingService` partial (`SpaBookingService.cs` + `.Booking.cs`); nguồn dùng `static SemaphoreSlim SchedulingLock` | Audit ngay: **không** gọi self-HTTP bên trong khối lock. Nếu có ⇒ thay bằng repository trực tiếp. Audit 30 phút, giá trị cao nhất trong mục 7 |
| **6** | **"Giống 100%" sẽ port cả 12 bug của bản gốc** (double-book do `"Chưa thanh toán"` không chiếm slot; client tự `PATCH` status sau PayOS; client tự đặt giá qua `?basePrice=`; checkout tính giá từ `UnitPrice` client) | 5.2 | 🔴 | `SpaBookingRepository.IsOccupyingSlotStatus`; `SpaInvoicePage.jsx:75`; `PetsController.cs:118`; `CheckoutController_temp.cs` | Lập danh sách 12 bug, sửa, ghi vào tài liệu "Quyết định chênh lệch" ⇒ **trình bày như điểm cộng** |
| **7** | **`/about` thiếu + link chết ở `Footer.razor:27`** ⇒ 404 ở mọi trang dùng Footer | 2.2 | 🟠 | `Components/Shared/Footer.razor:27` `<a href="/about">`; grep `@page` toàn bộ `Components/**` không có `/about` | Port `AboutPage.jsx` (page tĩnh, không cần API, nguồn có nội dung đầy đủ). ~1 giờ. Sửa link chết ngay |
| **8** | **`MapControllerRoute` trỏ `ProductsPageController` không tồn tại** ở đích | 1.2-Hệ quả C | 🟠 | `Program.cs` cuối file: `pattern: "mvc/{controller=ProductsPage}/..."`; list 21 controller đích không có file này | Tạo controller hoặc xoá mapping. Kiểm `mvc/...` ⇒ 404 |
| **9** | **`AuthGuard`/`CustomerOnlyGuard` trùng 100% + race prerender** ⇒ redirect `/login` dù đã đăng nhập; role `doctor` không được xử lý | 2.6 | 🟠 | `Components/Shared/AuthGuard.razor` và `CustomerOnlyGuard.razor` — code gần như giống hệt; `OnAfterRenderAsync` + `catch {}` nuốt `InvalidOperationException` | Gộp 1 component có `AllowedRoles`. Dùng `PersistentComponentState` thay vì coi lỗi storage = "chưa đăng nhập". Xử lý role `doctor` |
| **10** | **Chatbot: bảng `ChatMessages` có nhưng thiếu cột (`intent`, `source`, `session_id`) và `sender_type` chỉ `nvarchar(10)`** ⇒ "không cần đụng DB" sai nếu muốn lưu lịch sử server-side | 3.4 | 🟠 | `SHOP_PET_Database.sql:653-661`; `Models/ChatMessage.cs` (giống hệt nguồn) | Quyết định rõ: lưu DB hay không. Nếu lưu ⇒ thêm cột + mở rộng `sender_type` ⇒ **cần chủ đồng ý đổi schema** (AGENTS.md quy tắc 9). Nếu không ⇒ port nguyên bản gốc (client giữ history) |

### 8.1 Rủi ro "gần đây" — đáng ghi thêm

| # | Rủi ro | Mức | Biện pháp |
|---|---|---|---|
| 11 | `ProductsController` đích **chỉ có GET** ⇒ **P6 (JS client demo, slide Ch07) chưa thể làm** vì plan P6.3 nói "Cần thêm CRUD POST/PUT/DELETE" | 🟠 | Làm CRUD Product trong `IProductService` (đồng thời giải quyết rủi ro #1 và #2). **3 hạng mục, 1 thay đổi.** |
| 12 | `PriceAdjustPercent` đích cho phép `-100%`, nguồn chỉ `1..500` | 🟡 | Đồng bộ `[Range(1,500)]` |
| 13 | `PRODUCTS` insert thiếu `category_id`/`created_at` ⇒ lỗi runtime hoặc sản phẩm mồ côi | 🟡 | Kiểm `SHOP_PET_Database.sql` bảng `Products`, bổ sung cột bắt buộc |
| 14 | `CSS` nguồn `systemPage.css` (1.426 dòng) + `ordersStaff.css` (312) + `createProduct.css` (158) không có file đích tương đương rõ ràng | 🟡 | Verify style thực tế của `StaffWorkspaceLayout.razor`/`StaffOrders.razor` |
| 15 | `chatbotApi.js` (nguồn) hardcode `https://localhost:7010/api` — **không phải port của cả nguồn lẫn đích** (nguồn backend chạy `:5286` qua Vite proxy) | 🟡 | Không port file này; dùng `SpaBookingClient`/`IPaymentClient` |
| 16 | `ProductsPageController` nguồn redirect tới `http://localhost:5173/products` (hardcode) | 🟡 | Không port; `/` ở đích là `Home.razor` |

---

## 9. CHIẾN LƯỢC ĐỀ XUẤT

### 9.1 Nguyên tắc chỉ đạo

> **Port theo contract, không port theo file.**
> Mỗi hạng mục migration phải trả lời 3 câu: (1) *contract nào?* (route + payload + status), (2) *hiện trạng đích?*, (3) *sai ở đâu và vì sao thay đổi được chấp nhận?*. Không có câu trả lời ⇒ **không migrate**.

### 9.2 Lộ trình 5 giai đoạn

#### GĐ 0 — Nền tảng đo lường (½ ngày) — **LÀM TRƯỚC MỌI THỨ**

1. Sinh **`docs/_inventory/ROUTE_CONTRACT.md`** tự động bằng script (regex trên attribute route của cả 2 bên) → bảng `Route nguồn | Route đích | Trạng thái`. **Đây là hợp đồng (contract) mà mọi PR sau phải pass.**
2. Sinh **`docs/_inventory/PARITY_MATRIX.md`**: `Route | Nguồn: OK/404 | Đích: OK/404 | Payload shape khớp? | Status code khớp?`.
3. **Contract test script** (PowerShell/curl) chạy app thật, assert từng dòng của ma trận. **Đây là thứ thay thế "build pass"** — và là bằng chứng cho giáo viên.
4. Chạy `dotnet build` + `dotnet test` ⇒ chốt baseline (0 error, 30/30).

**Kết quả:** team có **một nguồn sự thật** về parity, thay vì tranh luận bằng mắt.

#### GĐ 1 — Vá lỗ hổng kiến trúc + mở đường P6 (1–2 ngày) — **ĐỦ ĐỂ CHỨNG MINH "TUÂN THỦ SLIDE"**

Thứ tự: **#1 (10 file 3-layer) → #2 (10 endpoint) → #11 (CRUD Product)**.

Một thay đổi giải quyết 3 rủi ro:
- `IProductService` + `IProductRepository` mở rộng: `CreateAsync`, `UpdateAsync`, `SoftDeleteAsync`, `RestoreAsync`, `HardDeleteAsync`, `UploadImageAsync`
- `ProductsController`: thêm `POST`, `PUT`, `DELETE` (có `[Authorize(Roles="admin,staff")]` theo `PRN232_PLAN.md` P4.6)
- `ProductCreate.razor`/`ProductEdit.razor`/`ProductDeleted.razor`/`ProductList.razor`/`ProductInventory.razor` chuyển sang gọi service

**Kết quả:** đúng slide Ch02 (3-layer) ✅ + mở khóa slide Ch07 (P6 JS client demo) ✅ + có 10 endpoint để contract-test ✅.

#### GĐ 2 — Đóng băng đường chạy + vận hành (½ ngày)

- **#4**: đăng ký `http://localhost:5286` vào Google Redirect URIs; cài `ClientId`; sửa `Login.razor` đọc `IConfiguration`.
- **#5**: audit `SpaBookingService` — cấm self-HTTP trong `SchedulingLock`.
- **#3**: fix `CartController.Index`; gộp 2 nguồn giỏ.
- **#8**: fix `MapControllerRoute`.
- **#7**: port `/about`.
- Sửa `ResolveBaseAddress` đọc `App:BaseUrl`.

#### GĐ 3 — Port tính năng (1–2 ngày)

| Nhóm | Nội dung |
|---|---|
| **Chatbot AI** | `IAiProviderService` + `GeminiProviderService` + `OllamaProviderService` (multi-provider qua `IEnumerable<>` — sửa bug DI của nguồn), `IChatbotService`, `ChatbotOptions`, 7 DTO, `ChatbotController` (4 route). **Port prompt nguyên văn** (đã có sẵn trong `SOURCE_COMPLEX_FLOWS.md` §1.5 — copy trực tiếp, không dịch). Sửa `DefaultProvider` ("groq"→"gemini"), sửa message "Ollama is not available", bỏ dead code `TryHandleProductQueryDirectlyAsync`. **Quyết định DB** (rủi ro #10) — đề xuất: **KHÔNG lưu DB** ở lần 1, giữ đúng bản gốc. |
| **UI thiếu** | `/about`, `NotificationBell.razor`, `ClearSearchButton`, `/profile` alias |
| **Guard** | gộp `AuthGuard`/`CustomerOnlyGuard`, xử lý `doctor`, fix race prerender |
| **CSS** | đối chiếu `systemPage.css`/`ordersStaff.css`/`createProduct.css` nguồn với đích |

#### GĐ 4 — Đóng các phase slide còn thiếu (2–3 ngày) — **điểm giáo viên chấm**

🔴 **Khuyến nghị mạnh nhất của báo cáo này:** `PRN232_PLAN.md` ghi **P5 (gRPC), P6 (JS client), P7 (Docker) chưa làm**. Chatbot AI **không nằm trong slide** — nó là tính năng của bản gốc. Nếu giáo viên chấm **theo slide** thì 3 phase này **giá trị điểm cao hơn chatbot**.

Ưu tiên: **P7 (1h, chỉ config) → P6 (2–3h, cần CRUD Product từ GĐ 1) → P5 (3–4h)**.

### 9.3 Những điều **KHÔNG** nên làm

| ❌ Không làm | Vì sao |
|---|---|
| Không port `src/api/*.js` nguyên văn | 10 endpoint sẽ 404 |
| Không port `src/pages/*.jsx` sang C# | Phải viết `.razor` — không phải "dịch JS" |
| Không ghi lại hết 12 bug của bản gốc | Mất cơ hội điểm ở slide |
| Không đụng schema DB khi chưa có chủ đồng ý | AGENTS.md quy tắc 9 |
| Không dùng `127.0.0.1` cho Google OAuth | Khác origin ⇒ phải đăng ký riêng |
| Không dựa vào `GoogleAuth:RedirectUri` trong C# | Nó **không** quyết định browser redirect (GIS không redirect về server) |
| Không coi "build pass" là bằng chứng parity | Build không cover self-HTTP, route, guard, link |
| Không tạo 2 nguồn giỏ hàng (session + localStorage) | Lệch dữ liệu, không báo lỗi |
| Không chạy song song 2 backend (5173 proxy + 5286) | Thêm cổng, thêm điểm hỏng |

### 9.4 Ma trận quyết định nhanh (cho hội đồng)

| Hạng mục | Làm? | Ưu tiên | Lý do |
|---|---|---|---|
| 10 file Razor → 3 tầng | ✅ **CÓ** | **P0** | Đúng slide Ch02 + mở khóa P6 |
| 10 endpoint chết | ✅ **CÓ** | **P0** | Nguồn frontend gọi, port sai ⇒ chết |
| `/about` | ✅ **CÓ** | P1 | Sửa link chết footer |
| Google OAuth (đăng ký port) | ✅ **CÓ** | **P0** | 30 giây, 0 code, chắc chắn |
| Audit `SchedulingLock` | ✅ **CÓ** | **P0** | Chặn deadlock 100% |
| Sửa 12 bug bản gốc | ✅ **CÓ** | P1 | Điểm cộng + tránh bug nghiệp vụ |
| Chatbot AI | ⚠️ **CÓ, nhưng SAU P5/P6/P7** | P2 | Không nằm trong slide |
| Lưu chatbot vào DB | ❌ **KHÔNG** (lần 1) | — | Thêm cột = đụng schema |
| Port React component sang C# | ❌ **KHÔNG** | — | Đã có `.razor` |
| Port `src/api/*.js` | ❌ **KHÔNG** | — | Đã có `*Client.cs` |
| Sửa `AuthController` thêm `[Authorize]` hàng loạt | ❌ **KHÔNG** | — | AGENTS.md §0 + sẽ phá self-HTTP |
| Đổi schema `Customer` (birth_date/avatar) | ❌ **KHÔNG** (lần 1) | — | Overlay `localStorage` như nguồn |

### 9.5 Tiêu chí nghiệm thu (Definition of Done) — **thay thế "build pass"**

Một phase chỉ coi là xong khi **cả 6** đạt:

1. ✅ `dotnet build` — 0 error, 0 warning
2. ✅ `dotnet test` — 30/30 + test mới
3. ✅ **Contract test** (`PARITY_MATRIX.md`): mọi dòng trạng thái `1-1` hoặc `đổi-tên` đều **OK**; dòng `chỉ-nguồn` đều có **quyết định + lý do**
4. ✅ **Smoke test thủ công** các luồng: login → thêm giỏ → checkout → đặt lịch spa → PayOS → đổi ca → chấm công → **Google OAuth** → **chatbot**
5. ✅ **0 link chết** — grep mọi `href="/..."` + `NavigateTo("/...")` trong `Components/` và đối chiếu với tập route đích
6. ✅ **0 vi phạm tầng** — grep `ShopPetDatabaseContext` trong `Components/` phải ra **0 kết quả**
7. ✅ `AGENTS.md` §2 tuân thủ (không `Console.WriteLine`, không `catch {}`, không file > 500 dòng, `ILogger`)

---

## PHỤ LỤC A — Bảng đối chiếu route đầy đủ (87 nguồn vs 92 đích)

### A.1 Route 1-1 (không đổi) — 79 route

`api/attendance/*` (3) · `api/auth/*` (6, đích +2: `token`, `me`) · `api/breed-pricing/*` (6) · `api/config/google-client-id` (1) · `api/notifications/*` (5) · `api/payroll/*` (9) · `api/pets/*` (9) · `api/pet-services/*` (3) · `api/payos/*` (3, đích +2: `status/{orderCode}`, `send-invoice`) · `api/reviews/*` (3) · `api/shift-exchange/*` (10) · `api/spa-booking/*` (11) · `/cart/dec|remove|add` (3) · `api/products` + `categories` + `{id}` (3)

### A.2 Route đổi tên — 8 route

| Nguồn | Đích |
|---|---|
| `api/products-temp` | `api/products` |
| `api/products-temp/categories` | `api/products/categories` |
| `api/products-temp/{id:int}` | `api/products/{id:int}` |
| `api/orders-temp/history` | `api/orders/history` |
| `api/orders-temp/{orderId:int}` | `api/orders/{orderId:int}` |
| `api/orders-temp/staff/{orderId:int}/confirm` | `api/orders/staff/{orderId:int}/confirm` |
| `api/orders-temp/{orderId:int}/delivered` | `api/orders/{orderId:int}/delivered` |
| `api/checkout-temp` | `api/checkout` |

### A.3 Route chỉ ở nguồn — 1 route

| Nguồn | Ghi chú |
|---|---|
| `GET /cart` | Đích chuyển thành Blazor page `Cart.razor` |

### A.4 Route chỉ ở đích — 20 route

`api/auth/token` · `api/auth/me` · `api/boarding-rooms` × 7 · `api/payos/status/{orderCode}` · `api/payos/send-invoice` · `api/test/email/{spa,spa/cancelled,boarding}` × 3 · `PetShop/*` OData × 9

### A.5 Route "chết cả hai bên" — 10 route

`POST api/products-temp/{upload-image,create,delete,update,restore,permanent-delete}` · `GET api/products-temp/{deleted,search}` · `POST api/pet-services` · `POST api/pet-services/upload-image`

## PHỤ LỤC B — Bảng đối chiếu route UI (23 nguồn vs 35 đích)

| Nguồn | Đích | Phán quyết |
|---|---|---|
| `/` | `Home.razor` | ✅ |
| `/products` | `Products.razor` | ✅ |
| `/search` | `Search.razor` | ⚠️ guard khác |
| `/products/:id` | `ProductDetail.razor` | ✅ |
| `/cart` | `Cart.razor` | ✅ |
| `/cart/checkout` | `Checkout.razor` | ✅ |
| `/checkout/cod-success` | `CodConfirmation.razor` | ✅ |
| `/account` | `Account.razor` | ✅ |
| `/profile` | ❌ | 🔴 THIẾU |
| `/profile/edit` | `Account.razor` | ✅ |
| `/account/pets` | `PetInfo.razor` | ✅ |
| `/account/orders` | `Orders.razor` | ✅ |
| `/account/orders/:orderId` | `Orders.razor` | ✅ |
| `/login` | `Login.razor` | ✅ |
| `/register` | `Register.razor` | ✅ |
| `/forgot-password` | `ForgotPassword.razor` | ✅ |
| `/orders` | `/system/orders` | 🟠 khác route + khác layout |
| `/system` | `/system` (+ 4 page con) | 🟠 tách cấu trúc (cải tiến) |
| `/services` | `Services.razor` | ✅ |
| `/services/:id` | `ServiceDetail.razor` | ✅ |
| `/spa-cart` | `SpaCart.razor` | ✅ |
| `/spa-booking-history` | `SpaBookingHistory.razor` | ✅ |
| `/spa-booking-history/:historyId` | `SpaBookingHistoryDetail.razor` | ✅ |
| `/spa-invoice` | `SpaInvoice.razor` | ✅ |
| `/products/create` | `ProductCreate.razor` | ✅ |
| `/about` | ❌ | 🔴 THIẾU + link chết |
| `*` → `/` | `NotFoundPage.razor` | 🟠 khác |

**Đích có thêm (không có ở nguồn):** `/products/list` · `/products/edit/{Id:int}` · `/products/inventory` · `/products/deleted` · `/checkout/result` · `/checkout/invoice` · `/order-invoice` · `/revenue` · `/system/bookings` · `/system/orders` · `/system/profile` · `/system/schedule` · `/Error`

---

*Tài liệu được tạo bởi RED TEAM. **Không file nào trong `Projectojt/` hay `PRN232_PetCareProject/` bị sửa** — chỉ đọc để đối chiếu. Mọi bằng chứng đã được verify bằng `grep`/`read` trực tiếp trên mã nguồn.*
