using Microsoft.AspNetCore.Mvc;
using PetShop.Models;

namespace PetShop.Controllers
{
    /// <summary>
    /// LS-13 / SL-05 / SL-11 — Endpoint demo cho rerun validation
    /// (slide PRN232 Ch05 S25, S27, S28).
    ///
    /// <para>
    /// Slide S25: validation chạy tự động, nhưng đôi khi muốn chạy lại thủ công — ví dụ
    /// sau khi tính một giá trị. Khi đó gọi <c>ModelState.ClearValidationState()</c> để xoá
    /// lỗi cũ của model, rồi <c>TryValidateModel(model)</c> để validate lại.
    /// </para>
    ///
    /// <para>
    /// Slide S28: DTO dùng custom validation attribute <c>PositiveQuantityAttribute</c>
    /// (kế thừa <c>ValidationAttribute</c>, override <c>IsValid</c>) cho field
    /// <c>StockQuantity</c>.
    /// </para>
    ///
    /// <para>
    /// Slide S27: field <c>AuditNote</c> gắn <c>[ValidateNever]</c> ⇒ không bị validate.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/validation-demo")]
    public sealed class ValidationDemoController : ControllerBase
    {
        private readonly ILogger<ValidationDemoController> _logger;

        public ValidationDemoController(ILogger<ValidationDemoController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// SL-13 — POST api/validation-demo/product
        ///
        /// <para>
        /// Chạy lại validation thủ công: xoá trạng thái validation hiện tại của model,
        /// sau đó validate lại model vừa nhận và trả về kết quả cùng các thông báo lỗi.
        /// </para>
        /// </summary>
        /// <param name="request">DTO demo có custom validation attribute.</param>
        /// <returns>200 khi hợp lệ; 400 khi còn lỗi validation.</returns>
        [HttpPost("product")]
        [ProducesResponseType(typeof(ValidationDemoResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        public ActionResult<ValidationDemoResult> ValidateProduct(
            [FromBody] ProductValidationDemoRequest request)
        {
            // Bỏ qua lỗi model binding để chỉ quan tâm lỗi do validate lại sinh ra.
            // (Nếu không có bước này, lỗi binding cũ sẽ lẫn vào kết quả.)
            ModelState.ClearValidationState(nameof(ProductValidationDemoRequest));

            var isValid = TryValidateModel(request);

            _logger.LogInformation(
                "ValidationDemoController: IsValid={IsValid}, StockQuantity={StockQuantity}.",
                isValid,
                request.StockQuantity);

            var errors = ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "Dữ liệu không hợp lệ."
                    : error.ErrorMessage)
                .ToList();

            var result = new ValidationDemoResult
            {
                IsValid = isValid,
                Errors = errors
            };

            if (!isValid)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}
