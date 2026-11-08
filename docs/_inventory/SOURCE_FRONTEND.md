# INVENTORY FRONTEND — DỰ ÁN OJT (Nguồn để migrate sang PRN232)

> **Nguồn (chỉ đọc):** `D:\1_F_Matierials\Kỳ 8\1_PRN232\New folder\Projectojt\frontend`
> **Stack:** React 19 + Vite 7 + React Router 7 + react-icons 5 + Chart.js 4 (react-chartjs-2)
> **Ngày lập:** 2026-11-08
> **Phạm vi:** toàn bộ `src/` (pages, api, components, styles, images, utils) + `public/`

---

## 0. TỔNG QUAN NHANH

| Chỉ số | Số lượng |
|---|---|
| Route entries trong `App.jsx` | **35** (23 URL unique) |
| URL unique (sau khi loại route trùng do guard) | **23** |
| Page files trong `src/pages/` | **27** (26 page thực + 1 file re-export `AccountPage.jsx`) |
| Component files trong `src/components/` | **17** `.jsx` (+ 1 file data `checkout/daNangData.js`) |
| API modules trong `src/api/` | **10** |
| Hàm API export | **70** (gồm 2 helper nội bộ không gọi API: `resolveProductId`, `getBrowserLocalIp`) |
| CSS files trong `src/styles/` | **14** (+ `src/index.css`, `src/App.css`) |
| Tổng số dòng CSS trong `src/styles/` | **12.551** dòng |
| Asset ảnh trong `src/images/` | **144** file |
| Asset trong `public/` | **2** file (+ `default-service-placeholder.png` được tham chiếu nhưng KHÔNG tồn tại) |
| Media video | **2** (`video.mp4`, `video.MOV`) |
| Utils | **1** (`userProfileStore.js`) |

---

## 1. ROUTER — BẢNG ROUTE

Nguồn: `src/App.jsx` (`<BrowserRouter>` + `<Routes>`).

**Layout dùng chung:** Không có `layout` component riêng. Mỗi page tự lắp ghép `TopBar` → `Header` → `Navigation` → `<main>` → `Footer` (hoặc `PageContextBar` thay cho `Header`+`Navigation`). `ScrollToTop` là component nội tuyến cuối trong `App.jsx`, tự cuộn đầu trang khi đổi pathname.

**Route guards (component nội tuyến trong `App.jsx`):**

| Guard | Hành vi |
|---|---|
| `ProtectedCustomerRoute` | Nếu `authUser.role === "staff"` → `<Navigate to="/system" replace />`; ngược lại render children |
| `AuthRequiredRoute` | Staff → `/system`; chưa đăng nhập → `/login`; ngược lại render children |

> ⚠️ **Lưu ý phát hiện bug**: `/profile`, `/profile/edit`, `/account/orders`, `/account/orders/:orderId`, `/search`, `/orders`, `/system`, `/products/create` được khai báo **KHÔNG có guard**. Riêng `/cart`, `/cart/checkout`, `/checkout/cod-success`, `/account`, `/account/pets` được khai báo 2 lần: 1 bản public + 1 bản có `AuthRequiredRoute`. Trong React Router, route khai báo sau thắng → bản **có guard** được áp dụng. `ServicePage`/`ServiceDetailPage`/`SpaCartPage`/`SpaBookingHistoryPage`/`SpaBookingHistoryDetailPage`/`SpaInvoicePage` **chỉ** có bản `ProtectedCustomerRoute`.

