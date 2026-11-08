# TARGET_EXISTING — Inventory code PRN232 đã có sẵn

> **Mục đích:** Lập kiểm kê toàn bộ code đã tồn tại trong project đích `PRN232_PetCareProject` để **tái sử dụng**, tránh viết trùng.
> **Phạm vi khảo sát:** Controllers, Services, Repositories, Interfaces, Models, Data, Components (Pages/Layout/Shared/Home), tests, wwwroot, Program.cs.
> **Nguồn đối chiếu:** `../Projectojt/` (gồm `BackEnd/` .NET 8 MVC API + `frontend/` React Vite).
> **Ngày khảo sát:** 08/11/2026.
> **Lưu ý:** Tài liệu này **chỉ đọc**, không sửa file nào trong project đích.

---

## 0. Tóm tắt nhanh

| Hạng mục | Số lượng | Ghi chú |
|---|---|---|
| Controller | 22 file (21 controller + 1 partial split) | Phủ 100% nghiệp vụ cốt lõi |
| Service | 32 file (gồm 6 partial split) | Business logic + self-HTTP client |
| Repository | 7 file | Truy vấn EF trực tiếp |
| Interface | 30 file | Repository / Service / Client abstraction |
| Model | 52 file | 30 entity + 22 DTO/record |
| Razor Component | 68 file | Pages 34, Shared 16, Layout 3, Home 4, Products 1, root 2 |
| DbSet | 39 | 27 entity bảng gốc + 11 view (`v_*`/`vw_*`) + 1 keyless |
| Unit test | 4 file / 30 test | `tests/PetShop.Tests` (xUnit) |
| DI registration | 45+ | Trong `Program.cs` |

**Kết luận nhanh:** Đích đã là bản **Blazor Server hoàn chỉnh** của hệ thống OJT. Những gì còn thiếu so với nguồn gần như **chỉ là tính năng Chatbot AI** (toàn bộ stack AI provider) — xem §7.

---

## 1. Entity → DbSet → Key

**Nguồn:** `Data/ShopPetDatabaseContext.cs` (39 `DbSet`, `OnModelCreating` dùng `HasKey(...).HasName("PK__...")` — schema scaffold từ DB có sẵn).

### 1.1 Entity bảng gốc (27)

| Entity | DbSet | Key | Bảng DB |
|---|---|---|---|
| `Admin` | `Admins` | `AdminId` | `admin` |
| `AttendanceRecord` | `AttendanceRecords` | `AttendanceId` | `AttendanceRecords` |
| `BoardingBooking` | `BoardingBookings` | `BookingId` | `boarding_bookings` |
| `BoardingRoom` | `BoardingRooms` | `RoomId` | `BoardingRooms` |
| `Booking` | `Bookings` | `BookingId` | `Booking` |
| `BookingService` | `BookingServices` | composite (`BookingId`,`ServiceId`) | `BookingService` |
| `Breed` | `Breeds` | `BreedId` | `Breed` |
| `BreedPricing` | `BreedPricings` | `BreedPricingId` | `BreedPricing` |
| `ChatMessage` | `ChatMessages` | `MessageId` | `ChatMessages` |
| `Customer` | `Customers` | `CustomerId` | `Customer` |
| `Doctor` | `Doctors` | `DoctorId` | `Doctor` |
| `MedicalRecord` | `MedicalRecords` | `RecordId` | `MedicalRecords` |
| `Order` | `Orders` | `OrderId` | `Order` |
| `OrderDetail` | `OrderDetails` | `DetailId` | `Order_Details` |
| `Payment` | `Payments` | `PaymentId` | `Payments` |
| `PayrollRecord` | `PayrollRecords` | `PayrollId` | `PayrollRecords` |
| `Pet` | `Pets` | `Id` | `Pet` |
| `PetService` | `PetServices` | `ServiceId` | `PetService` |
| `Product` | `Products` | `ProductId` | `Product` |
| `ProductCategory` | `ProductCategories` | `CategoryId` | `ProductCategory` |
| `Review` | `Reviews` | `ReviewId` | `Review` |
| `Shift` | `Shifts` | `ShiftId` | `Shifts` |
| `ShiftRequest` | `ShiftRequests` | `RequestId` | `ShiftRequests` |
| `Species` | `Species` | `SpeciesId` | `Species` |
| `Staff` | `Staff` | `StaffId` | `Staff` |
| `StaffSalary` | `StaffSalaries` | `SalaryId` | `StaffSalaries` |
| `Supplier` | `Suppliers` | `SupplierId` | `Supplier` |
| `WorkSchedule` | `WorkSchedules` | `ScheduleId` | `WorkSchedule` |

### 1.2 Entity view (11) — `ToView(...)`, đọc-only

| Entity | DbSet | View DB | Key (model) |
|---|---|---|---|
| `VBooking` | `VBookings` | `v_Booking` | `BookingId` |
| `VCart` | `VCarts` | `v_Cart` | `ItemId` |
| `VOrderItemCompact` | `VOrderItemCompacts` | `v_OrderItem_Compact` | `ItemId` |
| `VOrderItemProduct` | `VOrderItemProducts` | `v_OrderItem_Product` | `ItemId` |
| `VPayment` | `VPayments` | `v_Payments` | `PaymentId` |
| `VProductReview` | `VProductReviews` | `v_ProductReview` | `ReviewId` |
| `VSalesOrder` | `VSalesOrders` | `v_SalesOrder` | `OrderId` |
| `VServiceReview` | `VServiceReviews` | `v_ServiceReview` | `ReviewId` |
| `VwBoardingAvailabilityStat` | `VwBoardingAvailabilityStats` | `vw_BoardingAvailabilityStats` | keyless |
| `VwBookingStatusCount` | `VwBookingStatusCounts` | `vw_BookingStatusCount` | keyless |
| `VwPetWithBreedPricing` | `VwPetWithBreedPricings` | `vw_PetWithBreedPricing` | `Id` |

### 1.3 Entity không có DbSet

`SystemSetting` (bảng `SystemSetting`) — có model nhưng **không** khai báo `DbSet`; không service nào dùng.

### 1.4 Mapping quan trọng trong `OnModelCreating`

- `BookingService` — composite key `(BookingId, ServiceId)`, **không** có `BookingServiceId` thật trong DB (model có property convenience).
- `Pet` — key là `Id` (không phải `PetId`) — đây là lý do OData phải khai báo key tường minh.
- `Product` — key `ProductId`; `PetService` — `ServiceId`; `ProductCategory` — `CategoryId`; `Supplier` — `SupplierId`; `Admin` — `AdminId`; `Breed` — `BreedId`; `Species` — `SpeciesId`.
- Có `HasConstraintName(...)` cho quan hệ chính: `fk_orderdetail_product`, …
- Bảng view được map bằng `ToView("...")` — không có FK thật, chỉ property navigation để join.

