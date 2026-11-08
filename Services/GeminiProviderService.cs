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

        public GeminiProviderService(
            HttpClient httpClient,
            IOptions<ChatbotOptions> options,
            ILogger<GeminiProviderService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;

            if (IsAvailable)
            {
                var timeout = _options.Providers["gemini"].TimeoutSeconds;
                _httpClient.Timeout = TimeSpan.FromSeconds(timeout > 0 ? timeout : 120);
            }
        }

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

            var providerConfig = _options.Providers.GetValueOrDefault("gemini") ?? new AiProviderConfig();
            var requestModel = string.IsNullOrEmpty(model) || model == "default"
                ? (string.IsNullOrEmpty(providerConfig.DefaultModel) ? "gemini-flash-latest" : providerConfig.DefaultModel)
                : model;

            var requestTemp = temperature > 0 && temperature <= 2 ? temperature : providerConfig.Temperature;
            var requestMaxTokens = maxTokens > 0 ? maxTokens : (providerConfig.MaxTokens > 0 ? providerConfig.MaxTokens : 2048);

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

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                requestBody = new
                {
                    system_instruction = new
                    {
                        parts = new[] { new { text = systemPrompt } }
                    },
                    contents = contents,
                    generationConfig = new
                    {
                        temperature = requestTemp,
                        maxOutputTokens = requestMaxTokens
                    }
                };
            }
            else
            {
                requestBody = new
                {
                    contents = contents,
                    generationConfig = new
                    {
                        temperature = requestTemp,
                        maxOutputTokens = requestMaxTokens
                    }
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

            var response = await _httpClient.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Gemini API error: {StatusCode} - {Error}", response.StatusCode, errorBody);
                throw new HttpRequestException($"Gemini API error: {response.StatusCode} - {errorBody}");
            }

            var responseJson = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);

            var responseText = responseJson?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

            if (string.IsNullOrEmpty(responseText))
            {
                _logger.LogWarning("Gemini returned empty content. Response: {Response}", System.Text.Json.JsonSerializer.Serialize(responseJson));
            }

            return responseText ?? string.Empty;
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
        }
    }
}