| # | Route URL | Page file | Layout | Nội dung chính | Trạng thái (loading/empty/error) |
|---|---|---|---|---|---|
| 1 | `/` | `pages/HomePage.jsx` | TopBar + Header + Navigation + Footer + Toast + CartDrawer | HeroBanner (2 slide carousel), FeaturedCategories (6 danh mục tròn), ProductList_temp (sản phẩm nổi bật, preview 10 + "Xem tất cả"), CustomerReviews (4 review tĩnh). Ẩn `FeaturedCategories`/`ProductList` nếu role = `doctor` | loading: skeleton 10 card; error: khối "❌ {error}" + nút "Thử lại"; empty: "Chưa có sản phẩm nào." / "Không tìm thấy sản phẩm phù hợp."; toast 3s "🛒 Đã thêm sản phẩm vào giỏ hàng!" |
| 2 | `/products` | `pages/ProductsPage.jsx` | TopBar + Header + Navigation + Footer | Danh sách sản phẩm + sidebar danh mục (`ProductsSidebar`), badge "N sản phẩm", nút staff "Những sản phẩm đã xóa", "Tạo sản phẩm", "Xem doanh thu". Query: `?q=`, `?categoryId=` | loading: "Đang tải sản phẩm..."; error: `products-error`; empty: "Không tìm thấy sản phẩm phù hợp."; alert "Bạn cần đăng nhập để thêm sản phẩm vào giỏ hàng." |
| 3 | `/search` | `pages/SearchProductsPage.jsx` | TopBar + Header + Footer + CartDrawer (KHÔNG có Navigation) | Tìm kiếm theo keyword + filter chip danh mục + sort tab (Liên quan/Mới nhất/Bán chạy) + select giá + phân trang (PAGE_SIZE=20, có ellipsis). Query: `?q=` | loading: "Đang tải dữ liệu sản phẩm..."; error: `search-state error`; empty: "Không tìm thấy sản phẩm khớp bộ lọc hiện tại."; sub: "Bạn chưa nhập từ khóa — đang hiển thị toàn bộ sản phẩm." |
| 4 | `/products/:id` | `pages/ProductDetailPage.jsx` | TopBar + Header + Navigation + Footer + CartDrawer | Breadcrumb, gallery ảnh + thumbnails, pill "Còn hàng"/"Hết hàng", giá, SKU (`PET-000001`), rating sao, "Vận chuyển"/"An tâm mua", mô tả, chọn số lượng (+/-), "Thêm vào giỏ hàng"/"Mua ngay", share FB/TW/Pin, section đánh giá sản phẩm | loading: "Đang tải chi tiết sản phẩm…"; error: `pd-state`; empty: "Không có dữ liệu sản phẩm."; reviews: "Đang tải đánh giá…" / "Sản phẩm này chưa có đánh giá."; alert "Sản phẩm đã hết hàng." |
| 5 | `/cart` | `pages/CartPage.jsx` | TopBar + PageContextBar("Giỏ hàng") + Footer (+ Navigation import nhưng không render) | Chọn tất cả / chọn từng item, tăng giảm số lượng, xóa item, tổng sản phẩm/tạm tính/phí vận chuyển/tổng cộng, nút "Thanh toán (N sản phẩm)" | empty: "Giỏ hàng trống" + "Hãy thêm sản phẩm vào giỏ hàng của bạn" + nút "Mua sắm ngay"; alert "Vui lòng chọn ít nhất một sản phẩm." |
| 6 | `/cart/checkout` | `pages/CheckoutPage.jsx` | TopBar + PageContextBar("Thanh toán") + Footer + AddressModal | Form địa chỉ (tỉnh/quận/phường/đường), bảng sản phẩm, chọn COD/PayOS, lời nhắn cho người bán, iframe Google Maps embed, tổng thanh toán, nút "Đặt hàng" | Không có loading state; redirect `/cart` nếu không có items; validate on-blur + on-submit; alert "Thanh toán thất bại. Vui lòng thử lại." / "Có lỗi khi xử lý thanh toán." |
| 7 | `/checkout/cod-success` | `pages/CodConfirmationPage.jsx` | TopBar + PageContextBar("Xác nhận đơn hàng") + Footer | Banner "Đặt hàng thành công!", card thông tin đơn (mã đơn, người nhận, SĐT, địa chỉ, tổng tiền), nút "Về trang chủ"/"Xem giỏ hàng", 10 sản phẩm gợi ý | loading: "Đang tải sản phẩm..."; empty: "Không có sản phẩm nào"; alert "Đã thêm vào giỏ hàng!" |
| 8 | `/account` | `pages/ProfilePage.jsx` | TopBar + Header + Navigation + Footer | Hồ sơ cá nhân: avatar (crop tròn, kéo + zoom), họ tên, ngày sinh, SĐT, email, địa chỉ; nút "Thông tin thú cưng", "Chỉnh sửa thông tin", "Về trang chủ" | Không có loading; khi chưa đăng nhập: "Vui lòng đăng nhập để xem thông tin hồ sơ." + nút "Đăng nhập"; giá trị rỗng → "Chưa cập nhật"; ngày sinh sai → "Ngày sinh không tồn tại" |
| 9 | `/profile` | `pages/ProfilePage.jsx` (cùng file) | Như `/account` | Như `/account` | Như `/account` |
| 10 | `/profile/edit` | `pages/ProfileEditPage.jsx` | TopBar + Header + Navigation + Footer | Form chỉnh sửa: họ tên, ngày sinh (mask dd/mm/yyyy + date picker), SĐT, email; aside avatar cropper; nút "Lưu"/"Hủy"/"Thông tin thú cưng" | error: "Ngày sinh không tồn tại"; chưa đăng nhập: "Vui lòng đăng nhập để chỉnh sửa thông tin."; giới hạn avatar 4MB |
| 11 | `/account/pets` | `pages/PetInfoPage.jsx` | TopBar + Header + Navigation + Footer | Form thú cưng (tên, tuổi, giới tính, cân nặng, loài, giống, tình trạng sức khỏe, mô tả, ảnh) + danh sách thú cưng (sửa/xóa) | loading: "Đang tải danh sách thú cưng..."; error notice: "Chỉ hỗ trợ tệp ảnh." / "Vui lòng nhập tên thú cưng." / "Vui lòng chọn loài trước." / "Vui lòng chọn giống."; empty: "Chưa có thú cưng nào."; success: "Đã thêm/cập nhật/xóa thú cưng." |
| 12 | `/account/orders` | `pages/OrderHistoryPage.jsx` | TopBar + PageContextBar("Lịch sử đơn hàng") + Footer | 6 tab trạng thái (Tất cả/Chờ thanh toán/Chờ xác nhận/Chờ giao hàng/Hoàn thành/Đã hủy) kèm số đếm, ô tìm kiếm, card kiểu Shopee (badge "Yêu thích", tên shop "Pet Toy Shop", nút "Xem Shop", trạng thái vận chuyển, tổng tiền) | loading: "Đang tải đơn hàng..."; empty: "Bạn chưa có đơn hàng nào." / "Không có đơn phù hợp bộ lọc hoặc từ khóa tìm kiếm."; error chỉ log console |
| 13 | `/account/orders/:orderId` | `pages/OrderHistoryDetailPage.jsx` | TopBar + PageContextBar("Chi tiết đơn hàng") + Footer | Header shop, mã đơn, ngày đặt, địa chỉ, tổng tiền, trạng thái thanh toán, nút "Đã nhận hàng", danh sách line sản phẩm, form đánh giá 5 sao + nội dung (chỉ khi đơn "hoàn thành") | loading: "Đang tải chi tiết đơn hàng..."; error: "Mã đơn không hợp lệ." / "Không tìm thấy đơn hàng." / "Không tải được chi tiết đơn hàng."; review loading: "Đang tải đánh giá..."; alert "Vui lòng chọn số sao (1-5)." / "Gửi đánh giá thất bại." |
| 14 | `/login` | `pages/LoginPage.jsx` | Layout auth 2 cột (`auth-page`): logo trái, form phải | Đăng nhập email/password, link "Quên mật khẩu?", nút Google Identity Services, link đăng ký | loading: "Đang đăng nhập..."; error: "Vui lòng nhập Email và mật khẩu" / "Đăng nhập thất bại" / "Không lấy được ID Token từ Google." / "Google Login chưa sẵn sàng. Vui lòng thử lại sau vài giây."; success: "Đăng nhập thành công: {name} ({role})" |
| 15 | `/register` | `pages/RegisterPage.jsx` | Layout auth 2 cột | 2 bước: (1) form Họ và tên/Email/SĐT/Địa chỉ/Mật khẩu; (2) xác thực OTP (email + OTP code) + đếm ngược gửi lại 120s | loading: "Đang đăng ký..." / "Đang xác thực..." / "Đang gửi lại OTP..."; error: "Vui lòng nhập đầy đủ Họ và tên, Email, Mật khẩu." / "Vui lòng nhập email và OTP."; success: "Đã gửi OTP đến email. Vui lòng nhập OTP để kích hoạt tài khoản." / "Xác thực OTP thành công. Vui lòng đăng nhập." |
| 16 | `/forgot-password` | `pages/ForgotPasswordPage.jsx` | Layout auth 2 cột | 3 bước: (1) gửi OTP, (2) xác minh OTP + resend, (3) đặt mật khẩu mới (email/newPassword/confirmPassword) | loading: "Đang gửi OTP..." / "Đang xác minh..." / "Đang reset..."; error: "Vui lòng nhập email để gửi OTP" / "Vui lòng nhập email và OTP" / "Vui lòng nhập email" / "Vui lòng nhập mật khẩu mới và xác nhận" / "Reset token chưa được tạo. Hãy xác minh OTP trước." / "Email không khớp với email đã xác thực OTP."; success: "Đã gửi OTP đến email." / "OTP hợp lệ..." / "Mật khẩu đã được cập nhật thành công..." |
| 17 | `/orders` | `pages/OrdersStaffPage.jsx` | TopBar + Footer (không PageContextBar) | Wrapper mỏng render `<OrdersStaffPanel layout="page" />` — quản lý đơn hàng staff | (xem OrdersStaffPanel) |
| 18 | `/system` | `pages/SystemPage.jsx` | TopBar + PageContextBar (không title) + Footer | Trang quản trị staff, sidebar 5 tab: Dashboard / Chấm công / Lịch làm việc / Dịch vụ / Đơn hàng (mỗi tab còn có `breed-pricing` trong code nhưng KHÔNG có trong MENU_ITEMS → không truy cập được). Dashboard: 4 KPI + 2 bar chart 8 tuần + 2 donut trạng thái. Chấm công: check-in/out theo ca, verify WiFi nội bộ, đổi ca/nhờ làm thay, lịch sử 30 bản ghi. Lịch làm việc: board 7 ngày × 3 ca. Dịch vụ: duyệt/đổi status booking spa | loading: "Đang tải…" / "Đang tải danh sách dịch vụ…" / "Đang tải lịch làm việc…"; empty: "Chưa có dữ liệu chấm công." / "Hiện chưa có booking dịch vụ nào." / "Chưa có lịch sử chấm công." / "Chưa có dữ liệu lịch làm việc." / "Chưa có dữ liệu. Nhấn 'Thêm mới'..."; error: "Không tải được dữ liệu chấm công." / "Bạn phải dùng đúng WiFi nội bộ công ty mới được chấm công." / "Phần trăm phải từ 1 đến 500" / "Vui lòng chọn đủ thông tin nhờ làm thay." / "Không thể gửi yêu cầu đổi ca cho ngày trong quá khứ."; dùng `window.alert` cho lỗi |
| 19 | `/services` | `pages/ServicePage.jsx` | TopBar + Header + Navigation + Footer | Danh sách dịch vụ (grid card + carousel ảnh 3 slide auto-play 3s + rating sao + thời gian + giá). Doctor có nút "Thêm dịch vụ" mở modal (tạo dịch vụ) | loading: "Đang tải dịch vụ..."; error: `service-error`; empty: "Chưa có dịch vụ nào được cung cấp."; modal error: "Vui lòng nhập tên dịch vụ khám." / "Thời gian (phút) phải là số lớn hơn 0." / "Giá tiền không hợp lệ." |
| 20 | `/services/:id` | `pages/ServiceDetailPage.jsx` | TopBar + Header + Navigation + Footer + CartDrawer | Breadcrumb, carousel ảnh, tên/loại dịch vụ, giá, rating, "Thời gian dịch vụ", "Cam kết chất lượng", mô tả, "Thêm vào giỏ spa"/"Đặt ngay", form đánh giá sao + nhận xét (chỉ khi đã đặt & trạng thái phù hợp) | loading: "Đang tải chi tiết dịch vụ…"; error: `pd-state`; empty: "Không có dữ liệu dịch vụ."; review: "Chưa có đánh giá." / "Dịch vụ này chưa có đánh giá." / "Vui lòng đăng nhập để đánh giá dịch vụ." / "Chỉ khách hàng đã hoàn thành dịch vụ mới được đánh giá." / "Bạn chưa đủ điều kiện để đánh giá dịch vụ này."; alert "Dịch vụ này đã có trong giỏ hàng rồi." |
| 21 | `/spa-cart` | `pages/SpaCartPage.jsx` | TopBar + PageContextBar("Giỏ hàng") + Footer | Bảng dịch vụ: chọn ngày hẹn (date), giờ hẹn (select slot 07:00–12:00 & 13:00–18:00 bước 30p), chọn thú cưng (checkbox, hiển thị % điều chỉnh giá theo giống), thanh toán (cash/payos), lời nhắn, tổng tiền, nút "Đặt hàng". Có modal gợi ý khung giờ khi trùng slot | loading: "Đang tải giỏ spa..."; error: `spa-cart-page__error`; empty: "Giỏ spa đang trống. Hãy chọn thêm dịch vụ nhé." / "Chưa có thú cưng."; validation: "Vui lòng chọn ngày và giờ." / "Ngày giờ không được ở quá khứ." / "Vui lòng chọn thú cưng." / "Không được đặt quá 2 dịch vụ cùng ngày giờ." / "Đã trùng giờ (đủ 2 slot)..." / "Giờ đã chọn trước đó không còn hợp lệ — vui lòng chọn lại từ danh sách." / "Khung giờ đã đủ slot" |
| 22 | `/spa-booking-history` | `pages/SpaBookingHistoryPage.jsx` | TopBar + PageContextBar("Lịch sử đặt dịch vụ") + Footer | 5 tab (Tất cả/Chờ thanh toán/Đang xử lý/Hoàn thành/Đã hủy) + đếm, ô tìm kiếm, card booking (mã booking, tên dịch vụ, thú cưng, thời gian, tổng tiền, màu viền theo nhóm thời gian), nút "Hủy lịch"/"Đặt lại thanh toán", notice PayOS success/cancel | empty: "Bạn chưa có lịch hẹn spa nào." / "Không có lịch phù hợp bộ lọc hoặc từ khóa tìm kiếm."; notice: "Thanh toán PayOS thành công" / "Bạn đã hủy thanh toán PayOS"; alert "Không thể hủy đặt lịch." |
| 23 | `/spa-booking-history/:historyId` | `pages/SpaBookingHistoryDetailPage.jsx` | TopBar + PageContextBar("Chi tiết đặt lịch spa") + Footer | Chi tiết booking (đọc từ localStorage `spaBookingHistory`), mô tả dịch vụ sinh tự động theo tên, giá, phương thức thanh toán, nút "Thanh toán ngay" (PayOS), "Xem hóa đơn" | empty: "Không tìm thấy chi tiết dịch vụ."; error: "Không tìm thấy mã booking để thanh toán." / "Không tạo được link PayOS."; loading: "Đang tạo thanh toán..." |
| 24 | `/spa-invoice` | `pages/SpaInvoicePage.jsx` | TopBar + PageContextBar("Hóa đơn") + Footer | Hóa đơn dịch vụ: header brand "Pet Toy Shop - Pet Spa Service", ngày đặt, mã booking, badge trạng thái, thông tin khách hàng (tên/email/thú cưng), thông tin đặt lịch (ngày/giờ/trạng thái), bảng dịch vụ, cảnh báo hủy PayOS, nút "In hóa đơn" (window.print) | loading: spinner + "Đang tải hóa đơn..."; error: SVG icon + "Thiếu mã booking." / "Không tải được hóa đơn." |
| 25 | `/products/create` | `pages/CreateProductPage.jsx` | TopBar + Header + Navigation + Footer | Form tạo sản phẩm (staff/admin only): tên, phân loại, giá (1000–10.000.000, step 1000), số lượng, tên công ty, mô tả, ảnh. Tự upload ảnh trước rồi tạo | loading: "Đang tạo..."; notice success: "Tạo sản phẩm thành công."; notice error: "Không thể tạo sản phẩm." / "Không tải được danh mục sản phẩm."; redirect nếu không đúng role |
| 26 | `/about` | `pages/AboutPage.jsx` | TopBar + Header + Navigation + Footer | Giới thiệu: hero, 3 giá trị cốt lõi, dịch vụ nổi bật, cam kết chất lượng, quy trình 4 bước, FAQ, CTA | Không có loading/error (static) |
| 27 | `*` (fallback) | — | — | `<Navigate to="/" replace />` — mọi URL không khớp đều về trang chủ | — |