---

## 2. Controller → Route → Chức năng

Tất cả controller dùng `[ApiController]` (trừ `CartController` kế thừa `Controller` cho form action).

### 2.1 Auth — `Controllers/AuthController.cs` — route `api/auth`

| Method | Route | Chức năng |
|---|---|---|
| POST | `api/auth/register` | Đăng ký tài khoản mới (customer) |
| POST | `api/auth/login` | Đăng nhập (email + password) → `AuthResponseDto` |
| POST | `api/auth/google-login` | Đăng nhập Google OAuth (GoogleLoginRequestDto) |
| POST | `api/auth/send-email-otp` | Gửi OTP qua email |
| POST | `api/auth/verify-email-otp` | Xác thực OTP |
| POST | `api/auth/reset-password` | Reset mật khẩu bằng OTP |
| GET | `api/auth/google-client-id` | Lấy Google Client ID cho frontend |
| POST | `api/auth/token` `[AllowAnonymous]` | **Phát JWT** (`TokenResponseDto`) — slide Ch06 |
| GET | `api/auth/me` `[Authorize]` | **Lấy user hiện tại** từ Bearer/session — endpoint demo JWT duy nhất |

### 2.2 Product / Catalog

| Controller | Route | Method | Chức năng |
|---|---|---|---|
| `ProductsController` | `api/products` | GET | Danh sách sản phẩm (`?keyword`, `?categoryId`) |
| | `api/products/categories` | GET | Danh sách category filter |
| | `api/products/{id:int}` | GET | Chi tiết 1 sản phẩm |
| `PetShopODataController` | `PetShop/Products` | GET | OData query set (prefix `PetShop`) |
| | `PetShop/Products({key})` | GET | OData single entity (`SingleResult.Create`) |
| | `PetShop/Products/$count` | GET | OData count (path segment thủ công) |
| | `PetShop/PetServices`, `PetShop/PetServices({key})`, `PetShop/PetServices/$count` | GET | OData PetService |
| | `PetShop/Pets`, `PetShop/Pets({key})`, `PetShop/Pets/$count` | GET | OData Pet |
| `PetServicesController` | `api/pet-services` | GET | Danh sách dịch vụ spa |
| | `api/pet-services/grouped` | GET | Dịch vụ nhóm theo category |
| | `api/pet-services/{serviceId:int}` | GET | Chi tiết dịch vụ |

### 2.3 Cart / Checkout / Orders

| Controller | Route | Method | Chức năng |
|---|---|---|---|
| `CartController` (MVC form) | `/cart/add/{id:int}` | POST | Thêm sản phẩm vào giỏ |
| | `/cart/dec/{id:int}` | POST | Giảm số lượng |
| | `/cart/remove/{id:int}` | POST | Xoá sản phẩm khỏi giỏ |
| `CheckoutController` | `api/checkout` | POST | Tạo đơn hàng (`CheckoutRequest` → `CheckoutResponse`: PaymentMethod, RedirectUrl, OrderCode, OrderId) |
| `OrdersController` | `api/orders/history` | GET | Lịch sử đơn của customer |
| | `api/orders/{orderId:int}` | GET | Chi tiết đơn |
| | `api/orders/staff/pending` | GET | Đơn chờ xử lý (staff) |
| | `api/orders/staff` | GET | Đơn theo status (staff) |
| | `api/orders/staff/{orderId:int}/confirm` | PATCH | Staff xác nhận đơn |
| | `api/orders/{orderId:int}/delivered` | PATCH | Customer xác nhận đã nhận |

### 2.4 Spa Booking

| Controller | Route | Method | Chức năng |
|---|---|---|---|
| `SpaBookingController` | `api/spa-booking/pets` | GET | Danh sách pet của customer |
| | `api/spa-booking/services` | GET | Dịch vụ spa đang active |
| | `api/spa-booking/availability` | POST | Check slot còn trống |
| | `api/spa-booking/validate-slot` | POST | Validate slot |
| | `api/spa-booking/estimate` | POST | Ước tính giá |
| | `api/spa-booking` | POST | Tạo booking spa |
| | `api/spa-booking/history` | GET | Lịch sử của customer |
| | `api/spa-booking/history/all` + `api/spa-booking/all-bookings` | GET | Toàn bộ booking (staff) |
| | `api/spa-booking/{bookingId:int}/invoice` | GET | Hoá đơn spa |
| | `api/spa-booking/{bookingId:int}/status` | PATCH | Cập nhật trạng thái |
| | `api/spa-booking/review` | POST | Đánh giá dịch vụ |

### 2.5 Pets / Breed Pricing

| Controller | Route | Method | Chức năng |
|---|---|---|---|
| `PetsController` | `api/pets/customer/{customerId:int}` | GET | Pet theo customer (kèm giá theo giống) |
| | `api/pets/customer/{customerId:int}/manage` | GET | Pet dạng quản lý |
| | `api/pets/customer/{customerId:int}/service/{serviceId:int}/pricing` | GET | Giá dịch vụ theo giống pet |
| | `api/pets/meta/species` | GET | Danh sách loài |
| | `api/pets/meta/breeds` | GET | Danh sách giống (`?speciesId`) |
| | `api/pets` | POST | Tạo pet |
| | `api/pets/{petId:int}` | PUT | Sửa pet |
| | `api/pets/{petId:int}` | DELETE | Xoá pet (`?customerId`) |
| | `api/pets/upload-image` | POST | Upload ảnh pet (`[FromForm] IFormFile`) |
| `BreedPricingController` | `api/breed-pricing` | GET | Toàn bộ bảng giá theo giống |
| | `api/breed-pricing/breed/{breedId:int}` | GET | Giá 1 giống |
| | `api/breed-pricing` | POST | Tạo giá |
| | `api/breed-pricing/{id:int}` | PUT / DELETE | Sửa / xoá giá |
| | `api/breed-pricing/seed-all` | POST | Seed giá cho tất cả giống |

### 2.6 Review / Notification / Config

| Controller | Route | Method | Chức năng |
|---|---|---|---|
| `ReviewController` | `api/reviews/services/{serviceId:int}` | GET | Đánh giá dịch vụ |
| | `api/reviews/products/{productId:int}` | GET | Đánh giá sản phẩm |
| | `api/reviews/products/{productId:int}` | POST | Upsert đánh giá sản phẩm |
| `NotificationController` | `api/notifications/{staffId:int}` | GET | Danh sách thông báo |
| | `api/notifications/{staffId:int}/unread-count` | GET | Số chưa đọc |
| | `api/notifications/{requestId:int}/read` | PUT | Đánh dấu đã đọc |
| | `api/notifications/{staffId:int}/read-all` | PUT | Đánh dấu đã đọc tất cả |
| | `api/notifications/{requestId:int}` | DELETE | Xoá thông báo |
| `ConfigController` | `api/config/google-client-id` | GET | Google Client ID |

