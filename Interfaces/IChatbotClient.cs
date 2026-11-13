using PetShop.Controllers;
using PetShop.Models;

namespace PetShop.Interfaces;

/// <summary>
/// Client HTTP gọi API chatbot AI (<c>api/chatbot/*</c>) từ tầng UI.
///
/// <para>
/// Hợp đồng được giữ đúng với API cũ (<c>ChatbotController</c>) — không đổi
/// request/response shape. Tầng UI chỉ cần gọi qua HTTP client tới chính app
/// (mô phỏng frontend ↔ backend tách rời) thay vì gọi service trực tiếp.
/// </para>
/// </summary>
public interface IChatbotClient
{
    /// <summary>Gửi câu hỏi và nhận câu trả lời từ chatbot AI.</summary>
    Task<FrontendApiResult<ChatMessageResponseDto>> SendMessageAsync(
        ChatMessageRequestDto request,
        CancellationToken ct = default);

    /// <summary>Phân tích intent (ý định) của câu hỏi.</summary>
    Task<FrontendApiResult<AiIntentResult>> DetectIntentAsync(
        string message,
        CancellationToken ct = default);

    /// <summary>Kiểm tra trạng thái sẵn sàng của chatbot.</summary>
    Task<FrontendApiResult<ChatbotHealthDto>> GetHealthAsync(CancellationToken ct = default);
}