### 1.1 Page KHÔNG được route (orphan) — cần xử lý khi migrate

| File | Tình trạng | Nội dung chính | Trạng thái (loading/empty/error) |
|---|---|---|---|
| `pages/SpaBookingDetailPage.jsx` | **Được implement đầy đủ nhưng KHÔNG được import trong `App.jsx` → không có route, không reachable** | Đọc `useParams().historyId`, tìm trong `localStorage["spaBookingHistory"]`; hiển thị card chi tiết booking (thumbnail dịch vụ, tên dịch vụ, mã booking, status pill, giá, thời gian), link "Quay về lịch sử". Có helper `getStatusConfig` (Hủy/Đã thanh toán/Chưa thanh toán) và `getSpaServiceThumb` (map tên dịch vụ → ảnh `bath1.png`/`cut1.jpg`/`spa-caocap1.jpg`/`teeth1.jpg`, fallback `placeholder.jpg`) | Không có loading; empty: "Không tìm thấy thông tin dịch vụ trong lịch sử đặt spa." + nút "Quay về lịch sử"; parse JSON lỗi → `null` (nuốt lỗi, không log) |

---

## 2. API — BẢNG HÀM API

> Lưu ý: nhiều page gọi `fetch()` **trực tiếp** (không qua `src/api/`): `/api/checkout-temp`, `/api/orders-temp/*`, `/api/pet-services`, `/api/spa-booking/*`, `/api/pet-services/:id`. Xem mục 2.2.

### 2.1 Hàm export trong `src/api/`

| # | API function | Method | Endpoint | Payload | File |
|---|---|---|---|---|---|
| 1 | `registerApi(payload)` | POST | `/api/auth/register` | `{ name, email, password, phone, address }` | `api/authApi.js` |
| 2 | `loginApi(payload)` | POST | `/api/auth/login` | `{ email, password }` | `api/authApi.js` |
| 3 | `googleLoginApi(payload)` | POST | `/api/auth/google-login` | `{ idToken }` | `api/authApi.js` |
| 4 | `sendEmailOtpApi(payload)` | POST | `/api/auth/send-email-otp` | `{ email }` | `api/authApi.js` |
| 5 | `verifyEmailOtpApi(payload)` | POST | `/api/auth/verify-email-otp` | `{ email, otpCode }` | `api/authApi.js` |
| 6 | `resetPasswordApi(payload)` | POST | `/api/auth/reset-password` | `{ email, resetToken, newPassword, confirmPassword }` | `api/authApi.js` |
| 7 | `fetchProducts({signal})` | GET | `/api/products-temp` | — | `api/productsApi.js` |
| 8 | `searchProducts(keyword, {categoryId, signal})` | GET | `/api/products-temp/search?keyword=&categoryId=` | query params | `api/productsApi.js` |
| 9 | `fetchProductCategories({signal})` | GET | `/api/products-temp/categories` | — | `api/productsApi.js` |
| 10 | `fetchProductById(productId, {signal})` | GET | `/api/products-temp/{productId}` | — (404 → `null`) | `api/productsApi.js` |
| 11 | `uploadProductImage(file, {signal})` | POST | `/api/products-temp/upload-image` | `FormData: image` | `api/productsApi.js` |
| 12 | `createProduct(payload, {signal})` | POST | `/api/products-temp/create` | `{ name, categoryId, price, stockQuantity, supplierName, description, imageUrl }` | `api/productsApi.js` |
| 13 | `fetchProductReviews(productId, {signal})` | GET | `/api/reviews/products/{productId}` | — | `api/productsApi.js` |
| 14 | `resolveProductId(product)` | — | *(helper, không gọi API)* | trả `productId \| ProductId \| id` đã chuẩn hóa về number > 0 | `api/productsApi.js` |
| 15 | `deleteProduct(productId, {signal})` | POST | `/api/products-temp/delete` | `{ productId }` + header `X-User-Role` | `api/productsApi.js` |
| 16 | `updateProduct(payload, {signal})` | POST | `/api/products-temp/update` | payload sản phẩm + header `X-User-Role` | `api/productsApi.js` |
| 17 | `fetchDeletedProducts({signal})` | GET | `/api/products-temp/deleted` | header `X-User-Role` (404 → `[]`) | `api/productsApi.js` |
| 18 | `restoreProduct(productId, {signal})` | POST | `/api/products-temp/restore` | `{ productId }` + `X-User-Role` | `api/productsApi.js` |
| 19 | `permanentDeleteProduct(productId, {signal})` | POST | `/api/products-temp/permanent-delete` | `{ productId }` + `X-User-Role` | `api/productsApi.js` |
| 20 | `upsertProductReview(productId, customerId, payload)` | POST | `/api/reviews/products/{productId}?customerId={customerId}` | `{ orderId, rating, comment }` | `api/productsApi.js` |
| 21 | `getFeaturedProducts({keyword, categoryId, signal})` | GET | `/api/products-temp?keyword=&categoryId=` | query params | `api/productsApi_temp.js` |
| 22 | `getSearchCategories({keyword, signal})` | GET | `/api/products-temp/categories?keyword=` | query params | `api/productsApi_temp.js` |
| 23 | `getSpeciesOptions({signal})` | GET | `/api/pets/meta/species` | header `X-User-Role`, `X-User-Id` | `api/petsApi.js` |
| 24 | `getBreedOptions(speciesId, {signal})` | GET | `/api/pets/meta/breeds?speciesId=` | query params | `api/petsApi.js` |
| 25 | `getPetsForManage(customerId, {signal})` | GET | `/api/pets/customer/{customerId}/manage` | — | `api/petsApi.js` |
| 26 | `createPet(payload, {signal})` | POST | `/api/pets` | `{ customerId, petName, age, gender, weightKg, breedId, healthStatus, description, imagePath }` | `api/petsApi.js` |
| 27 | `updatePet(petId, payload, {signal})` | PUT | `/api/pets/{petId}` | như #26 | `api/petsApi.js` |
| 28 | `uploadPetImage(file, {signal})` | POST | `/api/pets/upload-image` | `FormData: image` | `api/petsApi.js` |
| 29 | `deletePet(petId, customerId, {signal})` | DELETE | `/api/pets/{petId}?customerId=` | query params | `api/petsApi.js` |
| 30 | `fetchPetServices({signal})` | GET | `/api/pet-services` | — | `api/petServicesApi.js` |
| 31 | `createPetService(data)` | POST | `/api/pet-services` | `{ Name, Description, Duration, Price, ImageUrl }` (PascalCase!) | `api/petServicesApi.js` |
| 32 | `uploadPetServiceImage(file)` | POST | `/api/pet-services/upload-image` | `FormData: image` | `api/petServicesApi.js` |
| 33 | `fetchSpaServices({signal})` | GET | `/api/spa-booking/services` | — | `api/spaBookingApi.js` |
| 34 | `fetchPets(customerId, {signal})` | GET | `/api/spa-booking/pets?customerId=` | query params | `api/spaBookingApi.js` |
| 35 | `estimateSpaBooking(customerId, payload)` | POST | `/api/spa-booking/estimate?customerId=` | payload đặt lịch | `api/spaBookingApi.js` |
| 36 | `createSpaBooking(customerId, payload)` | POST | `/api/spa-booking?customerId=` | `{ petIds, appointmentStart, note, items, paymentMethod }` | `api/spaBookingApi.js` |
| 37 | `createSpaPayOSPayment(bookingId, payload)` | POST | `/api/payos/spa/create` | `{ bookingId, ...payload }` | `api/spaBookingApi.js` |
| 38 | `fetchSpaHistory(customerId, {signal})` | GET | `/api/spa-booking/history?customerId=` | query params | `api/spaBookingApi.js` |
| 39 | `updateSpaBookingStatus(bookingId, status)` | PATCH | `/api/spa-booking/{bookingId}/status` | `{ status }` | `api/spaBookingApi.js` |
| 40 | `submitSpaReview(customerId, payload)` | POST | `/api/spa-booking/review?customerId=` | `{ bookingId, serviceId, rating, comment }` | `api/spaBookingApi.js` |
| 41 | `fetchServiceReviews(serviceId, {signal})` | GET | `/api/reviews/services/{serviceId}` | — | `api/spaBookingApi.js` |
| 42 | `getNotificationsApi(staffId, {unreadOnly})` | GET | `/api/notifications/{staffId}[?unreadOnly=true]` | query params | `api/notificationApi.js` |
| 43 | `getUnreadCountApi(staffId)` | GET | `/api/notifications/{staffId}/unread-count` | — | `api/notificationApi.js` |
| 44 | `markAsReadApi(requestId, staffId)` | PUT | `/api/notifications/{requestId}/read?staffId=` | query params | `api/notificationApi.js` |
| 45 | `markAllAsReadApi(staffId)` | PUT | `/api/notifications/{staffId}/read-all` | — | `api/notificationApi.js` |
| 46 | `deleteNotificationApi(notificationId)` | DELETE | `/api/notifications/{notificationId}` | — | `api/notificationApi.js` |
| 47 | `getPendingRequestsApi(staffId)` | GET | `/api/shift-exchange/pending?staffId=` | query params | `api/notificationApi.js` |
| 48 | `acceptRequestApi(requestId, responderStaffId)` | POST | `/api/shift-exchange/accept/{requestId}?responderStaffId=` | query params | `api/notificationApi.js` |
| 49 | `declineRequestApi(requestId, responderStaffId)` | POST | `/api/shift-exchange/decline/{requestId}?responderStaffId=` | query params | `api/notificationApi.js` |
| 50 | `cancelRequestApi(requestId, staffId)` | POST | `/api/shift-exchange/cancel/{requestId}?staffId=` | query params | `api/notificationApi.js` |
| 51 | `getMyRequestsApi(staffId)` | GET | `/api/shift-exchange/my-requests?staffId=` | query params | `api/notificationApi.js` |
| 52 | `getBreedPricingAll({signal})` | GET | `/api/breed-pricing` | — | `api/breedPricingApi.js` |
| 53 | `fetchBreedPricingByService(serviceId)` | GET | `/api/breed-pricing/service/{serviceId}` | — | `api/breedPricingApi.js` |
| 54 | `fetchBreedPricingByBreed(breedId)` | GET | `/api/breed-pricing/breed/{breedId}` | — | `api/breedPricingApi.js` |
| 55 | `createBreedPricing(payload)` | POST | `/api/breed-pricing` | `{ serviceId, breedId, priceAdjustPercent }` | `api/breedPricingApi.js` |
| 56 | `updateBreedPricing(id, payload)` | PUT | `/api/breed-pricing/{id}` | `{ priceAdjustPercent }` | `api/breedPricingApi.js` |
| 57 | `deleteBreedPricing(id)` | DELETE | `/api/breed-pricing/{id}` | — | `api/breedPricingApi.js` |
| 58 | `getBrowserLocalIp()` | — | *(WebRTC STUN, không gọi API)* | trả IP LAN, cache 30s | `api/attendanceApi.js` |
| 59 | `getAttendanceOverviewApi(staffId)` | GET | `/api/attendance/staff/{staffId}/overview` | — | `api/attendanceApi.js` |
| 60 | `toggleAttendanceApi(staffId)` | POST | `/api/attendance/staff/{staffId}/toggle` | header `X-Client-IP` (IP từ WebRTC) | `api/attendanceApi.js` |
| 61 | `verifyNetworkApi({signal})` | GET | `/api/shift-exchange/verify-network` | header `X-Client-IP` | `api/attendanceApi.js` |
| 62 | `getMyShiftOptionsApi(staffId)` | GET | `/api/shift-exchange/staff/{staffId}/my-shifts` | — | `api/attendanceApi.js` |
| 63 | `getAllShiftOptionsApi()` | GET | `/api/shift-exchange/all-shifts` | — | `api/attendanceApi.js` |
| 64 | `getStaffOptionsApi(staffId)` | GET | `/api/shift-exchange/staff/options?excludeStaffId=` | query params | `api/attendanceApi.js` |
| 65 | `createPassShiftApi(payload)` | POST | `/api/shift-exchange/pass` | `{ staffId, fromDate, fromScheduleId, toStaffId, reason }` | `api/attendanceApi.js` |
| 66 | `createSwapShiftApi(payload)` | POST | `/api/shift-exchange/swap` | `{ staffId, fromDate, toDate, fromScheduleId, toScheduleId, toStaffId, reason }` | `api/attendanceApi.js` |
| 67 | `sendMessage(message, customerId, history)` | POST | `https://localhost:7010/api/chatbot/message` | `{ message, customerId, history }` + Bearer token | `api/chatbotApi.js` |
| 68 | `detectIntent(message)` | POST | `https://localhost:7010/api/chatbot/intent` | `{ message }` | `api/chatbotApi.js` |
| 69 | `getContext(customerId)` | GET | `https://localhost:7010/api/chatbot/context/{customerId}` | — | `api/chatbotApi.js` |
| 70 | `checkHealth()` | GET | `https://localhost:7010/api/chatbot/health` | — | `api/chatbotApi.js` |

