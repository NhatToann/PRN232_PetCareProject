using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho luồng PayOS — Phase LS-07.
///
/// Bảo vệ hành vi (đối chiếu với bản gốc OJT `Projectojt/BackEnd/Services/PayOSService.cs`):
///  - <c>VerifyWebhook</c> ký HMAC-SHA256 trên **raw body text** (không re-serialize) bằng
///    <c>ChecksumKey</c>, hex lowercase, so sánh <c>StringComparison.OrdinalIgnoreCase</c>.
///  - <c>CreatePaymentLinkAsync</c> gửi POST <c>{BaseUrl}/payment-requests</c> kèm header
///    <c>x-client-id</c> / <c>x-api-key</c>; khi <c>HttpRequestException</c> xảy ra và
///    <c>BaseUrl</c> **không** chứa <c>api.payos.vn</c> ⇒ retry với fallback
///    <c>https://api.payos.vn/v2</c> (cùng payload + cùng header).
///  - <c>NormalizeDescription</c> bỏ dấu, giữ <c>[a-zA-Z0-9 #]</c>, cắt còn tối đa 25 ký tự.
///
/// <para><b>Q19 — Vệ sinh secret:</b> bộ test dùng <b>key giả cố định</b>
/// (<c>test-client-id</c> / <c>test-api-key</c> / <c>test-checksum-key-…</c>) thay cho
/// credential thật của PayOS. HMAC-SHA256 hoạt động đúng với bất kỳ khoá nào ⇒ thuật toán
/// vẫn được kiểm chứng đầy đủ, và CI chạy được <b>không cần secret thật</b>.
/// Credential thật nằm trong User Secrets / <c>appsettings.Development.json</c> (không track).</para>
/// </summary>
public class PayOSServiceTests
{
    // ---------------------------------------------------------------------------
    // Q19 — Vệ sinh secret (R-05):
    //   KHÔNG hard-code credential thật của PayOS trong file test.
    //   Mục đích của bộ test này là kiểm chứng **thuật toán HMAC-SHA256** và
    //   **nhánh fallback URL** — không phải kiểm chứng credential thật.
    //   ⇒ Dùng **key giả cố định** (deterministic) cho test:
    //       - HMAC-SHA256 hoạt động đúng với BẤT KỲ key nào ⇒ key giả vẫn kiểm chứng được.
    //       - Test chạy được trong CI **không cần secret thật** ⇒ không cần skip.
    //   Base URL merchant là endpoint công khai (không phải secret) ⇒ giữ nguyên.
    // ---------------------------------------------------------------------------

    /// <summary>Client ID giả — chỉ dùng để đối chiếu header gửi đi.</summary>
    private const string ClientId = "test-client-id";

    /// <summary>API key giả — chỉ dùng để đối chiếu header gửi đi.</summary>
    private const string ApiKey = "test-api-key";

    /// <summary>Checksum key giả — chỉ dùng làm khoá HMAC trong unit test.</summary>
    private const string ChecksumKey = "test-checksum-key-0123456789abcdef0123456789abcdef";

    /// <summary>Base URL merchant PayOS — endpoint công khai, KHÔNG phải secret.</summary>
    private const string MerchantBaseUrl = "https://api-merchant.payos.vn/v2";

    /// <summary>Raw body webhook mẫu (giữ nguyên format byte như PayOS gửi).</summary>
    private const string RawWebhookBody =
        "{\"code\":\"00\",\"data\":{\"orderCode\":123456789,\"status\":\"PAID\"}}";

