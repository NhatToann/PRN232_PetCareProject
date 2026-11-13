using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Lưu trạng thái tạm cho luồng đăng nhập Google (OAuth 2.0 Authorization Code flow).
///
/// <para>
/// Vì sao cần store này?
/// </para>
///
/// <para>
/// Google Identity Services (GSI) chỉ hoạt động từ origin nằm trong
/// <c>javascript_origins</c> của OAuth client. Khi origin hiện tại chưa được đăng ký,
/// GSI trả 403 <c>"The given origin is not allowed for the given client ID."</c>.
/// </para>
///
/// <para>
/// Luồng thay thế dùng Authorization Code flow — chỉ phụ thuộc <c>redirect_uris</c>
/// ⇒ hoạt động từ mọi origin/port. Nhưng Google <b>không</b> cho phép <c>postMessage</c>
/// từ popup về tab gối (Google gửi header <c>Cross-Origin-Opener-Policy: same-origin</c>
/// ⇒ <c>window.opener</c> bị bỏ null). Vì vậy luồng dùng <b>redirect toàn trang</b>:
/// </para>
/// <list type="number">
///   <item>Tab gốc sinh <c>state</c> (chứa nonce + origin) và điều hướng tới Google.</item>
///   <item>Google redirect về <c>redirect_uri</c> kèm <c>?code=...&amp;state=...</c>.</item>
///   <item>Trang callback đổi code lấy token, đăng nhập, rồi sinh <b>one-time token</b>
///         lưu ở đây và chuyển hướng người dùng về origin ban đầu.</item>
///   <item>Origin ban đầu đổi one-time token lấy thông tin user và lưu session.</item>
/// </list>
///
/// <para>
/// Mọi entry đều có thời hạn ngắn (10 phút) và chỉ dùng được một lần.
/// </para>
/// </summary>
public sealed class GoogleOAuthStateStore
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, PendingEntry> _entries = new(StringComparer.Ordinal);
    private readonly ILogger<GoogleOAuthStateStore> _logger;

    public GoogleOAuthStateStore(ILogger<GoogleOAuthStateStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sinh một <c>state</c> mới gắn với <paramref name="origin"/> (origin của trang đang gọi).
    /// </summary>
    public string CreateState(string origin)
    {
        var nonce = GenerateNonce();
        var payload = System.Text.Json.JsonSerializer.Serialize(new GoogleStatePayload(origin, nonce));

        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        _entries[nonce] = new PendingEntry(PendingEntryKind.State, origin, null, DateTime.UtcNow);
        PurgeExpiredIfDue();

        return encoded;
    }

    /// <summary>
    /// Giải mã & kiểm tra <c>state</c>. Trả về <c>null</c> nếu state không hợp lệ/hết hạn.
    /// </summary>
    public GoogleStatePayload? ValidateState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        string json;
        try
        {
            var padded = state.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
                case 1: return null;
            }

            json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return null;
        }

        GoogleStatePayload? payload;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<GoogleStatePayload>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Nonce))
        {
            return null;
        }

        // State chỉ dùng được 1 lần.
        if (!_entries.TryRemove(payload.Nonce, out var entry) ||
            entry.Kind != PendingEntryKind.State ||
            DateTime.UtcNow - entry.CreatedUtc > EntryLifetime)
        {
            return null;
        }

        return payload;
    }

    /// <summary>
    /// Lưu kết quả đăng nhập (dùng 1 lần) và trả về one-time token để client đổi.
    /// </summary>
    public string StoreCompletion(AuthResponseDto user)
    {
        var token = GenerateNonce();
        _entries[token] = new PendingEntry(PendingEntryKind.Completion, null, user, DateTime.UtcNow);
        PurgeExpiredIfDue();
        return token;
    }

    /// <summary>
    /// Đổi one-time token lấy thông tin user. Trả về <c>null</c> nếu token không hợp lệ/hết hạn.
    /// </summary>
    public AuthResponseDto? ConsumeCompletion(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (!_entries.TryRemove(token, out var entry) ||
            entry.Kind != PendingEntryKind.Completion ||
            entry.User is null ||
            DateTime.UtcNow - entry.CreatedUtc > EntryLifetime)
        {
            return null;
        }

        return entry.User;
    }

    /// <summary>Xoá các entry đã hết hạn (gọi định kỳ để tránh rò rỉ bộ nhớ).</summary>
    public void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _entries)
        {
            if (now - pair.Value.CreatedUtc > EntryLifetime)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <summary>
    /// Dọn entry hết hạn một cách <b>thỉnh thoảng</b> (khi có hoạt động mới).
    ///
    /// <para>
    /// <b>Vì sao cần (tối ưu RAM):</b> trước đây <see cref="PurgeExpired"/> được
    /// khai báo nhưng <b>không ai gọi</b> ⇒ mỗi lần đăng nhập Google thêm 1 entry
    /// vào dictionary và không bao giờ bị xoá ⇒ bộ nhớ phình dần theo số lần
    /// đăng nhập (và theo số lần thử lại / spam). Nay mỗi lần tạo state hoặc
    /// lưu completion sẽ tự dọn ⇒ dictionary luôn nhỏ.
    /// </para>
    /// </summary>
    private void PurgeExpiredIfDue()
    {
        // Chỉ dọn khi đã qua mốc thời gian ⇒ không quét dictionary mỗi lần gọi.
        var now = DateTime.UtcNow;
        if (now - _lastPurgeUtc < PurgeInterval)
        {
            return;
        }

        _lastPurgeUtc = now;
        PurgeExpired();
    }

    /// <summary>Khoảng cách giữa 2 lần dọn entry hết hạn.</summary>
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromMinutes(1);

    private DateTime _lastPurgeUtc = DateTime.MinValue;

    private static string GenerateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private enum PendingEntryKind
    {
        State,
        Completion
    }

    private sealed record PendingEntry(
        PendingEntryKind Kind,
        string? Origin,
        AuthResponseDto? User,
        DateTime CreatedUtc);

    /// <summary>Nội dung giải mã từ <c>state</c>.</summary>
    public sealed record GoogleStatePayload(string Origin, string Nonce);
}
