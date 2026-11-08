using Microsoft.AspNetCore.Mvc;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Controllers
{
    /// <summary>
    /// LS-04 — API CRUD đầy đủ cho `api/products-temp/*`.
    /// Route/method/tên field khớp 100% với `frontend/src/api/productsApi.js` của bản gốc OJT.
    /// Controller chỉ gọi Service (3-layer: Controller → Service → Repository).
    /// </summary>
    [ApiController]
    [Route("api/products-temp")]
    public sealed class ProductsTempController : ControllerBase
    {
        private readonly IProductService _service;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ProductsTempController> _logger;

        public ProductsTempController(
            IProductService service,
            IWebHostEnvironment environment,
            ILogger<ProductsTempController> logger)
        {
            _service = service;
            _environment = environment;
            _logger = logger;
        }

        /// <summary>
        /// X-08 — GET api/products-temp/search?keyword=&amp;categoryId=
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<IReadOnlyList<ProductDto>>> Search(
            [FromQuery] string? keyword,
            [FromQuery] int? categoryId,
            CancellationToken ct)
        {
            var items = await _service.SearchAsync(keyword, categoryId, ct);
            return Ok(items);
        }

        /// <summary>
        /// X-01 — POST api/products-temp/create
        /// </summary>
        [HttpPost("create")]
        public async Task<ActionResult<ProductActionResponse>> Create(
            [FromBody] ProductCreateRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Tên sản phẩm không được để trống." });
            }

            if (request.CategoryId <= 0)
            {
                return BadRequest(new { message = "Phân loại sản phẩm không hợp lệ." });
            }

            if (request.Price < 0)
            {
                return BadRequest(new { message = "Giá tiền không hợp lệ." });
            }

            if (request.StockQuantity < 0)
            {
                return BadRequest(new { message = "Số lượng không hợp lệ." });
            }

            var created = await _service.CreateAsync(request, ct);
            if (created is null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Không tạo được sản phẩm." });
            }

            return Ok(new ProductActionResponse(
                Message: "Tạo sản phẩm thành công.",
                ProductId: created.ProductId,
                Product: created));
        }

        /// <summary>
        /// X-02 — POST api/products-temp/update
        /// </summary>
        [HttpPost("update")]
        public async Task<ActionResult<ProductActionResponse>> Update(
            [FromBody] ProductUpdateRequest request,
            CancellationToken ct)
        {
            if (request.ProductId <= 0)
            {
                return BadRequest(new { message = "Mã sản phẩm không hợp lệ." });
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Tên sản phẩm không được để trống." });
            }

            if (request.CategoryId <= 0)
            {
                return BadRequest(new { message = "Phân loại sản phẩm không hợp lệ." });
            }

            if (request.Price < 0)
            {
                return BadRequest(new { message = "Giá tiền không hợp lệ." });
            }

            if (request.StockQuantity < 0)
            {
                return BadRequest(new { message = "Số lượng không hợp lệ." });
            }

            var updated = await _service.UpdateAsync(request, ct);
            if (updated is null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm cần cập nhật." });
            }

            return Ok(new ProductActionResponse(
                Message: "Cập nhật sản phẩm thành công.",
                ProductId: updated.ProductId,
                Product: updated));
        }

        /// <summary>
        /// X-03 — POST api/products-temp/delete (soft delete: Products.is_deleted = 1)
        /// </summary>
        [HttpPost("delete")]
        public async Task<ActionResult<ProductActionResponse>> Delete(
            [FromBody] ProductIdRequest request,
            CancellationToken ct)
        {
            if (request.ProductId <= 0)
            {
                return BadRequest(new { message = "Mã sản phẩm không hợp lệ." });
            }

            var deleted = await _service.SoftDeleteAsync(request.ProductId, ct);
            if (!deleted)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm cần xóa." });
            }

            return Ok(new ProductActionResponse(
                Message: "Xóa sản phẩm thành công.",
                ProductId: request.ProductId));
        }

        /// <summary>
        /// X-04 — GET api/products-temp/deleted
        /// </summary>
        [HttpGet("deleted")]
        public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetDeleted(CancellationToken ct)
        {
            var items = await _service.GetDeletedAsync(ct);
            return Ok(items);
        }

        /// <summary>
        /// X-05 — POST api/products-temp/restore
        /// </summary>
        [HttpPost("restore")]
        public async Task<ActionResult<ProductActionResponse>> Restore(
            [FromBody] ProductIdRequest request,
            CancellationToken ct)
        {
            if (request.ProductId <= 0)
            {
                return BadRequest(new { message = "Mã sản phẩm không hợp lệ." });
            }

            var restored = await _service.RestoreAsync(request.ProductId, ct);
            if (!restored)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm cần khôi phục." });
            }

            return Ok(new ProductActionResponse(
                Message: "Khôi phục sản phẩm thành công.",
                ProductId: request.ProductId));
        }

        /// <summary>
        /// X-06 — POST api/products-temp/permanent-delete
        /// </summary>
        [HttpPost("permanent-delete")]
        public async Task<ActionResult<ProductActionResponse>> PermanentDelete(
            [FromBody] ProductIdRequest request,
            CancellationToken ct)
        {
            if (request.ProductId <= 0)
            {
                return BadRequest(new { message = "Mã sản phẩm không hợp lệ." });
            }

            var removed = await _service.PermanentDeleteAsync(request.ProductId, ct);
            if (!removed)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm cần xóa vĩnh viễn." });
            }

            return Ok(new ProductActionResponse(
                Message: "Xóa vĩnh viễn sản phẩm thành công.",
                ProductId: request.ProductId));
        }

        /// <summary>
        /// X-07 — POST api/products-temp/upload-image (multipart/form-data, field "image").
        /// </summary>
        [HttpPost("upload-image")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<ActionResult<object>> UploadImage(
            [FromForm] IFormFile image,
            CancellationToken ct)
        {
            if (image is null || image.Length == 0)
            {
                return BadRequest(new { message = "Vui lòng chọn ảnh hợp lệ." });
            }

            if (image.Length > 5 * 1024 * 1024)
            {
                return BadRequest(new { message = "Ảnh vượt quá 5MB." });
            }

            var contentType = image.ContentType?.ToLowerInvariant() ?? string.Empty;
            if (!contentType.StartsWith("image/"))
            {
                return BadRequest(new { message = "Chỉ cho phép tệp ảnh." });
            }

            var uploadDir = Path.Combine(_environment.WebRootPath, "uploads", "products");
            Directory.CreateDirectory(uploadDir);

            var ext = Path.GetExtension(image.FileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".jpg";
            }

            var safeExt = System.Text.RegularExpressions.Regex.Replace(ext, @"[^a-zA-Z0-9\.]", string.Empty);
            var fileName = $"product_{Guid.NewGuid():N}{safeExt}";
            var physicalPath = Path.Combine(uploadDir, fileName);

            await using (var stream = new FileStream(physicalPath, FileMode.Create))
            {
                await image.CopyToAsync(stream, ct);
            }

            _logger.LogInformation("Đã lưu ảnh sản phẩm tại {PhysicalPath}", physicalPath);

            var url = $"/uploads/products/{fileName}";
            return Ok(new { imageUrl = url });
        }
    }
}