### 2.7 Nhân viên / Vận hành

| Controller | Route | Method | Chức năng |
|---|---|---|---|
| `AttendanceController` | `api/attendance/unclosed` | GET | Ca chưa checkout |
| | `api/attendance/staff/{staffId:int}/overview` | GET | Tổng quan chấm công |
| | `api/attendance/staff/{staffId:int}/toggle` | POST | Toggle check-in/out |
| `PayrollController` | `api/payroll/history` | GET | Lịch sử lương |
| | `api/payroll/staff/{staffId:int}/detail` | GET | Chi tiết lương 1 nhân viên |
| | `api/payroll/staff/{staffId:int}/generate` | GET | Sinh lương 1 NV |
| | `api/payroll/generate-all` | POST | Sinh lương toàn bộ |
| | `api/payroll/config` | GET | Cấu hình lương |
| | `api/payroll/config/staff/{staffId:int}` | GET | Cấu hình lương 1 NV |
| | `api/payroll/config/staff` | PUT | Cập nhật cấu hình lương |
| | `api/payroll/staff-list` | GET | Danh sách nhân viên |
| `ShiftExchangeController` | `api/shift-exchange/verify-network` | GET | Kiểm tra mạng nội bộ |
| | `api/shift-exchange/staff/{staffId:int}/my-shifts` | GET | Ca của tôi |
| | `api/shift-exchange/all-shifts` | GET | Tất cả ca |
| | `api/shift-exchange/staff/options` | GET | Option staff (`?excludeStaffId`) |
| | `api/shift-exchange/pending` | GET | Yêu cầu chờ duyệt |
| | `api/shift-exchange/my-requests` | GET | Yêu cầu của tôi |
| | `api/shift-exchange/pass` | POST | Xin nghỉ ca (`.Requests.cs`) |
| | `api/shift-exchange/swap` | POST | Xin đổi ca (`.Requests.cs`) |
| | `api/shift-exchange/accept/{requestId:int}` | POST | Duyệt (`.Processing.cs`) |
| | `api/shift-exchange/decline/{requestId:int}` | POST | Từ chối (`.Processing.cs`) |
| | `api/shift-exchange/cancel/{requestId:int}` | POST | Huỷ (`.Processing.cs`) |

### 2.8 Boarding / Thanh toán / Email

| Controller | Route | Method | Chức năng |
|---|---|---|---|
| `BoardingController` | `api/boarding-rooms` | GET | Danh sách phòng |
| | `api/boarding-rooms/{roomId:int}` | GET | Chi tiết phòng |
| | `api/boarding-rooms/bookings` | GET | Tất cả booking |
| | `api/boarding-rooms/bookings/{bookingId:int}/checkin` | PATCH | Check-in |
| | `api/boarding-rooms/bookings/{bookingId:int}/checkout` | PATCH | Checkout |
| | `api/boarding-rooms/availability` | GET | Phòng còn trống |
| | `api/boarding-rooms/checkout` | POST | Checkout phòng (tạo đơn) |
| `PayOSController` | `api/payos/spa/create` | POST | Tạo link thanh toán spa |
| | `api/payos/order/create` | POST | Tạo link thanh toán đơn hàng |
| | `api/payos/webhook` | POST | Webhook (`x-payos-signature`) |
| | `api/payos/status/{orderCode}` | GET | Trạng thái thanh toán |
| | `api/payos/send-invoice` | POST | Gửi hoá đơn qua email |
| `EmailTestController` | `api/test/email/spa` | POST | Test email spa |
| | `api/test/email/spa/cancelled` | POST | Test email huỷ spa |
| | `api/test/email/boarding` | POST | Test email boarding |

---

## 3. Service / Repository → Interface → Trách nhiệm

### 3.1 Repository (7) — truy vấn EF trực tiếp, luôn `Scoped`

| Implementation | Interface | Trách nhiệm (method chính) |
|---|---|---|
| `AuthRepository` | `IAuthRepository` | `GetCustomerByEmailAsync`, `GetStaffByEmailAsync`, `GetDoctorByEmailAsync`, `GetAdminByEmailOrUsernameAsync`, `CreateCustomerAsync`, `UpdateCustomerAsync`, `EmailExistsInAnyRoleAsync`, `UpgradePasswordHashAsync` (migrate-on-login) |
| `ProductRepository` | `IProductRepository` | `GetAllAsync(keyword, categoryId)`, `GetCategoriesAsync`, `GetByIdAsync` |
| `PetServiceRepository` | `IPetServiceRepository` | `GetAllAsync`, `GetDetailAsync` |
| `ReviewRepository` | `IReviewRepository` | `GetServiceReviewsAsync`, `GetProductReviewsAsync`, `UpsertProductReviewAsync` |
| `SpaBookingRepository` | `ISpaBookingRepository` | `GetPetsByCustomerIdAsync`, `GetActiveSpaServicesAsync`, `CountSpaBookingsInTimeSlotAsync`, `CountAnySpaBookingsInTimeSlotAsync`, `CreateBookingAsync`, `AddBookingServiceAsync`, `DeleteBookingsAsync`, `GetSpaBookingsByCustomerIdAsync`, `GetAllSpaBookingsAsync`, `GetSpaBookingInvoiceAsync`, `UpdateBookingStatusAsync`, `HasCompletedBookingAsync`, `GetReviewIdAsync`, `AddReviewAsync`, `UpdateReviewAsync` |
| `BoardingRepository` | `IBoardingRepository` | `GetAllRoomsAsync`, `GetRoomByIdAsync`, `GetAllBookingsAsync`, `ConfirmCheckinAsync`, `ConfirmCheckoutAsync`, `GetAvailabilityAsync` |
| `StaffRepository` | `IStaffRepository` | `GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` |

### 3.2 Service nghiệp vụ (Scoped) — chứa logic, gọi repository

