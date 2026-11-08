using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    [ApiController]
    [Route("api/payos")]
    public sealed class PayOSController : ControllerBase
    {
        private readonly IPayOSService _payOS;
        private readonly ILogger<PayOSController> _logger;
        private readonly ShopPetDatabaseContext _db;

        public PayOSController(IPayOSService payOS, ShopPetDatabaseContext db,
            ILogger<PayOSController> logger)
        {
            _payOS = payOS;
            _logger = logger;
            _db = db;
        }

        [HttpPost("spa/create")]
        [ProducesResponseType(typeof(PayOSCheckoutResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PayOSCheckoutResult>> CreateSpaPayment([FromBody] CreateSpaPayOSRequest request, CancellationToken ct)
        {
            var rawBookingIds = new List<int>();
            if (request.BookingIds is { Count: > 0 })
            {
                rawBookingIds.AddRange(request.BookingIds);
            }

            if (request.BookingId > 0)
            {
                rawBookingIds.Add(request.BookingId);
            }

            var bookingIds = rawBookingIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (bookingIds.Count == 0)
            {
                return BadRequest(new { message = "Thiếu bookingId để tạo thanh toán." });
            }

            var bookings = await _db.Bookings
                .AsNoTracking()
                .Where(b => bookingIds.Contains(b.BookingId))
                .Include(b => b.BookingServices)
                .ToListAsync(ct);

            if (bookings.Count != bookingIds.Count)
            {
                return NotFound(new { message = "Một hoặc nhiều booking không tồn tại." });
            }

            var firstBooking = bookings[0];
            var hasDifferentCustomer = bookings.Any(b => b.CustomerId != firstBooking.CustomerId);
            if (hasDifferentCustomer)
            {
                return BadRequest(new { message = "Không thể gộp thanh toán cho booking của nhiều khách hàng." });
            }

            var amount = bookings
                .SelectMany(b => b.BookingServices)
                .Sum(bs => (bs.UnitPrice ?? 0m) * bs.Quantity);

            if (amount <= 0)
            {
                return BadRequest(new { message = "Booking không có tổng tiền." });
            }

            var orderCode = GenerateOrderCode(firstBooking.BookingId);
            var bookingLabel = bookingIds.Count == 1
                ? $"#{firstBooking.BookingId}"
                : $"#{firstBooking.BookingId} (+{bookingIds.Count - 1})";
            var description = _payOS.NormalizeDescription($"Thanh toan Spa {bookingLabel}");

            var payment = new Payment
            {
                PaymentType = "spa",
                ReferenceId = firstBooking.BookingId,
                CustomerId = firstBooking.CustomerId,
                Amount = amount,
                PaymentStatus = "pending",
                PaymentMethod = "PayOS",
                PayosOrderCode = orderCode,
                Note = $"{description}; bookings={string.Join(',', bookingIds)}",
                CreatedAt = DateTime.Now
            };

            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct);

            var checkout = await _payOS.CreatePaymentLinkAsync(new CreatePayOSPaymentRequest(
                orderCode,
                amount,
                description,
                request.ReturnUrl,
                request.CancelUrl
            ), ct);

            if (checkout is null)
            {
                return BadRequest(new
                {
                    message = "Không tạo được link PayOS.",
                    error = _payOS.GetLastError(),
                    response = _payOS.GetLastResponse()
                });
            }

            return Ok(checkout);
        }

        [HttpPost("order/create")]
        [ProducesResponseType(typeof(PayOSCheckoutResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PayOSCheckoutResult>> CreateOrderPayment([FromBody] CreateOrderPayOSRequest request, CancellationToken ct)
        {
            var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == request.OrderId, ct);
            if (order is null)
            {
                return NotFound(new { message = "Order không tồn tại." });
            }

            if (order.TotalAmount <= 0)
            {
                return BadRequest(new { message = "Order không có tổng tiền." });
            }

            var orderCode = GenerateOrderCode(order.OrderId);
            var description = _payOS.NormalizeDescription($"Thanh toan don hang #{order.OrderId}");

            var payment = new Payment
            {
                PaymentType = "order",
                ReferenceId = order.OrderId,
                CustomerId = order.CustomerId ?? 0,
                Amount = order.TotalAmount,
                PaymentStatus = "pending",
                PaymentMethod = "PayOS",
                PayosOrderCode = orderCode,
                Note = description,
                CreatedAt = DateTime.Now
            };

            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct);

            var checkout = await _payOS.CreatePaymentLinkAsync(new CreatePayOSPaymentRequest(
                orderCode,
                payment.Amount,
                description,
                request.ReturnUrl,
                request.CancelUrl
            ), ct);

            if (checkout is null)
            {
                return BadRequest(new
                {
                    message = "Không tạo được link PayOS.",
                    error = _payOS.GetLastError(),
                    response = _payOS.GetLastResponse()
                });
            }

            return Ok(checkout);
        }

        [HttpPost("webhook")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Webhook([FromBody] JsonElement payload, [FromHeader(Name = "x-payos-signature")] string? signature, CancellationToken ct)
        {
            var raw = payload.GetRawText();
            _logger.Line($"[PayOS] Webhook received. Signature: {signature ?? "(null)"}");

            if (!_payOS.VerifyWebhook(raw, signature))
            {
                _logger.Line("[PayOS] Invalid signature.");
                return Unauthorized(new { message = "Invalid signature" });
            }

            var result = await _payOS.HandleWebhookAsync(raw, ct);
            if (!result.Success)
            {
                _logger.Line($"[PayOS] HandleWebhook failed: {result.Message}");
                return BadRequest(new { message = result.Message });
            }

            _logger.Line($"[PayOS] Webhook handled: {result.Message}");
            return Ok(new { message = result.Message });
        }

        [HttpGet("status/{orderCode}")]
        [ProducesResponseType(typeof(PayOSPaymentStatusResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PayOSPaymentStatusResult>> GetPaymentStatus(int orderCode, CancellationToken ct)
        {
            var payment = await _db.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PayosOrderCode == orderCode, ct);

            if (payment is null)
            {
                return NotFound(new { message = "Payment not found" });
            }

            // Neu da co trang thai trong DB thi tra ve ngay, khong can goi PayOS
            if (!string.Equals(payment.PaymentStatus, "pending", StringComparison.OrdinalIgnoreCase))
            {
                var isPaid = string.Equals(payment.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
                return Ok(new PayOSPaymentStatusResult(
                    orderCode,
                    isPaid ? "PAID" : (payment.PaymentStatus ?? "unknown"),
                    isPaid,
                    (int)payment.Amount,
                    null));
            }

            // Neu con pending, check voi PayOS de biet trang thai thuc te
            var status = await _payOS.GetPaymentStatusAsync(orderCode, ct);
            if (status is null)
            {
                return Ok(new PayOSPaymentStatusResult(
                    orderCode,
                    "unknown",
                    false,
                    (int)payment.Amount,
                    null));
            }

            // Neu PayOS tra ve da thanh toan nhung DB con pending -> goi webhook handler de cap nhat
            if (status.IsPaid)
            {
                var webhookResult = await _payOS.HandleWebhookForStatusAsync(orderCode, status, ct);
                if (webhookResult.Success)
                {
                    payment.PaymentStatus = "paid";
                    await _db.SaveChangesAsync(ct);
                }
            }
            else if (string.Equals(status.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(status.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                payment.PaymentStatus = "cancelled";
                await _db.SaveChangesAsync(ct);

                if (string.Equals(payment.PaymentType, "order", StringComparison.OrdinalIgnoreCase))
                {
                    var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderId == payment.ReferenceId, ct);
                    if (order is not null && string.Equals(order.Status, "Chờ thanh toán", StringComparison.OrdinalIgnoreCase))
                    {
                        order.Status = "Đã hủy";
                        order.PaymentStatus = "cancelled";
                        await _db.SaveChangesAsync(ct);
                    }
                }
            }

            return Ok(new PayOSPaymentStatusResult(
                orderCode,
                status.Status,
                status.IsPaid,
                status.Amount,
                status.CheckoutUrl));
        }

        [HttpPost("send-invoice")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendInvoice([FromBody] SendInvoiceRequest request, CancellationToken ct)
        {
            if (request.OrderId <= 0)
            {
                return BadRequest(new { message = "Invalid orderId" });
            }

            var order = await _db.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == request.OrderId, ct);

            if (order is null)
            {
                return NotFound(new { message = "Order not found" });
            }

            // Lay email tu tai khoan khach hang dang nhap (uu tien), fallback sang email tren don hang
            var toEmail = order.CustomerId.HasValue
                ? await _db.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == order.CustomerId.Value)
                    .Select(c => c.Email)
                    .FirstOrDefaultAsync(ct)
                : null;

            if (string.IsNullOrWhiteSpace(toEmail))
            {
                toEmail = request.Email;
            }

            if (string.IsNullOrWhiteSpace(toEmail))
            {
                return BadRequest(new { message = "Khong co email de gui hoa don." });
            }

            var items = order.OrderDetails
                .Select(d => (
                    ItemName: d.Product?.Name ?? $"San pham #{d.ProductId}",
                    Quantity: d.Quantity ?? 1,
                    UnitPrice: d.UnitPrice ?? 0m
                ))
                .ToList();

            var total = items.Sum(i => i.UnitPrice * i.Quantity);
            if (total <= 0)
            {
                total = order.TotalAmount;
            }

            var orderCodeDisplay = $"ORD-{order.OrderId:D4}";
            var customerName = order.ShippingAddress ?? "Quy khach";

            try
            {
                var emailService = HttpContext.RequestServices.GetService<IEmailService>();
                if (emailService is not null)
                {
                    await emailService.SendOrderInvoiceEmailAsync(toEmail, customerName, orderCodeDisplay, items, total, ct);
                    _logger.Line($"[PayOS] Invoice email sent to {toEmail} for order #{order.OrderId}.");
                    return Ok(new { message = $"Hoa don da duoc gui toi {toEmail}." });
                }
                else
                {
                    _logger.Line("[PayOS] EmailService not available — skipping invoice email.");
                    return Ok(new { message = "Email service unavailable." });
                }
            }
            catch (Exception ex)
            {
                _logger.Line($"[PayOS] Failed to send invoice email: {ex.Message}");
                return Ok(new { message = $"Gui email that bai: {ex.Message}" });
            }
        }

        public sealed record SendInvoiceRequest(
            [Range(1, int.MaxValue, ErrorMessage = "Mã đơn hàng không hợp lệ.")]
            int OrderId,
            string? Email);

        public sealed record CreateSpaPayOSRequest(
            [Range(1, int.MaxValue, ErrorMessage = "Mã đặt lịch không hợp lệ.")]
            int BookingId,
            IReadOnlyList<int>? BookingIds,
            [Required(ErrorMessage = "URL trả về không được để trống.")]
            string ReturnUrl,
            [Required(ErrorMessage = "URL huỷ không được để trống.")]
            string CancelUrl);

        public sealed record CreateOrderPayOSRequest(
            [Range(1, int.MaxValue, ErrorMessage = "Mã đơn hàng không hợp lệ.")]
            int OrderId,
            [Required(ErrorMessage = "URL trả về không được để trống.")]
            string ReturnUrl,
            [Required(ErrorMessage = "URL huỷ không được để trống.")]
            string CancelUrl);

        private static int GenerateOrderCode(int seed)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var code = (int)((timestamp % 1_000_000_000) * 1000 + (seed % 1000));
            return Math.Abs(code);
        }
    }
}
