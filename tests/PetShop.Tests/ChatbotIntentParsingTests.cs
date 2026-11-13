using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho <see cref="ChatbotService.ParseIntentJson"/> và
/// <see cref="GeminiProviderService"/>.
///
/// <para>
/// <b>Bối cảnh (lỗi thật đã tái hiện):</b> model <c>gemini-3.5-flash</c> là
/// <b>thinking model</b> — mặc định dùng một phần <c>maxOutputTokens</c> cho suy luận
/// nội bộ. Lệnh phân tích intent yêu cầu JSON ngắn (vài chục token) ⇒ token suy luận
/// chiếm gần hết budget ⇒ JSON bị cắt cụt (<c>finishReason = MAX_TOKENS</c>,
/// text = <c>{"intent":"product</c>) ⇒ deserialize lỗi ⇒ intent luôn rơi về
/// <c>"unknown"</c> ⇒ chatbot không bao giờ dùng được intent thật.
/// </para>
///
/// <para>Ba lớp bảo vệ được viết test ở đây:</para>
/// <list type="number">
///   <item>Tắt thinking (<c>thinkingBudget = 0</c>) cho lệnh ngắn cần JSON.</item>
///   <item>Lấy đúng part TRẢ LỜI (bỏ part suy luận có <c>thought = true</c>).</item>
///   <item>Parse JSON bền: bỏ code fence, bỏ text thừa, quét cân bằng ngoặc.</item>
/// </list>
/// </summary>
public class ChatbotIntentParsingTests
{
    // =======================================================================
    // 1. ParseIntentJson — bền trước các dạng output thực tế của Gemini.
    // =======================================================================

    [Fact]
    public void ParseIntentJson_JsonThu_TraVeIntentDung()
    {
        var result = ChatbotService.ParseIntentJson(
            "{\"intent\":\"product_search\",\"parameters\":{\"product_type\":\"đồ chơi\",\"pet\":\"chó\"}}");

        Assert.NotNull(result);
        Assert.Equal("product_search", result!.Intent);
        Assert.Equal("đồ chơi", result.Parameters["product_type"]);
        Assert.Equal("chó", result.Parameters["pet"]);
    }

    [Fact]
    public void ParseIntentJson_JsonBocCodeFence_TraVeIntentDung()
    {
        var result = ChatbotService.ParseIntentJson(
            "```json\n{\"intent\":\"greeting\",\"parameters\":{}}\n```");

        Assert.NotNull(result);
        Assert.Equal("greeting", result!.Intent);
    }

    [Fact]
    public void ParseIntentJson_CodeFenceKhongCoNhanJson_TraVeIntentDung()
    {
        var result = ChatbotService.ParseIntentJson(
            "```\n{\"intent\":\"booking\",\"parameters\":{}}\n```");

        Assert.NotNull(result);
        Assert.Equal("booking", result!.Intent);
    }

    [Fact]
    public void ParseIntentJson_TextThuaTruoc_TraVeIntentDung()
    {
        // Model hay trả kèm câu giới thiệu trước JSON.
        var result = ChatbotService.ParseIntentJson(
            "Dưới đây là kết quả phân tích:\n{\"intent\":\"order_status\",\"parameters\":{}}");

        Assert.NotNull(result);
        Assert.Equal("order_status", result!.Intent);
    }

    [Fact]
    public void ParseIntentJson_TextThuaSau_TraVeIntentDung()
    {
        var result = ChatbotService.ParseIntentJson(
            "{\"intent\":\"pet_info\",\"parameters\":{}}\n\nHy vọng hữu ích!");

        Assert.NotNull(result);
        Assert.Equal("pet_info", result!.Intent);
    }

    [Fact]
    public void ParseIntentJson_JsonBiCatCut_TraVeNull()
    {
        // REGRESSION: JSON bị cắt cụt do MAX_TOKENS (thinking model).
        var result = ChatbotService.ParseIntentJson("{\"intent\":\"product");

        Assert.Null(result);
    }

    [Fact]
    public void ParseIntentJson_KhongPhaiJson_TraVeNull()
    {
        Assert.Null(ChatbotService.ParseIntentJson("Xin chào, tôi có thể giúp gì?"));
        Assert.Null(ChatbotService.ParseIntentJson(string.Empty));
        Assert.Null(ChatbotService.ParseIntentJson("   "));
        Assert.Null(ChatbotService.ParseIntentJson(null));
    }

    [Fact]
    public void ParseIntentJson_IntentRong_TraVeNull()
    {
        Assert.Null(ChatbotService.ParseIntentJson("{\"intent\":\"\",\"parameters\":{}}"));
        Assert.Null(ChatbotService.ParseIntentJson("{\"parameters\":{}}"));
    }

    [Fact]
    public void ParseIntentJson_ThieuParameters_TraVeIntentDung()
    {
        var result = ChatbotService.ParseIntentJson("{\"intent\":\"support\"}");

        Assert.NotNull(result);
        Assert.Equal("support", result!.Intent);
        Assert.NotNull(result.Parameters);
        Assert.Empty(result.Parameters);
    }