> **Lưu ý kiến trúc**: `chatbotApi.js` là module **duy nhất** dùng `axios` với `baseURL` hardcode `https://localhost:7010/api` và interceptor gắn `Authorization: Bearer {token}` từ `localStorage.token`. Tất cả module khác dùng `fetch` tương đối (`/api/...`) — phụ thuộc Vite proxy.

### 2.2 Endpoint được gọi TRỰC TIẾP (không qua `src/api/`)

| Method | Endpoint | Payload | Nơi gọi |
|---|---|---|---|
| GET | `/api/auth/google-client-id` | — | `pages/LoginPage.jsx` (fallback khi thiếu `VITE_GOOGLE_CLIENT_ID`) |
| POST | `/api/checkout-temp` | `{ customerId, email, customerName, phone, address, paymentMethod, items[{productId,itemName,quantity,unitPrice}], returnUrl, cancelUrl }` | `pages/CheckoutPage.jsx` |
| GET | `/api/orders-temp/history?customerId=` | — | `pages/OrderHistoryPage.jsx` |
| GET | `/api/orders-temp/{orderId}?customerId=` | — | `pages/OrderHistoryDetailPage.jsx` |
| PATCH | `/api/orders-temp/{orderId}/delivered?customerId=` | — | `pages/OrderHistoryDetailPage.jsx` |
| GET | `/api/orders-temp/staff[?status=]` | — | `components/staff/OrdersStaffPanel.jsx`, `pages/SystemPage.jsx` |
| PATCH | `/api/orders-temp/staff/{orderId}/confirm` | — | `components/staff/OrdersStaffPanel.jsx` |
| GET | `/api/pet-services` | — | `pages/ServicePage.jsx`, `pages/SystemPage.jsx` |
| GET | `/api/pet-services/{id}` | — | `pages/ServiceDetailPage.jsx`, `pages/SpaCartPage.jsx` |
| GET | `/api/pets/customer/{customerId}` | — | `pages/SpaCartPage.jsx` |
| GET | `/api/pets/customer/{customerId}/service/{serviceId}/pricing` | — | `pages/SpaCartPage.jsx` |
| GET | `/api/pets/meta/breeds` | — | `pages/SystemPage.jsx` |
| POST | `/api/spa-booking/availability` | `{ start, durationMin, quantity }` | `pages/SpaCartPage.jsx` |
| POST | `/api/spa-booking?customerId=` | `{ petIds, appointmentStart, note, items, paymentMethod }` | `pages/SpaCartPage.jsx` |
| GET | `/api/spa-booking/history?customerId=` | — | `pages/SpaCartPage.jsx`, `pages/ServiceDetailPage.jsx`, `pages/SpaBookingHistoryPage.jsx` |
| PATCH | `/api/spa-booking/{bookingId}/status` | `{ status }` | `pages/SpaBookingHistoryPage.jsx`, `pages/SpaInvoicePage.jsx`, `pages/SystemPage.jsx` |
| POST | `/api/spa-booking/review?customerId=` | `{ bookingId, serviceId, rating, comment }` | `pages/ServiceDetailPage.jsx` |
| GET | `/api/spa-booking/{bookingId}/invoice` | — | `pages/SpaInvoicePage.jsx` |
| GET | `/api/spa-booking/all-bookings` hoặc `/api/spa-booking/history/all` | — | `pages/SystemPage.jsx` (thử lần lượt) |
| POST | `/api/payos/spa/create` | `{ bookingId, bookingIds?, returnUrl, cancelUrl }` | `pages/SpaCartPage.jsx`, `pages/SpaBookingHistoryDetailPage.jsx` |

---

## 3. COMPONENTS — BẢNG VAI TRÒ