| Implementation | Interface | Trách nhiệm (method chính) |
|---|---|---|
| `AuthService` (+ `AuthService.Google.cs`) | `IAuthService` | `RegisterAsync`, `LoginAsync`, `LoginWithGoogleAsync`, `SendEmailOtpAsync`, `VerifyEmailOtpAsync`, `ResetPasswordAsync` |
| `ProductService` | `IProductService` | `GetAllAsync`, `GetCategoriesAsync`, `GetByIdAsync` (delegate → `IProductRepository`) |
| `PetServiceService` | `IPetServiceService` | `GetAllAsync`, `GetDetailAsync` |
| `SpaBookingService` (+ `SpaBookingService.Booking.cs`) | `ISpaBookingService` | `GetPetsAsync`, `GetSpaServicesAsync`, `CheckAvailabilityAsync`, `ValidateBookingSlotAsync`, `EstimateAsync`, `CreateBookingAsync`, `GetHistoryAsync`, `GetAllHistoryAsync`, `GetInvoiceAsync`, `UpdateStatusAsync`, `UpsertReviewAsync` |
| `BoardingService` | `IBoardingService` | `GetAllRoomsAsync`, `GetRoomByIdAsync`, `GetAllBookingsAsync`, `ConfirmCheckinAsync`, `ConfirmCheckoutAsync`, `GetAvailabilityAsync` |
| `AttendanceService` | `IAttendanceService` | `CalculateWorkHours` (pure), `GetOverviewAsync`, `ToggleAsync`, `GetAttendanceWithScheduleAsync`, `HasAttendanceForDateAsync`, `GetUnclosedAsync` |
| `PayrollService` | `IPayrollService` | `GeneratePayrollForStaffAsync`, `GenerateAllPayrollAsync`, `GetPayrollHistoryAsync`, `GetStaffPayrollDetailAsync`, `GetAllSalaryConfigsAsync`, `UpdateSalaryConfigAsync`, `GetSalaryConfigAsync` |
| `StaffService` | `IStaffService` | `GetAllStaffsAsync`, `GetStaffByIdAsync`, `CreateStaffAsync`, `UpdateStaffAsync`, `DeleteStaffAsync` |
| `StaffDashboardService` | `IStaffDashboardService` | `GetDashboardSummaryAsync`, `GetStaffBookingsAsync`, `UpdateBookingStatusAsync`, `GetStaffSchedulesAsync`, `CheckInAsync` |
| `StaffProfileService` | `IStaffProfileService` | `GetProfileAsync(userId, email)`, `ResolveStaffIdAsync`, `UpdateProfileAsync` |
| `EmailService` | `IEmailService` | `SendOtpEmailAsync`, `SendGoogleFirstLoginEmailAsync`, `SendOrderInvoiceEmailAsync`, `SendSpaInvoiceEmailAsync`, `SendBoardingInvoiceEmailAsync` (MailKit + Handlebars) |
| `PayOSService` (+ `PayOSService.Fulfillment.cs`) | `IPayOSService` | `CreatePaymentLinkAsync`, `VerifyWebhook(rawData, signature)`, `GetPaymentStatusAsync`, `HandleWebhookAsync`, `HandleWebhookForStatusAsync`, `NormalizeDescription` |
| `CartStateService` | *(không có interface)* | `AttachRuntime`, `LoadAsync`, `AddAsync`, `UpdateQuantityAsync`, `RemoveAsync`, `ClearAsync` (state giỏ hàng phía client) |
| `UserSessionService` | *(không có interface)* | `CurrentUser`, `LoadAsync`, `SignInAsync`, `SignOutAsync` (session Blazor) |
| `JwtTokenService` | `IJwtTokenService` | `CreateToken(userId, name, email, role[, ...])` — HS256, kiểm tra issuer/audience/sig/lifetime |
| `WorkScheduleDedup` | *(static helper)* | `ByStaffDateTimeSlot`, `ByTimeSlot`, `ShiftOptions` — khử trùng lịch |
| `ApiExceptionMiddleware` | *(middleware)* | Chuẩn hoá mọi exception → `ProblemDetails` (RFC 7807) |
| `SessionHeaderAuthenticationHandler` | *(auth scheme)* | Scheme `Session`: đọc header `X-User-Id` / `X-User-Role` |
| `SpaAuthHeaderHandler` | *(delegating handler)* | Gắn header session vào self-HTTP request |
| `CsvOutputFormatter` | *(output formatter)* | `text/csv` cho `IReadOnlyList<T>` |
| `XmlOutputFormatter` | *(output formatter)* | `application/xml` (nối tiếp `AddPetShopXmlFormatters`) |
| `SelfHttpClientExtensions` | *(extension)* | `AddSelfHttpClient<T>()`, `AddSelfHttpClientWithAuth<T>()` — typed client trỏ về chính origin |
| `MvcBuilderExtensions` | *(extension)* | `AddPetShopXmlFormatters()` |
| `LoggerExtensions` | *(extension)* | `_logger.Line(...)`, `LineWarning`, `LineError` |

### 3.3 HTTP Client (self-HTTP — Blazor gọi chính API của app)

