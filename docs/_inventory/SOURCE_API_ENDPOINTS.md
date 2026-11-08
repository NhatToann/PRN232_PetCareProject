# SOURCE API ENDPOINTS — Inventory backend ASP.NET Core (OJT)

> **Nguồn (chỉ đọc):** `D:\1_F_Matierials\Kỳ 8\1_PRN232\New folder\Projectojt\BackEnd`
> **Phạm vi:** Toàn bộ controller trong `BackEnd/Controllers` (18 file).
> **Tổng endpoint:** **92 action-routes** (90 method-routes + 2 route bị alias bởi attribute thứ hai trên cùng action).
> **Ngày lập:** 2026-11-08

---

## ⚠️ GHI CHÚ VỀ AUTH (ĐỌC TRƯỚC KHI MIGRATE)

Toàn bộ backend **KHÔNG có attribute `[Authorize]` / `[AllowAnonymous]` ở bất kỳ controller hay action nào**, và `Program.cs` **không đăng ký** `AddAuthentication()` / `UseAuthentication()` (chỉ có `UseAuthorization()`).

Hệ quả:
- **Mọi endpoint API đều mở công khai** — không có middleware xác thực JWT/cookie ở tầng framework.
- Việc "phân quyền" hiện chỉ được làm **thủ công bên trong code** qua header tự do do client gửi (`X-User-Role`, `X-User-Id`) — xem `PetsController.ValidateSelfAccess`. Đây **không phải** xác thực an toàn vì client tự khai báo role.
- `Program.cs` có `builder.Services.AddControllersWithViews()` + `app.MapControllers()` + `app.MapControllerRoute(...)` ⇒ các controller MVC (`Controller`) như `CartController`, `ProductsPageController` cũng được map.
- Khi migrate sang PRN232 cần **thiết kế lại auth** (JWT + `[Authorize]` + policy theo role) — không thể chuyển nguyên trạng.

---

## BẢNG INVENTORY ENDPOINT

