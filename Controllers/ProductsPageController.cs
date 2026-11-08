using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Models;

namespace PetShop.Controllers
{
    /// <summary>
    /// Trang MVC sản phẩm — port từ OJT: <c>Projectojt\BackEnd\Controllers\ProductsPageController.cs</c>.
    ///
    /// Khác biệt có chủ đích so với bản gốc:
    /// - Bản gốc dùng attribute route <c>[HttpGet("/")]</c> + <c>[HttpGet("/products")]</c> và redirect tới
    ///   <c>http://localhost:5173/products</c> (SPA Vite/React). Ở đích, route <c>/</c> và <c>/products</c>
    ///   đã do Blazor phục vụ (port 1:1 giao diện React), nên controller này **không** chiếm route đó.
    /// - Thay vào đó, controller phục vụ route MVC convention <c>/mvc/ProductsPage/{action}</c>
    ///   (đúng route mà <c>Program.cs</c> đã map: <c>mvc/{{controller=ProductsPage}}/{{action=Index}}/{{id?}}</c>),
    ///   giữ cho route <c>/mvc/...</c> không còn 404 (gap R4).
    /// </summary>
    public sealed class ProductsPageController : Controller
    {
        private readonly ShopPetDatabaseContext _db;

        public ProductsPageController(ShopPetDatabaseContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Bản gốc redirect tới SPA ở <c>http://localhost:5173/products</c>.
        /// Ở đích không còn SPA riêng ⇒ redirect tới route Blazor tương đương <c>/products</c>.
        /// </summary>
        [HttpGet]
        public IActionResult Index()
        {
            return Redirect("/products");
        }

        /// <summary>
        /// Chi tiết sản phẩm — port nguyên văn projection của bản gốc.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Details([FromRoute] int id, CancellationToken ct)
        {
            var p = await _db.Products
                .AsNoTracking()
                .Include(x => x.Category)
                .Include(x => x.Supplier)
                .Where(x => x.ProductId == id)
                .Select(x => new ProductDto(
                    x.ProductId,
                    x.Name ?? string.Empty,
                    x.Price,
                    x.StockQuantity,
                    x.Description,
                    x.ImageUrl,
                    x.CategoryId,
                    x.Category != null ? x.Category.Name : null,
                    x.SupplierId,
                    x.Supplier != null ? x.Supplier.NameCompany : null,
                    0d,
                    0
                ))
                .FirstOrDefaultAsync(ct);

            if (p is null) return NotFound("Không tìm thấy sản phẩm.");

            return View(p);
        }
    }
}
