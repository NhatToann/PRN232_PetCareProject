using Microsoft.AspNetCore.Mvc;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Controllers
{
    [ApiController]
    [Route("api/pet-services")]
    public sealed class PetServicesController : ControllerBase
    {
        private readonly IPetServiceService _service;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<PetServicesController> _logger;

        public PetServicesController(
            IPetServiceService service,
            IWebHostEnvironment environment,
            ILogger<PetServicesController> logger)
        {
            _service = service;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<PetServiceDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<PetServiceDto>>> GetAll(CancellationToken ct)
        {
            var items = await _service.GetAllAsync(ct);
            return Ok(items);
        }

        [HttpGet("grouped")]
        [ProducesResponseType(typeof(PetServiceGroupedDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<PetServiceGroupedDto>> GetGrouped(CancellationToken ct)
        {
            var items = await _service.GetAllAsync(ct);

            var spaServices = items
                .Where(s => string.Equals(s.ServiceType, "spa", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var healthCheckServices = items
                .Where(s => string.Equals(s.ServiceType, "health_check", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var response = new PetServiceGroupedDto(
                SpaServices: spaServices,
                HealthCheckServices: healthCheckServices,
                SeparatorText: "bạn muốn kiểm tra sức khoẻ cho thú cưng của bạn?"
            );

            return Ok(response);
        }

        [HttpGet("{serviceId:int}")]
        [ProducesResponseType(typeof(PetServiceDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PetServiceDetailDto>> GetDetail(int serviceId, CancellationToken ct)
        {
            var detail = await _service.GetDetailAsync(serviceId, ct);
            if (detail == null)
            {
                return NotFound();
            }

            return Ok(detail);
        }

        /// <summary>
        /// X-09 — POST api/pet-services
        /// Khớp `frontend/src/api/petServicesApi.js::createPetService` (JSON body).
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(PetServiceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PetServiceDto>> Create(
            [FromBody] PetServiceCreateRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Tên dịch vụ không được để trống." });
            }

            if (string.IsNullOrWhiteSpace(request.ServiceType))
            {
                return BadRequest(new { message = "Loại dịch vụ không được để trống." });
            }

            if (request.Price < 0)
            {
                return BadRequest(new { message = "Giá tiền không hợp lệ." });
            }

            if (request.Duration <= 0)
            {
                return BadRequest(new { message = "Thời lượng không hợp lệ." });
            }

            var created = await _service.CreateAsync(request, ct);
            if (created is null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Không tạo được dịch vụ." });
            }

            return Ok(created);
        }

        /// <summary>
        /// X-10 — POST api/pet-services/upload-image (multipart/form-data, field "image").
        /// Khớp `frontend/src/api/petServicesApi.js::uploadPetServiceImage`.
        /// </summary>
        [HttpPost("upload-image")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

            var uploadDir = Path.Combine(_environment.WebRootPath, "uploads", "pet-services");
            Directory.CreateDirectory(uploadDir);

            var ext = Path.GetExtension(image.FileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".jpg";
            }

            var safeExt = System.Text.RegularExpressions.Regex.Replace(ext, @"[^a-zA-Z0-9\.]", string.Empty);
            var fileName = $"service_{Guid.NewGuid():N}{safeExt}";
            var physicalPath = Path.Combine(uploadDir, fileName);

            await using (var stream = new FileStream(physicalPath, FileMode.Create))
            {
                await image.CopyToAsync(stream, ct);
            }

            _logger.LogInformation("Đã lưu ảnh dịch vụ tại {PhysicalPath}", physicalPath);

            var url = $"/uploads/pet-services/{fileName}";
            return Ok(new { imageUrl = url, url });
        }
    }
}
