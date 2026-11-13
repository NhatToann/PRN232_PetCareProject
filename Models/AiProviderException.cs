namespace PetShop.Models;

/// <summary>
/// Lỗi từ nhà cung cấp AI (Gemini) — dùng để phân loại rõ ràng các nguyên nhân
/// (quota, rate limit, timeout, model không tồn tại, lỗi xác thực).
///
/// <para>
/// Mục đích: <c>ChatbotService</c> / <c>ChatbotController</c> cần trả về thông
/// báo TIẾNG VIỆT đúng nghĩa cho từng trường hợp, đồng thời tránh gọi lại
/// nhà cung cấp khi chắc chắn sẽ thất bại (tiết kiệm quota + độ trễ).
/// </para>
/// </summary>
public sealed class AiProviderException : Exception
{
    /// <summary>Phân loại lỗi.</summary>
    public AiProviderErrorKind Kind { get; }

    /// <summary>Thông điệp tiếng Việt dành cho người dùng cuối.</summary>
    public string UserMessage { get; }

    /// <summary>Thời điểm sớm nhất mà nên thử lại (null nếu không xác định).</summary>
    public DateTime? RetryAfterUtc { get; }

    public AiProviderException(
        AiProviderErrorKind kind,
        string userMessage,
        string? rawError = null,
        DateTime? retryAfterUtc = null,
        Exception? innerException = null)
        : base(string.IsNullOrWhiteSpace(rawError) ? userMessage : $"{userMessage} ({rawError})", innerException)
    {
        Kind = kind;
        UserMessage = userMessage;
        RetryAfterUtc = retryAfterUtc;
    }
}

/// <summary>Phân loại lỗi của nhà cung cấp AI.</summary>
public enum AiProviderErrorKind
{
    /// <summary>Lỗi không xác định / mạng.</summary>
    Unknown = 0,

    /// <summary>Hết quota (HTTP 429 — quota theo ngày/tháng).</summary>
    QuotaExceeded = 1,

    /// <summary>Bị giới hạn tần suất tạm thời (HTTP 429 — rate limit theo phút).</summary>
    RateLimited = 2,

    /// <summary>Timeout / quá thời gian chờ.</summary>
    Timeout = 3,

    /// <summary>Model không tồn tại hoặc không được hỗ trợ (HTTP 404).</summary>
    ModelNotFound = 4,

    /// <summary>API key sai / thiếu (HTTP 401, 403).</summary>
    AuthenticationFailed = 5,

    /// <summary>Request không hợp lệ (HTTP 400).</summary>
    InvalidRequest = 6
}
