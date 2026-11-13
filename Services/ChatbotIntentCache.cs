namespace PetShop.Services;

/// <summary>
/// Cache kết quả phân tích intent của chatbot (singleton, TTL ngắn).
///
/// <para>
/// <b>VÌ SAO CẦN (tối ưu quota + độ trễ):</b> mỗi tin nhắn chatbot hiện tại
/// tốn <b>2 request</b> tới Gemini:
/// </para>
/// <list type="number">
///   <item><c>AnalyzeIntentWithAi</c> — phân tích intent (JSON ngắn).</item>
///   <item><c>CallAiProviderAsync</c> — sinh câu trả lời.</item>
/// </list>
///
/// <para>
/// Gemini free tier chỉ cho <b>20 request/ngày</b> ⇒ người dùng hỏi 10 câu là
/// hết quota cả ngày. Cache intent theo nội dung câu hỏi giúp:
/// </para>
/// <list type="bullet">
///   <item>Câu hỏi lặp lại ("giá thức ăn cho chó", "giá thức ăn cho chó?") ⇒ dùng kết quả cũ.</item>
///   <item>Câu hỏi tương tự nhau (chỉ khác dấu / viết hoa / khoảng trắng) ⇒ chuẩn hoá trước khi tra.</item>
/// </list>
///
/// <para>
/// <b>Giới hạn bộ nhớ (RAM):</b> cache tối đa <see cref="MaxEntries"/> mục; khi vượt
/// ⇒ xóa mục cũ nhất (FIFO). Không có timer/background task ⇒ không tốn thread.
/// </para>
/// </summary>
public sealed class ChatbotIntentCache
{
    /// <summary>Số mục tối đa được giữ trong RAM.</summary>
    public const int MaxEntries = 100;

    /// <summary>Thời gian sống của một mục cache (phút).</summary>
    public const int TtlMinutes = 15;

    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Đọc kết quả intent đã cache (null nếu chưa có / đã hết hạn).</summary>
    public Models.AiIntentResult? Get(string normalizedMessage)
    {
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return null;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(normalizedMessage, out var entry))
            {
                return null;
            }

            if (DateTime.UtcNow - entry.CreatedUtc > TimeSpan.FromMinutes(TtlMinutes))
            {
                _entries.Remove(normalizedMessage);
                return null;
            }

            return entry.Result;
        }
    }

    /// <summary>Lưu kết quả intent vào cache.</summary>
    public void Set(string normalizedMessage, Models.AiIntentResult result)
    {
        if (string.IsNullOrWhiteSpace(normalizedMessage) || result is null)
        {
            return;
        }

        lock (_gate)
        {
            // Vượt giới hạn ⇒ xóa mục cũ nhất (FIFO) để không phình RAM.
            while (_entries.Count >= MaxEntries)
            {
                var oldest = _entries.OrderBy(kv => kv.Value.CreatedUtc).First().Key;
                _entries.Remove(oldest);
            }

            _entries[normalizedMessage] = new CacheEntry(result, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Chuẩn hoá câu hỏi để tăng tỉ lệ trúng cache:
    /// bỏ khoảng trắng thừa, chuyển về chữ thường, bỏ dấu chấm hỏi ở cuối.
    /// </summary>
    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var text = message.Trim().ToLowerInvariant();
        text = text.TrimEnd('?', '!', '.', ' ');
        return text;
    }

    private sealed record CacheEntry(Models.AiIntentResult Result, DateTime CreatedUtc);
}
