using System.Net;
using System.Text;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Test cho <see cref="ApiErrorMessageReader"/> — class chịu trách nhiệm chuyển
/// body lỗi HTTP thô thành thông điệp tiếng Việt dễ đọc.
///
/// <para>
/// Bối cảnh: user báo "lỗi thông báo sai mật khẩu" kèm body thô:
/// <code>
/// {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
///  "title":"One or more validation errors occurred.","status":400,
///  "errors":{"Password":["Mật khẩu phải có ít nhất 6 ký tự."]},"traceId":"..."}
/// </code>
/// Trước đây client trả nguyên body này cho UI ⇒ user thấy JSON khó hiểu.
/// </para>
/// </summary>
public sealed class ApiErrorMessageReaderTests
{
    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.BadRequest)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task ValidationProblemDetails_PhaiTraVeMessageLoiChuKhongPhaiJsonTho()
    {
        const string body = """
        {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
         "title":"One or more validation errors occurred.","status":400,
         "errors":{"Password":["Mật khẩu phải có ít nhất 6 ký tự."]},"traceId":"00-abc"}
        """;

        var message = await ApiErrorMessageReader.ReadAsync(Response(body));

        Assert.Equal("Mật khẩu phải có ít nhất 6 ký tự.", message);
        Assert.DoesNotContain("traceId", message, StringComparison.Ordinal);
        Assert.DoesNotContain("{", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationProblemDetails_NhieuField_PhaiGomTatCaMessage()
    {
        const string body = """
        {"title":"One or more validation errors occurred.","status":400,
         "errors":{
           "Email":["Email không hợp lệ."],
           "NewPassword":["Mật khẩu mới phải có ít nhất 6 ký tự."],
           "ConfirmPassword":["Xác nhận mật khẩu phải có ít nhất 6 ký tự."]
         }}
        """;

        var message = await ApiErrorMessageReader.ReadAsync(Response(body));

        Assert.Contains("Email không hợp lệ.", message, StringComparison.Ordinal);
        Assert.Contains("Mật khẩu mới phải có ít nhất 6 ký tự.", message, StringComparison.Ordinal);
        Assert.Contains("Xác nhận mật khẩu phải có ít nhất 6 ký tự.", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationProblemDetails_MessageTrungNhau_PhaiLoaiBoTrung()
    {
        const string body = """
        {"status":400,
         "errors":{
           "NewPassword":["Mật khẩu phải có ít nhất 6 ký tự."],
           "ConfirmPassword":["Mật khẩu phải có ít nhất 6 ký tự."]
         }}
        """;

        var message = await ApiErrorMessageReader.ReadAsync(Response(body));

        var occurrences = message.Split("Mật khẩu phải có ít nhất 6 ký tự.").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task ControllerMessage_PhaiTraVeDungMessage()
    {
        const string body = """{"message":"Mật khẩu xác nhận không khớp."}""";

        var message = await ApiErrorMessageReader.ReadAsync(Response(body));

        Assert.Equal("Mật khẩu xác nhận không khớp.", message);
    }

    [Fact]
    public async Task ProblemDetails_PhaiUuTienDetailRoiTitle()
    {
        const string body = """
        {"type":"about:blank","title":"Thao tác không thể thực hiện","status":409,
         "detail":"Record type has validation metadata defined on property that will be ignored",
         "traceId":"00-xyz"}
        """;

        var message = await ApiErrorMessageReader.ReadAsync(Response(body, HttpStatusCode.Conflict));

        Assert.Equal("Record type has validation metadata defined on property that will be ignored", message);
    }

    [Fact]
    public async Task ProblemDetails_ChiCoTitle_PhaiTraVeTitle()
    {
        const string body = """{"title":"Không tìm thấy dữ liệu","status":404}""";

        var message = await ApiErrorMessageReader.ReadAsync(Response(body, HttpStatusCode.NotFound));

        Assert.Equal("Không tìm thấy dữ liệu", message);
    }

    [Fact]
    public async Task BodyRong_PhaiTraVeThongDiepCoStatusCode()
    {
        var message = await ApiErrorMessageReader.ReadAsync(
            Response(string.Empty, HttpStatusCode.NotFound));

        Assert.Contains("404", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BodyKhongPhaiJson_PhaiTraVeNguyenVan()
    {
        const string body = "<html><body>500 - Internal Server Error</body></html>";

        var message = await ApiErrorMessageReader.ReadAsync(
            Response(body, HttpStatusCode.InternalServerError));

        Assert.Equal(body, message);
    }

    [Fact]
    public void TryExtractFromJson_BodyRong_PhaiTraVeNull()
    {
        Assert.Null(ApiErrorMessageReader.TryExtractFromJson(string.Empty));
        Assert.Null(ApiErrorMessageReader.TryExtractFromJson("   "));
    }

    [Fact]
    public void TryExtractFromJson_JsonKhongPhaiObject_PhaiTraVeNull()
    {
        Assert.Null(ApiErrorMessageReader.TryExtractFromJson("[1,2,3]"));
        Assert.Null(ApiErrorMessageReader.TryExtractFromJson("\"chuoi\""));
    }
}