    [Fact]
    public void ParseIntentJson_NgoacLongTrongChuoi_TraVeIntentDung()
    {
        // Giá trị chuỗi chứa ngoặc ⇒ quét cân bằng ngoặc phải bỏ qua nội dung chuỗi.
        var result = ChatbotService.ParseIntentJson(
            "{\"intent\":\"product_search\",\"parameters\":{\"keywords\":\"a{b}c\",\"pet\":\"chó\"}}");

        Assert.NotNull(result);
        Assert.Equal("product_search", result!.Intent);
        Assert.Equal("a{b}c", result.Parameters["keywords"]);
    }

    [Fact]
    public void ParseIntentJson_NhieuObject_TraVeObjectDauTien()
    {
        var result = ChatbotService.ParseIntentJson(
            "{\"intent\":\"greeting\",\"parameters\":{}} {\"intent\":\"booking\",\"parameters\":{}}");

        Assert.NotNull(result);
        Assert.Equal("greeting", result!.Intent);
    }

    // =======================================================================
    // 2. GeminiProviderService — request payload phải có thinkingConfig khi cần.
    // =======================================================================

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(Uri Url, string Body, string? ApiKey)> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            request.Headers.TryGetValues("X-goog-api-key", out var keys);
            Requests.Add((request.RequestUri!, body, keys?.FirstOrDefault()));