| Implementation | Interface | Trách nhiệm |
|---|---|---|
| `ProductCatalogClient` | `IProductCatalogClient` | `GetProductsAsync`, `GetCategoriesAsync`, `GetProductAsync` → trả `ProductCatalogResult<T>` |
| `AuthClient` | `IAuthClient` | `LoginAsync`, `RegisterAsync`, `SendOtpAsync`, `VerifyOtpAsync`, `ResetPasswordAsync`, `GoogleLoginAsync`, `GetGoogleClientIdAsync` → `FrontendApiResult<T>` |
| `CheckoutClient` | `ICheckoutClient` | `CheckoutAsync` → `FrontendCheckoutResponse` |
| `OrderClient` | `IOrderClient` | Lịch sử/chi tiết/xác nhận đơn hàng |
| `ReviewClient` | `IReviewClient` | `GetProductReviewsAsync`, `UpsertProductReviewAsync` |
| `PaymentClient` | `IPaymentClient` | Thanh toán qua self-HTTP |
| `PetServiceClient` | `IPetServiceClient` | `GetAllAsync`, `GetDetailAsync`, `GetGroupedAsync` |
| `SpaBookingClient` | `ISpaBookingClient` | `GetPetsAsync`, `GetSpaServicesAsync`, `CheckAvailabilityAsync`, `EstimateAsync`, `CreateAsync`, `GetHistoryAsync`, `GetInvoiceAsync`, `UpdateStatusAsync`, `SubmitReviewAsync`, `CreateSpaPayOSPaymentAsync`, `GetPayOSStatusAsync` |
| `PetsClient` | `IPetsClient` | `GetByCustomerAsync`, `GetWithPricingAsync`, `GetManageAsync`, `GetSpeciesAsync`, `GetBreedsAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |

---

## 4. Razor Page → Route

### 4.1 Pages (route có `@page`)

| File | Route | Ghi chú |
|---|---|---|
| `Components/Pages/Home.razor` | `/` | Trang chủ |
| `Components/Pages/Login.razor` | `/login` | |
| `Components/Pages/Register.razor` | `/register` | |
| `Components/Pages/ForgotPassword.razor` | `/forgot-password` | |
| `Components/Pages/Account.razor` | `/account`, `/profile/edit` | 2 route trong 1 file |
| `Components/Pages/Orders.razor` | `/account/orders`, `/account/orders/{OrderId:int}` | |
| `Components/Pages/PetInfo.razor` | `/account/pets` | |
| `Components/Pages/Cart.razor` | `/cart` | |
| `Components/Pages/Checkout.razor` | `/cart/checkout` | có `.razor.cs` partial |
| `Components/Pages/CheckoutResult.razor` | `/checkout/result` | |
| `Components/Pages/CodConfirmation.razor` | `/checkout/cod-success` | |
| `Components/Pages/OrderInvoice.razor` | `/checkout/invoice`, `/order-invoice` | có `.razor.cs` partial |
| `Components/Pages/Products.razor` | `/products` | |
| `Components/Pages/ProductDetail.razor` | `/products/{ProductId:int}` | |
| `Components/Pages/ProductList.razor` | `/products/list` | |
| `Components/Pages/ProductCreate.razor` | `/products/create` | |
| `Components/Pages/ProductEdit.razor` | `/products/edit/{Id:int}` | |
| `Components/Pages/ProductInventory.razor` | `/products/inventory` | |
| `Components/Pages/ProductDeleted.razor` | `/products/deleted` | ⚠️ theo P0.3 nên **xoá** (không route trong menu) |
| `Components/Pages/Search.razor` | `/search` | |
| `Components/Pages/Services.razor` | `/services` | |
| `Components/Pages/ServiceDetail.razor` | `/services/{Id:int}` | có `.razor.cs` partial |
| `Components/Pages/SpaCart.razor` | `/spa-cart` | có `.razor.cs` partial |
| `Components/Pages/SpaBookingHistory.razor` | `/spa-booking-history` | |
| `Components/Pages/SpaBookingHistoryDetail.razor` | `/spa-booking-history/{HistoryId:int}` | |
| `Components/Pages/SpaInvoice.razor` | `/spa-invoice` | |
| `Components/Pages/StaffDashboard.razor` | `/system` | |
| `Components/Pages/StaffBookings.razor` | `/system/bookings` | |
| `Components/Pages/StaffOrders.razor` | `/system/orders` | |
| `Components/Pages/StaffProfile.razor` | `/system/profile` | |
| `Components/Pages/StaffSchedule.razor` | `/system/schedule` | |
| `Components/Pages/Revenue.razor` | `/revenue` | |
| `Components/Pages/Error.razor` | `/Error` | |
| `Components/Pages/NotFoundPage.razor` | *(không có `@page`)* | |

### 4.2 Layout

| File | Vai trò |
|---|---|
| `Components/Layout/MainLayout.razor` | Layout chính (customer) |
| `Components/Layout/StaffWorkspaceLayout.razor` | Layout riêng cho staff workspace (11.5KB) |
| `Components/Layout/EmptyLayout.razor` | Layout rỗng |

### 4.3 Shared (component tái sử dụng)

`AuthGuard.razor`, `CustomerOnlyGuard.razor` (route guard), `CartDrawer.razor`, `ErrorMessage.razor`, `Footer.razor`, `Header.razor`, `LoadingIndicator.razor`, `Navigation.razor`, `PageContextBar.razor`, `ProductCard.razor`, `SearchBar.razor`, `ServiceImageCarousel.razor`, `Toast.razor`, `TopBar.razor`, `Format.cs` (helper format).

### 4.4 Home / Products / Root

- `Components/Home/`: `HeroBanner.razor`, `FeaturedCategories.razor`, `ProductList.razor`, `CustomerReviews.razor`
- `Components/Products/ProductsSidebar.razor`
- `Components/App.razor`, `Components/Routes.razor`, `Components/_Imports.razor`

### 4.5 wwwroot

`app.css`, `favicon.png`, `bootstrap/`, `css/spa/` (cart-react.css, service-react.css), `css/template/` (17 file: auth, cart, cartDrawer, checkout, checkoutResult, codConfirmation, home, index, invoice, pageContextBar, productDetails, profile, searchProducts, spa, App), `images/` (~50 file ảnh sản phẩm/nhân viên/phòng).

---

## 5. DI registrations (`Program.cs`)

### 5.1 Framework / hạ tầng

| Đăng ký | Ghi chú |
|---|---|
| `AddControllersWithViews` + `CsvOutputFormatter` | Output formatter `text/csv` |
| `.AddPetShopXmlFormatters()` | `application/xml` |
| `.AddOData(...)` | Route component `PetShop`; `Select/Filter/OrderBy/Expand/Count/SkipToken`, `SetMaxTop(200)` |
| `.AddJsonOptions` | `PropertyNamingPolicy = CamelCase` |
| `AddRazorComponents().AddInteractiveServerRenderMode` | Blazor Server |
| `AddHttpContextAccessor` | |
| `Configure<JwtSettings>` | Section `JwtSettings` |
| `AddAuthentication` + `AddPolicyScheme("Default")` | Forward: có `Authorization: Bearer` → `JwtBearer`, ngược lại → `Session` |
| `AddScheme<..., SessionHeaderAuthenticationHandler>("Session")` | Header `X-User-Id` / `X-User-Role` |
| `AddJwtBearer("Bearer")` | Validate issuer/audience/signing key/lifetime, ClockSkew 1 phút |
| `AddAuthorization` | |
| `AddEndpointsApiExplorer` + `AddSwaggerGen` (+ `FormFileOperationFilter`) | Swagger chỉ bật ở Development |
| `AddCors("FrontendDev")` | Origin `http://localhost:5173` |
| `AddDbContext<ShopPetDatabaseContext>(UseSqlServer)` | Connection string `DBDefault` hoặc `DefaultConnection` (throw nếu thiếu) |
| `AddDistributedMemoryCache` + `AddSession` | Cookie HttpOnly, IsEssential, IdleTimeout 8h |

### 5.2 Service / Repository (Scoped)

| Interface | Implementation |
|---|---|
| `IProductRepository` | `ProductRepository` |
| `IProductService` | `ProductService` |
| `IAuthRepository` | `AuthRepository` |
| `IAuthService` | `AuthService` |
| `IEmailService` | `EmailService` |
| `IPetServiceRepository` | `PetServiceRepository` |
| `IPetServiceService` | `PetServiceService` |
| `ISpaBookingRepository` | `SpaBookingRepository` |
| `ISpaBookingService` | `SpaBookingService` |
| `IReviewRepository` | `ReviewRepository` |
| `IAttendanceService` | `AttendanceService` |
| `IPayrollService` | `PayrollService` |
| `IBoardingRepository` | `BoardingRepository` |
| `IBoardingService` | `BoardingService` |
| `IStaffRepository` | `StaffRepository` |
| `IStaffService` | `StaffService` |
| `IStaffDashboardService` | `StaffDashboardService` |
| `IStaffProfileService` | `StaffProfileService` |
| `UserSessionService` | *(concrete)* |
| `CartStateService` | *(concrete)* |
| `IJwtTokenService` | `JwtTokenService` (**Singleton**) |

### 5.3 HTTP Client

