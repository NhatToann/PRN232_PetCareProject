using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Provider AI Gemini — POST {baseUrl}/{model}:generateContent.
    /// Port nguyên văn từ OJT (BackEnd/Services/GeminiProviderService.cs).
    /// </summary>
    public class GeminiProviderService : IAiProviderService
    {
        private readonly HttpClient _httpClient;
        private readonly ChatbotOptions _options;
        private readonly ILogger<GeminiProviderService> _logger;

        public string ProviderName => "gemini";

        public bool IsAvailable => _options.Providers.TryGetValue("gemini", out var p) && !string.IsNullOrEmpty(p.ApiKey);

        /// <summary>
        /// Trạng thái cooldown DÙNG CHUNG (singleton) — xem <see cref="AiProviderCooldown"/>.
        ///
        /// <para>
        /// <b>TỐI ƯU QUOTA + ĐỘ TRỄ:</b> khi Gemini trả 429 (hết quota / rate limit),
        /// mọi lần gọi tiếp theo đều chắc chắn thất bại với cùng lý do. Nếu cứ gọi
        /// tiếp thì vừa tốn request (free tier chỉ 20 request/ngày), vừa khiến người
        /// dùng chờ 30-120 giây cho một lỗi chắc chắn.
        /// </para>
        /// </summary>
        private readonly AiProviderCooldown _cooldown;

        public GeminiProviderService(
            HttpClient httpClient,
            IOptions<ChatbotOptions> options,
            ILogger<GeminiProviderService> logger,
            AiProviderCooldown cooldown)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
            _cooldown = cooldown;

            if (IsAvailable)
            {
                var timeout = _options.Providers["gemini"].TimeoutSeconds;
                _httpClient.Timeout = TimeSpan.FromSeconds(timeout > 0 ? timeout : 120);
            }
        }

        /// <summary>
        /// Bật/tắt cooldown khi nhà cung cấp từ chối (429).
        /// </summary>
        /// <param name="seconds">Thời gian chặn (giây).</param>
        /// <param name="reason">Lý do (để log).</param>
        private void EnterCooldown(int seconds, string reason)
        {
            _cooldown.Enter(seconds, reason);
            _logger.LogWarning(
                "Gemini provider bị tạm chặn tới {Until:O} do: {Reason}",
                _cooldown.BlockedUntilUtc,
                reason);
        }

        /// <summary>
        /// Model Gemini thế hệ 3 (<c>gemini-3.x-flash</c>) là <b>thinking model</b>: mặc định
        /// dành một phần <c>maxOutputTokens</c> cho suy luận nội bộ.
        ///
        /// <para>
        /// Hệ quả với chatbot: lệnh phân tích intent yêu cầu model trả về <b>JSON ngắn</b>
        /// (vài chục token). Nếu không tắt thinking, token suy luận chiếm gần hết budget
        /// ⇒ JSON bị cắt cụt ở giữa (thấy <c>finishReason = MAX_TOKENS</c>, text =
        /// <c>{"intent":"product</c>) ⇒ <c>JsonSerializer.Deserialize</c> ném lỗi ⇒
        /// <c>intent</c> luôn rơi về <c>"unknown"</c>.
        /// </para>
        ///
        /// <para>
        /// Vì vậy:
        /// <list type="bullet">
        ///   <item><b>Tắt thinking</b> (<c>thinkingBudget = 0</c>) cho các lệnh ngắn cần JSON.</item>
        ///   <item><b>Tách phần JSON</b> khỏi phần text thật khi parse (bỏ code fence ```).</item>
        /// </list>
        /// </para>
        /// </summary>
        private const int ThinkingBudgetDisabled = 0;

        /// <summary>Cooldown khi hết quota theo ngày (15 phút — thường hồi sau vài giờ).</summary>
        private const int QuotaCooldownSeconds = 900;

        /// <summary>Cooldown khi bị rate limit theo phút (60 giây).</summary>
        private const int RateLimitCooldownSeconds = 60;

        /// <summary>Cooldown khi lỗi không xác định (30 giây).</summary>
        private const int UnknownCooldownSeconds = 30;

        public async Task<string> GenerateTextAsync(
            string model,
            string systemPrompt,
            List<ChatHistoryItemDto> messages,
            double temperature = 0.7,
            int maxTokens = 2048,
            CancellationToken ct = default)
        {
            if (!IsAvailable)
                throw new InvalidOperationException("Gemini is not configured. Please set ApiKey in the gemini provider config.");

            // Đang trong cooldown (quota hết / rate limit) ⇒ trả lỗi NGAY, không gọi mạng.
            if (_cooldown.IsBlocked)
            {
                _cooldown.RecordBlockedCall();
                throw new AiProviderException(
                    AiProviderErrorKind.QuotaExceeded,
                    "Dịch vụ AI đang tạm thời quá tải. Vui lòng thử lại sau ít phút.",
                    _cooldown.Reason,
                    _cooldown.BlockedUntilUtc);
            }

            var providerConfig = _options.Providers.GetValueOrDefault("gemini") ?? new AiProviderConfig();
            var requestModel = string.IsNullOrEmpty(model) || model == "default"
                ? (string.IsNullOrEmpty(providerConfig.DefaultModel) ? "gemini-3.5-flash" : providerConfig.DefaultModel)
                : model;

            var requestTemp = temperature > 0 && temperature <= 2 ? temperature : providerConfig.Temperature;
            var requestMaxTokens = maxTokens > 0 ? maxTokens : (providerConfig.MaxTokens > 0 ? providerConfig.MaxTokens : 2048);

            // Lệnh ngắn cần JSON ⇒ tắt thinking (xem ghi chú ThinkingBudgetDisabled).
            var disableThinking = LooksLikeJsonRequest(messages);

            var contents = new List<object>();

            foreach (var msg in messages)
            {
                // Skip invalid placeholder messages
                if (string.IsNullOrWhiteSpace(msg.Role) || msg.Role == "string" ||
                    string.IsNullOrWhiteSpace(msg.Content) || msg.Content == "string")
                {
                    continue;
                }

                // Gemini expects "user" or "model" roles
                var role = msg.Role.ToLowerInvariant() == "assistant" ? "model" : "user";

                contents.Add(new
                {
                    role = role,
                    parts = new[] { new { text = msg.Content } }
                });
            }

            object requestBody;

            // generationConfig dùng Dictionary để chỉ thêm "thinkingConfig" khi cần.
            // (Nếu luôn gửi thinkingConfig, các model không hỗ trợ sẽ trả 400
            //  "Request contains an invalid argument" — ví dụ gemini-3.5-flash-lite.)
            var generationConfig = new Dictionary<string, object>
            {
                ["temperature"] = requestTemp,
                ["maxOutputTokens"] = requestMaxTokens
            };

            if (disableThinking)
            {
                generationConfig["thinkingConfig"] = new Dictionary<string, object>
                {
                    ["thinkingBudget"] = ThinkingBudgetDisabled
                };
            }

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                requestBody = new
                {
                    system_instruction = new
                    {
                        parts = new[] { new { text = systemPrompt } }
                    },
                    contents = contents,
                    generationConfig = generationConfig
                };
            }
            else
            {
                requestBody = new
                {
                    contents = contents,
                    generationConfig = generationConfig
                };
            }

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var baseUrl = string.IsNullOrEmpty(providerConfig.BaseUrl)
                ? "https://generativelanguage.googleapis.com/v1beta/models"
                : providerConfig.BaseUrl.TrimEnd('/');

            // Always append the model identifier and :generateContent
            var url = $"{baseUrl}/{requestModel}:generateContent";

            _httpClient.DefaultRequestHeaders.Remove("X-goog-api-key");
            _httpClient.DefaultRequestHeaders.Add("X-goog-api-key", providerConfig.ApiKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(url, content, ct);

                // Hạn mức và tải được áp dụng theo model. Thử model Gemini dự
                // phòng một lần khi model chính trả 429/503 trước khi bật cooldown.
                var fallbackModel = providerConfig.FallbackModel?.Trim();
                if ((response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                        or System.Net.HttpStatusCode.ServiceUnavailable)
                    && !string.IsNullOrWhiteSpace(fallbackModel)
                    && !string.Equals(fallbackModel, requestModel, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Gemini model {PrimaryModel} trả HTTP {StatusCode}; thử model dự phòng {FallbackModel}",
                        requestModel,
                        (int)response.StatusCode,
                        fallbackModel);
                    response.Dispose();

                    var fallbackUrl = $"{baseUrl}/{fallbackModel}:generateContent";
                    using var fallbackContent = new StringContent(json, Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync(fallbackUrl, fallbackContent, ct);
                    requestModel = fallbackModel;
                }
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // Timeout của HttpClient (KHÔNG phải hủy theo yêu cầu của user).
                _logger.LogError(ex, "Gemini request timeout sau {Timeout}s", _httpClient.Timeout.TotalSeconds);
                throw new AiProviderException(
                    AiProviderErrorKind.Timeout,
                    "Dịch vụ AI phản hồi quá thời gian cho phép. Vui lòng thử lại.",
                    ex.Message,
                    innerException: ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Gemini request lỗi mạng");
                throw new AiProviderException(
                    AiProviderErrorKind.Unknown,
                    "Không kết nối được tới dịch vụ AI. Vui lòng thử lại sau.",
                    ex.Message,
                    innerException: ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                var status = (int)response.StatusCode;

                // Phân loại lỗi để (a) báo đúng nguyên nhân cho người dùng và
                // (b) BẬT COOLDOWN ⇒ các lần gọi sau trả lỗi ngay, không đốt thêm quota.
                var (kind, userMessage, cooldownSeconds) = ClassifyError(status, errorBody);

                if (cooldownSeconds > 0)
                {
                    EnterCooldown(cooldownSeconds, $"HTTP {status}: {TrimForLog(errorBody)}");
                }

                _logger.LogError("Gemini API error for model {Model}: {StatusCode} - {Error}", requestModel, status, errorBody);

                throw new AiProviderException(kind, userMessage, TrimForLog(errorBody));
            }

            _cooldown.Reset();

            var responseJson = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);

            var candidate = responseJson?.Candidates?.FirstOrDefault();
            var finishReason = candidate?.FinishReason;

            // Model thinking có thể trả về nhiều part: part đầu là suy luận nội bộ
            // (có "thought" = true), part cuối mới là câu trả lời thật.
            var parts = candidate?.Content?.Parts;
            var responseText = ExtractAnswerText(parts);

            if (string.IsNullOrEmpty(responseText))
            {
                _logger.LogWarning(
                    "Gemini returned empty content. FinishReason={FinishReason}, Response={Response}",
                    finishReason,
                    System.Text.Json.JsonSerializer.Serialize(responseJson));
            }
            else if (!string.IsNullOrEmpty(finishReason)
                     && !string.Equals(finishReason, "STOP", StringComparison.OrdinalIgnoreCase))
            {
                // MAX_TOKENS ⇒ output bị cắt (thường do thinking model dùng hết budget).
                _logger.LogWarning("Gemini finished with {FinishReason} — output may be truncated.", finishReason);
            }

            return responseText ?? string.Empty;
        }

        /// <summary>
        /// Lấy phần text TRẢ LỜI THẬT từ danh sách parts của Gemini.
        ///
        /// <para>
        /// Với thinking model, <c>parts</c> có thể gồm:
        /// <list type="bullet">
        ///   <item>part suy luận (<c>thought = true</c>) — KHÔNG phải câu trả lời;</item>
        ///   <item>part trả lời thật (<c>thought = false</c> hoặc không có field).</item>
        /// </list>
        /// Nếu lấy <c>Parts.FirstOrDefault()</c> thì sẽ trả về suy luận ⇒ câu trả lời
        /// cho người dùng bị sai. Vì vậy ưu tiên part không phải "thought".
        /// </para>
        /// </summary>
        private static string? ExtractAnswerText(List<GeminiPart>? parts)
        {
            if (parts is null || parts.Count == 0)
            {
                return null;
            }

            var nonThought = parts
                .Where(p => !p.Thought && !string.IsNullOrEmpty(p.Text))
                .Select(p => p.Text)
                .ToList();

            if (nonThought.Count > 0)
            {
                return string.Concat(nonThought);
            }

            // Không có part nào không-thought ⇒ lấy part text đầu tiên (fallback).
            return parts.FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text;
        }

        /// <summary>
        /// Phân loại lỗi HTTP từ Gemini ⇒ (kind, thông điệp tiếng Việt, thời gian cooldown).
        /// </summary>
        /// <param name="status">HTTP status code.</param>
        /// <param name="errorBody">Body lỗi (để đọc message chi tiết).</param>
        private static (AiProviderErrorKind Kind, string UserMessage, int CooldownSeconds) ClassifyError(int status, string errorBody)
        {
            var isQuota = errorBody.Contains("quota", StringComparison.OrdinalIgnoreCase)
                          || errorBody.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);

            switch (status)
            {
                case 429:
                    return isQuota
                        ? (AiProviderErrorKind.QuotaExceeded,
                            "Hết hạn mức sử dụng API AI hôm nay. Vui lòng thử lại vào ngày mai.",
                            QuotaCooldownSeconds)
                        : (AiProviderErrorKind.RateLimited,
                            "Bạn hỏi hơi nhanh. Vui lòng chờ một chút rồi thử lại.",
                            RateLimitCooldownSeconds);

                case 404:
                    return (AiProviderErrorKind.ModelNotFound,
                        "Model AI không tồn tại hoặc không được hỗ trợ. Vui lòng kiểm tra cấu hình.",
                        0);

                case 401:
                case 403:
                    return (AiProviderErrorKind.AuthenticationFailed,
                        "API key AI không hợp lệ. Vui lòng liên hệ quản trị viên.",
                        0);

                case 400:
                    return (AiProviderErrorKind.InvalidRequest,
                        "Yêu cầu gửi tới AI không hợp lệ.",
                        0);

                default:
                    return (AiProviderErrorKind.Unknown,
                        "Dịch vụ AI tạm thời không khả dụng. Vui lòng thử lại sau.",
                        UnknownCooldownSeconds);
            }
        }

        /// <summary>Cắt ngắn body lỗi để log gọn.</summary>
        private static string TrimForLog(string body, int maxLength = 600)
        {
            if (string.IsNullOrEmpty(body))
            {
                return string.Empty;
            }

            return body.Length <= maxLength ? body : body[..maxLength] + "...";
        }

        /// <summary>
        /// Heuristic: yêu cầu này có cần model trả về JSON không?
        ///
        /// <para>
        /// Tắt thinking chỉ an toàn khi lệnh ngắn và cần JSON — tắt cho lệnh dài
        /// (trả lời tự nhiên) sẽ làm giảm chất lượng câu trả lời.
        /// </para>
        /// </summary>
        private static bool LooksLikeJsonRequest(List<ChatHistoryItemDto> messages)
        {
            if (messages is null || messages.Count == 0)
            {
                return false;
            }

            foreach (var msg in messages)
            {
                var content = msg?.Content;
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                // Dấu hiệu rõ: prompt yêu cầu JSON.
                if (content.Contains("JSON", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Lệnh ngắn (< 500 ký tự) mà model có thể trả về JSON ngắn.
                if (content.Length < 500 && content.Contains('{') && content.Contains('}'))
                {
                    return true;
                }
            }

            return false;
        }

        private class GeminiResponse
        {
            [JsonPropertyName("candidates")]
            public List<GeminiCandidate>? Candidates { get; set; }
        }

        private class GeminiCandidate
        {
            [JsonPropertyName("content")]
            public GeminiContent? Content { get; set; }

            [JsonPropertyName("finishReason")]
            public string? FinishReason { get; set; }
        }

        private class GeminiContent
        {
            [JsonPropertyName("parts")]
            public List<GeminiPart>? Parts { get; set; }

            [JsonPropertyName("role")]
            public string? Role { get; set; }
        }

        private class GeminiPart
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }

            /// <summary>
            /// <c>true</c> ⇒ đây là phần suy luận nội bộ của thinking model,
            /// KHÔNG phải câu trả lời cho người dùng.
            /// </summary>
            [JsonPropertyName("thought")]
            public bool Thought { get; set; }
        }
    }
}