    /// <summary>
    /// Tính HMAC-SHA256 giống hệt <c>ComputeHmac</c> của service:
    /// key = UTF8(ChecksumKey), data = UTF8(raw body), hash = hex lowercase (<c>x2</c>).
    /// </summary>
    private static string ComputeHmacHex(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));

        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private static PayOSService CreateService(HttpClient httpClient, string baseUrl = MerchantBaseUrl)
    {
        var options = Options.Create(new PayOSOptions
        {
            ClientId = ClientId,
            ApiKey = ApiKey,
            ChecksumKey = ChecksumKey,
            BaseUrl = baseUrl
        });

        // VerifyWebhook / NormalizeDescription / CreatePaymentLinkAsync không chạm _db / _emailService.
        return new PayOSService(
            httpClient,
            options,
            null!,
            null!,
            NullLogger<PayOSService>.Instance);
    }

    // ---------------------------------------------------------------------
    // VerifyWebhook
    // ---------------------------------------------------------------------

    [Fact]
    public void VerifyWebhook_ChuKyDung_TraVeTrue()
    {
        var service = CreateService(new HttpClient());
        var signature = ComputeHmacHex(RawWebhookBody, ChecksumKey);

        Assert.True(service.VerifyWebhook(RawWebhookBody, signature));
    }

    [Fact]
    public void VerifyWebhook_ChuKySai_TraVeFalse()
    {
        var service = CreateService(new HttpClient());

        Assert.False(service.VerifyWebhook(RawWebhookBody, ComputeHmacHex(RawWebhookBody, "khoa-khac")));
    }

    [Fact]
    public void VerifyWebhook_ChuKyKhongPhaiHex_TraVeFalse()
    {
        var service = CreateService(new HttpClient());

        Assert.False(service.VerifyWebhook(RawWebhookBody, "khong-phai-chu-ky-hex"));
    }

    [Fact]
    public void VerifyWebhook_KhongPhanBietHoaThuong_TraVeTrue()
    {
        // Nguồn dùng StringComparison.OrdinalIgnoreCase ⇒ chữ hoa/thường đều hợp lệ.
        var service = CreateService(new HttpClient());
        var signature = ComputeHmacHex(RawWebhookBody, ChecksumKey);

        Assert.True(service.VerifyWebhook(RawWebhookBody, signature.ToUpperInvariant()));
        Assert.True(service.VerifyWebhook(RawWebhookBody, signature.ToLowerInvariant()));
    }

    [Fact]
    public void VerifyWebhook_RawBodyRong_TraVeFalse()
    {
        var service = CreateService(new HttpClient());
        var signature = ComputeHmacHex(RawWebhookBody, ChecksumKey);

        Assert.False(service.VerifyWebhook(string.Empty, signature));
        Assert.False(service.VerifyWebhook("   ", signature));
    }

    [Fact]
    public void VerifyWebhook_ChuKyRong_TraVeFalse()
    {
        var service = CreateService(new HttpClient());

        Assert.False(service.VerifyWebhook(RawWebhookBody, null));
        Assert.False(service.VerifyWebhook(RawWebhookBody, string.Empty));
        Assert.False(service.VerifyWebhook(RawWebhookBody, "   "));
    }

    [Fact]
    public void VerifyWebhook_DungRawBodyKhongTaiSerialize_TraVeTrue()
    {
        // Payload có khoảng trắng/thứ tự field khác ⇒ re-serialize sẽ đổi chữ ký.
        // Service phải ký đúng chuỗi raw nhận được (payload.GetRawText()).
        const string raw = "{\n  \"data\" : {\n    \"status\": \"PAID\",\n    \"orderCode\": 42\n  },\n  \"code\": \"00\"\n}";
        var service = CreateService(new HttpClient());

        Assert.True(service.VerifyWebhook(raw, ComputeHmacHex(raw, ChecksumKey)));
        // Chữ ký của bản re-serialize (không giữ format) phải bị từ chối.
        Assert.False(service.VerifyWebhook(raw, ComputeHmacHex(
            "{\"data\":{\"status\":\"PAID\",\"orderCode\":42},\"code\":\"00\"}", ChecksumKey)));
    }

    // ---------------------------------------------------------------------
    // CreatePaymentLinkAsync — nhánh fallback
    // ---------------------------------------------------------------------

    /// <summary>Bản chụp (snapshot) của 1 HTTP request tại thời điểm gửi.</summary>
    private sealed record CapturedRequest(Uri Url, string Body, string? ClientId, string? ApiKey, string? ContentType);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly List<CapturedRequest> _requests = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public IReadOnlyList<CapturedRequest> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Phải chụp lại header + body TẠI THỜI ĐIỂM GỬI: service dispose HttpRequestMessage
            // sau khi SendAsync trả về ⇒ đọc content bên ngoài sẽ bị ObjectDisposedException.
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

            _requests.Add(new CapturedRequest(
                request.RequestUri!,
                body,
                request.Headers.TryGetValues("x-client-id", out var cid) ? cid.Single() : null,
                request.Headers.TryGetValues("x-api-key", out var key) ? key.Single() : null,
                request.Content?.Headers.ContentType?.MediaType));

            return Task.FromResult(_responder(request));
        }
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_BaseUrlMerchant_HoaDungKhongFallback()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"code\":\"00\",\"desc\":\"Success\",\"data\":{\"checkoutUrl\":\"https://app.payos.vn/checkout/abc\",\"status\":\"Pending\"}}")
        });
        var service = CreateService(new HttpClient(handler));

        var result = await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 250000m, "Thanh toan Spa #12",
                "https://petshop.vn/spa-invoice?payos=success&bookingId=12",
                "https://petshop.vn/spa-invoice?payos=cancel&bookingId=12"));

        Assert.NotNull(result);
        Assert.Equal("https://app.payos.vn/checkout/abc", result!.CheckoutUrl);
        Assert.Equal(123456789, result.OrderCode);
        Assert.Equal(250000m, result.Amount);

        // Đúng 1 request, tới merchant endpoint — KHÔNG retry fallback.
        Assert.Single(handler.Requests);
        Assert.Equal("https://api-merchant.payos.vn/v2/payment-requests", handler.Requests[0].Url.ToString());
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_LoiHttpRequestException_BaseUrlChuaApiPayosVn_PhaiFallback()
    {
        // Bản gốc kiểm tra `_options.BaseUrl.Contains("api.payos.vn", OrdinalIgnoreCase)`.
        // "api-merchant.payos.vn" KHÔNG chứa chuỗi "api.payos.vn" (có "api-merchant." ở giữa)
        // ⇒ nhánh fallback VẪN được kích hoạt. Test bám sát hành vi thật của bản gốc.
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.Host == "api-merchant.payos.vn")
            {
                throw new HttpRequestException("Khong ket noi duoc PayOS");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"code\":\"00\",\"desc\":\"Success\",\"data\":{\"checkoutUrl\":\"https://app.payos.vn/checkout/fallback\",\"status\":\"Pending\"}}")
            };
        });
        var service = CreateService(new HttpClient(handler));

        var result = await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 250000m, "Thanh toan Spa #12",
                "https://petshop.vn/return", "https://petshop.vn/cancel"));

        Assert.NotNull(result);
        Assert.Equal("https://app.payos.vn/checkout/fallback", result!.CheckoutUrl);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api-merchant.payos.vn/v2/payment-requests", handler.Requests[0].Url.ToString());
        Assert.Equal("https://api.payos.vn/v2/payment-requests", handler.Requests[1].Url.ToString());
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_LoiHttpRequestException_BaseUrlChuaDungApiPayosVn_KhongFallback()
    {
        // BaseUrl ĐÚNG chứa "api.payos.vn" ⇒ bản gốc trả null luôn, không retry.
        var handler = new RecordingHandler(_ => throw new HttpRequestException("Khong ket noi duoc PayOS"));
        var service = CreateService(new HttpClient(handler), "https://api.payos.vn/v2");

        var result = await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 250000m, "Thanh toan Spa #12",
                "https://petshop.vn/return", "https://petshop.vn/cancel"));

        Assert.Null(result);
        Assert.Single(handler.Requests);
        Assert.Equal("https://api.payos.vn/v2/payment-requests", handler.Requests[0].Url.ToString());
        Assert.Contains("Khong ket noi duoc PayOS", service.GetLastError());
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_LoiHttpRequestException_BaseUrlKhongPayos_FallbackVeApiPayosVn()
    {
        // BaseUrl KHÔNG chứa "api.payos.vn" ⇒ phải retry với fallback https://api.payos.vn/v2.
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.Host == "api-merchant.example.com")
            {
                throw new HttpRequestException("Khong ket noi duoc PayOS");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"code\":\"00\",\"desc\":\"Success\",\"data\":{\"checkoutUrl\":\"https://app.payos.vn/checkout/fallback\",\"status\":\"Pending\"}}")
            };
        });
        var service = CreateService(new HttpClient(handler), "https://api-merchant.example.com/v2");

        var result = await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 250000m, "Thanh toan Spa #12",
                "https://petshop.vn/return", "https://petshop.vn/cancel"));

        Assert.NotNull(result);
        Assert.Equal("https://app.payos.vn/checkout/fallback", result!.CheckoutUrl);

        // 2 request: lần đầu tới BaseUrl, lần hai tới fallback.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api-merchant.example.com/v2/payment-requests", handler.Requests[0].Url.ToString());
        Assert.Equal("https://api.payos.vn/v2/payment-requests", handler.Requests[1].Url.ToString());
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_FallbackGuiDungHeaderVaPayload()
    {
        // Header + payload của request fallback phải giống hệt request chính.
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.Host == "api-merchant.example.com")
            {
                throw new HttpRequestException("Khong ket noi duoc PayOS");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"code\":\"00\",\"desc\":\"Success\",\"data\":{\"checkoutUrl\":\"https://app.payos.vn/checkout/fallback\",\"status\":\"Pending\"}}")
            };
        });
        var service = CreateService(new HttpClient(handler), "https://api-merchant.example.com/v2");

        await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 250000m, "Thanh toan Spa #12",
                "https://petshop.vn/return", "https://petshop.vn/cancel"));

        Assert.Equal(2, handler.Requests.Count);

        var primary = handler.Requests[0];
        var fallback = handler.Requests[1];

        Assert.Equal("application/json", primary.ContentType);
        Assert.Equal(primary.ContentType, fallback.ContentType);

        Assert.Equal(ClientId, primary.ClientId);
        Assert.Equal(primary.ClientId, fallback.ClientId);

        Assert.Equal(ApiKey, primary.ApiKey);
        Assert.Equal(primary.ApiKey, fallback.ApiKey);

        // Payload fallback phải y hệt payload chính (cùng signature đã ký).
        Assert.Equal(primary.Body, fallback.Body);

        // Payload có đủ field đúng format PayOS.
        Assert.Contains("\"amount\":250000", primary.Body);
        Assert.Contains("\"cancelUrl\":\"https://petshop.vn/cancel\"", primary.Body);
        Assert.Contains("\"description\":\"Thanh toan Spa #12\"", primary.Body);
        Assert.Contains("\"items\":[]", primary.Body);
        Assert.Contains("\"orderCode\":123456789", primary.Body);
        Assert.Contains("\"returnUrl\":\"https://petshop.vn/return\"", primary.Body);
        Assert.Contains("\"signature\":", primary.Body);
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_FallbackCungHttpError_TraVeNullVaGhiLastError()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.Host == "api-merchant.example.com")
            {
                throw new HttpRequestException("Khong ket noi duoc PayOS");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{\"code\":\"99\",\"desc\":\"Bad Gateway\"}")
            };
        });
        var service = CreateService(new HttpClient(handler), "https://api-merchant.example.com/v2");

        var result = await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 250000m, "Thanh toan Spa #12",
                "https://petshop.vn/return", "https://petshop.vn/cancel"));

        Assert.Null(result);
        Assert.Equal("HTTP 502", service.GetLastError());
        Assert.Contains("Bad Gateway", service.GetLastResponse());
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_FallbackThieuCheckoutUrl_TraVeNullVaGhiLastError()
    {
        // Bản gốc: `_lastError = root?.Desc ?? "Missing checkoutUrl"`.
        // ⇒ khi PayOS trả desc ⇒ lấy desc; khi desc rỗng ⇒ "Missing checkoutUrl".
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.Host == "api-merchant.example.com")
            {
                throw new HttpRequestException("Khong ket noi duoc PayOS");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":\"00\",\"data\":{}}")
            };
        });
        var service = CreateService(new HttpClient(handler), "https://api-merchant.example.com/v2");

        var result = await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 250000m, "Thanh toan Spa #12",
                "https://petshop.vn/return", "https://petshop.vn/cancel"));

        Assert.Null(result);
        Assert.Equal("Missing checkoutUrl", service.GetLastError());
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_FallbackCoDesc_TraVeNullVaGhiDesc()
    {
        // Có `desc` trong response ⇒ _lastError lấy `desc` (không phải "Missing checkoutUrl").
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.Host == "api-merchant.example.com")
            {
                throw new HttpRequestException("Khong ket noi duoc PayOS");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":\"99\",\"desc\":\"Don khong hop le\",\"data\":{}}")
            };
        });
        var service = CreateService(new HttpClient(handler), "https://api-merchant.example.com/v2");

        var result = await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 250000m, "Thanh toan Spa #12",
                "https://petshop.vn/return", "https://petshop.vn/cancel"));

        Assert.Null(result);
        Assert.Equal("Don khong hop le", service.GetLastError());
    }

    [Fact]
    public async Task CreatePaymentLinkAsync_AmountKhongHopLe_TraVeNullKhongGoiHttp()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var service = CreateService(new HttpClient(handler));

        var result = await service.CreatePaymentLinkAsync(
            new CreatePayOSPaymentRequest(123456789, 0m, "Thanh toan", "https://petshop.vn/return", "https://petshop.vn/cancel"));

        Assert.Null(result);
        Assert.Empty(handler.Requests);
        Assert.Equal("Invalid payload", service.GetLastError());
    }

    // ---------------------------------------------------------------------
    // NormalizeDescription
    // ---------------------------------------------------------------------

    [Fact]
    public void NormalizeDescription_BoDau_GiuChuVaSo()
    {
        var service = CreateService(new HttpClient());

        Assert.Equal("Thanh toan don hang", service.NormalizeDescription("Thanh toán đơn hàng"));
    }

    [Fact]
    public void NormalizeDescription_CatConToiDa25KyTu()
    {
        var service = CreateService(new HttpClient());

        var result = service.NormalizeDescription("Thanh toan don hang so 1234567890");

        Assert.True(result.Length <= 25);
        Assert.Equal(result, result.Trim());
    }

    [Fact]
    public void NormalizeDescription_ChuRong_TraVeChuoiRong()
    {
        var service = CreateService(new HttpClient());

        Assert.Equal(string.Empty, service.NormalizeDescription("   "));
    }
}