| # | Component file | Vai trò | Dùng ở đâu |
|---|---|---|---|
| 1 | `components/home/TopBar.jsx` | Thanh trên cùng: điện thoại, email, badge khuyến mãi, chào hỏi theo role (`admin`/`staff`/`doctor`/`user`), dropdown menu tài khoản, nút đăng xuất, nhúng `NotificationBell` cho staff/admin | **23 page** (mọi page trừ 4 page auth: Login/Register/ForgotPassword — dùng layout `.auth-page` riêng) |
| 2 | `components/home/Header.jsx` | Header chính: logo (staff → `/system`), ô tìm kiếm có autocomplete (gợi ý tên sản phẩm, max 8), badge giỏ hàng, badge giỏ spa (heart icon), tổng tiền giỏ. Props `cartCount`, `onOpenCart` | **11 page**: HomePage, ProductsPage, ProductDetailPage, SearchProductsPage, ServicePage, ServiceDetailPage, CreateProductPage, ProfilePage, ProfileEditPage, PetInfoPage, AboutPage |
| 3 | `components/home/Navigation.jsx` | Menu điều hướng: Trang chủ / Sản phẩm (scroll `#featured-products`) / Dịch vụ chăm sóc / Giới thiệu / Liên hệ / Blog | **10 page** (Home, Products, ProductDetail, Service, ServiceDetail, CreateProduct, Profile, ProfileEdit, PetInfo, About). KHÔNG dùng ở các page con như Cart, Checkout, Order History, Spa |
| 4 | `components/home/Footer.jsx` | Footer 4 cột: Về chúng tôi (logo, mô tả, địa chỉ), Liên kết nhanh, Chính sách, Đăng ký nhận tin (form newsletter), social icons (FB/IG/YT/TikTok/Zalo), copyright | **23 page** (mọi page trừ 4 page auth: Login/Register/ForgotPassword) |
| 5 | `components/home/PageContextBar.jsx` | Thanh ngữ cảnh trang (thay Header+Navigation): logo compact + separator + tiêu đề page. Logo target theo role | **11 page**: CartPage, CheckoutPage, CodConfirmationPage, OrderHistoryPage, OrderHistoryDetailPage, SystemPage, SpaCartPage, SpaBookingHistoryPage, SpaBookingHistoryDetailPage, SpaInvoicePage, SpaBookingDetailPage (orphan) |
| 6 | `components/home/Toast.jsx` | Toast notification đơn giản (`show`, `message`), tự ẩn sau 3s | HomePage |
| 7 | `components/home/CartDrawer.jsx` | Drawer giỏ hàng trượt (overlay, ESC đóng, focus trap, lock body scroll): danh sách item, xóa item, tạm tính, nút "Xem giỏ hàng"/"Thanh toán". Empty: "Giỏ hàng trống" | HomePage, ProductDetailPage, SearchProductsPage, ServiceDetailPage |
| 8 | `components/home/HeroBanner.jsx` | Carousel hero 2 slide (auto 6s, prev/next, dots): tiêu đề, mô tả, 3 stat (1000+ / 5000+ / 100%), CTA "Khám phá ngay" | HomePage |
| 9 | `components/home/FeaturedCategories.jsx` | 6 danh mục tròn (ảnh + accent color): Đồ chơi cho chó/mèo/thông minh/nhai/vận động/tương tác | HomePage (ẩn với role doctor) |
| 10 | `components/home/ProductList_temp.jsx` | Section "Sản phẩm nổi bật": skeleton loading, error + nút "Thử lại", preview 10 sản phẩm + "Xem tất cả"/"Thu gọn", grid `ProductCard variant="featured"` | HomePage |
| 11 | `components/home/ProductCard.jsx` | Card sản phẩm đa variant: `default` / `featured` / `search`. Badge (Hết hàng / Sắp hết / Mới / -%), overlay "Hết hàng", giá + giá cũ, rating, tồn kho, nút "Thêm vào giỏ"/"Mua ngay" | ProductList_temp, ProductsPage, SearchProductsPage |
| 12 | `components/home/CustomerReviews.jsx` | Section "Khách hàng nói gì về chúng tôi": 4 review tĩnh (avatar icon FaDog/FaCat/GiRabbit, tên, pet, ngày, nhận xét) | HomePage |
| 13 | `components/products/ProductsSidebar.jsx` | Sidebar lọc theo danh mục ("Danh mục đồ chơi" / "Tất cả sản phẩm") | ProductsPage |
| 14 | `components/products/ClearSearchButton.jsx` | Nút "Xóa từ khóa" (chỉ hiện khi có keyword) | ProductsPage |
| 15 | `components/staff/OrdersStaffPanel.jsx` | Panel quản lý đơn hàng staff: 3 tab pill (Chờ xác nhận / Chờ giao hàng / Hoàn thành) + accent bar, card đơn (mã, ngày, thumbnail, tên, +N sản phẩm khác, phương thức thanh toán, tổng tiền, nút "Xác nhận đơn", địa chỉ). Props `layout: 'page' \| 'embedded'` | OrdersStaffPage (`page`), SystemPage tab "Đơn hàng" (`embedded`) |
| 16 | `components/staff/NotificationBell.jsx` | Chuông thông báo staff: badge unread, panel dropdown (thông báo chung, yêu cầu đổi ca chờ duyệt, yêu cầu của bạn), nút đánh dấu đã đọc / chấp nhận / từ chối. Props `staffId`, `variant: 'topbar' \| 'default'` | TopBar (variant `topbar`) |
| 17 | `components/checkout/AddressModal.jsx` | Modal quản lý địa chỉ giao hàng, 2 view: **list** (radio chọn, "Cập nhật", tag "Mặc định", "Thêm địa chỉ mới") và **form** (họ tên, SĐT, email, tỉnh disabled, dropdown quận + phường có search, số nhà/đường) + validate per-field + nút "Xóa địa chỉ"/"Quay lại"/"Lưu địa chỉ" | CheckoutPage |
| 18 | `components/checkout/daNangData.js` | *(Data module, không phải component)*: dữ liệu 8 quận/huyện Đà Nẵng + 100+ phường/xã (`DA_NANG_WARDS_BY_DISTRICT`, `ALL_DA_NANG_WARDS`, `DA_NANG_DISTRICTS`, `DEFAULT_PROVINCE`) + helper `normalizeText`, `validatePhone` (regex `^(0[0-9]{9,10})$`), `validateEmail`, `isWardValidForDistrict`, `formatPhoneDisplay`, `buildGoogleMapsEmbedSrc` | CheckoutPage, AddressModal |

**File re-export:** `pages/AccountPage.jsx` = `export { default } from './OrderHistoryPage'` (không được import ở bất kỳ đâu — dead code).

---

## 4. CSS — BẢNG MÔ TẢ

**Bảng màu chủ đạo toàn hệ thống:**
- Tím chủ đạo: `#4a2477` (system-primary), `#5c2d91` (ziggy-purple), `#7b3eb6` (ziggy-purple-bright)
- Cam/hồng accent: `#f77f00`, `#ff9f1c`, `#ff8c94`, `#ffd6c0`, `#e8b4bc`
- Nền: `#faf7fb` (main-bg), trắng `#ffffff`, dải hồng `#f9f4f7` / `#f4e8ec`
- Text: `#3d3550` (chính), `#8b8498` (nhạt), `#2a2438` (đậm)
- Bo góc lớn: `--border-radius: 22px`, nhỏ 18px, tròn 50%
- Shadow màu: tím `rgba(92,45,145,.18)`, hồng `rgba(232,180,195,.35)`, vàng `rgba(247,127,0,.2)`