| # | Controller | METHOD | Route đầy đủ | Action | Request DTO | Response | Status codes | Auth |
|---|---|---|---|---|---|---|---|---|
| 1 | `AuthController` | POST | `api/auth/register` | `Register` | `RegisterRequestDto` (body): `Name`, `Email`, `Password`, `Phone?`, `Address?` | `AuthResponseDto` | 200 `Ok`; 409 `Conflict` (`InvalidOperationException`); 400 `BadRequest` (`ArgumentException`) | Không |
| 2 | `AuthController` | POST | `api/auth/login` | `Login` | `LoginRequestDto` (body): `Email`, `Password` | `AuthResponseDto` | 200 `Ok`; 401 `Unauthorized` (`UnauthorizedAccessException`) | Không |
| 3 | `AuthController` | POST | `api/auth/google-login` | `GoogleLogin` | `GoogleLoginRequestDto` (body): `IdToken?`, `AccessToken?` | `AuthResponseDto` | 200 `Ok`; 500 (lỗi Google config); 400 `BadRequest`; 401 `Unauthorized` | Không |
| 4 | `AuthController` | POST | `api/auth/send-email-otp` | `SendEmailOtp` | `SendEmailOtpRequestDto` (body): `Email` | `SimpleMessageDto` (`"Đã gửi OTP đến email."`) | 200 `Ok`; 404 `NotFound`; 500; 400 `BadRequest` | Không |
| 5 | `AuthController` | POST | `api/auth/verify-email-otp` | `VerifyEmailOtp` | `VerifyEmailOtpRequestDto` (body): `Email`, `OtpCode` | `SimpleMessageDto` | 200 `Ok`; 404 `NotFound`; 401 `Unauthorized`; 400 `BadRequest` | Không |
| 6 | `AuthController` | POST | `api/auth/reset-password` | `ResetPassword` | `ResetPasswordRequestDto` (body): `Email`, `ResetToken?`, `OtpCode?`, `NewPassword`, `ConfirmPassword` | `SimpleMessageDto` (`"Đặt lại mật khẩu thành công."`) | 200 `Ok`; 404 `NotFound`; 401 `Unauthorized`; 400 `BadRequest` | Không |
| 7 | `AuthController` | GET | `api/auth/google-client-id` | `GetGoogleClientId` | Không (đọc `IConfiguration["GoogleAuth:ClientId"]`) | `{ clientId }` | 200 `Ok`; 404 `NotFound` (`"Google Client ID chưa được cấu hình."`) | Không |
| 8 | `ConfigController` | GET | `api/config/google-client-id` | `GetGoogleClientId` | Không (đọc config) | `{ clientId }` | 200 `Ok`; 404 `NotFound` (`"Google Client ID chưa được cấu hình."`) | Không — **trùng lặp** với #7 | Không |
| 9 | `PetsController` | GET | `api/pets/customer/{customerId:int}` | `GetPetsByCustomer` | Query: `customerId` | `IReadOnlyList<PetWithBreedPriceDto>` | 200 `Ok` | Không (lọc `DeletedAt == null`) |
| 10 | `PetsController` | GET | `api/pets/customer/{customerId:int}/manage` | `GetPetsForManage` | Route: `customerId` | `IReadOnlyList<PetManageDto>` | 200 `Ok`; 401 `Unauthorized`; 403 | **Header thủ công** `X-User-Role` ∈ {user, staff} + `X-User-Id` == `customerId` |
| 11 | `PetsController` | POST | `api/pets/upload-image` | `UploadImage` | `[FromForm] IFormFile image` | `{ imageUrl }` | 200 `Ok`; 400 `BadRequest` | Không — `[ApiExplorerSettings(IgnoreApi = true)]` (ẩn khỏi Swagger) |
| 12 | `PetsController` | GET | `api/pets/customer/{customerId:int}/service/{serviceId:int}/pricing` | `GetPetsWithPricing` | Route: `customerId`, `serviceId`; Query: `basePrice?` | `IReadOnlyList<PetWithBreedPriceDto>` | 200 `Ok`; 400 `BadRequest` (`"Dịch vụ không tồn tại."`) | Không |
| 13 | `PetsController` | GET | `api/pets/meta/species` | `GetSpecies` | Không | `IReadOnlyList<SpeciesOptionDto>` | 200 `Ok` | Không |
| 14 | `PetsController` | GET | `api/pets/meta/breeds` | `GetBreeds` | Query: `speciesId?` | `IReadOnlyList<BreedOptionDto>` | 200 `Ok` | Không |
| 15 | `PetsController` | POST | `api/pets` | `CreatePet` | `UpsertPetRequestDto` (body): `CustomerId`, `PetName`, `Age`, `Gender`, `WeightKg?`, `Description?`, `HealthStatus?`, `ImagePath?`, `BreedId?` | `PetManageDto` | 201 `CreatedAtAction`; 400 `BadRequest`; 401; 403 | Header thủ công (`ValidateSelfAccess`) |
| 16 | `PetsController` | PUT | `api/pets/{petId:int}` | `UpdatePet` | `UpsertPetRequestDto` (body) | `PetManageDto` | 200 `Ok`; 400 `BadRequest`; 404 `NotFound` (`"Không tìm thấy thú cưng."`); 401; 403 | Header thủ công |
| 17 | `PetsController` | DELETE | `api/pets/{petId:int}?customerId=` | `DeletePet` | Query: `customerId` | Rỗng (204) | 204 `NoContent`; 400 `BadRequest`; 404 `NotFound`; 401; 403 | Header thủ công |
| 18 | `SpaBookingController` | GET | `api/spa-booking/pets?customerId=` | `GetPets` | Query: `customerId` | `IReadOnlyList<PetSummaryDto>` | 200 `Ok` | Không |
| 19 | `SpaBookingController` | GET | `api/spa-booking/services` | `GetServices` | Không | `IReadOnlyList<SpaServiceDto>` | 200 `Ok` | Không |
| 20 | `SpaBookingController` | POST | `api/spa-booking/availability` | `CheckAvailability` | `SpaAvailabilityRequest` (body, nested record): `Start` (DateTime), `DurationMin` (int), `Quantity` (int) | `SpaAvailabilityResponse` | 200 `Ok` | Không |
| 21 | `SpaBookingController` | POST | `api/spa-booking/validate-slot` | `ValidateSlot` | `BookingSlotValidationRequest` (body): `AppointmentStart` (DateTime), `DurationMinutes` (int) | `BookingSlotValidationResult` | 200 `Ok`; 400 `BadRequest` (khi `!IsValid`) | Không |
| 22 | `SpaBookingController` | POST | `api/spa-booking/estimate?customerId=` | `Estimate` | Query: `customerId`; `SpaBookingEstimateRequest` (body): `PetIds` (IReadOnlyList\<int\>), `Items` (IReadOnlyList\<`SpaCartItemRequest`{`ServiceId`,`Quantity`}\>) | `SpaBookingEstimateDto` | 200 `Ok`; 400 `BadRequest` (service ném `ArgumentException`/`InvalidOperationException` → **chưa bắt** ⇒ có thể 500) | Không |
| 23 | `SpaBookingController` | POST | `api/spa-booking?customerId=` | `Create` | Query: `customerId`; `CreateSpaBookingRequest` (body): `PetIds`, `AppointmentStart` (DateTime), `Note?`, `Items`, `PaymentMethod?` | `SpaBookingDto` | 200 `Ok`; 400 `BadRequest` (kèm `suggestedStart` khi hết slot) | Không |
| 24 | `SpaBookingController` | GET | `api/spa-booking/history?customerId=` | `History` | Query: `customerId` | `IReadOnlyList<SpaBookingDto>` | 200 `Ok` | Không |
| 25 | `SpaBookingController` | GET | `api/spa-booking/history/all` **&** `api/spa-booking/all-bookings` | `HistoryAll` | Không | `IReadOnlyList<SpaBookingDto>` | 200 `Ok` | Không — **alias 2 route trên 1 action** |
| 26 | `SpaBookingController` | GET | `api/spa-booking/{bookingId:int}/invoice` | `Invoice` | Route: `bookingId` | `SpaBookingInvoiceDto` | 200 `Ok`; 404 `NotFound` (`"Không tìm thấy booking."`) | Không |
| 27 | `SpaBookingController` | PATCH | `api/spa-booking/{bookingId:int}/status` | `UpdateStatus` | Route: `bookingId`; `UpdateBookingStatusRequest` (body, nested): `Status` (string) | `{ message = "Updated" }` | 200 `Ok`; 400 `BadRequest`; 404 `NotFound` | Không |
| 28 | `SpaBookingController` | POST | `api/spa-booking/review?customerId=` | `Review` | Query: `customerId`; `CreateSpaReviewRequest` (body): `BookingId`, `ServiceId`, `Rating` (1-5), `Comment?` | `{ reviewId }` | 200 `Ok`; 400 `BadRequest` | Không |
| 29 | `PayOSController` | POST | `api/payos/spa/create` | `CreateSpaPayment` | `CreateSpaPayOSRequest` (body, nested): `BookingId` (int), `BookingIds?` (IReadOnlyList\<int\>), `ReturnUrl`, `CancelUrl` | `PayOSCheckoutResult` | 200 `Ok`; 400 `BadRequest`; 404 `NotFound` | Không |
| 30 | `PayOSController` | POST | `api/payos/order/create` | `CreateOrderPayment` | `CreateOrderPayOSRequest` (body, nested): `OrderId` (int), `ReturnUrl`, `CancelUrl` | `PayOSCheckoutResult` | 200 `Ok`; 400 `BadRequest`; 404 `NotFound` | Không |
| 31 | `PayOSController` | POST | `api/payos/webhook` | `Webhook` | Raw `JsonElement payload` (body); Header `x-payos-signature` | `{ message }` | 200 `Ok`; 400 `BadRequest`; 401 `Unauthorized` (`"Invalid signature"`) | **Webhook PayOS** — xác thực bằng HMAC signature header |
| 32 | `ShiftExchangeController` | GET | `api/shift-exchange/verify-network` | `VerifyNetwork` | Không (đọc `X-Client-IP`, `X-Real-IP`, `X-Forwarded-For`) | `VerifyNetworkDto` | 200 `Ok` | Không — chỉ dùng header để kiểm tra IP mạng công ty |
| 33 | `ShiftExchangeController` | GET | `api/shift-exchange/staff/{staffId:int}/my-shifts` | `GetMyShifts` | Route: `staffId` | `IReadOnlyList<ShiftOptionDto>` | 200 `Ok` | Không |
| 34 | `ShiftExchangeController` | GET | `api/shift-exchange/all-shifts` | `GetAllShifts` | Không | `IReadOnlyList<ShiftOptionDto>` | 200 `Ok` | Không |
| 35 | `ShiftExchangeController` | GET | `api/shift-exchange/staff/options?excludeStaffId=` | `GetStaffOptions` | Query: `excludeStaffId` (int) | `IReadOnlyList<StaffOptionDto>` | 200 `Ok` | Không |
| 36 | `ShiftExchangeController` | POST | `api/shift-exchange/pass` | `CreatePass` | `CreatePassShiftRequestDto` (body): `StaffId`, `FromDate` (DateOnly), `FromScheduleId`, `ToStaffId`, `Reason?` | `ShiftExchangeActionResultDto` (`Status`, `Message`) | 200 `Ok` (`"Gửi yêu cầu nhờ làm thay thành công."`); 400 `BadRequest` | Không |
| 37 | `ShiftExchangeController` | POST | `api/shift-exchange/swap` | `CreateSwap` | `CreateSwapShiftRequestDto` (body): `StaffId`, `FromDate`, `ToDate`, `FromScheduleId`, `ToScheduleId`, `ToStaffId`, `Reason?` | `ShiftExchangeActionResultDto` | 200 `Ok` (`"Gửi yêu cầu đổi ca thành công."`); 400 `BadRequest` | Không |
| 38 | `ShiftExchangeController` | GET | `api/shift-exchange/pending?staffId=` | `GetPendingRequests` | Query: `staffId` | `IReadOnlyList<PendingRequestDto>` | 200 `Ok` | Không |
| 39 | `ShiftExchangeController` | GET | `api/shift-exchange/my-requests?staffId=` | `GetMyRequests` | Query: `staffId` | `IReadOnlyList<MyRequestDto>` | 200 `Ok` | Không |
| 40 | `ShiftExchangeController` | POST | `api/shift-exchange/accept/{requestId:int}?responderStaffId=` | `AcceptRequest` | Route: `requestId`; Query: `responderStaffId` | `ShiftExchangeActionResultDto` | 200 `Ok` (`"Đã chấp nhận yêu cầu đổi ca."`); 400 `BadRequest` | Không — `responderStaffId` tự khai trong query |
| 41 | `ShiftExchangeController` | POST | `api/shift-exchange/decline/{requestId:int}?responderStaffId=` | `DeclineRequest` | Route: `requestId`; Query: `responderStaffId` | `ShiftExchangeActionResultDto` | 200 `Ok` (`"Đã từ chối yêu cầu."`); 400 `BadRequest` | Không |
| 42 | `ShiftExchangeController` | POST | `api/shift-exchange/cancel/{requestId:int}?staffId=` | `CancelRequest` | Route: `requestId`; Query: `staffId` | `ShiftExchangeActionResultDto` | 200 `Ok` (`"Đã huỷ yêu cầu."`); 400 `BadRequest` | Không |
| 43 | `AttendanceController` | GET | `api/attendance/unclosed?date=` | `GetUnclosed` | Query: `date?` (DateOnly) | `ActionResult` (payload service) | 200 `Ok` | Không — comment ghi "Admin dashboard" nhưng **không có auth** |
| 44 | `AttendanceController` | GET | `api/attendance/staff/{staffId:int}/overview` | `GetOverview` | Route: `staffId` | `ActionResult` (payload service) | 200 `Ok` | Không |
| 45 | `AttendanceController` | POST | `api/attendance/staff/{staffId:int}/toggle` | `Toggle` | Route: `staffId` | `ActionResult` | 200 `Ok` (khi `result.Status == "success"`); 400 `BadRequest` | Không |
| 46 | `PayrollController` | GET | `api/payroll/history?staffId=` | `GetPayrollHistory` | Query: `staffId?` (int?) | `IReadOnlyList<PayrollSummaryDto>` | 200 `Ok` | Không |
| 47 | `PayrollController` | GET | `api/payroll/staff/{staffId:int}/detail?periodStart=&periodEnd=` | `GetStaffPayrollDetail` | Route: `staffId`; Query: `periodStart` (DateOnly, **bắt buộc**), `periodEnd` (DateOnly, **bắt buộc**) | `StaffPayrollDetailDto` | 200 `Ok`; 404 `NotFound` (`"Khong tim thay nhan vien."` — **không dấu**) | Không |
| 48 | `PayrollController` | GET | `api/payroll/staff/{staffId:int}/generate?periodStart=&periodEnd=` | `GenerateStaffPayroll` | Route: `staffId`; Query: `periodStart`, `periodEnd` (DateOnly) | `PayrollListResultDto` | 200 `Ok`; 400 `BadRequest` (`"Ngay ket thuc phai lon hon hoac bang ngay bat dau."` — **không dấu**) | Không |
| 49 | `PayrollController` | POST | `api/payroll/generate-all` | `GenerateAllPayroll` | `GenerateAllPayrollDto` (body): `PeriodStart` (DateOnly), `PeriodEnd` (DateOnly) | `PayrollListResultDto` | 200 `Ok`; 400 `BadRequest` | Không — **ghi DB (side-effect)** |
| 50 | `PayrollController` | GET | `api/payroll/config` | `GetAllSalaryConfigs` | Không | `IReadOnlyList<StaffSalaryConfigDto>` | 200 `Ok` | Không |
| 51 | `PayrollController` | GET | `api/payroll/config/staff/{staffId:int}` | `GetSalaryConfig` | Route: `staffId` | `PayrollConfigResultDto` | 200 `Ok`; 404 `NotFound` | Không |
| 52 | `PayrollController` | PUT | `api/payroll/config/staff` | `UpdateSalaryConfig` | `UpdateSalaryConfigDto` (body): `StaffId`, `MonthlyBaseSalary?`, `StandardShifts?`, `HourlyRate?` | `PayrollConfigResultDto` | 200 `Ok`; 400 `BadRequest` | Không — **ghi DB** |
| 53 | `PayrollController` | GET | `api/payroll/staff-list` | `GetStaffList` | Không | `IReadOnlyList<object>` (`StaffId`, `Name`, `Position`) | 200 `Ok` | Không |
| 54 | `NotificationController` | GET | `api/notifications/{staffId:int}?unreadOnly=` | `GetNotifications` | Route: `staffId`; Query: `unreadOnly?` (bool?) | `IReadOnlyList<NotificationDto>` | 200 `Ok` | Không — đọc bảng `ShiftRequest`, **không** dùng bảng `Notifications` |
| 55 | `NotificationController` | GET | `api/notifications/{staffId:int}/unread-count` | `GetUnreadCount` | Route: `staffId` | `UnreadCountDto` | 200 `Ok` | Không |
| 56 | `NotificationController` | PUT | `api/notifications/{requestId:int}/read?staffId=` | `MarkAsRead` | Route: `requestId`; Query: `staffId` | `{ message = "Đã đánh dấu đã đọc." }` | 200 `Ok`; 404 `NotFound` (`"Không tìm thấy yêu cầu hoặc đã xử lý."`) | Không — `ExecuteUpdateAsync` ghi DB |
| 57 | `NotificationController` | PUT | `api/notifications/{staffId:int}/read-all` | `MarkAllAsRead` | Route: `staffId` | `{ message = "Đã đánh dấu {count} thông báo là đã đọc." }` | 200 `Ok` | Không — ghi DB |
| 58 | `NotificationController` | DELETE | `api/notifications/{requestId:int}` | `DeleteNotification` | Route: `requestId` | `{ message = "Không hỗ trợ xóa; yêu cầu được quản lý trong đổi ca." }` | 200 `Ok` (**luôn** — stub, không xóa) | Không |
| 59 | `ChatbotController` | POST | `api/chatbot/message` | `SendMessage` | `ChatMessageRequestDto` (body): `Message` (string), `CustomerId?` (int?), `History?` (List\<`ChatHistoryItemDto`{`Role`,`Content`}\>) | `ChatMessageResponseDto` | 200 `Ok`; 400 `BadRequest`; 500; 502 `BadGateway`; 503 `ServiceUnavailable` | Không |
| 60 | `ChatbotController` | POST | `api/chatbot/intent` | `DetectIntent` | `DetectIntentRequestDto` (body, defined in controller file): `Message` (string) | `AiIntentResult` | 200 `Ok`; 400 `BadRequest`; 500 | Không |
| 61 | `ChatbotController` | GET | `api/chatbot/context/{customerId:int}` | `GetContext` | Route: `customerId` | `ConversationContextDto` | 200 `Ok`; 500 | Không |
| 62 | `ChatbotController` | GET | `api/chatbot/health` | `GetHealth` | Không | `ChatbotHealthDto` (`Status`, `Message`, `Timestamp`, `AiProviderConfigured`, `DatabaseConnected`) | 200 `Ok` (**luôn 200**, kể cả khi `degraded`) | Không |
| 63 | `BreedPricingController` | GET | `api/breed-pricing` | `GetAll` | Không | `IReadOnlyList<BreedPricingDto>` | 200 `Ok` | Không |
| 64 | `BreedPricingController` | GET | `api/breed-pricing/breed/{breedId:int}` | `GetByBreed` | Route: `breedId` | `BreedPricingDto` | 200 `Ok`; 404 `NotFound` (`"Không tìm thấy BreedPricing cho giống loài này."`) | Không |
| 65 | `BreedPricingController` | POST | `api/breed-pricing` | `Create` | `CreateBreedPricingRequest` (body): `BreedId` (int), `PriceAdjustPercent` (decimal) | `BreedPricingDto` | 201 `CreatedAtAction`; 400 `BadRequest` | Không — **ghi DB** |
| 66 | `BreedPricingController` | PUT | `api/breed-pricing/{id:int}` | `Update` | `UpdateBreedPricingRequest` (body): `PriceAdjustPercent` (decimal) | `BreedPricingDto` | 200 `Ok`; 400 `BadRequest`; 404 `NotFound` | Không — **ghi DB** |
| 67 | `BreedPricingController` | DELETE | `api/breed-pricing/{id:int}` | `Delete` | Route: `id` | Rỗng (204) | 204 `NoContent`; 404 `NotFound` | Không — **xoá cứng DB** |
| 68 | `BreedPricingController` | POST | `api/breed-pricing/seed-all` | `SeedAllBreeds` | Không | `IReadOnlyList<BreedPricingDto>` | 200 `Ok` | Không — **bulk upsert toàn bộ breed** |
| 69 | `PetServicesController` | GET | `api/pet-services` | `GetAll` | Không | `IReadOnlyList<PetServiceDto>` | 200 `Ok` | Không |
| 70 | `PetServicesController` | GET | `api/pet-services/grouped` | `GetGrouped` | Không | `PetServiceGroupedDto` (`SpaServices`, `HealthCheckServices`, `SeparatorText`) | 200 `Ok` | Không |
| 71 | `PetServicesController` | GET | `api/pet-services/{serviceId:int}` | `GetDetail` | Route: `serviceId` | `PetServiceDetailDto` | 200 `Ok`; 404 `NotFound` (**không body**) | Không |
| 72 | `ReviewController` | GET | `api/reviews/services/{serviceId:int}` | `GetServiceReviews` | Route: `serviceId` | `IReadOnlyList<ReviewDisplayDto>` | 200 `Ok` | Không |
| 73 | `ReviewController` | GET | `api/reviews/products/{productId:int}` | `GetProductReviews` | Route: `productId` | `IReadOnlyList<ReviewDisplayDto>` | 200 `Ok` | Không |
| 74 | `ReviewController` | POST | `api/reviews/products/{productId:int}?customerId=` | `UpsertProductReview` | Route: `productId`; Query: `customerId`; `CreateProductReviewRequestDto` (body): `OrderId` (int), `Rating` (int), `Comment?` | `SimpleMessageDto` | 200 `Ok`; 400 `BadRequest` (`"Thiếu dữ liệu đánh giá."` hoặc `result.Message`) | Không — **upsert, `customerId` tự khai** |
| 75 | `ProductsController_temp` | GET | `api/products-temp?keyword=&categoryId=` | `GetAll` | Query: `keyword?`, `categoryId?` | `IReadOnlyList<ProductDto_temp>` | 200 `Ok` | Không |
| 76 | `ProductsController_temp` | GET | `api/products-temp/categories?keyword=` | `GetCategories` | Query: `keyword?` | `IReadOnlyList<ProductCategoryFilterDto_temp>` | 200 `Ok` | Không |
| 77 | `ProductsController_temp` | GET | `api/products-temp/{id:int}` | `GetById` | Route: `id` | `ProductDto_temp` | 200 `Ok`; 404 `NotFound` (**không body**) | Không |
| 78 | `OrdersController_temp` | GET | `api/orders-temp/history?customerId=` | `History` | Query: `customerId` | `IReadOnlyList<OrderSummaryDto_temp>` | 200 `Ok` | Không |
| 79 | `OrdersController_temp` | GET | `api/orders-temp/{orderId:int}?customerId=` | `Detail` | Route: `orderId`; Query: `customerId` | `OrderDetailDto_temp` | 200 `Ok`; 404 `NotFound` (`"Không tìm thấy đơn hàng."`) | Không |
| 80 | `OrdersController_temp` | GET | `api/orders-temp/staff/pending` | `StaffPending` | Không | `IReadOnlyList<OrderSummaryDto_temp>` | 200 `Ok` | Không — endpoint dành cho staff nhưng **không auth** |
| 81 | `OrdersController_temp` | GET | `api/orders-temp/staff?status=` | `StaffByStatus` | Query: `status?` (string) | `IReadOnlyList<OrderSummaryDto_temp>` | 200 `Ok` | Không — **không auth** |
| 82 | `OrdersController_temp` | PATCH | `api/orders-temp/staff/{orderId:int}/confirm` | `StaffConfirm` | Route: `orderId` | `OrderSummaryDto_temp` | 200 `Ok`; 400 `BadRequest`; 404 `NotFound` | Không — **ghi DB**, staff endpoint không auth |
| 83 | `OrdersController_temp` | PATCH | `api/orders-temp/{orderId:int}/delivered?customerId=` | `CustomerDelivered` | Route: `orderId`; Query: `customerId` | `OrderSummaryDto_temp` | 200 `Ok`; 400 `BadRequest`; 404 `NotFound` | Không — **ghi DB**, `customerId` tự khai |
| 84 | `CheckoutController_temp` | POST | `api/checkout-temp` | `CreateAsync` | `CheckoutRequest_temp` (body): `CustomerId?`, `Email?`, `CustomerName?`, `Address` (string), `PaymentMethod` (string), `Items` (List\<`CheckoutItem_temp`{`ProductId`,`ItemName`,`Quantity`,`UnitPrice`}\>), `ReturnUrl?`, `CancelUrl?` | `CheckoutResponse_temp` (`PaymentMethod`, `RedirectUrl?`, `OrderCode`, `OrderId`) | 200 `Ok`; 400 `BadRequest` | Không — **tạo Order + OrderDetail trong DB** |
| 85 | `CartController` (MVC) | GET | `/cart` | `Index` | Không | `ViewResult` (Razor view) | 200 | Không — dùng **Session** (`SessionKey = "cart"`) |
| 86 | `CartController` (MVC) | POST | `/cart/dec/{id:int}` | `Decrement` | Route: `id` | `RedirectToAction` → `/cart` | 302/303 redirect | Không — session |
| 87 | `CartController` (MVC) | POST | `/cart/remove/{id:int}` | `Remove` | Route: `id` | `RedirectToAction` → `/cart` | 302/303 redirect | Không — session |
| 88 | `CartController` (MVC) | POST | `/cart/add/{id:int}` | `Add` | Route: `id`; `[FromForm]`: `qty` (int=1), `goCart` (bool=false), `returnUrl?` (string) | `RedirectToAction` / `Redirect` | 302/303 redirect; 404 `NotFound` (`"Không tìm thấy sản phẩm."`); 400 `BadRequest` (`"Sản phẩm đã hết hàng."`) | Không — session |
| 89 | `ProductsPageController` (MVC) | GET | `/` **&** `/products` | `Index` | Không | `Redirect` → `http://localhost:5173/products` | 302 | Không — **hardcode URL localhost:5173** |
| 90 | `ProductsPageController` (MVC) | GET | `/products/{id:int}` | `Details` | Route: `id` | `ViewResult` (Razor view) | 200; 404 `NotFound` (`"Không tìm thấy sản phẩm."`) | Không |