| Đăng ký | Ghi chú |
|---|---|
| `AddSelfHttpClient<IProductCatalogClient, ProductCatalogClient>()` | không auth header |
| `AddSelfHttpClient<IAuthClient, AuthClient>()` | không auth header |
| `AddSelfHttpClient<ICheckoutClient, CheckoutClient>()` | không auth header |
| `AddSelfHttpClient<IOrderClient, OrderClient>()` | không auth header |
| `AddSelfHttpClient<IReviewClient, ReviewClient>()` | không auth header |
| `AddSelfHttpClient<IPaymentClient, PaymentClient>()` | không auth header |
| `AddSelfHttpClient<IPetServiceClient, PetServiceClient>()` | không auth header |
| `SpaAuthHeaderHandler` (**Transient**) | gắn header session |
| `AddSelfHttpClientWithAuth<ISpaBookingClient, SpaBookingClient>()` | có auth header |
| `AddSelfHttpClientWithAuth<IPetsClient, PetsClient>()` | có auth header |
| `AddHttpClient<IPayOSService, PayOSService>()` + `ConfigureHttpClient` | Header `x-client-id`, `x-api-key`; timeout 30s |

### 5.4 Middleware pipeline (theo thứ tự)

```
UseSwagger / UseSwaggerUI / UseODataRouteDebug   (Development)
UseExceptionHandler("/Error") + UseHsts          (Production)
→ UseHttpsRedirection
→ UseStaticFiles
→ UseCors("FrontendDev")
→ UseSession
→ UseAuthentication
→ UseAuthorization
→ UseAntiforgery
→ UseMiddleware<ApiExceptionMiddleware>
→ inline middleware: X-Forwarded-For → context.Items["RealClientIp"]
→ MapControllers()
→ MapRazorComponents<App>().AddInteractiveServerRenderMode()
→ MapControllerRoute("default", "mvc/{controller=ProductsPage}/{action=Index}/{id?}")
```

---

## 6. Quy ước bắt buộc & phase đã hoàn thành (AGENTS.md + PRN232_PLAN.md)

### 6.1 Bối cảnh dự án

- Bài tập môn **PRN232** — quản lý cửa hàng thú cưng, **chạy 100% LOCAL**, mức độ bảo mật **THẤP**.
- `.NET 8` (`net8.0`), Blazor Server + API Controllers trong cùng project.
- SQL Server `SHOP_PET_Database`, EF Core 8 code-first scaffold.
- Port `http://localhost:5286` (http) / `https://localhost:7083` (https).
- **Giao tiến với chủ dự án: TIẾNG VIỆT — BẮT BUỘC.** Code tiếng Anh, thông báo nghiệp vụ tiếng Việt.

### 6.2 Quy ước bắt buộc (khi sửa code)

| # | Quy ước |
|---|---|
| 1 | **KHÔNG** `catch { }` / `catch { return false; }` — phải log qua `ILogger<T>` rồi trả lỗi có thông tin |
| 2 | **KHÔNG** `Console.WriteLine` trong code ứng dụng — dùng `ILogger<T>` / extension `LoggerExtensions`. Không log mật khẩu/token/`ChecksumKey` |
| 3 | `Nullable = enable` → **không warning CS86xx**; xử lý null bằng `?.` / `?? string.Empty` / `required` |
| 4 | `async/await` bắt buộc cho I/O — **không** `.Result`/`.Wait()` |
| 5 | EF read-only → `AsNoTracking()`; dùng `.Select(...)` projection thay vì load cả entity |
| 6 | SQL thô chỉ dùng `ExecuteSqlRawAsync` với tham số `@p0`; **`FromSqlRaw` bị cấm** |
| 7 | File > 500 dòng hoặc component > 25KB → **tách**: C# dùng `partial class` + `<Tên>.<Phần>.cs`; Razor tách `@code` ra `<Tên>.razor.cs` |
| 8 | Business logic trùng lặp → gom về `Services/` dùng chung; **không copy-paste** logic |
| 9 | Sửa **đúng phạm vi** yêu cầu; không tự ý đổi schema DB / seed data / `SHOP_PET_Database.sql` |
| 10 | **KHÔNG** push/commit/force-push nếu chưa được yêu cầu |
| 11 | **KHÔNG** tự ý gắn `[Authorize]` vào endpoint đang chạy tốt (sẽ phá luồng Blazor self-HTTP) |
| 12 | **KHÔNG** hardcode `JwtSettings:Key` — phải nằm trong `appsettings.Development.json` / User Secrets, ≥ 32 ký tự |
| 13 | Đọc `.editorconfig` trước khi sửa (nullability warning → error) |

### 6.3 Trạng thái Phase (PRN232_PLAN.md)

| Phase | Nội dung | Trạng thái |
|---|---|---|
| **P0** | Dọn code chết + regression net an toàn | ✅ Xong (01/11/2026) |
| **P1** | DataAnnotations Validation + ProblemDetails (RFC 7807) | ✅ Xong |
| **P2** | Content Negotiation (JSON / XML / CSV formatter) | ✅ Xong |
| **P3** | **OData 9.5** (prefix `/PetShop`) | ✅ Xong |
| **P4** | JWT Bearer dual-mode (Session + Bearer) | ✅ **Xong 02/11/2026** (30/30 test pass) |
| **P5** | gRPC service (protobuf, unary + streaming) | ⬜ Chưa làm |
| **P6** | JavaScript client demo (jQuery AJAX CRUD) | ⬜ Chưa làm |
| **P7** | Docker containerization | ⬜ Chưa làm |

### 6.4 Build / test hiện tại

- `dotnet build` → **0 Error, 0 Warning** (sau P0–P2).
- `dotnet test` → **30/30 passed** (Attendance 7 + WorkScheduleDedup 7 + AuthService 8 + JwtTokenService 8).
- CI: `.github/workflows/ci.yml` (build + test trên `windows-latest`, .NET 8).
- Test project: `tests/PetShop.Tests` (xUnit), csproj web app đã `Compile Remove="tests\**\*.cs"`.

### 6.5 Cơ chế xác thực dual-mode

```
Request ──> AddPolicyScheme("Default")
             ├── có "Authorization: Bearer ..."  ──> JwtBearer  (client ngoài)
             └── không có                        ──> Session    (self-HTTP của Blazor)
```

---

## 7. TÁI SỬ DỤNG ĐƯỢC

> Đây là phần quan trọng nhất — đọc kỹ trước khi viết code mới.

### 7.1 Tái sử dụng **nguyên vẹn** (không viết lại)