| # | CSS file | Số dòng | Mô tả |
|---|---|---|---|
| 1 | `styles/home.css` | **2.788** | File lớn nhất về layout chung toàn site. Biến CSS gốc (ziggy-purple/orange, main-bg, card-bg, primary/secondary/accent, text, border-radius, shadow, transition). Class chính: `.home-page`, `.container`, `.container--home-wide`, `.top-bar*` (+`.top-bar-dropdown`, `.top-bar-menu`), `.header*` (`.header-container`, `.brand-logo`, `.search-wrapper`, `.suggestions-list`, `.header-icon-badge`, `.header-cart-sum`), `.navigation`/`.nav-list`/`.nav-item`, `.hero-banner`/`.hero-carousel-*`/`.hero-slide`/`.hero-stats`/`.hero-cta`, `.featured-categories`/`.categories-circular`/`.category-circle`, `.products-section`/`.products-grid--featured`/`.product-card*`/`.product-badge`/`.out-of-stock-overlay`/`.product-card-skeleton`, `.customer-reviews`/`.review-card`, `.footer*` (4 cột, newsletter, social), `.toast-notification`, `.section-title__paws`, `.home-section-head`, `.error-message`, `.retry-button`, `.products-search-page`/`.products-intro`/`.products-layout`/`.products-sidebar`/`.products-content`/`.products-badge`/`.products-loading`/`.products-error`/`.products-empty`/`.products-create-btn`/`.products-trash-nav-btn`, `.products-grid` |
| 2 | `styles/service.css` | **3.681** | File lớn nhất về dòng. Toàn bộ UI dịch vụ/spa: `.service-page`, `.service-section`, `.service-grid`, `.service-grid-card` (+`__media`, `__content`, `__header`, `__rating`, `__stars`, `__desc`, `__footer`, `__price`), `.service-carousel*` (slide/track/btn/dot), `.service-loading`/`.service-error`/`.service-empty`, `.service-add-service-modal`, `.service-add-image-preview`, `.service-add-modal-actions`, `.spa-cart-page`/`.spa-cart-checkout`/`.spa-cart-checkout__*`, `.spa-time-input--invalid`/`.spa-time-input-error`, `.spa-bh-*` (lịch sử booking: tabs/tab/list/card/row/status), `.spa-history-card*`, `.spa-invoice-page`/`.spa-invoice-card*`, `.spa-cart__notice`/`.spa-cart__redo`, `.order-shopee-*` (lịch sử đơn hàng), `.order-detail-*`/`.order-hist-*`, `.product-delete-confirm-*` |
| 3 | `styles/systemPage.css` | **1.426** | Trang quản trị staff. Biến `--system-primary/-hover/-soft/-glow` (= `#4a2477`). Class: `.system-page`, `.system-sidebar` (+`-title`, `-list`, `-item`, `-icon`), `.system-content`, `.system-stats-grid`, `.system-card` (+`--spa`, `--orders`, `--pending`, `--revenue`), `.system-charts-grid`/`.system-chart-card*`, `.system-donut-grid`/`.system-donut-card*`/`.system-donut-legend*`, `.system-attendance-*`, `.system-shift-*`, `.system-week-nav`/`.system-week-board`/`.system-week-day-card`/`.system-week-slot`/`.system-week-shift-item`, `.system-service-*`, `.bp-actions-bar`/`.bp-table`/`.bp-badge`/`.bp-adjust-label`/`.bp-modal*`/`.bp-input`/`.bp-select`, `.notif-bell-*`/`.notif-panel*`/`.notif-request-*`/`.notif-btn--accept/--decline`, `.top-bar-notif-slot` |
| 4 | `styles/profile.css` | **917** | Hồ sơ + thú cưng. Class: `.profile-page`, `.profile-card` (+`--shopee`), `.profile-avatar*`/`.profile-avatarEdit`/`.profile-avatarInput`/`.profile-avatar__fallback`, `.profile-title*`, `.profile-actions`, `.profile-btn` (+`--primary`, `--ghost`), `.profile-grid`/`.profile-field` (+`--span`), `.profile-shopee__*` (top/titles/h1/sub/rule/body/main/form/fieldRow/label/input/dateWrap/dateBtn/aside/avatarRing/avatarImg/avatarFallback/choosePhoto/photoHint/error/formFooter/btn), `.pet-page__*` (notice/loading/formCard/formLayout/formFields/fieldRow/label/input/select/formFooter/listSection/listTitle/list/item/itemImg/itemInfo/itemName/itemDetail/itemMeta/itemActions/empty), `.avatarCropper*` (backdrop/panel/head/title/sub/close/stage/circle/img/controls/label/actions), `.profile-dateNative` |
| 5 | `styles/checkout.css` | **864** | Checkout. Class: `.checkout-page-wrapper`/`.checkout-page`/`.checkout-container`, `.checkout-section` (+`--address`, `--products`, `--payment`, `--message`, `--summary`), `.checkout-address-*`, `.checkout-products-table`/`.checkout-table-header`/`.checkout-table-row`/`.col-product`/`.col-price`/`.col-qty`/`.col-total`, `.checkout-payment-options`/`.checkout-payment-option`, `.checkout-input` (+`--full`), `.checkout-input-wrapper`, `.checkout-form-row`, `.checkout-select-trigger`, `.province-suggestions`/`.province-suggestion`, `.district-suggestions`/`.ward-suggestions`, `.checkout-map-wrap`/`.checkout-map-iframe`, `.checkout-summary-*`, `.checkout-place-btn`, `.checkout-terms`, `.address-modal*` (overlay/header/title/close/body/item/list/footer/add-btn/form/form-wrap/actions/btn-primary/btn-secondary/btn-danger/default-tag/radio-label/radio/name-phone/update-link/street/admin) |
| 6 | `styles/productDetails.css` | **678** | Chi tiết sản phẩm & dịch vụ (dùng chung `pd-` prefix). Biến `--pd-surface`, `--pd-accent`. Class: `.pd-page`, `.pd-wrap`, `.pd-state`, `.pd-bc-bar`/`.pd-breadcrumb`/`.pd-bc-sep`/`.pd-bc-current`, `.pd-layout-card`, `.pd-two-col`, `.pd-media`/`.pd-main-img`/`.pd-thumbs-row`/`.pd-thumbs-track`/`.pd-thumb` (+`--nav`, `.is-active`), `.pd-info`/`.pd-info-head`/`.pd-stock-pill` (+`--out`)/`.pd-title`/`.pd-sku`/`.pd-price-hero`/`.pd-stats-row`/`.pd-stats-rating`/`.pd-rating-num`/`.pd-stars`/`.pd-info-block`/`.pd-info-line`/`.pd-desc-block`/`.pd-desc-body`/`.pd-meta-chips`/`.pd-meta-chip`/`.pd-purchase-card`/`.pd-qty*`/`.pd-stock-hint`/`.pd-actions-row`/`.pd-btn-cart`/`.pd-btn-buy`/`.pd-meta-footer`, `.pd-reviews`/`.pd-reviewsHead`/`.pd-ratingSummary`/`.pd-reviewsBody`/`.pd-emptyReviews`/`.pd-reviewItem`/`.pd-reviewTop`/`.pd-reviewName`/`.pd-reviewDate`/`.pd-reviewComment`, `.pd-icon-truck`/`.pd-icon-shield` |
| 7 | `styles/cart.css` | **611** | Giỏ hàng sản phẩm. Class: `.cart-page-wrapper`/`.cart-page`, `.cart-empty` (+`--icon`, `--btn`), `.cart-items-section`/`.cart-items-header`/`.cart-items-list`, `.cart-select-all`, `.cart-item` (+`.selected`, `--checkbox`, `--image`, `--no-image`, `--info`, `--name`, `--price`, `--actions`, `--remove`, `--total`, `--total-price`, `--total-note`), `.quantity-control`/`.quantity-btn`/`.quantity-value`, `.cart-summary-inline`/`.cart-summary-row`/`.cart-summary-label`/`.cart-summary-value`/`.cart-summary-total`, `.cart-checkout-btn` |
| 8 | `styles/ordersStaff.css` | **312** | Panel đơn hàng staff. Class: `.orders-staff-page`/`.orders-staff-container`, `.osp-wrap`/`.osp-page-head`/`.osp-page-title`/`.osp-page-sub`, `.osp-tabs`/`.osp-tab` (+`--active`, `-icon`), `.osp-accent-bar`, `.osp-empty` (+`.osp-loading`, `.osp-error`, `.osp-empty-icon`), `.osp-spinner`, `.osp-list`/`.osp-card`/`.osp-card-strip`/`.osp-card-id`/`.osp-card-date`/`.osp-card-main`/`.osp-card-product`/`.osp-thumb` (+`--placeholder`)/`.osp-card-info`/`.osp-card-name`/`.osp-card-more`/`.osp-card-method`/`.osp-meta-icon`/`.osp-card-right`/`.osp-card-amount`/`.osp-card-status`/`.osp-btn-confirm`/`.osp-btn-icon`/`.osp-card-address`/`.osp-addr-icon`, `.orders-staff-confirm`/`.orders-staff-cancel-btn`/`.orders-staff-edit-btn`/`.orders-staff-empty` (+`-error`)/`.orders-staff-meta-label` |
| 9 | `styles/codConfirmation.css` | **277** | Trang xác nhận COD. Class: `.cod-confirmation-page`/`.cod-confirmation-content`, `.success-banner`/`.success-icon`/`.success-title`/`.success-subtitle`, `.order-info-card`/`.order-info-title`/`.order-info-grid`/`.order-info-item`/`.order-info-label`/`.order-info-value`/`.order-total`, `.success-actions`/`.btn-primary`/`.btn-secondary`, `.suggested-products`/`.suggested-title`/`.suggested-grid`/`.suggested-product-card`/`.suggested-product-image`/`.suggested-product-info`/`.product-name`/`.product-price-row`/`.product-price`/`.cod-add-to-cart-btn`, `.loading`/`.no-products` |
| 10 | `styles/searchProducts.css` | **267** | Trang tìm kiếm. Class: `.search-products-page`/`.search-products-main`/`.search-products-head`/`.search-products-title`/`.search-keyword`/`.search-products-sub`/`.clear-keyword-btn`/`.search-filter-note`, `.search-products-layout`/`.search-filter-panel`/`.filter-header`/`.filter-chips`/`.filter-chip`, `.search-products-content`, `.search-state` (+`.error`), `.search-sort-toolbar`/`.search-sort-tabs`/`.search-sort-tab`/`.search-sort-price`/`.search-sort-price-label`/`.search-sort-select`/`.search-sort-meta`/`.search-result-count`, `.search-toolbar-pager*`, `.search-products-grid`, `.search-pagination`/`.search-pagination-arrow`/`.search-pagination-pages`/`.search-pagination-num`/`.search-pagination-ellipsis` |
| 11 | `styles/cartDrawer.css` | **273** | Cart drawer. Class: `.cart-drawer-root`/`.cart-drawer-backdrop`/`.cart-drawer`/`.cart-drawer__header`/`.cart-drawer__title`/`.cart-drawer__close` (+`--label`, `--icon`)/`.cart-drawer__body`/`.cart-drawer__empty` (+`--title`, `--hint`)/`.cart-drawer__list`/`.cart-drawer__item`/`.cart-drawer__thumb` (+`--placeholder`)/`.cart-drawer__item-main`/`.cart-drawer__item-name`/`.cart-drawer__item-meta`/`.cart-drawer__remove`/`.cart-drawer__footer`/`.cart-drawer__subtotal`/`.cart-drawer__actions`/`.cart-drawer__btn` (+`--secondary`, `--primary`) |
| 12 | `styles/auth.css` | **186** | Layout đăng nhập/đăng ký/quên mật khẩu. Class: `.auth-page`, `.brand-logo--auth`/`.logo-home`/`.brand-logo__img`/`.brand-logo__text`, `.auth-left`/`.auth-right`, `.auth-error`/`.auth-success`, `.forgot`, `.google-login-btn` |
| 13 | `styles/pageContextBar.css` | **113** | Thanh ngữ cảnh. Class: `.page-context-bar`/`.page-context-bar__inner`/`.brand-logo--compact`/`.page-context-bar__logo`/`.page-context-bar__sep`/`.page-context-bar__title` |
| 14 | `styles/createProduct.css` | **158** | Form tạo sản phẩm. Class: `.create-product-page`/`.create-product-card`/`.create-product-body`/`.create-product-head`/`.create-product-back-btn`/`.create-product-notice` (+`--success`, `--error`, `-icon`)/`.create-product-error`/`.create-product-form`/`.create-field` (+`--full`)/`.create-label`/`.create-input`/`.create-textarea`/`.create-actions`/`.create-submit-btn` |
| — | `src/index.css` | 66 | CSS gốc Vite (biến `:root`, reset `#root`, `html`, `body`, `a`, `button`). **Còn màu nền tối mặc định `#242424`** — dễ gây xung đột với theme sáng của app |
| — | `src/App.css` | — | Import ở `App.jsx`, không có rule đáng kể được dùng |

---

## 5. IMAGES / ASSETS

