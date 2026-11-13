using Microsoft.AspNetCore.Mvc;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Controllers
{
    [ApiController]
    [Route("api/products")]
    public sealed class ProductsController : ControllerBase
    {
        private readonly IProductService _service;

        public ProductsController(IProductService service)
        {
            _service = service;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ProductDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAll([FromQuery] string? keyword, [FromQuery] int? categoryId, CancellationToken ct)
        {
            var items = await _service.GetAllAsync(keyword, categoryId, ct);
            return Ok(items);
        }

        [HttpGet("categories")]
        [ProducesResponseType(typeof(IReadOnlyList<ProductCategoryFilterDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<ProductCategoryFilterDto>>> GetCategories([FromQuery] string? keyword, CancellationToken ct)
        {
            var items = await _service.GetCategoriesAsync(keyword, ct);
            return Ok(items);
        }

        /// <summary>
        /// Danh sách TÊN sản phẩm — payload nhẹ cho ô tìm kiếm (gợi ý ở Header).
        ///
        /// <para>
        /// <b>Vì sao cần endpoint riêng?</b>
        /// Header nằm ở MỌI trang và trước đây gọi <c>api/products</c> (toàn bộ bảng
        /// Products + Category + Supplier + aggregate Reviews) chỉ để lấy ra cột
        /// <c>Name</c>. Với 100+ sản phẩm đó là hàng trăm KB JSON mỗi lần điều hướng
        /// ⇒ độ trễ render + giữ nhiều object trong RAM của circuit.
        /// Endpoint này trả về đúng một mảng string ⇒ nhẹ hơn hàng trăm lần.
        /// </para>
        ///
        /// <para>
        /// Kết quả được cache phía server (TTL ngắn) trong
        /// <see cref="ProductService"/>/<see cref="IProductCache"/> để mọi circuit
        /// dùng chung ⇒ không phải query DB mỗi lần điều hướng.
        /// </para>
        /// </summary>
        [HttpGet("names")]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<string>>> GetNames(CancellationToken ct)
        {
            var names = await _service.GetProductNamesAsync(ct);
            return Ok(names);
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProductDto>> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item is null ? NotFound() : Ok(item);
        }
    }

}
