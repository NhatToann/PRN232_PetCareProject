using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    /// <summary>
    /// API đơn hàng.
    ///
    /// <para>
    /// <b>Sửa trong đợt fix luồng mua hàng:</b>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// Gắn <c>[Authorize]</c> cho các endpoint của khách ⇒ chặn IDOR
    /// (trước đây ai cũng xem/hủy được đơn của người khác chỉ bằng cách đổi
    /// <c>customerId</c> trên URL).
    /// </item>
    /// <item>
    /// <c>CustomerId</c> lấy từ CLAIM; nếu client gửi <c>customerId</c> khác
    /// thì bị từ chối (chống việc đọc đơn người khác bằng cách đổi query string).
    /// </item>
    /// <item>
    /// <b>Chống trừ kho 2 lần:</b> trước đây cả webhook PayOS
    /// (<c>UpdateOrderStatusAsync</c>) lẫn staff xác nhận đơn
    /// (<c>StaffConfirm</c>) đều trừ tồn kho ⇒ đơn PayOS bị trừ 2 lần.
    /// Nay chỉ trừ kho ở đúng một chỗ: <b>staff xác nhận đơn</b> (đơn COD lẫn
    /// đơn PayOS đã quét QR đều đi qua bước này). Webhook chỉ cập nhật trạng thái.
    /// </item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/orders")]
    [Authorize]
    public sealed class OrdersController : ControllerBase
    {
        private readonly ShopPetDatabaseContext _db;

        public OrdersController(ShopPetDatabaseContext db)
        {
            _db = db;
        }

        private static IQueryable<OrderSummaryDto> ProjectOrderSummary(IQueryable<Order> query) =>
            query.Select(o => new OrderSummaryDto(
                o.OrderId,
                o.OrderDate,
                o.Status,
                o.PaymentStatus,
                o.PaymentMethod,
                o.TotalAmount,
                o.ShippingAddress,
                o.OrderDetails
                    .Where(od => od.ProductId != null)
                    .OrderBy(od => od.DetailId)
                    .Select(od => od.Product != null ? od.Product.Name : null)
                    .FirstOrDefault(),
                o.OrderDetails
                    .Where(od => od.ProductId != null)
                    .OrderBy(od => od.DetailId)
                    .Select(od => od.Product != null ? od.Product.ImageUrl : null)
                    .FirstOrDefault(),
                o.OrderDetails.Count(od => od.ProductId != null)));

        /// <summary>
        /// Lấy CustomerId của người đang đăng nhập.
        /// Nếu client gửi <c>customerId</c> khác ⇒ từ chối (IDOR).
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

        [HttpGet("history")]
        public async Task<ActionResult<IReadOnlyList<OrderSummaryDto>>> History([FromQuery] int customerId, CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            var orders = await ProjectOrderSummary(
                    _db.Orders
                        .AsNoTracking()
                        .Where(o => o.CustomerId == resolvedId)
                        .OrderByDescending(o => o.OrderDate))
                .ToListAsync(ct);

            return Ok(orders);
        }

        [HttpGet("{orderId:int}")]
        public async Task<ActionResult<OrderDetailDto>> Detail(int orderId, [FromQuery] int customerId, CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            var order = await _db.Orders
                .AsNoTracking()
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == resolvedId, ct);

            if (order is null)
            {
                return NotFound(new { message = "Không tìm thấy đơn hàng." });
            }

            return Ok(new OrderDetailDto(
                order.OrderId,
                order.OrderDate,
                order.Status,
                order.PaymentStatus,
                order.PaymentMethod,
                order.TotalAmount,
                order.ShippingAddress,
                order.OrderDetails.Select(od => new OrderDetailItemDto(
                    od.ProductId ?? 0,
                    od.Product?.Name ?? "Sản phẩm",
                    od.Quantity ?? 0,
                    od.UnitPrice ?? 0m,
                    od.Product?.ImageUrl))
                .ToList()));
        }

        // Danh sách đơn chờ nhân viên xác nhận (COD).
        [HttpGet("staff/pending")]
        [Authorize(Policy = AuthPolicies.StaffOrAdmin)]
        public async Task<ActionResult<IReadOnlyList<OrderSummaryDto>>> StaffPending(CancellationToken ct)
        {
            var orders = await ProjectOrderSummary(
                    _db.Orders
                        .AsNoTracking()
                        .Where(o => o.Status == "Chờ xác nhận")
                        .OrderByDescending(o => o.OrderDate))
                .ToListAsync(ct);

            return Ok(orders);
        }

        // Staff xem đơn theo trạng thái (dùng cho UI tab).
        // status: "Chờ xác nhận" | "Chờ giao hàng" | "Hoàn thành"
        [HttpGet("staff")]
        [Authorize(Policy = AuthPolicies.StaffOrAdmin)]
        public async Task<ActionResult<IReadOnlyList<OrderSummaryDto>>> StaffByStatus([FromQuery] string? status, CancellationToken ct)
        {
            var query = _db.Orders
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(o => o.Status != null && o.Status == status);
            }

            var orders = await ProjectOrderSummary(query.OrderByDescending(o => o.OrderDate))
                .ToListAsync(ct);

            return Ok(orders);
        }

        // Nhân viên xác nhận đơn -> chuyển sang "Chờ giao hàng" và trừ stock sản phẩm.
        //
        // ĐÂY LÀ NƠI DUY NHẤT trừ kho (xem ghi chú ở đầu file):
        // webhook PayOS KHÔNG trừ kho nữa ⇒ không còn trừ 2 lần.
        [HttpPatch("staff/{orderId:int}/confirm")]
        [Authorize(Policy = AuthPolicies.StaffOrAdmin)]
        public async Task<ActionResult<OrderSummaryDto>> StaffConfirm(int orderId, CancellationToken ct)
        {
            var order = await _db.Orders
                .AsTracking()
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.OrderId == orderId, ct);
            if (order is null)
            {
                return NotFound(new { message = "Không tìm thấy đơn hàng." });
            }

            if (!string.Equals(order.Status, "Chờ xác nhận", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = $"Không thể xác nhận đơn ở trạng thái: {order.Status}" });
            }

            // Trừ stock cho từng sản phẩm trong đơn hàng.
            // Dùng ExecuteUpdateAsync để 1 câu lệnh cho mọi sản phẩm (nhanh hơn N round-trip).
            foreach (var detail in order.OrderDetails.Where(d => d.ProductId != null))
            {
                var qty = detail.Quantity ?? 1;

                await _db.Products
                    .Where(p => p.ProductId == detail.ProductId && p.StockQuantity >= qty)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - qty), ct);
            }

            order.Status = "Chờ giao hàng";
            await _db.SaveChangesAsync(ct);

            var summary = await ProjectOrderSummary(
                    _db.Orders.AsNoTracking().Where(o => o.OrderId == orderId))
                .FirstAsync(ct);

            return Ok(summary);
        }

        // Khách hàng xác nhận đã nhận hàng -> "Hoàn thành".
        [HttpPatch("{orderId:int}/delivered")]
        public async Task<ActionResult<OrderSummaryDto>> CustomerDelivered(
            int orderId,
            [FromQuery] int customerId,
            CancellationToken ct)
        {
            if (!TryResolveCustomerId(customerId, out var resolvedId))
            {
                return Forbid();
            }

            var order = await _db.Orders
                .AsTracking()
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == resolvedId, ct);
            if (order is null)
            {
                return NotFound(new { message = "Không tìm thấy đơn hàng." });
            }

            if (!string.Equals(order.Status, "Chờ giao hàng", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = $"Không thể xác nhận đã giao ở trạng thái: {order.Status}" });
            }

            order.Status = "Hoàn thành";
            await _db.SaveChangesAsync(ct);

            var summary = await ProjectOrderSummary(
                    _db.Orders.AsNoTracking().Where(o => o.OrderId == orderId))
                .FirstAsync(ct);

            return Ok(summary);
        }
    }

    public sealed record OrderSummaryDto(
        int OrderId,
        DateTime OrderDate,
        string? Status,
        string? PaymentStatus,
        string? PaymentMethod,
        decimal TotalAmount,
        string? ShippingAddress,
        string? PrimaryProductName,
        string? PrimaryProductImageUrl,
        int ProductLineCount);

    public sealed record OrderDetailItemDto(
        int ProductId,
        string ProductName,
        int Quantity,
        decimal UnitPrice,
        string? ProductImageUrl);

    public sealed record OrderDetailDto(
        int OrderId,
        DateTime OrderDate,
        string? Status,
        string? PaymentStatus,
        string? PaymentMethod,
        decimal TotalAmount,
        string? ShippingAddress,
        IReadOnlyList<OrderDetailItemDto> Items);
}
