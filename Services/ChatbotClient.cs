using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PetShop.Controllers;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Client HTTP gọi API chatbot AI (<c>api/chatbot/*</c>) từ tầng UI.
///
/// <para>
/// Theo đúng convention của dự án: các trang Blazor không gọi service trực
/// tiếp mà gọi qua HTTP client tới chính endpoint trong app (mô phỏng kiến trúc
/// frontend ↔ backend tách rời).
/// </para>
///
/// <para>
/// <b>KHÔNG cần header phiên đăng nhập:</b> các endpoint chatbot (<c>/api/chatbot/*</c>)
/// là <c>[AllowAnonymous]</c> — khách chưa đăng nhập vẫn tư vấn được. Chỉ
/// <c>customerId</c> trong body quyết định có lấy ngữ cảnh cá nhân hay không.
/// </para>
/// </summary>
public sealed class ChatbotClient : IChatbotClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ChatbotClient> _logger;

    public ChatbotClient(HttpClient httpClient, ILogger<ChatbotClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Gửi câu hỏi cho chatbot và nhận câu trả lời.
    /// </summary>
    public async Task<FrontendApiResult<ChatMessageResponseDto>> SendMessageAsync(
        ChatMessageRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/chatbot/message", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await ApiErrorMessageReader.ReadAsync(response, ct);
                return FrontendApiResult<ChatMessageResponseDto>.Failure(error, (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<ChatMessageResponseDto>(ct);
            return data is null
                ? FrontendApiResult<ChatMessageResponseDto>.Failure("Chatbot trả về phản hồi rỗng.")
                : FrontendApiResult<ChatMessageResponseDto>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<ChatMessageResponseDto>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Phân tích intent của câu hỏi (dùng để hiển thị nhãn trong UI).
    /// </summary>
    public async Task<FrontendApiResult<AiIntentResult>> DetectIntentAsync(
        string message,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/chatbot/intent", new { message }, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await ApiErrorMessageReader.ReadAsync(response, ct);
                return FrontendApiResult<AiIntentResult>.Failure(error, (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<AiIntentResult>(ct);
            return data is null
                ? FrontendApiResult<AiIntentResult>.Failure("Không phân tích được intent.")
                : FrontendApiResult<AiIntentResult>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<AiIntentResult>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Kiểm tra chatbot có sẵn sàng không.
    /// </summary>
    public async Task<FrontendApiResult<ChatbotHealthDto>> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/chatbot/health", ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await ApiErrorMessageReader.ReadAsync(response, ct);
                return FrontendApiResult<ChatbotHealthDto>.Failure(error, (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<ChatbotHealthDto>(ct);
            return data is null
                ? FrontendApiResult<ChatbotHealthDto>.Failure("Chatbot health trả về phản hồi rỗng.")
                : FrontendApiResult<ChatbotHealthDto>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<ChatbotHealthDto>.Failure(ex.Message);
        }
    }
}
