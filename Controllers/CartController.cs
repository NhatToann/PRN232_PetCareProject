using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Models;

namespace PetShop.Controllers
{
    /// <summary>
    /// Giỏ hàng MVC theo session (port nguyên văn từ OJT: <c>Projectojt\BackEnd\Controllers\CartController.cs</c>).
    ///
    /// Lưu ý về route:
    /// - Bản gốc OJT có <c>GET /cart</c> (MVC) trả <c>View(items)</c> nhưng project gốc **không có thư mục Views**
    ///   ⇒ request này luôn lỗi "No view found". Giao diện giỏ hàng thật của bản gốc là React SPA
    ///   tại route <c>/cart</c> (file <c>frontend/src/pages/CartPage.jsx</c>).
    /// - Ở đích, route <c>/cart</c> đã do Blazor <c>Components/Pages/Cart.razor</c> phục vụ (port 1:1 React SPA),
    ///   nên MVC controller dùng route <c>/cart/session</c> để tránh trùng route (AmbiguousMatchException)
    ///   và để tái sử dụng đúng cấu trúc MVC mà slide yêu cầu.
    /// - Các action POST giữ nguyên hành vi nguồn: sau khi cập nhật session sẽ redirect về
    ///   <c>Index</c> (tức <c>/cart/session</c>), tương đương <c>RedirectToAction(nameof(Index))</c> của bản gốc.
    /// </summary>
    public sealed class CartController : Controller
    {
        private const string SessionKey = "cart";
        private readonly ShopPetDatabaseContext _db;

        public CartController(ShopPetDatabaseContext db)
        {
            _db = db;
        }

        /// <summary>
        /// GET /cart/session — hiển thị giỏ hàng lưu trong session (tương đương GET /cart của bản gốc).
        /// </summary>
        [HttpGet("/cart/session")]
        public IActionResult Index()
        {
            var items = HttpContext.Session.GetJson<List<CartItem>>(SessionKey) ?? new List<CartItem>();
            return View(items);
        }

        [HttpPost("/cart/dec/{id:int}")]
        public IActionResult Decrement([FromRoute] int id)
        {
            var items = HttpContext.Session.GetJson<List<CartItem>>(SessionKey) ?? new List<CartItem>();
            var idx = items.FindIndex(x => x.ProductId == id);
            if (idx >= 0)
            {
                var current = items[idx];
                var nextQty = current.Quantity - 1;
                if (nextQty <= 0) items.RemoveAt(idx);
                else items[idx] = current with { Quantity = nextQty };
                HttpContext.Session.SetJson(SessionKey, items);
            }
            return RedirectToCartIndex();
        }

        [HttpPost("/cart/remove/{id:int}")]
        public IActionResult Remove([FromRoute] int id)
        {
            var items = HttpContext.Session.GetJson<List<CartItem>>(SessionKey) ?? new List<CartItem>();
            var idx = items.FindIndex(x => x.ProductId == id);
            if (idx >= 0)
            {
                items.RemoveAt(idx);
                HttpContext.Session.SetJson(SessionKey, items);
            }
            return RedirectToCartIndex();
        }

        [HttpPost("/cart/add/{id:int}")]
        public async Task<IActionResult> Add(
            [FromRoute] int id,
            [FromForm] int qty = 1,
            [FromForm] bool goCart = false,
            [FromForm] string? returnUrl = null,
            CancellationToken ct = default)
        {
            if (qty < 1) qty = 1;

            var p = await _db.Products
                .AsNoTracking()
                .Where(x => x.ProductId == id)
                .Select(x => new { x.ProductId, x.Name, x.Price, x.StockQuantity, x.ImageUrl })
                .FirstOrDefaultAsync(ct);

            if (p is null) return NotFound("Không tìm thấy sản phẩm.");
            if (p.StockQuantity <= 0) return BadRequest("Sản phẩm đã hết hàng.");
            if (qty > p.StockQuantity) qty = p.StockQuantity;

            var items = HttpContext.Session.GetJson<List<CartItem>>(SessionKey) ?? new List<CartItem>();
            var idx = items.FindIndex(x => x.ProductId == id);
            if (idx >= 0)
            {
                var current = items[idx];
                items[idx] = current with { Quantity = current.Quantity + qty };
            }
            else
            {
                items.Add(new CartItem(p.ProductId, p.Name, p.Price, qty, p.ImageUrl));
            }

            HttpContext.Session.SetJson(SessionKey, items);

            if (goCart) return RedirectToCartIndex();

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToCartIndex();
        }

        /// <summary>
        /// Redirect tới action Index của chính controller này.
        /// Bản gốc dùng <c>RedirectToAction(nameof(Index))</c> nhưng thiếu action Index nên nhận HTTP 500
        /// (InvalidOperationException: No route matches). Ở đích đã bổ sung action Index nên
        /// <c>RedirectToAction</c> hoạt động đúng — dùng URL tường minh để không phụ thuộc route convention.
        /// </summary>
        private IActionResult RedirectToCartIndex() => Redirect("/cart/session");
    }
}