**Tổng: 144 file trong `src/images/` + 2 file trong `public/` = 146 asset ảnh, cộng 2 file video.**

### 5.1 `public/` (2 file)
| Đường dẫn | Dùng cho |
|---|---|
| `public/logo-pet-toy.png` | Logo thương hiệu (static) |
| `public/ziggy-hero-reference.png` | Ảnh tham chiếu thiết kế hero |

### 5.2 `src/images/` — theo nhóm (144 file)

| Nhóm | Đường dẫn | Dùng cho |
|---|---|---|
| Dịch vụ tắm (3) | `bath1.png`, `bath2.jpg`, `bath3.jpg` | `ServicePage`, `ServiceDetailPage`, `SpaCartPage`, `SpaBookingHistoryPage`, `SpaBookingHistoryDetailPage` |
| Dịch vụ cắt tỉa (3) | `cut1.jpg`, `cut2.jpg`, `cut3.jpg` | Như trên |
| Dịch vụ spa cao cấp (3) | `spa-caocap1.jpg`, `spa-caocap2.jpg`, `spa-caocap3.jpg` | Như trên |
| Dịch vụ răng miệng (3) | `teeth1.jpg`, `teeth2.jpg`, `teeth3.jpg` | Như trên |
| Placeholder (1) | `placeholder.jpg` | Ảnh dự phòng dịch vụ |
| Phòng mèo (6) | `room-cat-normal1.jpg`, `room-cat-normal2.jpg`, `room-cat-normal3.jpg`, `room-cat-vip1.jpg`, `room-cat-vip2.jpg`, `room-cat-vip3.jpg` | Hạ tầng phòng |
| Phòng chó (6) | `room-dog-large1.jpg`, `room-dog-large2.jpg`, `room-dog-large3.jpg`, `room-dog-small1.jpg`, `room-dog-small2.jpg`, `room-dog-small3.jpg` | Hạ tầng phòng |
| Slider (4) | `slide_1.jpg`, `slide_2.jpg`, `slide_3.jpg`, `slide_4.jpg` | Slide trang chủ |
| Tin tức pet (6) | `petnews1.jpg` … `petnews6.jpg` | Mục tin tức |
| Đánh giá (6) | `meovat1.jpg` … `meovat6.jpg` | Ảnh đánh giá khách hàng |
| Thành viên (2) | `member1.jpg`, `member3.jpg` | Ảnh thành viên/nhân viên |
| Đồ chơi (96) | `toy_1.jpg` … `toy_35.jpg` (35 file), `toy_156.jpg` … `toy_216.jpg` (61 file) | Ảnh sản phẩm đồ chơi (96 file) |
| Khác (6) | `gt.jpg`, `gt.png`, `nember_9.png`, `placeholder.jpg`, `video.mp4`, `video.MOV` | `placeholder.jpg` = ảnh dự phòng (dùng bởi `SpaBookingDetailPage`, `ServicePage`); `gt.*`/`nember_9.png` không thấy import (kế hoạch blog/media); 2 file video |

> **Tổng `src/images/`: 144 file** = 139 `.jpg` + 3 `.png` (`gt.png`, `nember_9.png`, `bath1.png`) + 1 `.mp4` + 1 `.MOV`.

### 5.3 Ảnh từ xa (không nằm trong repo)
| Nguồn | Dùng cho |
|---|---|
| `https://images.unsplash.com/photo-1583511655857-d19b40a7a54e` | Logo thương hiệu (TopBar, Header, Footer, PageContextBar, Login/Register/ForgotPassword) |
| `https://images.unsplash.com/photo-1561948955-570b270e7c36` | Fallback ảnh sản phẩm |
| `https://images.unsplash.com/photo-1517849845537-4d257902454a` | Fallback ảnh dịch vụ |
| `https://images.unsplash.com/photo-1548199973-03cce0bbc87b` (và 5 ảnh danh mục khác) | `FeaturedCategories` |
| `https://img.freepik.com/free-photo/adorable-white-little-puppy...` | Hero slide 1 |
| `https://img.freepik.com/premium-photo/dog-wearing-sunglasses...` | Hero slide 2 |
| `https://developers.google.com/identity/images/g-logo.png` | Nút đăng nhập Google |

> ⚠️ **Tài nguyên thiếu**: `ServicePage.jsx` tham chiếu `"/default-service-placeholder.png"` (đường dẫn public) nhưng file này **không tồn tại** trong `public/`.

---

## 6. `src/utils/userProfileStore.js` — CƠ CHẾ LƯU TRẠNG THÁI USER

| Hàm export | Mô tả |
|---|---|
| `getUserKey(authUser)` | Tạo khóa duy nhất cho user: ưu tiên `userId` → `UserId` → `email`/`Email`; chuẩn hóa về lowercase. Trả `null` nếu không xác định được |
| `loadUserProfile(authUser)` | Đọc `localStorage["userProfile:" + key]`, parse JSON, trả `null` nếu lỗi/không có |
| `saveUserProfile(authUser, patch)` | Merge `patch` vào profile hiện tại, thêm `updatedAt: ISO string`, ghi lại `localStorage`, trả object mới |
| `hydrateAuthUserFromProfile(authUser)` | Trộn profile cục bộ vào `authUser`, ghi đè các field: `avatarUrl`, `fullName` (→ `name`/`Name`/`fullName`/`FullName`), `email`/`Email`, `phone`/`Phone`/`phoneNumber`/`PhoneNumber`, `address`/`Address`, `birthDate`/`BirthDate` |

**Cơ chế:**
- **Prefix key**: `userProfile:` + userKey (email hoặc userId, lowercase) → mỗi user một namespace riêng trong `localStorage`.
- **Nguồn sự thật**: `authUser` trong `localStorage["authUser"]` là chính; `userProfile:*` là **lớp overlay cục bộ** giữ các field backend chưa lưu (đặc biệt `avatarUrl` — vì backend OJT chưa hỗ trợ upload avatar).
- **Dual naming convention**: mọi field được ghi cả camelCase và PascalCase để tương thích backend .NET (`name`/`Name`, `fullName`/`FullName`, `phone`/`Phone`...).
- **Avatar**: lưu dạng **data URL** (JPEG quality 0.92, canvas 320×320 crop tròn) trong cả `authUser.avatarUrl` lẫn `userProfile:*`.
- **Tính bất biến**: `saveUserProfile` KHÔNG ghi đè `authUser`; việc đồng bộ `authUser` được làm thủ công bởi `ProfilePage.jsx` / `ProfileEditPage.jsx` (gọi `localStorage.setItem("authUser", ...)` rồi `saveUserProfile(...)`).

**Toàn bộ key `localStorage` dùng trong app:**

| Key | Nội dung | Nơi dùng |
|---|---|---|
| `authUser` | JSON user đã đăng nhập `{ name, role, userId, email, avatarUrl, ... }` | Hầu hết pages + `App.jsx` guards |
| `userProfile:{key}` | Profile overlay (xem trên) | `ProfilePage`, `ProfileEditPage` |
| `cart` | Giỏ hàng sản phẩm `[{ id, name, price, image, quantity }]` | HomePage, ProductsPage, ProductDetailPage, CartPage, CheckoutPage, CartDrawer, CodConfirmationPage, Header |
| `checkoutItems` | Items được chọn để checkout | CartPage, ProductDetailPage, ProductCard, CheckoutPage |
| `lastCodOrder` | Thông tin đơn COD vừa đặt | CodConfirmationPage |
| `savedDeliveryAddresses` | Danh sách địa chỉ giao hàng đã lưu | CheckoutPage, AddressModal |
| `spaCart` | Mảng `serviceId` dịch vụ trong giỏ spa | ServicePage, ServiceDetailPage, SpaCartPage, Header (badge) |
| `spaBookingHistory` | Lịch sử booking spa (cache local) | SpaBookingHistoryPage, SpaBookingHistoryDetailPage, SpaInvoicePage, SpaCartPage |
| `systemPageTab` | Tab đang mở của trang `/system` (**sessionStorage**) | SystemPage |
| `token` | JWT cho chatbot (**chưa được set ở đâu trong code**) | `api/chatbotApi.js` interceptor |

---

## 7. PATTERN UI LẶP LẠI (cần biết khi migrate)

### 7.1 Layout chia sẻ
| Pattern | Mô tả | Số page dùng |
|---|---|---|
| **Full storefront** | `TopBar` → `Header` → `Navigation` → `<main>` → `Footer` | 10 page (Home, Products, ProductDetail, Service, ServiceDetail, CreateProduct, Profile, ProfileEdit, PetInfo, About) |
| **Storefront + CartDrawer** | Full storefront + `<CartDrawer />` | 4 page (Home, ProductDetail, Search, ServiceDetail) — `Search` dùng `TopBar`→`Header`→`Footer` (**không** có `Navigation`) |
| **Context bar** | `TopBar` → `PageContextBar(title)` → `<main>` → `Footer` (bỏ `Header` + `Navigation`) | 11 page (Cart, Checkout, CodConfirmation, OrderHistory×2, System, SpaCart, SpaBookingHistory×2, SpaInvoice, + SpaBookingDetail orphan) |
| **Auth 2 cột** | `.auth-page` = logo + `.auth-left` (pitch) + `.auth-right` (form) | 3 page (Login, Register, ForgotPassword) — **không** có TopBar/Footer |
| **Staff minimal** | `TopBar` → wrapper → `Footer` | 1 page (OrdersStaff) |
| **Shared layout component** | ❌ **KHÔNG có** — mọi page tự import và bố trí `TopBar`/`Header`/`Footer` thủ công. Đây là điểm cần cải thiện khi migrate sang PRN232 (nên tạo `<StorefrontLayout>` / `<ContextLayout>`) |

