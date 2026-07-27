using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services.AI;

public sealed class GeminiProviderService : IAiProviderService
{
    private readonly HttpClient _httpClient;
    private readonly ChatbotOptions _options;
    private readonly ILogger<GeminiProviderService> _logger;

    public GeminiProviderService(HttpClient httpClient, IOptions<ChatbotOptions> options, ILogger<GeminiProviderService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "gemini";

    public bool IsAvailable =>
        _options.Providers.TryGetValue("gemini", out var provider)
        && !string.IsNullOrWhiteSpace(provider.ApiKey);

    public async Task<string> GenerateTextAsync(
        string model,
        string systemPrompt,
        List<ChatHistoryItemDto> messages,
        double temperature = 0.7,
        int maxTokens = 2048,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Gemini chưa được cấu hình API key.");
        }

        var provider = _options.Providers["gemini"];
        var requestModel = string.IsNullOrWhiteSpace(model) || model == "default"
            ? (string.IsNullOrWhiteSpace(provider.DefaultModel) ? _options.DefaultModel : provider.DefaultModel)
            : model;
        var baseUrl = string.IsNullOrWhiteSpace(provider.BaseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta/models"
            : provider.BaseUrl.TrimEnd('/');

        var contents = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Role) && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new
            {
                role = m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                parts = new[] { new { text = m.Content } }
            })
            .ToList();

        var requestBody = new
        {
            system_instruction = string.IsNullOrWhiteSpace(systemPrompt)
                ? null
                : new { parts = new[] { new { text = systemPrompt } } },
            contents,
            generationConfig = new
            {
                temperature,
                maxOutputTokens = maxTokens
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        _httpClient.DefaultRequestHeaders.Remove("X-goog-api-key");
        _httpClient.DefaultRequestHeaders.Add("X-goog-api-key", provider.ApiKey);

        using var response = await _httpClient.PostAsync($"{baseUrl}/{requestModel}:generateContent", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Gemini API error: {StatusCode} - {Error}", response.StatusCode, error);
            throw new HttpRequestException($"Gemini API error: {response.StatusCode}");
        }

        var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
        return payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
