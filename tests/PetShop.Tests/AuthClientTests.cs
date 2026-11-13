using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Test cho <see cref="AuthClient"/> — self-HTTP client mà UI Blazor dùng để gọi
/// chính API của ứng dụng.
///
/// <para>
/// Vì sao cần test này:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     <b>Route lệch</b>: trước đây client gọi <c>api/auth/send-otp</c> trong khi
///     controller expose <c>api/auth/send-email-otp</c> ⇒ 404 ⇒ luồng quên mật
///     khẩu chết ở bước 1. Test bắt việc gọi đúng path thật sự trên HTTP.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Body lỗi thô</b>: client trước đây trả nguyên JSON
///     (<c>{"errors":{"Password":[...]}}</c>) cho UI ⇒ user thấy JSON.
///     Test bắt việc client trả message tiếng Việt sạch.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// Dùng <see cref="HttpMessageHandler"/> giả để chặn request ⇒ test chạy nhanh,
/// không cần DB, không cần mở server, nhưng VẪN kiểm tra được đường dẫn URI thật
/// mà client tạo ra (giống hệt byte-for-byte so với request thật).
/// </para>
/// </summary>
public sealed class AuthClientTests
{
    /// <summary>
    /// Handler giả: ghi lại request đã gửi và trả về response được cấu hình.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static (AuthClient Client, StubHandler Handler) BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        string baseAddress = "http://localhost:5286/")
    {
        var handler = new StubHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
        var client = new AuthClient(httpClient, NullLogger<AuthClient>.Instance, userSession: null);
        return (client, handler);
    }

    // -----------------------------------------------------------------
    // 1) Endpoint phải đúng
    // -----------------------------------------------------------------

    [Fact]
    public async Task SendOtpAsync_PhaiGoiDungPathSendEmailOtp()
    {
        var (client, handler) = BuildClient(_ => Json(HttpStatusCode.OK, """{"message":"Đã gửi OTP."}"""));

        var result = await client.SendOtpAsync(new SendEmailOtpRequestDto("a@b.com"));

        Assert.True(result.IsSuccess);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/auth/send-email-otp", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task VerifyOtpAsync_PhaiGoiDungPathVerifyEmailOtp()
    {
        var (client, handler) = BuildClient(_ => Json(HttpStatusCode.OK, """{"message":"token-123"}"""));

        var result = await client.VerifyOtpAsync(new VerifyEmailOtpRequestDto("a@b.com", "123456"));

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/auth/verify-email-otp", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("token-123", result.Data?.Message);
    }

    [Fact]
    public async Task ResetPasswordAsync_PhaiGoiDungPathResetPassword()
    {
        var (client, handler) = BuildClient(_ => Json(HttpStatusCode.OK, """{"message":"OK"}"""));

        var result = await client.ResetPasswordAsync(new ResetPasswordRequestDto(
            "a@b.com", "token", null, "Abcdef12", "Abcdef12"));

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/auth/reset-password", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    // -----------------------------------------------------------------
    // 2) Lỗi phải trả message tiếng Việt, KHÔNG phải JSON thô
    // -----------------------------------------------------------------

    [Fact]
    public async Task ResetPasswordAsync_LoiValidation_PhaiTraMessageTiengVietKhongPhaiJsonTho()
    {
        const string validationBody = """
        {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
         "title":"One or more validation errors occurred.","status":400,
         "errors":{"Password":["Mật khẩu phải có ít nhất 6 ký tự."]},"traceId":"00-abc"}
        """;

        var (client, _) = BuildClient(_ => Json(HttpStatusCode.BadRequest, validationBody));

        var result = await client.ResetPasswordAsync(new ResetPasswordRequestDto(
            "a@b.com", "token", null, "abc", "abc"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Mật khẩu phải có ít nhất 6 ký tự.", result.Error);
        Assert.DoesNotContain("traceId", result.Error!, StringComparison.Ordinal);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task SendOtpAsync_EmailKhongTonTai_PhaiTraMessageTuController()
    {
        var (client, _) = BuildClient(_ => Json(HttpStatusCode.NotFound, """{"message":"Email chưa đăng ký tài khoản."}"""));

        var result = await client.SendOtpAsync(new SendEmailOtpRequestDto("a@b.com"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Email chưa đăng ký tài khoản.", result.Error);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ResetPasswordAsync_OtpSai_PhaiTraMessageTuController()
    {
        var (client, _) = BuildClient(_ => Json(HttpStatusCode.Unauthorized, """{"message":"Mã OTP không đúng."}"""));

        var result = await client.ResetPasswordAsync(new ResetPasswordRequestDto(
            "a@b.com", null, "000000", "Abcdef12", "Abcdef12"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Mã OTP không đúng.", result.Error);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task ResetPasswordAsync_LoiProblemDetails_PhaiTraVeDetail()
    {
        const string problemBody = """
        {"type":"about:blank","title":"Thao tác không thể thực hiện","status":409,
         "detail":"Email đã tồn tại.","traceId":"00-xyz"}
        """;

        var (client, _) = BuildClient(_ => Json(HttpStatusCode.Conflict, problemBody));

        var result = await client.ResetPasswordAsync(new ResetPasswordRequestDto(
            "a@b.com", "token", null, "Abcdef12", "Abcdef12"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Email đã tồn tại.", result.Error);
    }

    [Fact]
    public async Task ResetPasswordAsync_XacNhanKhongKhop_PhaiTraVeThongDiepTiengViet()
    {
        var (client, _) = BuildClient(_ => Json(HttpStatusCode.BadRequest, """{"message":"Mật khẩu xác nhận không khớp."}"""));

        var result = await client.ResetPasswordAsync(new ResetPasswordRequestDto(
            "a@b.com", "token", null, "Abcdef12", "Khac1234"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Mật khẩu xác nhận không khớp.", result.Error);
    }
}
