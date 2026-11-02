using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PetShop.Data;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    [ApiController]
    [Route("api/checkout")]
    public sealed class CheckoutController : ControllerBase
    {
        private readonly ShopPetDatabaseContext _db;
        private readonly ILogger<CheckoutController> _logger;
        private readonly IPayOSService _payOsService;

        public CheckoutController(ShopPetDatabaseContext db, IPayOSService payOsService,
            ILogger<CheckoutController> logger)
        {
            _db = db;
            _logger = logger;
            _payOsService = payOsService;
        }

        [HttpPost]
        public async Task<ActionResult<CheckoutResponse>> CreateAsync(
            [FromBody] CheckoutRequest request,
            CancellationToken ct)
        {
            _logger.Line($"[Checkout] Received request: Address='{request.Address}', PaymentMethod='{request.PaymentMethod}', Items count={request.Items?.Count ?? 0}");
            _logger.Line($"[Checkout] CustomerId={request.CustomerId}, Email={request.Email}");

            if (string.IsNullOrWhiteSpace(request.Address))
            {
                return BadRequest(new { message = "Vui lòng nhập địa chỉ giao hàng." });
            }

            if (request.Items is null || request.Items.Count == 0)
            {
                return BadRequest(new { message = "Giỏ hàng trống." });
            }

            foreach (var item in request.Items)
            {
                _logger.Line($"[Checkout] Item: ProductId={item.ProductId}, Name={item.ItemName}, Qty={item.Quantity}, Price={item.UnitPrice}");
            }

            var total = request.Items.Sum(i => (i.Quantity <= 0 ? 1 : i.Quantity) * i.UnitPrice);
            var orderCodeInt = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue);
            var orderCode = orderCodeInt.ToString();
            var order = new Order
            {
                OrderDate = DateTime.Now,
                Status = string.Equals(request.PaymentMethod, "PayOS", StringComparison.OrdinalIgnoreCase)
                    ? "Chờ thanh toán"
                    : "Chờ xác nhận",
                TotalAmount = total,
                PaymentStatus = string.Equals(request.PaymentMethod, "PayOS", StringComparison.OrdinalIgnoreCase)
                    ? "pending"
                    : "COD",
                CustomerId = request.CustomerId,
                PaymentMethod = request.PaymentMethod,
                ShippingAddress = request.Address
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);

            foreach (var item in request.Items)
            {
                _db.OrderDetails.Add(new OrderDetail
                {
                    OrderId = order.OrderId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity <= 0 ? 1 : item.Quantity,
                    UnitPrice = item.UnitPrice
                });
            }

            await _db.SaveChangesAsync(ct);

            if (string.Equals(request.PaymentMethod, "PayOS", StringComparison.OrdinalIgnoreCase))
            {
                // Tao Payment record de webhook co the tim thay va cap nhat trang thai
                var payment = new Payment
                {
                    PaymentType = "order",
                    ReferenceId = order.OrderId,
                    CustomerId = request.CustomerId ?? 0,
                    Amount = total,
                    PaymentStatus = "pending",
                    PaymentMethod = "PayOS",
                    PayosOrderCode = orderCodeInt,
                    Note = $"Thanh toan don hang #{order.OrderId}",
                    CreatedAt = DateTime.Now
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync(ct);

                var payRequest = new CreatePayOSPaymentRequest(
                    orderCodeInt,
                    total,
                    _payOsService.NormalizeDescription($"Thanh toan don hang #{order.OrderId}"),
                    request.ReturnUrl ?? $"{Request.Scheme}://{Request.Host}/checkout/result",
                    request.CancelUrl ?? $"{Request.Scheme}://{Request.Host}/checkout/result");

                _logger.Line($"[Checkout] Creating PayOS payment. Amount={total}, OrderCode={orderCodeInt}");
                var checkout = await _payOsService.CreatePaymentLinkAsync(payRequest, ct);
                if (checkout is null)
                {
                    var error = _payOsService.GetLastError();
                    var response = _payOsService.GetLastResponse();
                    _logger.Line($"[Checkout] PayOS failed. Error={error}, Response={response}");
                    return BadRequest(new
                    {
                        message = "Không tạo được link PayOS.",
                        error,
                        response
                    });
                }

                _logger.Line($"[Checkout] PayOS success. CheckoutUrl={checkout.CheckoutUrl}");
                return Ok(new CheckoutResponse("PayOS", checkout.CheckoutUrl, orderCode, order.OrderId));
            }

            _logger.Line($"[Checkout] COD order created. OrderId={order.OrderId}, Total={total}");
            return Ok(new CheckoutResponse("COD", null, orderCode, order.OrderId));
        }
    }

    public sealed record CheckoutItem(
        [Range(1, int.MaxValue, ErrorMessage = "Mã sản phẩm không hợp lệ.")]
        int ProductId,

        [StringLength(200, ErrorMessage = "Tên sản phẩm không được vượt quá 200 ký tự.")]
        string ItemName,

        [Range(1, 100, ErrorMessage = "Số lượng phải từ 1 đến 100.")]
        int Quantity,

        [Range(0, double.MaxValue, ErrorMessage = "Đơn giá không hợp lệ.")]
        decimal UnitPrice);

    public sealed record CheckoutRequest(
        int? CustomerId,
        string? Email,
        string? CustomerName,

        [Required(ErrorMessage = "Địa chỉ giao hàng không được để trống.")]
        [StringLength(255, ErrorMessage = "Địa chỉ không được vượt quá 255 ký tự.")]
        string Address,

        [Required(ErrorMessage = "Phương thức thanh toán không được để trống.")]
        [StringLength(30, ErrorMessage = "Phương thức thanh toán không hợp lệ.")]
        string PaymentMethod,

        [Required(ErrorMessage = "Giỏ hàng không được để trống.")]
        [MinLength(1, ErrorMessage = "Giỏ hàng không được để trống.")]
        List<CheckoutItem> Items,
        string? ReturnUrl,
        string? CancelUrl);

    public sealed record CheckoutResponse(string PaymentMethod, string? RedirectUrl, string OrderCode, int OrderId);

}