### 7.2 Component lặp lại
| Pattern | Mô tả | Số lần dùng |
|---|---|---|
| **Header** | Logo + search autocomplete + 2 badge giỏ (sản phẩm + spa) + tổng tiền | 11 page |
| **TopBar** | Liên hệ + khuyến mãi + menu user theo role + notification bell | 23 page |
| **Footer** | 4 cột + newsletter + social | 23 page |
| **Navigation** | 6 mục menu | 10 page |
| **PageContextBar** | Logo + tiêu đề page | 11 page |
| **CartDrawer** | Drawer trượt bên phải (overlay, ESC, focus) | 4 page |
| **Toast** | Notification đơn giản 3s (chỉ 1 chỗ dùng thật) | 1 page (Home) |
| **NotificationBell** | Chuông thông báo staff + panel dropdown | 1 nơi (TopBar, role staff/admin) |
| **AvatarCropper** | Modal crop tròn kéo + zoom (canvas 320×320) | 2 page (Profile, ProfileEdit) — **code nhân bản ~100 dòng** |
| **Carousel ảnh** | Auto-play 3s / 6s, prev/next + dots | 3 biến thể: `HeroBanner` (2 slide, 6s), `ServiceImageCarousel` (trong ServicePage), `ServiceDetailCarousel` (trong ServiceDetailPage) — **2 bản gần như trùng nhau** |
| **Star rating** | Render 5 sao `★` với class `.on` | 4 page (ProductDetail, ServiceDetail, OrderHistoryDetail, Home) |
| **Form field + label + error** | `<label class="...">` + `<input class="...">` + `<span class="error-message">` | Hầu hết form |
| **Confirm dialog** | `window.confirm(...)` | Delete pet, delete breed pricing, decline/cancel shift request |
| **Alert thông báo** | `window.alert(...)` | ~15 chỗ (thêm giỏ, thanh toán, xóa, đổi ca) |
| **Empty state** | Icon + message + CTA link (`Mua sắm ngay` / `Xem dịch vụ spa`) | 8 page |
| **Loading state** | 3 biến thể: text đơn giản, skeleton grid (10 card), spinner | Xem bảng route |
| **Error state** | Text trong div có class `*-error` hoặc `notif-empty--error` | ~10 page |
| **Tab filter (Shopee-style)** | Pill tabs + đếm số + accent bar | 3 page (OrderHistory, SpaBookingHistory, OrdersStaffPanel) |
| **Card list kiểu Shopee** | Shop badge + status + thumb + tên + giá + tổng | 3 page (OrderHistory, OrderHistoryDetail, SpaBookingHistory) |
| **Modal form** | Backdrop overlay + card + header/footer actions | 4 (AddressModal, breed pricing modal, service add modal, slot suggestion modal, avatar cropper) |
| **Form state machine 2–3 bước** | `step` state + `setStep()` | 2 (RegisterPage, ForgotPasswordPage) |
| **OTP flow** | Gửi → verify → resend với countdown 120s | 2 page (Register, ForgotPassword) + Login có code chưa dùng |
| **Spa service thumbnail fallback** | `getSpaServiceThumb(name)` map tên → ảnh | **3 bản copy gần như giống hệt** (SpaCartPage, SpaBookingHistoryPage, SpaBookingHistoryDetailPage) |
| **Cart count sync** | Đọc `localStorage.cart`, nghe event `storage` + `focus` | 6 page (Home, Products, ProductDetail, Search, Service, ServiceDetail) |
| **Spa cart count sync** | Nghe custom event `spaCartUpdated` | 3 page (Service, ServiceDetail, Header) |
| **`normalizeVietnamese`** | Bỏ dấu + lowercase để so khớp trạng thái tiếng Việt | 4 page (OrderHistory, OrderHistoryDetail, SpaBookingHistory, System) |
| **Status → color mapping** | `{ keywords, label, color }` cho đơn hàng & booking spa | 2 page (SystemPage, OrdersStaffPanel) |

### 7.3 Vấn đề kỹ thuật phát hiện (cần xử lý khi migrate)
1. **Route trùng lặp** trong `App.jsx`: `/cart`, `/cart/checkout`, `/checkout/cod-success`, `/account`, `/account/pets` khai báo 2 lần (public + guarded). `/products`, `/products/:id`, `/` cũng vậy.
2. **Route không được guard**: `/search`, `/profile`, `/profile/edit`, `/account/orders`, `/account/orders/:orderId`, `/orders`, `/system`, `/products/create`.
3. **`breed-pricing` tab không truy cập được**: `SystemPage` có nhánh `activeMenu === 'breed-pricing'` nhưng `MENU_ITEMS` không chứa key này (chỉ 5 item).
4. **`/products/deleted` không có route** nhưng `ProductsPage` nút "Những sản phẩm đã xóa" điều hướng tới → fallback về `/`.
5. **`/contact`, `/blog`, `/categories`, `/promotions`, `/policy`, `/terms`, `/shipping`, `/return`, `/payment`, `/warranty`, `/category/:name`** được link trong `Navigation`/`Footer`/`FeaturedCategories` nhưng **không có route** → fallback về `/`.
6. **`AccountPage.jsx`** là file re-export thừa, không được import.
7. **`productsApi_temp.js`** tồn tại song song với `productsApi.js` — cả hai gọi cùng endpoint `/api/products-temp`.
8. **`chatbotApi.js`** dùng `axios` + URL tuyệt đối `https://localhost:7010/api` — không dùng được khi deploy thật.
9. **Code trùng lặp lớn cần refactor**: `getSpaServiceThumb` (3 bản), `AvatarCropper` (2 bản ~100 dòng), `refreshCartCount` (6 bản), `renderStars` (4 bản), `normalizeVietnamese` (4 bản), `pickValue` (3 bản).
10. **Dữ liệu localStorage không có nguồn API thống nhất** — nhiều page đọc trực tiếp `localStorage` thay vì qua store/hook.
11. **`src/index.css`** còn nền tối mặc định của Vite (`#242424`) — dễ gây lỗi hiển thị.
12. **Thiếu file** `public/default-service-placeholder.png` được tham chiếu trong `ServicePage.jsx`.
13. **`createPetService` dùng PascalCase** (`Name`, `Description`, `Duration`, `Price`, `ImageUrl`) trong khi các API khác dùng camelCase — dễ gây lỗi khi đổi backend.
14. **Header có 2 loại giỏ** dùng chung 1 icon set: `FaHeart` cho giỏ spa (lạ) và `FaShoppingCart` cho giỏ hàng.

---

## 8. DANH SÁCH FILE ĐẦY ĐỦ (74 file trong `src/`)

```
src/
├── main.jsx                                  (entry, StrictMode + createRoot)
├── App.jsx                                   (router + 2 route guards + ScrollToTop)
├── index.css                                 (66 dòng — Vite default, còn nền tối)
├── App.css
├── api/            (10 file — 70 fn)
│   ├── authApi.js            (6 fn)
│   ├── productsApi.js        (14 fn)
│   ├── productsApi_temp.js   (2 fn)
│   ├── petsApi.js            (7 fn)
│   ├── petServicesApi.js     (3 fn)
│   ├── spaBookingApi.js      (9 fn)
│   ├── notificationApi.js     (10 fn)
│   ├── breedPricingApi.js    (6 fn)
│   ├── attendanceApi.js      (9 fn)
│   └── chatbotApi.js         (4 fn, axios)
├── components/      (18 file — 17 component + 1 data module)
│   ├── home/         TopBar, Header, Navigation, HeroBanner, FeaturedCategories,
│   │                 ProductList_temp, ProductCard, CustomerReviews, Footer,
│   │                 Toast, CartDrawer, PageContextBar
│   ├── products/     ProductsSidebar, ClearSearchButton
│   ├── staff/        OrdersStaffPanel, NotificationBell
│   └── checkout/     AddressModal, daNangData.js
├── pages/           (27 file — 26 page + 1 re-export)
│   ├── HomePage, ProductsPage, SearchProductsPage, ProductDetailPage,
│   │   CreateProductPage, CartPage, CheckoutPage, CodConfirmationPage,
│   │   OrderHistoryPage, OrderHistoryDetailPage, AccountPage (re-export),
│   │   LoginPage, RegisterPage, ForgotPasswordPage, AboutPage,
│   │   ProfilePage, ProfileEditPage, PetInfoPage,
│   │   ServicePage, ServiceDetailPage, SpaCartPage,
│   │   SpaBookingHistoryPage, SpaBookingHistoryDetailPage, SpaInvoicePage,
│   │   SpaBookingDetailPage (ORPHAN — không có route),
│   │   OrdersStaffPage, SystemPage
├── styles/          (14 file — 12.551 dòng)
│   ├── home.css (2788), service.css (3681), systemPage.css (1426),
│   │   profile.css (917), checkout.css (864), productDetails.css (678),
│   │   cart.css (611), ordersStaff.css (312), codConfirmation.css (277),
│   │   cartDrawer.css (273), searchProducts.css (267), auth.css (186),
│   │   createProduct.css (158), pageContextBar.css (113)
├── utils/
│   └── userProfileStore.js   (4 fn)
└── images/          (144 file)
```

**Root:** `public/` (2 file), `index.html`, `vite.config.js`, `eslint.config.js`, `package.json`, `package-lock.json`, `README.md`, `.gitignore`

**Dependencies:** `react@19.2`, `react-dom@19.2`, `react-router-dom@7.13`, `react-icons@5.6`, `chart.js@4.5`, `react-chartjs-2@5.3` | Dev: `vite@7.3`, `@vitejs/plugin-react@5.1`, `eslint@9.39`

---

*Tài liệu được tạo bằng cách đọc trực tiếp toàn bộ mã nguồn. Nguồn KHÔNG bị chỉnh sửa.*