| Hạng mục | Dùng lại ở đâu |
|---|---|
| **Toàn bộ model entity + DTO** (`Models/`, 52 file) | Dùng trực tiếp, không cần tạo DTO mới cho cùng nghiệp vụ |
| **`ShopPetDatabaseContext`** + 39 `DbSet` + `OnModelCreating` | Mọi truy vấn EF đi qua context này — **không** scaffold lại |
| **Toàn bộ API controller** (§2) | Gọi trực tiếp qua HTTP route đã có |
| **Toàn bộ Service + Repository** (§3) | Inject interface sẵn có, không viết lại business logic |
| **`ISpaBookingService`, `ISpaBookingRepository`, `SpaBookingClient`** | Luồng spa booking (cart → đặt lịch → hoá đơn → đánh giá) đã hoàn chỉnh |
| **`IBoardingService` + `BoardingController`** | Luồng đặt phòng + check-in/check-out |
| **`IAttendanceService` + `AttendanceService`** | Luật tính công đã có **unit test** → không viết lại |
| **`IPayrollService`** | Sinh lương, cấu hình lương |
| **`IStaffService` / `IStaffDashboardService` / `IStaffProfileService`** | CRUD staff + dashboard + hồ sơ |
| **`IEmailService`** | 5 loại email (OTP, Google first-login, order/spa/boarding invoice) — MailKit + Handlebars sẵn |
| **`IPayOSService`** | Tạo link thanh toán + webhook + trạng thái (đã có HMAC verify) |
| **`IJwtTokenService`** + `AuthController` | Cấp JWT & xác thực user — không cần viết lại |
| **`AuthService` (migrate-on-login)** | Logic BCrypt + nâng cấp hash plaintext — đã có test |
| **`WorkScheduleDedup`** | Khử trùng lịch — đã có test |
| **`ApiExceptionMiddleware`** | Chuẩn hoá lỗi RFC 7807 cho toàn bộ API |
| **`CsvOutputFormatter` / `XmlOutputFormatter` / `MvcBuilderExtensions`** | Content negotiation |
| **`SelfHttpClientExtensions`** | Typed client trỏ về chính app — dùng cho client mới thay vì `new HttpClient()` |
| **`PetShopEdmModel`** | Khai báo key OData cho `Product`, `PetService`, `Pet`, `Category`, `Supplier`, `Breed`, `Species`, `Admin` — thêm entity mới vào đây |
| **Toàn bộ Razor page** (§4) | 34 page có sẵn, route đã ổn định |
| **Toàn bộ Shared component** | `ProductCard`, `CartDrawer`, `Header`, `Footer`, `TopBar`, `Toast`, `SearchBar`, `ServiceImageCarousel`, `AuthGuard`, `CustomerOnlyGuard`, `PageContextBar` |
| **Toàn bộ Home component** | `HeroBanner`, `FeaturedCategories`, `ProductList`, `CustomerReviews` |
| **`wwwroot/css/`** | 17 file CSS template + 2 file CSS spa — không cần viết lại style |
| **`wwwroot/images/`** | ~50 ảnh sản phẩm/nhân viên/phòng |
| **Test project** | Pattern xUnit sẵn có trong `tests/PetShop.Tests` |

### 7.2 Tái sử dụng **có điều kiện** (cần kiểm tra trước khi dùng lại)

| Hạng mục | Lưu ý |
|---|---|
| `ProductDeleted.razor` (`/products/deleted`) | PRN232_PLAN P0.3 đã đánh dấu **nên xoá** — không có trong menu, không ai link |
| `Models/ChatMessage.cs` + `ChatMessages` DbSet | Có entity nhưng **chưa** có controller/service dùng (P0.1 "verify") — nếu làm chatbot thì tái sử dụng |
| `Models/SystemSetting.cs` | Có model nhưng **không có DbSet**, không ai đọc |
| `Models/Shift.cs` / `Shifts` DbSet | `ShiftRequests` mới dùng; `Shift` không ai đọc |
| `Data/SessionExtensions.cs` | `SetJson`/`GetJson` không ai gọi |
| `Models/PayrollDtos.cs` → `PayrollResultDto`, `GeneratePayrollDto` | Không controller nào dùng |
| `Models/StaffDtos.cs` → `RevenueDetailDto` | Không ai dùng |
| `ProductService` / `PetServiceService` / `BoardingService` | Lớp bọc mỏng quanh repository — nếu thêm tính năng nhỏ thì có thể bỏ qua lớp bọc và gọi repository, nhưng **giữ nguyên** để không phá DI |
| `ReviewController` (`api/reviews/services/{id}`) | Chỉ có GET — nguồn cũng chỉ có GET, giữ nguyên |
| `ProductsController` | **Chỉ có GET**, không có POST/PUT/DELETE (P6 ghi chú cần CRUD để demo JS client) |

---

## 8. CẦN VIẾT MỚI / THIẾU SO VỚI NGUỒN

> So sánh với nguồn `../Projectojt/`: `BackEnd/` (.NET 8 MVC API) + `frontend/` (React + Vite).

### 8.1 ⚠️ THIẾU — Tính năng Chatbot AI (khác biệt lớn nhất)

Đích **hoàn toàn không có** stack chatbot. Nguồn có đầy đủ:

| File nguồn (`Projectojt/BackEnd/`) | Nội dung | Trạng thái ở đích |
|---|---|---|
| `Controllers/ChatbotController.cs` | route `api/chatbot`: POST `message`, POST `intent`, GET `context/{customerId}`, GET `health` | ❌ **Thiếu** |
| `Interfaces/IChatbotService.cs` | `GetResponseAsync`, `AnalyzeIntentWithAi`, `GetContextAsync`, `BuildSystemPromptAsync` | ❌ Thiếu |
| `Interfaces/IAiProviderService.cs` | `ProviderName`, `GenerateTextAsync`, `IsAvailable` | ❌ Thiếu |
| `Services/ChatbotService.cs` | Orchestration chatbot + intent detection + context | ❌ Thiếu |
| `Services/GeminiProviderService.cs` | Provider `gemini` (Google AI) | ❌ Thiếu |
| `Services/OllamaProviderService.cs` | Provider `ollama` (local) | ❌ Thiếu |
| `Models/ChatbotDtos.cs` | `ChatMessageRequestDto`, `ChatHistoryItemDto`, `ChatMessageResponseDto`, `ConversationContextDto`, `ChatIntentDto`, `ApiErrorDto`, `AiIntentResult` | ❌ Thiếu |
| `Models/ChatbotOptions.cs` | `SectionName="Chatbot"`, `DefaultProvider`, `DefaultModel`, `Temperature`, `MaxTokens`, `EnableDatabaseQuery`, `SystemPromptPath`, `Providers: Dictionary<string, AiProviderConfig>` | ❌ Thiếu |
| `frontend/src/api/chatbotApi.js` | Client JS gọi chatbot API | ❌ Thiếu |

**Tái sử dụng được khi viết mới:** `Models/ChatMessage.cs` + `DbSet<ChatMessage> ChatMessages` (đã có sẵn trong context!), `IEmailService`, `IProductRepository`, `ISpaBookingRepository`, `IOrderClient` — để chatbot truy vị context nghiệp vụ.