---

## GHI CHÚ HÀNH VI (theo controller)

### `AuthController` — `api/auth`
- Trả `AuthResponseDto` (`UserId`, `Name`, `Email`, `Role`, `Status`). Phân biệt lỗi qua exception: `InvalidOperationException` → 409 (register) / 500 (google-login, send-otp); `UnauthorizedAccessException` → 401; `ArgumentException` → 400; `KeyNotFoundException` → 404.
- Message trả về cho người dùng là tiếng Việt: `"Đã gửi OTP đến email."`, `"Đặt lại mật khẩu thành công."`, `"Google Client ID chưa được cấu hình."`.
- `send-email-otp` bắt mọi `Exception` → 500 với message `"Gửi OTP thất bại: {ex.Message}"` (rò rỉ chi tiết lỗi nội bộ).
- OTP/reset-password đều **không có rate-limit**, không có CAPTCHA; `verify-email-otp` trả 401 nếu OTP sai.
- Endpoint `google-client-id` **bị lặp**: tồn tại ở cả `api/auth/google-client-id` (#7) và `api/config/google-client-id` (#8), cùng logic, cùng message lỗi.

### `ConfigController` — `api/config`
- Chỉ 1 endpoint: lấy `GoogleAuth:ClientId` từ configuration, trả 404 nếu rỗng: `"Google Client ID chưa được cấu hình."`.
- **Trùng lặp** với `AuthController.GetGoogleClientId`.

### `PetsController` — `api/pets`
- **Quyền truy cập kiểu thủ công**: `ValidateSelfAccess` đọc header `X-User-Role` và `X-User-Id` do client gửi. Nếu role không phải `user`/`staff` → 403 `"Chỉ user/staff được quản lý pet của chính mình."`; thiếu/sai định dạng `X-User-Id` → 401 `"Thiếu thông tin người dùng."`; `X-User-Id != customerId` → 403 `"Bạn chỉ được phép CRUD pet của chính mình."`.
- Validation tiếng Việt (trả 400): `"CustomerId không hợp lệ."`, `"Tên thú cưng là bắt buộc."`, `"Tuổi thú cưng không hợp lệ."` (0-60), `"Giới tính là bắt buộc."`, `"Giới tính không hợp lệ. Vui lòng chọn Đực/Cái (male/female)."`, `"Cân nặng không hợp lệ."`, `"Khách hàng không tồn tại trong hệ thống."`, `"Giống loài không tồn tại."`.
- `Gender` được normalize về `"male"`/`"female"` (chấp nhận `"đực"`, `"duc"`, `"m"`, `"cái"`, `"cai"`, `"f"`). Sai → 400 với message ở trên.
- **Soft delete**: `DELETE api/pets/{petId}` set `DeletedAt = DateTime.Now` (giữ lại lịch sử y tế / booking), trả 204. Mọi query đọc đều lọc `DeletedAt == null`.
- **Upload file** (`POST api/pets/upload-image`): `[FromForm] IFormFile image`, giới hạn **5MB** (`"Ảnh vượt quá 5MB."`), chỉ nhận `ContentType` bắt đầu bằng `image/` (`"Chỉ cho phép tệp ảnh."`), rỗng → `"Vui lòng chọn ảnh hợp lệ."`. Lưu vào `wwwroot/uploads/pets/pet_{Guid:N}{ext}` (ext được lọc bằng regex `[^a-zA-Z0-9\.]`), trả `{ imageUrl = "/uploads/pets/{fileName}" }`. Endpoint bị `[ApiExplorerSettings(IgnoreApi = true)]` ⇒ **ẩn khỏi Swagger**.
- **Truy vấn đặc biệt**: `GetPetsWithPricing` tính `finalPrice = effectiveBasePrice * PriceAdjustPercent / 100` (80 = giảm 20%, 120 = tăng 20%, 100 = giá gốc), clamp tối thiểu 0. `basePrice` query có thể override giá dịch vụ ⇒ **client có thể tự đặt giá** (cần kiểm lại khi migrate). Dịch vụ không tồn tại → 400 `"Dịch vụ không tồn tại."`.
- `GetPetsByCustomer` (#9) **không** kiểm tra quyền và trả về `PriceAdjustPercent = null`, `FinalPrice = null` (không join bảng pricing).

### `SpaBookingController` — `api/spa-booking`
- Lưu ý: `Create` (#23) **trả 200 `Ok`** chứ không phải 201 dù tạo booking.
- Quy tắc đặt lịch (ném exception trong `SpaBookingService` → 400): `"Vui lòng chọn pet."`, `"Giỏ dịch vụ trống."`, `"Không tìm thấy pet của khách hàng."`, `"Dịch vụ không hợp lệ."`, `"Thời gian hẹn không được nằm trong quá khứ."`, `"Ca tối (bắt đầu từ 19:00) không nhận đặt dịch vụ. Vui lòng chọn ca sáng hoặc ca chiều."`, `"Giờ bắt đầu ({HH:mm}) sau giới hạn đặt lịch (22:00). Vui lòng chọn thời gian sớm hơn."`.
- **Slot capacity**: tối đa 2 pet/slot; hết slot → `SlotCapacityException` → 400 với `{ message, suggestedStart }` (ISO `"o"` format) để FE popup dùng trực tiếp. Nếu lỗi là `InvalidOperationException`, controller parse message bằng regex `(\d{2}/\d{2}/\d{4})\s+(\d{2}:\d{2})` để lấy `suggestedStart` (fallback).
- **Side-effect lớn**: dùng `SchedulingLock` (semaphore) + danh sách `createdBookingIds` để **rollback** nếu fail giữa chừng (tránh partial success). Chia pet thành "wave" — wave sau tự tìm khung gần nhất còn slot.
- `UpdateStatus` (#27): status hợp lệ do service kiểm, sai → `ArgumentException("Trạng thái không hợp lệ.")` → 400; không tìm thấy booking → `InvalidOperationException("Không tìm thấy booking để cập nhật trạng thái.")` → 404.
- `Review` (#28): rating phải 1-5 (`"Rating phải từ 1 đến 5."`); phải có booking đã hoàn thành (`"Bạn chưa hoàn thành dịch vụ này."`). **Luôn tạo review mới, không ghi đè** (cho phép đánh giá nhiều lần).
- `Estimate` (#22) **không bọc try/catch** ⇒ lỗi validation từ service có thể trả 500 thay vì 400 (khác `Create`).
- `HistoryAll` (#25) gắn **2 route** trên cùng action: `api/spa-booking/history/all` và `api/spa-booking/all-bookings`.

### `PayOSController` — `api/payos`
- **Webhook** (`POST api/payos/webhook`): nhận raw JSON + header `x-payos-signature`; gọi `_payOS.VerifyWebhook(raw, signature)` — sai → 401 `"Invalid signature"`; xử lý fail → 400 `{ message }`. Đây là endpoint duy nhất có xác thực thật sự.
- `spa/create`: gom `BookingIds` + `BookingId` (loại trùng, bỏ id ≤ 0). Không có id → 400 `"Thiếu bookingId để tạo thanh toán."`; booking không tồn tại → 404 `"Một hoặc nhiều booking không tồn tại."`; booking thuộc nhiều khách hàng → 400 `"Không thể gộp thanh toán cho booking của nhiều khách hàng."`; tổng tiền ≤ 0 → 400 `"Booking không có tổng tiền."`.
- **Side-effect**: tạo bản ghi `Payment` (`PaymentType = "spa"`, `PaymentStatus = "pending"`, `PaymentMethod = "PayOS"`) **trước** khi gọi PayOS ⇒ nếu PayOS fail vẫn còn payment row "pending" (không rollback).
- `order/create`: order không tồn tại → 404 `"Order không tồn tại."`; tổng ≤ 0 → 400 `"Order không có tổng tiền."`. Cùng pattern tạo `Payment` pending trước.
- `GenerateOrderCode` ghép timestamp Unix ms + seed `% 1000` ⇒ **không đảm bảo duy nhất tuyệt đối** khi gọi song song.
- Lỗi PayOS trả 400 với `{ message = "Không tạo được link PayOS.", error, response }` — có thể **rò rỉ response raw** của PayOS.
- Lưu ý: `CheckoutController_temp` (`api/checkout-temp`) cũng tạo link PayOS ⇒ **2 luồng thanh toán song song**, cần hợp nhất khi migrate.

### `ShiftExchangeController` — `api/shift-exchange`
- `verify-network` (#32): chỉ để kiểm tra IP mạng công ty; đọc theo thứ tự `X-Client-IP` → `X-Real-IP` → `Items["RealClientIp"]` → `X-Forwarded-For[0]` → `RemoteIpAddress` → `"unknown"`. Prefix allowlist từ config `Attendance:AllowedIpPrefixes` (mặc định `"192.168.1."`). Normalize IPv6-mapped-IPv4 và loopback về `127.0.0.1`. **Dựa hoàn toàn vào header do client gửi ⇒ không an toàn.**
- Quy tắc nghiệp vụ tạo yêu cầu (`CreatePass` #36 / `CreateSwap` #37), tất cả trả 400 với `ShiftExchangeActionResultDto("error", ...)`:
  - Ngày trong quá khứ: `"Không thể gửi yêu cầu cho ngày trong quá khứ."` / `"Không thể đổi ca cho ngày trong quá khứ."`
  - Ngày hôm nay: `"Đã qua 0h sáng ngày hôm nay, không thể gửi yêu cầu đổi ca cùng ngày."` / `"...không thể đổi ca nhận ngày hôm nay."`
  - Đã chấm công: `"Bạn đã chấm công ngày này rồi, không thể yêu cầu đổi ca."` / `"Nhân viên được đổi đã chấm công ngày kia rồi, không thể đổi."`
  - Sai ca: `"Không tìm thấy ca của bạn để nhờ làm thay."` / `"Ngày ca không khớp với ca đã chọn."` / `"Không tìm thấy ca của bạn để đổi."` / `"Ngày ca của bạn không khớp."` / `"Không tìm thấy ca của nhân viên được chọn để đổi."` / `"Ngày ca muốn đổi không khớp."`
  - Trùng yêu cầu Pending (cùng `ScheduleId`): `"Ca (dòng lịch) này đã có yêu cầu nhờ làm thay đang chờ duyệt. Huỷ yêu cầu cũ nếu muốn gửi lại."`
  - Người được nhờ đã có ca cùng khung giờ: `"Nhân viên được nhờ đã có ca cùng khung giờ ngày {dd/MM/yyyy} rồi, không thể nhận thêm ca này."`
- `AcceptRequest` (#40) — **side-effect mạnh**: với `Type = "Swap"` thì **hoán đổi `StaffId`** giữa `myShift` và `partnerShift` trong bảng `WorkSchedules`; với `Type = "Leave"` thì chuyển `myShift.StaffId = responderStaffId`. Sau đó gọi `RemoveDuplicateSlotRowsForStaffAsync` (giữ dòng `ScheduleId` nhỏ nhất, xoá các dòng trùng khung giờ còn lại). Đã chấm công → tự set `Status = "Rejected"`, trả 400 `"Bạn đã chấm công ngày đó rồi, không thể nhận đổi."`.
- `AcceptRequest`/`DeclineRequest` đều lọc `r.ToStaffId == responderStaffId` — **`responderStaffId` lấy từ query string do client gửi**, không xác thực.
- `CancelRequest` (#42): lọc `r.EmployeeId == staffId` (cũng từ query).
- Thông báo lỗi ở `accept` có format ngày `{dd/MM/yyyy}` (ví dụ `"Bạn đã có ca cùng khung giờ ngày 08/11/2026 rồi, không thể nhận thêm ca nữa."`).
- **Rò rỉ log**: dùng `Console.WriteLine` in `staffId`, `requestId`, `ScheduleId`, `CreatedAt` ra stdout ở hầu hết các action.

### `AttendanceController` — `api/attendance`
- `GET unclosed` (#43) chỉ trả danh sách nhân viên chưa checkout; có tham số `date?` (DateOnly) để lọc theo ngày.
- `GET staff/{staffId}/overview` (#44) — tổng quan chấm công cá nhân.
- `POST staff/{staffId}/toggle` (#45) — **toggle check-in/check-out** (side-effect). Chỉ trả 200 khi `result.Status == "success"`, ngược lại 400 với toàn bộ payload result.
- Không có endpoint xoá/sửa chấm công thủ công. Không có auth ⇒ bất kỳ ai cũng toggle được.

### `PayrollController` — `api/payroll`
- Message lỗi **KHÔNG CÓ DẤU tiếng Việt** (khác với các controller khác): `"Khong tim thay nhan vien."`, `"Ngay ket thuc phai lon hon hoac bang ngay bat dau."`.
- `periodStart`/`periodEnd` là **query parameter bắt buộc** kiểu `DateOnly` (thiếu ⇒ model binding 400 mặc định của `[ApiController]`).
- `generate` (#48) và `generate-all` (#49) là **side-effect ghi bảng lương** — `GET` cũng sinh dữ liệu (GET có side-effect ⇒ không idempotent, không cache-safe).
- `detail` (#47) trả 404 nếu không có nhân viên; `config/staff/{staffId}` (#51) trả 404 nếu `result.Status == "error"`.
- `UpdateSalaryConfig` (#52) là `PUT` không có route param — `StaffId` nằm trong body.
- `staff-list` (#53) trả `IReadOnlyList<object>` (anonymous type: `StaffId`, `Name`, `Position`) — không có DTO rõ ràng ⇒ cần định nghĩa lại khi migrate.

### `NotificationController` — `api/notifications`
- **Không dùng bảng `Notifications`**. Comment ghi rõ: "API tương thích frontend — dữ liệu lấy từ bảng `ShiftRequest`". Nguồn thông báo = các yêu cầu đổi ca `Status == "Pending"` gửi cho `ToStaffId`.
- `GET {staffId}` (#54): lọc `ToStaffId == staffId && Status == "Pending"`; `unreadOnly=true` thêm điều kiện `ToNotified == false || null`; **giới hạn `Take(50)`**, sắp xếp `CreatedAt` giảm dần.
- `Title` dựng từ `Type`: `"Swap"` → `"Yêu cầu đổi ca"`, ngược lại → `"Yêu cầu làm thay"`. `Message`: `"Yêu cầu đổi ca (Request #{id})"` hoặc `"Yêu cầu nhờ làm thay (Request #{id})"`.
- `PUT {requestId}/read` (#56) và `PUT {staffId}/read-all` (#57) dùng `ExecuteUpdateAsync` (không load entity) ⇒ **ghi thẳng DB, không raise notification event**. `read-all` trả `"Đã đánh dấu {count} thông báo là đã đọc."`.
- `DELETE {requestId}` (#58) là **stub**: luôn trả 200 `"Không hỗ trợ xóa; yêu cầu được quản lý trong đổi ca."` ⇒ client có thể tưởng đã xoá.

### `ChatbotController` — `api/chatbot`
- Message lỗi **tiếng Anh** (khác phần còn lại của dự án): `"Message cannot be empty."`, `"Message is too long. Maximum 2000 characters allowed."`, `"Chatbot service is not available."`, `"AI service temporarily unavailable."`, `"An unexpected error occurred."`, `"Failed to detect intent."`, `"Failed to retrieve context."`.
- `POST message` (#59): validate `Message` không rỗng và **≤ 2000 ký tự**. `InvalidOperationException` (chưa cấu hình AI provider) → **503** với `Details = "No AI provider is configured. Please contact the administrator."`; `HttpRequestException` → **502**; exception khác → **500**.
- `POST intent` (#60): gọi `AnalyzeIntentWithAi` (Gemini), mọi lỗi → 500 `"Failed to detect intent."`.
- `GET context/{customerId}` (#61): lấy ngữ cảnh hội thoại theo khách hàng (pet, booking, lịch sử) — **truy vấn DB nhiều bảng**.
- `GET health` (#62): **luôn trả 200**; khi exception thì trả `Status = "degraded"`, `Message = "Chatbot service is running but may have limited functionality."`, `AiProviderConfigured = false`, `DatabaseConnected = false` ⇒ client không phân biệt được bằng status code.
- `DetectIntentRequestDto` được khai báo **trong file controller** (không nằm trong `Models/`) ⇒ khi tách project cần chuyển sang `Models`.
- **Side-effect**: chatbot đọc/ghi lịch sử hội thoại; `Program.cs` đăng ký `IChatbotService` **2 lần** (`AddScoped` trùng) và `IPayOSService`/`PayOSOptions` cũng đăng ký **trùng 2 lần**.

### `BreedPricingController` — `api/breed-pricing`
- `PriceAdjustPercent` là **phần trăm**: ràng buộc **1% – 500%**, sai → 400 `"PriceAdjust phải từ 1% đến 500%."`.
- `Create` (#65): `BreedId <= 0` → 400 `"BreedId không hợp lệ."`; breed không tồn tại → 400 `"Giống loài không tồn tại."`; **đã tồn tại pricing cho breed** → 400 `"Đã tồn tại PriceAdjust cho giống loài này. Vui lòng sử dụng PUT để cập nhật."` (dùng `POST` để update — thiếu `PUT`/`PATCH`).
- `Create` trả **201** `CreatedAtAction` trỏ tới `GetByBreed` với `{ breedId }` ⇒ URL trả về `api/breed-pricing/breed/{breedId}`.
- `Update` (#66) cập nhật theo `BreedPricingId`; không đổi `BreedId`.
- `Delete` (#67) là **xoá cứng** (`_db.BreedPricings.Remove`), trả 204.
- `seed-all` (#68): **bulk upsert toàn bộ bảng `Breeds`** — tính `priceAdjust` qua `CalculatePriceAdjust(breedName, speciesName)`; bản ghi có sẵn thì update nếu khác, không có thì add (đếm `addedCount`/`updatedCount`). **Không có auth ⇒ endpoint nguy hiểm nếu bị gọi ngoài ý muốn.**
- Query đọc luôn `Include(Breed).ThenInclude(Species)` ⇒ trả kèm `BreedName` và `SpeciesName`.

### `PetServicesController` — `api/pet-services`
- `GET` (#69) trả toàn bộ danh sách dịch vụ (không phân trang).
- `GET grouped` (#70) chia theo `ServiceType` (so sánh không phân biệt hoa/thường): `"spa"` → `SpaServices`, `"health_check"` → `HealthCheckServices`; `SeparatorText` cố định `"bạn muốn kiểm tra sức khoẻ cho thú cưng của bạn?"` (**hardcode** trong controller).
- `GET {serviceId}` (#71) → 404 **không có body** (khác phần lớn các endpoint khác trả `{ message }`).

### `ReviewController` — `api/reviews`
- `GET services/{serviceId}` (#72) và `GET products/{productId}` (#73) trả `ReviewDisplayDto` — dùng chung 1 kiểu DTO cho cả 2 loại.
- `POST products/{productId}` (#74): body null → 400 `"Thiếu dữ liệu đánh giá."`; gọi `UpsertProductReviewAsync(productId, customerId, OrderId, Rating, Comment)` — **`customerId` lấy từ query string, không xác thực**; service trả `{ Success, Message }` ⇒ `!Success` → 400 với `result.Message` (tiếng Việt từ service).
- **Upsert**: đánh giá lại sẽ ghi đè bản ghi cũ (khác hành vi của Spa review vốn luôn tạo mới).

### `ProductsController_temp` — `api/products-temp`
- 3 endpoint đọc-only, filter bằng `keyword?` và `categoryId?`.
- `GET {id}` (#77) không tìm thấy → 404 **không body**.
- Hậu tố `_temp` trong tên ⇒ **API tạm, cần chốt lại trước khi migrate**.

### `OrdersController_temp` — `api/orders-temp`
- `GET history?customerId=` (#78) và `GET {orderId}?customerId=` (#79): scope theo `CustomerId` truyền từ query (không xác thực).
- `GET staff/pending` (#80): lọc `Status == "Chờ xác nhận"`. `GET staff?status=` (#81): lọc theo status nếu có; **status dùng tiếng Việt có dấu** — `"Chờ xác nhận"` | `"Chờ giao hàng"` | `"Hoàn thành"`.
- `PATCH staff/{orderId}/confirm` (#82): `"Chờ xác nhận"` → `"Chờ giao hàng"`; sai trạng thái → 400 `"Không thể xác nhận đơn ở trạng thái: {status}"`; không tìm thấy → 404 `"Không tìm thấy đơn hàng."`.
- `PATCH {orderId}/delivered?customerId=` (#83): `"Chờ giao hàng"` → `"Hoàn thành"`; sai trạng thái → 400 `"Không thể xác nhận đã giao ở trạng thái: {status}"`.
- Projection `ProjectOrderSummary` lấy **chỉ sản phẩm đầu tiên** (`FirstOrDefault`) + đếm `ProductLineCount` ⇒ mô tả đơn có thể sai với đơn nhiều dòng.
- Cả 2 endpoint `/staff/*` **không có auth** ⇒ bất kỳ ai cũng xác nhận đơn hàng.

### `CheckoutController_temp` — `api/checkout-temp`
- Validation: address rỗng → 400 `"Vui lòng nhập địa chỉ giao hàng."`; items rỗng → 400 `"Giỏ hàng trống."`.
- **Side-effect 2 bước**: tạo `Order` (`Status = "Chờ thanh toán"` nếu `PaymentMethod == "PayOS"` ngược lại `"Chờ xác nhận"`; `PaymentStatus = "pending"` hoặc `"COD"`), `SaveChanges`, rồi tạo `OrderDetail` cho từng item và `SaveChanges` lần 2 ⇒ **không transaction ⇒ có thể tạo order trống nếu lỗi giữa chừng**.
- `Quantity <= 0` được coerce về **1** (không báo lỗi). Tổng tiền tính từ `UnitPrice` do **client gửi** ⇒ không đối chiếu giá sản phẩm trong DB (rủi ro).
- Nếu `PaymentMethod == "PayOS"`: tạo link PayOS, trả `CheckoutResponse_temp("PayOS", checkout.CheckoutUrl, orderCode, order.OrderId)`; lỗi → 400 `{ message = "Không tạo được link PayOS.", error, response }`.
- Không giảm `StockQuantity` khi checkout ⇒ **còn hàng không được kiểm soát ở luồng này** (chỉ kiểm ở `CartController.Add`).

### `CartController` (MVC, không phải `[ApiController]`)
- Lưu giỏ hàng trong **Session** (`SessionKey = "cart"`, `List<CartItem>`); `Program.cs` cấu hình `IdleTimeout = 8 giờ`, `Cookie.HttpOnly = true`.
- `POST /cart/add/{id}`: `qty < 1` → ép về 1; product không tồn tại → 404 `"Không tìm thấy sản phẩm."`; hết hàng (`StockQuantity <= 0`) → 400 `"Sản phẩm đã hết hàng."`; `qty > StockQuantity` → **clamp** về tồn kho (không báo lỗi). Đọc `Price` và `ImageUrl` snapshot vào session.
- `POST /cart/dec/{id}`: giảm 1; nếu còn ≤ 0 thì **xoá luôn item** khỏi session.
- `POST /cart/remove/{id}`: xoá item.
- Cả 3 action POST đều `RedirectToAction(nameof(Index))` ⇒ **trả 302/303, không phải JSON** ⇒ frontend phải dùng form submit, không fetch JSON.
- `POST /cart/add` hỗ trợ `returnUrl` nhưng chỉ redirect khi `Url.IsLocalUrl(returnUrl)` (chống open redirect). `goCart=true` ⇒ về `/cart`.
- Session giỏ hàng **không gắn với user** ⇒ dùng chung cookie trình duyệt, không đồng bộ theo customer.

### `ProductsPageController` (MVC)
- `GET /` và `GET /products` cùng trỏ tới `Index` → `Redirect("http://localhost:5173/products")` ⇒ **URL hardcode môi trường dev**, sẽ hỏng khi deploy production.
- `GET /products/{id}` trả **Razor View** (`ViewResult`), đọc `Product` kèm `Category` và `Supplier`; không tìm thấy → 404 `"Không tìm thấy sản phẩm."`.
- Vì dùng `MapControllerRoute` mặc định `{controller=ProductsPage}/{action=Index}/{id?}` nên còn route convention ngoài các attribute route.

---

## BẤT THƯỜNG & RỦI RO PHÁT HIỆN

### 1. 🔴 Không có hệ thống xác thực/authorization ở tầng framework
- **0 attribute `[Authorize]`** trong toàn bộ backend; `Program.cs` không có `AddAuthentication()` và không có `UseAuthentication()` (chỉ `UseAuthorization()` — vô nghĩa khi không có authentication scheme).
- Hệ quả: **mọi endpoint API đều truy cập được không cần đăng nhập**, kể cả các endpoint nghiệp vụ nhạy cảm:
  - `POST api/pets` / `PUT api/pets/{id}` / `DELETE api/pets/{id}` — tạo/sửa/xoá hồ sơ thú cưng.
  - `PATCH api/orders-temp/staff/{orderId}/confirm` — xác nhận đơn hàng.
  - `PATCH api/orders-temp/{orderId}/delivered?customerId=` — đánh dấu giao hàng (chỉ cần đoán đúng `customerId`).
  - `POST api/shift-exchange/pass|swap|accept|decline|cancel` — tạo & duyệt đổi ca (chỉ cần đoán `staffId`).
  - `POST api/payroll/generate-all`, `PUT api/payroll/config/staff` — tính lương & sửa cấu hình lương.
  - `POST api/breed-pricing/seed-all`, `DELETE api/breed-pricing/{id}` — ghi/xoá bảng giá.
  - `GET api/payroll/staff-list`, `GET api/breed-pricing` — rò rỉ dữ liệu nhân viên.
  - `POST api/attendance/staff/{staffId}/toggle` — chấm công giả.
  - `POST api/reviews/products/{productId}?customerId=` — ghi đè đánh giá.
- Các "role" hiện tại chỉ là **chuỗi do client tự khai** qua header `X-User-Role`/`X-User-Id` (`PetsController.ValidateSelfAccess`), và `responderStaffId`/`staffId`/`customerId` trong query string của các endpoint staff ⇒ **IDOR (Insecure Direct Object Reference) xuyên suốt**.

### 2. 🟠 Hệ thống role bị bỏ qua ở phần lớn endpoint
- `AuthResponseDto` trả `Role`, nhưng **không endpoint nào ngoài `PetsController` kiểm tra role**. Cần định nghĩa rõ ma trận role (`admin`, `staff`, `customer`) khi migrate.

### 3. 🟠 `Response` không nhất quán giữa các controller
| Nhóm | Quy ước |
|---|---|
| `Auth`, `Pets`, `SpaBooking`, `PayOS`, `Review`, `Orders_temp`, `Checkout_temp`, `Config` | `{ message: "..." }` (tiếng Việt) |
| `Payroll` | `{ status: "error", message: "..." }` — **message KHÔNG DẤU** |
| `Chatbot` | `{ error: "...", details?: "..." }` — **tiếng Anh** |
| `ShiftExchange` | `{ status: "success"|"error", message: "..." }` |
| `Notification` | `{ message: "..." }` |
| `PetServices` #71, `Products_temp` #77 | **404 không có body** |

⇒ Cần chuẩn hoá 1 envelope lỗi duy nhất (ví dụ ProblemDetails hoặc `{ code, message }`) khi migrate.

### 4. 🟠 Trùng lặp API cần gộp
- `google-client-id`: **2 bản** (`api/auth/google-client-id` + `api/config/google-client-id`).
- Thanh toán PayOS: **2 luồng** (`api/payos/spa/create` + `api/payos/order/create` vs `api/checkout-temp` tạo link trực tiếp).
- Danh sách sản phẩm: `api/products-temp` (service layer) vs `ProductsPageController.Details` (query trực tiếp `_db.Products`).
- Lịch ca: `api/shift-exchange/all-shifts` (tất cả) vs `api/shift-exchange/staff/{id}/my-shifts` (cá nhân) — cùng nguồn `WorkSchedules`.
- Route alias `api/spa-booking/history/all` **và** `api/spa-booking/all-bookings`.

### 5. 🟠 Controller có hậu tố `_temp` (API chưa ổn định)
`ProductsController_temp`, `OrdersController_temp`, `CheckoutController_temp` + DTO `*_temp`. Cần chốt tên/khối nghiệp vụ trước khi chuyển.

### 6. 🟠 Side-effect trên HTTP GET (không idempotent)
- `GET api/payroll/staff/{staffId}/generate` và `GET api/payroll/generate-all` → **sinh dữ liệu lương** (ghi DB).
- `POST api/breed-pricing/seed-all` → bulk upsert toàn bộ bảng giá.

### 7. 🟠 Giá tiền do client kiểm soát
- `GET api/pets/customer/{cid}/service/{sid}/pricing?basePrice=` cho phép override giá cơ sở.
- `POST api/checkout-temp` tính `TotalAmount` từ `UnitPrice` do client gửi, **không đối chiếu bảng `Products`**.
⇒ Cần chuyển sang tính giá server-side khi migrate.

### 8. 🟡 Đăng ký DI trùng lặp trong `Program.cs`
- `IPayOSService` + `PayOSOptions` đăng ký **2 lần**; `IChatbotService` đăng ký **2 lần** (cùng `AddScoped` ⇒ lấy bản cuối, vô hại nhưng gây nhiễu).
- Thiếu `UseAuthentication()` trong khi có `UseAuthorization()`.
- `AddControllersWithViews()` dùng chung cho API + MVC.

### 9. 🟡 Xác thực webhook PayOS
- `VerifyWebhook(raw, signature)` dựa trên header `x-payos-signature` — hợp lệ, nhưng cần **bảo đảm replay protection** (timestamp/nonce) và **idempotency** khi PayOS gửi lại cùng event (hiện `HandleWebhookAsync` không có idempotency key).

### 10. 🟡 Không có rate limiting / anti-abuse
- `api/auth/send-email-otp` và `api/auth/verify-email-otp` không giới hạn số lần ⇒ dễ bị **brute-force OTP** và **spam email**.
- `api/chatbot/message` gọi AI provider (Gemini) không giới hạn quota ⇒ rủi ro chi phí.
- `api/auth/register` không có CAPTCHA.

### 11. 🟡 Rò rỉ thông tin
- `PayOSController` trả `error` + `response` raw của PayOS khi tạo link thất bại.
- `AuthController.send-email-otp` trả `$"Gửi OTP thất bại: {ex.Message}"` (chi tiết lỗi nội bộ).
- `ChatbotController` trả `Details` kèm thông tin cấu hình provider.
- `ShiftExchangeController` in `Console.WriteLine` staff/request/schedule id.

### 12. 🟡 Upload file
- `POST api/pets/upload-image` chỉ kiểm `ContentType` bắt đầu bằng `image/` ⇒ **không kiểm magic bytes**, dễ bị upload file độc hại giả content-type.
- **Không kiểm dung lượng tổng theo request**, không randomize đủ mạnh (dùng `Guid.NewGuid()` — ổn), nhưng không có rate-limit ⇒ có thể lấp đầy `wwwroot/uploads/pets`.
- Không có cơ chế xoá file cũ khi pet bị xoá (soft delete).

### 13. 🟡 Hardcode môi trường
- `ProductsPageController.Index` → `Redirect("http://localhost:5173/products")`.
- `Program.cs` CORS chỉ cho `http://localhost:5173` (`WithOrigins`), `AllowAnyHeader()`, `AllowAnyMethod()`.

### 14. 🟡 Vấn đề khác
- `ChatbotController` đặt `DetectIntentRequestDto` trong file controller ⇒ khi tách project cần chuyển sang `Models`.
- `NotificationController.DeleteNotification` là stub trả 200 (không xoá) ⇒ **client có thể hiểu nhầm**.
- `NotificationController` dùng bảng `ShiftRequest` thay vì bảng `Notifications` ⇒ dữ liệu thông báo không tách biệt.
- `PetsController.GetPetsByCustomer` (#9) trả `PriceAdjustPercent = null`/`FinalPrice = null` — có thể là bug (thiếu join `BreedPricings`).
- `Program.cs` dùng `UseHttpsRedirection()` + `UseStaticFiles()` — thứ tự middleware không có `UseAuthentication()`.
- `ChatbotController.GetHealth` luôn trả HTTP 200 ⇒ monitoring dựa vào status code sẽ không phát hiện sự cố.
- `PayrollController.GetStaffPayrollDetail` / `GenerateStaffPayroll` dùng `DateOnly` **query param bắt buộc** ⇒ format phải là `yyyy-MM-dd`.
- Các action `Estimate` (`api/spa-booking/estimate`) không bắt exception ⇒ có thể trả 500 thay vì 400.
