using Microsoft.AspNetCore.Mvc;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers;

/// <summary>
/// LS-10 / SL-04 — Endpoint demo cho Custom Input Formatter (slide PRN232 Ch03 S17).
///
/// <para>
/// Formatter <c>UpperCaseTextInputFormatter</c> được đăng ký trong
/// <c>MvcOptions.InputFormatters</c> (xem <c>Program.cs</c>). Nó sửa toàn bộ giá trị chuỗi
/// trong body JSON thành CHỮ HOA trước khi model binding diễn ra.
/// </para>
///
/// <para>
/// Vì formatter chỉ can thiệp ở tầng model binding, các tham số <c>[FromQuery]</c> không bị
/// ảnh hưởng. Vì vậy endpoint demo ở đây nhận body JSON (POST) để thấy rõ tác dụng,
/// đồng thời có 1 endpoint GET <c>/api/formatter-demo/search</c> để đối chiếu hành vi
/// query-string (không qua formatter) — giữ nguyên endpoint GET sẵn có của
/// <c>api/products-temp/search</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/formatter-demo")]
public sealed class FormatterDemoController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ILogger<FormatterDemoController> _logger;

    /// <summary>
    /// Khởi tạo controller demo formatter.
    /// </summary>
    public FormatterDemoController(
        IProductService productService,
        ILogger<FormatterDemoController> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    /// <summary>
    /// SL-04 — POST api/formatter-demo/search
    ///
    /// <para>
    /// Body JSON: <c>{ "keyword": "abc", "categoryId": 0 }</c>.
    /// Giá trị <c>keyword</c> sẽ được <c>UpperCaseTextInputFormatter</c> chuyển thành
    /// "ABC" trước khi model binding — kết quả trả về cho thấy rõ giá trị đã bị chuẩn hoá.
    /// </para>
    /// </summary>
    /// <param name="request">Body đã qua custom input formatter.</param>
    /// <param name="ct">Token huỷ request.</param>
    [HttpPost("search")]
    [ProducesResponseType(typeof(FormatterDemoResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FormatterDemoResult>> SearchWithFormatter(
        [FromBody] FormatterDemoRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "FormatterDemoController: keyword sau khi qua formatter = '{Keyword}'.",
            request.Keyword);

        var items = await _productService.SearchAsync(request.Keyword, request.CategoryId, ct);

        return Ok(new FormatterDemoResult(
            FormatterName: nameof(UpperCaseTextInputFormatter),
            ReceivedKeyword: request.Keyword ?? string.Empty,
            TotalCount: items.Count,
            Products: items));
    }

    /// <summary>
    /// SL-04 — GET api/formatter-demo/search
    ///
    /// <para>
    /// Endpoint đối chiếu: tham số query-string <c>keyword</c> KHÔNG đi qua input formatter
    /// (formatter chỉ áp dụng cho body của request), nên giá trị giữ nguyên chữ thường.
    /// Dùng endpoint này để minh hoạ rõ sự khác biệt giữa body-formatter và query-string.
    /// </para>
    /// </summary>
    /// <param name="keyword">Từ khoá tìm kiếm (giữ nguyên, không qua formatter).</param>
    /// <param name="categoryId">Lọc theo phân loại (0 = không lọc).</param>
    /// <param name="ct">Token huỷ request.</param>
    [HttpGet("search")]
    [ProducesResponseType(typeof(FormatterDemoResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<FormatterDemoResult>> SearchWithoutFormatter(
        [FromQuery] string? keyword,
        [FromQuery] int? categoryId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "FormatterDemoController (query-string): keyword giữ nguyên = '{Keyword}'.",
            keyword);

        var items = await _productService.SearchAsync(keyword, categoryId, ct);

        return Ok(new FormatterDemoResult(
            FormatterName: "(không áp dụng — query-string)",
            ReceivedKeyword: keyword ?? string.Empty,
            TotalCount: items.Count,
            Products: items));
    }
}

/// <summary>
/// Body JSON cho endpoint demo của custom input formatter.
/// </summary>
/// <param name="Keyword">Từ khoá — sẽ bị formatter chuyển sang HOA khi gửi qua POST.</param>
/// <param name="CategoryId">Lọc theo phân loại; null hoặc 0 = không lọc.</param>
public sealed record FormatterDemoRequest(string? Keyword, int? CategoryId);

/// <summary>
/// Kết quả endpoint demo của custom input formatter.
/// </summary>
/// <param name="FormatterName">Tên formatter đã áp dụng (để đối chiếu).</param>
/// <param name="ReceivedKeyword">Giá trị keyword server thực sự nhận được.</param>
/// <param name="TotalCount">Số kết quả tìm kiếm.</param>
/// <param name="Products">Danh sách sản phẩm tìm được.</param>
public sealed record FormatterDemoResult(
    string FormatterName,
    string ReceivedKeyword,
    int TotalCount,
    IReadOnlyList<ProductDto> Products);
