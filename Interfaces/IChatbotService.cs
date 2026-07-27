using PetShop.Models;

namespace PetShop.Interfaces;

public interface IChatbotService
{
    Task<ChatMessageResponseDto> GetResponseAsync(
        ChatMessageRequestDto request,
        CancellationToken ct = default);

    Task<AiIntentResult> AnalyzeIntentWithAi(
        string message,
        CancellationToken ct = default);

    Task<ConversationContextDto> GetContextAsync(
        int? customerId,
        CancellationToken ct = default);

    Task<string> BuildSystemPromptAsync(
        ConversationContextDto? context,
        CancellationToken ct = default);
}
