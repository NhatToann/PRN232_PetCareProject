namespace PetShop.Interfaces;

using PetShop.Models;

public interface IAiProviderService
{
    string ProviderName { get; }
    Task<string> GenerateTextAsync(
        string model,
        string systemPrompt,
        List<ChatHistoryItemDto> messages,
        double temperature = 0.7,
        int maxTokens = 2048,
        CancellationToken ct = default);
    bool IsAvailable { get; }
}
