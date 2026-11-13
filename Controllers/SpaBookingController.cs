using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    /// <summary>
    /// API đặt lịch spa.
    ///
    /// <para>
    /// <b>Sửa trong đợt fix luồng đặt spa:</b>
    /// </para>
    /// <list type="bullet">
    /// <item>Gắn <c>[Authorize]</c> cho toàn bộ endpoint ⇒ chặn IDOR.</item>
    /// <item>
    /// <c>customerId</c> phải khớp với claim của người đang đăng nhập;
    /// nếu không khớp ⇒ <c>Forbid</c>. Trước đây chỉ cần đổi <c>customerId</c>
    /// trên URL là xem/hủy được lịch của người khác.
    /// </item>
    /// <item>
    /// Endpoint <c>history/all</c> và <c>status</c> (nhân viên thao tác) tách riêng
    /// với policy staff.
    /// </item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/spa-booking")]
    [Authorize]
        public sealed class SpaBookingController : ControllerBase
    {
        private readonly ISpaBookingService _service;
        private readonly IPayOSService _payOs;
        private readonly ShopPetDatabaseContext _db;
        private readonly ILogger<SpaBookingController> _logger;

        public SpaBookingController(ISpaBookingService service,
            IPayOSService payOs,
            ShopPetDatabaseContext db,
            ILogger<SpaBookingController> logger)
        {
            _service = service;
            _payOs = payOs;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Xác thực customerId: phải khớp claim của người đang đăng nhập.
        /// </summary>
        private bool TryResolveCustomerId(int requestedCustomerId, out int customerId)
        {
            customerId = 0;

            var claimId = User.ResolveCustomerId();
            if (!claimId.HasValue)
            {
                return false;
            }

            if (requestedCustomerId > 0 && requestedCustomerId != claimId.Value)
            {
                return false;
            }

            customerId = claimId.Value;
            return true;
        }

        [HttpGet("pets")]
        [ProducesResponseType(typeof(IReadOnlyList<PetSummaryDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<PetSummaryDto>>> GetPets([FromQuery] int customerId, CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            var pets = await _service.GetPetsAsync(resolvedId, ct);
            return Ok(pets);
        }

        [HttpGet("services")]
        [ProducesResponseType(typeof(IReadOnlyList<SpaServiceDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<SpaServiceDto>>> GetServices(CancellationToken ct)
        {
            var services = await _service.GetSpaServicesAsync(ct);
            return Ok(services);
        }

        [HttpPost("availability")]
        [ProducesResponseType(typeof(SpaAvailabilityResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SpaAvailabilityResponse>> CheckAvailability([FromBody] SpaAvailabilityRequest request, CancellationToken ct)
        {
            var result = await _service.CheckAvailabilityAsync(request.Start, request.DurationMin, request.Quantity, ct);
            return Ok(result);
        }

        [HttpPost("validate-slot")]
        [ProducesResponseType(typeof(BookingSlotValidationResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(BookingSlotValidationResult), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<BookingSlotValidationResult>> ValidateSlot([FromBody] BookingSlotValidationRequest request, CancellationToken ct)
        {
            var result = await _service.ValidateBookingSlotAsync(request, ct);
            if (!result.IsValid)
                return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("estimate")]
        [ProducesResponseType(typeof(SpaBookingEstimateDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SpaBookingEstimateDto>> Estimate([FromQuery] int customerId, [FromBody] SpaBookingEstimateRequest request, CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            var result = await _service.EstimateAsync(resolvedId, request, ct);
            return Ok(result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(SpaBookingDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SpaBookingDto>> Create([FromQuery] int customerId, [FromBody] CreateSpaBookingRequest request, CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            try
            {
                var result = await _service.CreateBookingAsync(resolvedId, request, ct);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (SlotCapacityException ex)
            {
                // Trả suggestedStart field riêng để frontend popup dùng trực tiếp, không phải parse message.
                return BadRequest(new { message = ex.Message, suggestedStart = ex.SuggestedStart?.ToString("o") });
            }
            catch (InvalidOperationException ex)
            {
                var suggestedStart = TryExtractSuggestedStart(ex.Message);
                return BadRequest(new { message = ex.Message, suggestedStart });
            }
        }

        [HttpGet("history")]
        [ProducesResponseType(typeof(IReadOnlyList<SpaBookingDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<SpaBookingDto>>> History([FromQuery] int customerId, CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            var history = await _service.GetHistoryAsync(resolvedId, ct);
            return Ok(history);
        }

        // Danh sách toàn bộ lịch đặt — chỉ nhân viên.
        [HttpGet("history/all")]
        [HttpGet("all-bookings")]
        [Authorize(Policy = AuthPolicies.StaffOrAdmin)]
        [ProducesResponseType(typeof(IReadOnlyList<SpaBookingDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<SpaBookingDto>>> HistoryAll(CancellationToken ct)
        {
            var history = await _service.GetAllHistoryAsync(ct);
            return Ok(history);
        }

        [HttpGet("{bookingId:int}/invoice")]
        [ProducesResponseType(typeof(SpaBookingInvoiceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SpaBookingInvoiceDto>> Invoice(int bookingId, CancellationToken ct)
        {
            var invoice = await _service.GetInvoiceAsync(bookingId, ct);
            if (invoice == null)
            {
                return NotFound(new { message = "Không tìm thấy booking." });
            }

            return Ok(invoice);
        }

        /// <summary>
        /// Tra cứu booking theo MÃ ĐƠN PAYOS (<c>orderCode</c>).
        ///
        /// <para>
        /// <b>VÌ SAO CẦN:</b> sau khi khách thanh toán qua PayOS, hệ thống redirect
        /// về <c>/spa-invoice</c>. Nếu đường dẫn redirect bị PayOS rút gọn hoặc trình
        /// duyệt làm mất query string ⇒ trang hóa đơn không có <c>bookingId</c> ⇒
        /// hiện "Thiếu mã booking" dù khách đã thanh toán thành công.
        /// </para>
        ///
        /// <para>
        /// Endpoint này cho phép khách tra cứu booking của CHÍNH MÌNH bằng
        /// <c>orderCode</c> (mã đơn PayOS) ⇒ trang hóa đơn có thể tự khôi phục
        /// mã booking còn thiếu. Server tự tra trong bảng <c>Payments</c> và kiểm tra
        /// <c>customerId</c> khớp claim ⇒ không lộ thông tin người khác.
        /// </para>
        /// </summary>
        [HttpGet("by-order/{orderCode:int}")]
        [ProducesResponseType(typeof(SpaBookingDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SpaBookingDto>> GetByOrderCode(
            int orderCode,
            [FromQuery] int customerId,
            CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            if (orderCode <= 0)
            {
                return NotFound(new { message = "Mã đơn không hợp lệ." });
            }

            var payment = await _db.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.PaymentType == "spa" &&
                    p.PayosOrderCode == orderCode &&
                    p.CustomerId == resolvedId,
                    ct);

            if (payment is null)
            {
                return NotFound(new { message = "Không tìm thấy giao dịch thanh toán." });
            }

            var booking = await _db.Bookings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookingId == payment.ReferenceId, ct);

            if (booking is null)
            {
                return NotFound(new { message = "Không tìm thấy booking." });
            }

            var history = await _service.GetHistoryAsync(resolvedId, ct);
            var dto = history.FirstOrDefault(b => b.BookingId == booking.BookingId);
            if (dto is null)
            {
                return NotFound(new { message = "Không tìm thấy booking." });
            }

            return Ok(dto);
        }

        /// <summary>
        /// Tra cứu booking spa ĐANG THANH TOÁN gần nhất của khách (theo payment PayOS).
        ///
        /// <para>
        /// <b>VÌ SAO CẦN (lớp dự phòng thứ ba):</b> sau khi thanh toán PayOS, trình
        /// duyệt có thể về <c>/spa-invoice</c> mà KHÔNG còn query string và localStorage
        /// cũng đã bị xoá. Khi đó endpoint này tự tra payment spa gần nhất của khách
        /// (có <c>payos_order_code</c>) ⇒ trang hóa đơn vẫn hiển thị đúng.
        /// </para>
        ///
        /// <para>
        /// Chỉ trả về booking thuộc CHÍNH khách đang đăng nhập (kiểm tra claim) ⇒
        /// không lộ thông tin người khác.
        /// </para>
        /// </summary>
        [HttpGet("latest-payos")]
        [ProducesResponseType(typeof(SpaBookingDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SpaBookingDto>> GetLatestPayOSBooking(
            [FromQuery] int customerId,
            CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            var payment = await _db.Payments
                .AsNoTracking()
                .Where(p =>
                    p.PaymentType == "spa" &&
                    p.CustomerId == resolvedId &&
                    p.PayosOrderCode != null &&
                    p.PayosOrderCode != 0)
                .OrderByDescending(p => p.PaymentId)
                .FirstOrDefaultAsync(ct);

            if (payment is null)
            {
                return NotFound(new { message = "Không tìm thấy giao dịch thanh toán spa." });
            }

            var booking = await _db.Bookings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookingId == payment.ReferenceId, ct);

            if (booking is null)
            {
                return NotFound(new { message = "Không tìm thấy booking." });
            }

            var history = await _service.GetHistoryAsync(resolvedId, ct);
            var dto = history.FirstOrDefault(b => b.BookingId == booking.BookingId);
            if (dto is null)
            {
                return NotFound(new { message = "Không tìm thấy booking." });
            }

            return Ok(dto);
        }

        /// <summary>
        /// Đồng bộ trạng thái thanh toán của booking spa từ PayOS (server-side).
        ///
        /// <para>
        /// <b>Vì sao cần:</b> trước đây trang hóa đơn spa (<c>SpaInvoice.razor</c>)
        /// tự gọi <c>PATCH api/spa-booking/{id}/status</c> với status do CLIENT quyết
        /// định — nhưng endpoint đó yêu cầu policy <c>StaffOrAdmin</c> ⇒ khách
        /// hàng luôn nhận <c>403</c> ⇒ luồng đặt spa "không ổn".
        /// </para>
        ///
        /// <para>
        /// Nay endpoint này cho phép CHÍNH khách hàng sở hữu booking gọi, nhưng:
        /// </para>
        /// <list type="number">
        /// <item>Server tự tra payment của booking trong DB (không tin client).</item>
        /// <item>
        /// Server hỏi PayOS (server → server) về trạng thái thật ⇒ client KHÔNG
        /// thể tự khai "đã thanh toán".
        /// </item>
        /// <item>Trạng thái DB được cập nhật qua đúng đường webhook chuẩn.</item>
        /// </list>
        /// </summary>
        [HttpPost("{bookingId:int}/sync-payment")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<object>> SyncPayment(int bookingId, [FromQuery] int customerId, CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            if (bookingId <= 0)
            {
                return BadRequest(new { message = "Booking id không hợp lệ." });
            }

            try
            {
                // Lấy TẤT CẢ giao dịch spa của booking này.
                //
                // VÌ SAO KHÔNG CHỈ LẤY 1:
                // Khách có thể bấm "Thanh toán" nhiều lần ⇒ tạo nhiều payment
                // (mỗi lần 1 mã đơn PayOS). Nếu chỉ lấy payment đầu tiên thì có
                // thể hỏi sai mã đơn ⇒ báo "chưa thanh toán" dù khách đã trả.
                var payments = await _db.Payments
                    .AsNoTracking()
                    .Where(p =>
                        p.PaymentType == "spa" &&
                        p.ReferenceId == bookingId &&
                        p.CustomerId == resolvedId)
                    .OrderByDescending(p => p.PaymentId)
                    .ToListAsync(ct);

                if (payments.Count == 0)
                {
                    return NotFound(new { message = "Không tìm thấy giao dịch thanh toán cho booking này." });
                }

                // Đã có giao dịch "paid" ⇒ đã thanh toán rồi, không cần hỏi lại.
                //
                // LƯU Ý: so sánh bằng `== "paid"` (chuỗi trực tiếp) vì
                // `p.PaymentStatus.Equals(...)` KHÔNG dịch được sang SQL ⇒ EF Core
                // ném lỗi "The LINQ expression ... could not be translated".
                var paid = payments.FirstOrDefault(p => p.PaymentStatus == "paid");

                if (paid is not null)
                {
                    return Ok(new { status = "Đã thanh toán", orderCode = paid.PayosOrderCode });
                }

                int? lastOrderCode = null;
                string lastStatus = payments[0].PaymentStatus ?? "pending";

                // Hỏi PayOS cho TỪNG giao dịch chưa thanh toán — cái nào đã trả thì
                // áp dụng ngay (dùng chung đường xử lý của webhook).
                foreach (var payment in payments)
                {
                    if (payment.PaymentStatus == "paid")
                    {
                        continue;
                    }

                    if (payment.PayosOrderCode is null or 0)
                    {
                        continue;
                    }

                    lastOrderCode ??= payment.PayosOrderCode;

                    // Hỏi PayOS trực tiếp (server → server) ⇒ không tin client.
                    var status = await _payOs.GetPaymentStatusAsync(payment.PayosOrderCode.Value, ct);
                    if (status is null || !status.IsPaid)
                    {
                        continue;
                    }

                    // Dùng chung đường xử lý của webhook ⇒ cập nhật booking + payment
                    // + gửi email hóa đơn một cách nhất quán.
                    await _payOs.HandleWebhookForStatusAsync(payment.PayosOrderCode.Value, status, ct);
                    return Ok(new { status = "Đã thanh toán", orderCode = payment.PayosOrderCode });
                }

                return Ok(new { status = lastStatus, orderCode = lastOrderCode });
            }
            catch (Exception ex)
            {
                _logger.LineError(ex, $"[SpaBooking] SyncPayment lỗi (booking={bookingId}): {ex.Message}");
                return BadRequest(new { message = "Không đồng bộ được trạng thái thanh toán." });
            }
        }

        [HttpPatch("{bookingId:int}/status")]
        [Authorize(Policy = AuthPolicies.StaffOrAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<object>> UpdateStatus(int bookingId, [FromBody] UpdateBookingStatusRequest request, CancellationToken ct)
        {
            _logger.Line($"[SpaBooking] UpdateStatus {bookingId} -> {request.Status}");

            try
            {
                await _service.UpdateStatusAsync(bookingId, request.Status, ct);
                return Ok(new { message = "Updated" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPatch("{bookingId:int}/cancel")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<object>> CancelBooking(
            int bookingId,
            [FromQuery] int customerId,
            CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            if (bookingId <= 0)
            {
                return BadRequest(new { message = "Booking id không hợp lệ." });
            }

            // Chặn IDOR: booking phải thuộc về khách đang đăng nhập.
            var booking = await _db.Bookings
                .AsTracking()
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.CustomerId == resolvedId, ct);

            if (booking is null)
            {
                return NotFound(new { message = "Không tìm thấy booking." });
            }

            var current = (booking.Status ?? string.Empty).Trim();

            if (current.Contains("hoàn thành", StringComparison.OrdinalIgnoreCase)
                || current.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Lịch đã hoàn thành, không thể huỷ." });
            }

            // Đã huỷ rồi ⇒ idempotent.
            if (current.Contains("hủy", StringComparison.OrdinalIgnoreCase)
                || current.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new { message = "Lịch đã được huỷ.", status = booking.Status });
            }

            try
            {
                await _service.UpdateStatusAsync(bookingId, SpaBookingStatus.Cancelled, ct);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }

            // Đồng bộ trạng thái giao dịch thanh toán (nếu có) cho khớp.
            var payment = await _db.Payments
                .AsTracking()
                .FirstOrDefaultAsync(p =>
                    p.PaymentType == "spa" &&
                    p.ReferenceId == bookingId &&
                    p.CustomerId == resolvedId,
                    ct);

            if (payment is not null && !string.Equals(payment.PaymentStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                payment.PaymentStatus = "cancelled";
                await _db.SaveChangesAsync(ct);
            }

            return Ok(new { message = "Đã huỷ lịch.", status = SpaBookingStatus.Cancelled });
        }

        [HttpPost("review")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<object>> Review([FromQuery] int customerId, [FromBody] CreateSpaReviewRequest request, CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            try
            {
                var reviewId = await _service.UpsertReviewAsync(resolvedId, request, ct);
                return Ok(new { reviewId });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        public sealed record SpaAvailabilityRequest(
            [Required(ErrorMessage = "Thời gian bắt đầu không được để trống.")]
            DateTime Start,
            [Range(5, 480, ErrorMessage = "Thời lượng phải từ 5 đến 480 phút.")]
            int DurationMin,
            [Range(1, 20, ErrorMessage = "Số lượng thú cưng phải từ 1 đến 20.")]
            int Quantity);

        public sealed record UpdateBookingStatusRequest(
            [Required(ErrorMessage = "Trạng thái không được để trống.")]
            [StringLength(30, ErrorMessage = "Trạng thái không được vượt quá 30 ký tự.")]
            string Status);

        private static string? TryExtractSuggestedStart(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var match = Regex.Match(message, @"(\d{2}/\d{2}/\d{4})\s+(\d{2}:\d{2})");
            if (!match.Success)
            {
                return null;
            }

            var text = $"{match.Groups[1].Value} {match.Groups[2].Value}";
            if (!DateTime.TryParseExact(text, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return null;
            }

            return dt.ToString("o");
        }
    }
}
