using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    /// <summary>
    /// API thanh toán.
    ///
    /// <para>
    /// <b>Quy tắc bất di bất dịch (đã sửa trong đợt fix luồng mua hàng):</b>
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>Giá và tồn kho lấy từ DB, không tin client.</b>
    /// Bản cũ dùng <c>request.Items.Sum(i =&gt; i.Quantity * i.UnitPrice)</c> ⇒
    /// client gửi <c>UnitPrice = 0</c> là tạo được đơn 0đ, gửi <c>UnitPrice = 1</c>
    /// là mua 1 sản phẩm giá 10.000đ với giá 1đ. Nay đơn giá luôn lấy từ
    /// bảng <c>Products</c>.
    /// </item>
    /// <item>
    /// <b>Kiểm tra tồn kho trước khi tạo đơn.</b>
    /// Bản cũ không hề kiểm tra ⇒ bán hàng vượt tồn kho.
    /// </item>
    /// <item>
    /// <b>Toàn bộ ghi DB nằm trong một transaction.</b>
    /// Bản cũ có 3 lần <c>SaveChangesAsync</c> rời rạc ⇒ nếu lỗi giữa chừng
    /// sẽ để lại đơn hàng không có chi tiết (hoặc chi tiết không có đơn).
    /// </item>
    /// <item>
    /// <b>PayOS lỗi ⇒ rollback đơn.</b>
    /// Bản cũ tạo đơn + payment rồi mới gọi PayOS; nếu PayOS lỗi thì đơn
    /// "Chờ thanh toán" mồ côi vẫn nằm trong DB ⇒ khách không thấy đơn,
    /// nhân viên thấy đơn treo. Nay rollback trong cùng transaction.
    /// </item>
    /// <item>
    /// <b>Ownership: CustomerId lấy từ claim.</b>
    /// Bản cũ tin <c>request.CustomerId</c> ⇒ IDOR (đặt đơn gán cho người khác).
    /// </item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/checkout")]
    [Authorize]
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
            // CustomerId lấy từ CLAIM (không tin client gửi lên).
            var claimCustomerId = User.ResolveCustomerId();
            if (!claimCustomerId.HasValue)
            {
                return Unauthorized(new { message = "Không xác định được khách hàng đang đăng nhập." });
            }

            var customerId = claimCustomerId.Value;

            if (string.IsNullOrWhiteSpace(request.Address))
            {
                return BadRequest(new { message = "Vui lòng nhập địa chỉ giao hàng." });
            }

            if (request.Items is null || request.Items.Count == 0)
            {
                return BadRequest(new { message = "Giỏ hàng trống." });
            }

            var isPayOs = string.Equals(request.PaymentMethod, "PayOS", StringComparison.OrdinalIgnoreCase);

            _logger.Line($"[Checkout] CustomerId={customerId}, PaymentMethod='{request.PaymentMethod}', Items={request.Items.Count}");

            // -----------------------------------------------------------------
            // Bước 1: nạp sản phẩm từ DB + kiểm tra tồn kho + tính tổng.
            // -----------------------------------------------------------------
            var requested = request.Items
                .GroupBy(i => i.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity <= 0 ? 1 : i.Quantity) })
                .ToList();

            var productIds = requested.Select(r => r.ProductId).ToList();

            var products = await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId, ct);

            if (products.Count != productIds.Count)
            {
                var missing = productIds.Where(id => !products.ContainsKey(id));
                return BadRequest(new
                {
                    message = "Một hoặc nhiều sản phẩm không còn tồn tại.",
                    missingProductIds = missing
                });
            }

            var details = new List<OrderDetail>();
            decimal total = 0m;

            foreach (var r in requested)
            {
                var product = products[r.ProductId];

                if (product.StockQuantity < r.Quantity)
                {
                    return BadRequest(new
                    {
                        message = $"Sản phẩm \"{product.Name}\" chỉ còn {product.StockQuantity} trong kho, không đủ {r.Quantity}.",
                        productId = product.ProductId,
                        available = product.StockQuantity
                    });
                }

                // Đơn giá LUÔN lấy từ DB.
                var unitPrice = product.Price;

                details.Add(new OrderDetail
                {
                    ProductId = product.ProductId,
                    Quantity = r.Quantity,
                    UnitPrice = unitPrice
                });

                total += unitPrice * r.Quantity;
            }

            if (total <= 0)
            {
                return BadRequest(new { message = "Tổng tiền đơn hàng không hợp lệ." });
            }

            // -----------------------------------------------------------------
            // Bước 2: tạo đơn + chi tiết + payment trong MỘT transaction.
            // -----------------------------------------------------------------
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                var orderCodeInt = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue);

                var order = new Order
                {
                    OrderDate = DateTime.Now,
                    Status = isPayOs ? "Chờ thanh toán" : "Chờ xác nhận",
                    TotalAmount = total,
                    PaymentStatus = isPayOs ? "pending" : "COD",
                    CustomerId = customerId,
                    PaymentMethod = request.PaymentMethod,
                    ShippingAddress = request.Address
                };

                _db.Orders.Add(order);
                await _db.SaveChangesAsync(ct);

                foreach (var detail in details)
                {
                    detail.OrderId = order.OrderId;
                    _db.OrderDetails.Add(detail);
                }

                await _db.SaveChangesAsync(ct);

                Payment? payment = null;
                if (isPayOs)
                {
                    payment = new Payment
                    {
                        PaymentType = "order",
                        ReferenceId = order.OrderId,
                        CustomerId = customerId,
                        Amount = total,
                        PaymentStatus = "pending",
                        PaymentMethod = "PayOS",
                        PayosOrderCode = orderCodeInt,
                        Note = $"Thanh toan don hang #{order.OrderId}",
                        CreatedAt = DateTime.Now
                    };
                    _db.Payments.Add(payment);
                    await _db.SaveChangesAsync(ct);
                }

                // -----------------------------------------------------------------
                // Bước 3: tạo link PayOS (ngoài transaction vì gọi mạng, nhưng
                // nếu lỗi ⇒ rollback transaction để không để lại đơn treo).
                // -----------------------------------------------------------------
                if (isPayOs)
                {
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
                        _logger.Line($"[Checkout] PayOS failed. Error={error}");

                        // Rollback: huỷ đơn + payment vừa tạo.
                        await tx.RollbackAsync(ct);

                        return BadRequest(new
                        {
                            message = "Không tạo được link PayOS.",
                            error,
                            response
                        });
                    }

                    await tx.CommitAsync(ct);

                    _logger.Line($"[Checkout] PayOS success. OrderId={order.OrderId}");
                    return Ok(new CheckoutResponse("PayOS", checkout.CheckoutUrl, orderCodeInt.ToString(), order.OrderId));
                }

                await tx.CommitAsync(ct);

                _logger.Line($"[Checkout] COD order created. OrderId={order.OrderId}, Total={total}");
                return Ok(new CheckoutResponse("COD", null, orderCodeInt.ToString(), order.OrderId));
            }
            catch (Exception ex)
            {
                _logger.LineError(ex, "[Checkout] Create failed, rolling back.");
                try
                {
                    await tx.RollbackAsync(ct);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LineError(rollbackEx, "[Checkout] Rollback failed.");
                }

                throw;
            }
        }
    }

    public sealed record CheckoutItem(
        [Range(1, int.MaxValue, ErrorMessage = "Mã sản phẩm không hợp lệ.")]
        int ProductId,

        [StringLength(200, ErrorMessage = "Tên sản phẩm không được vượt quá 200 ký tự.")]
        string ItemName,

        [Range(1, 100, ErrorMessage = "Số lượng phải từ 1 đến 100.")]
        int Quantity,

        // Đơn giá KHÔNG còn được tin: server luôn lấy giá từ bảng Products.
        // Giữ lại property để tương thích với client cũ (payload JSON không đổi).
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
