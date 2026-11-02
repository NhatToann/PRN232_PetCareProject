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
        public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAll([FromQuery] string? keyword, [FromQuery] int? categoryId, CancellationToken ct)
        {
            var items = await _service.GetAllAsync(keyword, categoryId, ct);
            return Ok(items);
        }

        [HttpGet("categories")]
        public async Task<ActionResult<IReadOnlyList<ProductCategoryFilterDto>>> GetCategories([FromQuery] string? keyword, CancellationToken ct)
        {
            var items = await _service.GetCategoriesAsync(keyword, ct);
            return Ok(items);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ProductDto>> GetById(int id, CancellationToken ct)
        {
            var item = await _service.GetByIdAsync(id, ct);
            return item is null ? NotFound() : Ok(item);
        }
    }

}