            return _responder(request);
        }
    }

    private static GeminiProviderService CreateProvider(
        HttpClient httpClient, string model = "gemini-3.5-flash")
    {
        var options = Options.Create(new ChatbotOptions
        {
            DefaultModel = model,
            Providers = new Dictionary<string, AiProviderConfig>
            {
                ["gemini"] = new AiProviderConfig
                {
                    BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models",
                    ApiKey = "test-api-key",
                    DefaultModel = model,
                    TimeoutSeconds = 60,
                    Enabled = true
                }
            }
        });

        return new GeminiProviderService(
            httpClient,
            options,
            NullLogger<GeminiProviderService>.Instance,
            new PetShop.Services.AiProviderCooldown());
    }

    [Fact]
    public async Task GenerateTextAsync_LenhCanJson_PhaiCoThinkingBudgetBangKhong()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"{\\\"intent\\\":\\\"greeting\\\"}\"}]}}]}")
        });
        var provider = CreateProvider(new HttpClient(handler));

        await provider.GenerateTextAsync(
            "gemini-3.5-flash",
            string.Empty,
            new List<ChatHistoryItemDto>
            {
                new() { Role = "user", Content = "Phân tích và trả về JSON: {\"intent\":\"greeting\"}" }
            },
            0.2,
            200);

        var body = handler.Requests[0].Body;

        Assert.Contains("thinkingConfig", body);
        Assert.Contains("thinkingBudget", body);
        Assert.Contains("\"thinkingBudget\":0", body);
    }

    [Fact]
    public async Task GenerateTextAsync_LenhDai_PhaiKhongCoThinkingConfig()
    {
        // Tắt thinking cho lệnh dài sẽ làm giảm chất lượng câu trả lời ⇒ chỉ tắt
        // khi lệnh ngắn/cần JSON.
        var handler = new RecordingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"Câu trả lời dài.\"}]}}]}")
        });
        var provider = CreateProvider(new HttpClient(handler));

        await provider.GenerateTextAsync(
            "gemini-3.5-flash",
            "Bạn là trợ lý PetShop, hãy trả lời thân thiện.",
            new List<ChatHistoryItemDto>
            {
                new() { Role = "user", Content = "Hãy giới thiệu tất cả dịch vụ spa mà shop đang có, mô tả chi tiết từng dịch vụ." }
            },
            0.7,
            1000);

        Assert.DoesNotContain("thinkingConfig", handler.Requests[0].Body);
    }

    [Fact]
    public async Task GenerateTextAsync_ApiKeyGuiTrongHeader()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]}}]}")
        });
        var provider = CreateProvider(new HttpClient(handler));

        await provider.GenerateTextAsync(
            "gemini-3.5-flash",
            string.Empty,
            new List<ChatHistoryItemDto> { new() { Role = "user", Content = "xin chào" } });

        Assert.Equal("test-api-key", handler.Requests[0].ApiKey);
        Assert.Contains("/gemini-3.5-flash:generateContent", handler.Requests[0].Url!.ToString());
    }

    [Fact]
    public async Task GenerateTextAsync_ThinkingPart_BoQuaVaLayPartTraLoi()
    {
        // Thinking model trả về part suy luận (thought = true) TRƯỚC part trả lời.
        // Nếu lấy Parts.FirstOrDefault() ⇒ trả về suy luận ⇒ câu trả lời sai.
        var handler = new RecordingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[" +
                "{\"text\":\"Suy luận nội bộ...\",\"thought\":true}," +
                "{\"text\":\"Câu trả lời cho bạn.\"}]}}]}")
        });
        var provider = CreateProvider(new HttpClient(handler));

        var result = await provider.GenerateTextAsync(
            "gemini-3.5-flash",
            string.Empty,
            new List<ChatHistoryItemDto> { new() { Role = "user", Content = "xin chào" } });

        Assert.Equal("Câu trả lời cho bạn.", result);
        Assert.DoesNotContain("Suy luận nội bộ", result);
    }

    [Fact]
    public async Task GenerateTextAsync_KhongCoApiKey_Throw()
    {
        var options = Options.Create(new ChatbotOptions
        {
            Providers = new Dictionary<string, AiProviderConfig>
            {
                ["gemini"] = new AiProviderConfig { ApiKey = string.Empty }
            }
        });

        var provider = new GeminiProviderService(
            new HttpClient(),
            options,
            NullLogger<GeminiProviderService>.Instance,
            new PetShop.Services.AiProviderCooldown());

        Assert.False(provider.IsAvailable);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GenerateTextAsync(
            "gemini-3.5-flash",
            string.Empty,
            new List<ChatHistoryItemDto> { new() { Role = "user", Content = "hi" } }));
    }

    [Fact]
    public async Task GenerateTextAsync_Loi429_ThrowAiProviderException_PhanLoaiQuota()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":{\"code\":429,\"message\":\"Quota exceeded\"}}")
        });
        var provider = CreateProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<PetShop.Models.AiProviderException>(() => provider.GenerateTextAsync(
            "gemini-3.5-flash",
            string.Empty,
            new List<ChatHistoryItemDto> { new() { Role = "user", Content = "hi" } }));

        Assert.Equal(PetShop.Models.AiProviderErrorKind.QuotaExceeded, ex.Kind);
    }

    [Fact]
    public async Task GenerateTextAsync_Loi429_PhaiBatCooldown_ThoiGianCallSauKhongGoiMang()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":{\"code\":429,\"message\":\"Quota exceeded\"}}")
        });
        var provider = CreateProvider(new HttpClient(handler));

        // Lần 1: gọi mạng và nhận 429.
        await Assert.ThrowsAsync<PetShop.Models.AiProviderException>(() => provider.GenerateTextAsync(
            "gemini-3.5-flash",
            string.Empty,
            new List<ChatHistoryItemDto> { new() { Role = "user", Content = "hi" } }));

        // Số request mạng sau lần 1 = số lần thử model (chính + model dự phòng).
        //
        // ⚠️ SỬA (12/11/2026): trước đây test kỳ vọng cứng `1`, nhưng
        // `ChatbotOptions.FallbackModel` mặc định khác model chính ⇒
        // `GeminiProviderService` thử lại model dự phòng khi model chính trả
        // 429/503 ⇒ thực tế có 2 request. Test kỳ vọng sai ⇒ fail dù cooldown
        // hoạt động đúng.
        //
        // ⇒ Chốt theo hành vi THẬT: đếm số request trước khi cooldown bật,
        // rồi xác nhận lần 2 KHÔNG phát sinh thêm request nào.
        var requestsAfterFirstCall = handler.Requests.Count;
        Assert.True(requestsAfterFirstCall >= 1,
            "Lần gọi đầu tiên phải phát sinh ít nhất 1 request mạng.");

        // Lần 2: PHẢI trả lỗi ngay, KHÔNG gọi mạng (tiết kiệm quota + độ trễ).
        await Assert.ThrowsAsync<PetShop.Models.AiProviderException>(() => provider.GenerateTextAsync(
            "gemini-3.5-flash",
            string.Empty,
            new List<ChatHistoryItemDto> { new() { Role = "user", Content = "hi" } }));

        Assert.Equal(requestsAfterFirstCall, handler.Requests.Count);
    }

    // =======================================================================
    // 3. ChatbotOptions — model mặc định phải là model đang tồn tại.
    // =======================================================================

    [Fact]
    public void ChatbotOptions_DefaultModelLaGemini35Flash()
    {
        var options = new ChatbotOptions();

        Assert.Equal("gemini-3.5-flash", options.DefaultModel);
    }

    [Fact]
    public void ChatbotOptions_DefaultProviderLaGemini()
    {
        var options = new ChatbotOptions();

        Assert.Equal("gemini", options.DefaultProviderName);
        Assert.Equal("gemini", ChatbotOptions.DefaultProvider);
    }

    [Fact]
    public void ChatbotOptions_JsonSerialize_GiuTenCauHinh()
    {
        // Đảm bảo khối cấu hình vẫn đọc được từ appsettings (contract cũ giữ nguyên).
        var json = "{\"defaultModel\":\"gemini-3.5-flash\",\"temperature\":0.5,\"maxTokens\":1000," +
                   "\"providers\":{\"gemini\":{\"baseUrl\":\"https://x\",\"apiKey\":\"k\",\"defaultModel\":\"gemini-3.5-flash\"}}}";

        var options = JsonSerializer.Deserialize<ChatbotOptions>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(options);
        Assert.Equal("gemini-3.5-flash", options!.DefaultModel);
        Assert.Equal(0.5, options.Temperature);
        Assert.Equal(1000, options.MaxTokens);
        Assert.Equal("k", options.Providers["gemini"].ApiKey);
    }
}