**Lưu ý:** nguồn dùng `AddHttpClient<IAiProviderService, GeminiProviderService>()` nhưng service lại có `OllamaProviderService` → nguồn có **bug DI** (2 provider cùng interface, chỉ 1 được đăng ký). Khi viết mới nên dùng `IEnumerable<IAiProviderService>` + `Options` pattern để hỗ trợ multi-provider.

### 8.2 THIẾU — Frontend React (nguồn `Projectojt/frontend`)

Đích đã chuyển React → **Blazor Server**, nên về mặt "port" các page đã được viết lại bằng Razor. **Không cần migrate lại React.** Tuy nhiên các mục sau nguồn có mà đích **chưa có bản Razor tương đương**:

| Nguồn React | Đích | Ghi chú |
|---|---|---|
| `pages/AboutPage.jsx` (route `/about`) | ❌ không có page `/about` | Cần viết mới nếu muốn giữ |
| `pages/ProfilePage.jsx` + `ProfileEditPage.jsx` | ✅ `Account.razor` (`/account`, `/profile/edit`) | Đã có |
| `pages/OrdersStaffPage.jsx` (route `/orders`) | ✅ `StaffOrders.razor` (`/system/orders`) | Đã có (route khác) |
| `components/checkout/AddressModal.jsx` | ⚠️ chưa thấy modal địa chỉ riêng trong đích | Cần kiểm tra trong `Checkout.razor` |
| `components/checkout/daNangData.js` (dữ liệu địa chỉ Đà Nẵng) | ❌ không có | Cần viết mới nếu checkout cần chọn địa chỉ |
| `components/staff/NotificationBell.jsx` | ⚠️ có API `api/notifications` + `NotificationController` nhưng chưa thấy component chuông trong đích | Cần kiểm tra |
| `components/products/ClearSearchButton.jsx` | ⚠️ chưa thấy | Kiểm tra `Search.razor` |
| `src/images/` (60+ ảnh: `room-cat-*`, `room-dog-*`, `petnews*`, `slide_*`) | ⚠️ đích có ~50 ảnh — **cần đối chiếu thiếu** (`room-cat-vip`, `room-dog-large`, `petnews*`, `slide_*` có vẻ chưa có) | |
| `src/api/*.js` (10 file) | ✅ tương đương `Services/*Client.cs` | Đã chuyển sang C# typed client |

### 8.3 THIẾU — Theo PRN232_PLAN (phase chưa làm)

| Phase | Việc cần làm | Ghi chú |
|---|---|---|
| **P5** | gRPC service: `Protos/pets.proto`, `Services/Grpc/PetCatalogService.cs` (GetPets / GetPetById / WatchBookings server-streaming), `AddGrpc()` + `MapGrpcService` | Chưa có package `Grpc.AspNetCore` / `Grpc.Tools` |
| **P6** | JS client demo: `Controllers/ApiDemoController.cs` (MVC) + `Views/ApiDemo/Index.cshtml` + jQuery trong `wwwroot/lib/jquery/`. **Cần thêm CRUD POST/PUT/DELETE cho `ProductsController`** (hiện chỉ có GET) | Không có thư mục `Views/` trong đích |
| **P7** | `Dockerfile` (multi-stage sdk → aspnet), `.dockerignore`, `docker-compose.yml` | Chưa có |

### 8.4 THIẾU — Ghi chú còn lại từ nguồn

| Hạng mục | Nguồn | Đích | Ghi chú |
|---|---|---|---|
| `Controllers/ProductsPageController.cs` (MVC: `/`, `/products`, `/products/{id}`) | ✅ có | ⚠️ đích có `MapControllerRoute("default", "mvc/{controller=ProductsPage}/{action=Index}/{id?}")` **nhưng KHÔNG có controller `ProductsPage`** | Route mvc trong `Program.cs` đang **trỏ tới controller không tồn tại** — sẽ 404 nếu ai truy cập `/mvc/...`. Cần tạo `ProductsPageController` hoặc xoá route mapping |
| `Helpers/WorkScheduleDedup.cs` | ✅ có | ✅ đã chuyển → `Services/WorkScheduleDedup.cs` | OK |
| `Infrastructure/SessionExtensions.cs` | ✅ có | ✅ `Data/SessionExtensions.cs` | OK (P0.1 nói không ai gọi) |
| File hậu tố `_temp` (9 file C# + 3 route API + 6 type) | ✅ có | ❌ đã đổi tên sạch (P1) | OK |
| `Interfaces/IProductRepository_temp` / `IProductService_temp` | ✅ | ✅ đã đổi tên | OK |
| `frontend/src/api/productsApi_temp.js` | ✅ | ❌ không có (đã bỏ React) | OK |

### 8.5 Cần kiểm tra/verify thêm trong đích (chưa xác nhận được từ grep)

| Hạng mục | Câu hỏi cần trả lời |
|---|---|
| `Views/` folder | Không tồn tại → `MapControllerRoute` "default" không dùng được; MVC `CartController` dùng `[HttpPost("/cart/...")]` nên không cần view |
| `/about` route | Nguồn có, đích không — quyết định có cần làm không |
| `NotificationBell` | API có, UI component trong Blazor chưa thấy |
| `AddressModal` + `daNangData` | Checkout của đích có phần chọn địa chỉ không |
| `ClearSearchButton` | Search UI của đích tương đương chưa |
| `wwwroot/images` | Đối chiếu file ảnh thiếu so với nguồn |
| `Components/Pages/ProductDeleted.razor` | Xoá theo P0.3 (đang còn) |
| `SystemSetting`, `Shift`, `ChatMessage` | 3 entity "verify" theo P0.1 — quyết định giữ/xoá |

---

## 9. Khuyến nghị hành động (không thực hiện — chỉ đề xuất)

1. **Không viết lại bất kỳ service/repository/controller nào đã có** — §7.1 liệt kê đầy đủ.
2. **Tập trung vào phần thiếu thật sự**: chatbot AI (8.1), phase P5/P6/P7 (8.3), và các gap UI nhỏ (8.2).
3. **Fix `MapControllerRoute` trỏ tới `ProductsPageController` không tồn tại** (8.4) — hoặc tạo controller, hoặc xoá mapping.
4. **Chạy `dotnet build` + `dotnet test` trước khi bắt đầu** để xác nhận baseline 0 error / 30 test pass.
5. **Tuân thủ AGENTS.md §2** — đặc biệt: log qua `ILogger`, không `Console.WriteLine`, không `catch {}`, tách file > 500 dòng, tiếng Việt khi giao tiếp.

---

*Tài liệu được tạo bởi agent kiểm kê code đích. Không có file nào trong project đích bị chỉnh sửa.*
