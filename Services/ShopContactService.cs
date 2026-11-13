using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Thông tin liên hệ của shop — đọc từ bảng <c>SystemSettings</c> (không hardcode).
///
/// <para>
/// <b>VÌ SAO CẦN:</b> trước đây Footer / TopBar / Contact ghi cứng
/// "123 Đường ABC, TP.HCM" và "0123 456 789" — trong khi DB thật ghi
/// "123 Nguyễn Văn Linh, Phường Hòa Cường Nam, Quận Hải Châu, Đà Nẵng"
/// và "0901 234 567". Ông dữ liệu khách nhàng thấy 2 nguồn khác nhau ⇒
/// loạn. Nay mọi nơi đều đọc cùng một nguồn: <c>SystemSettings</c>.
/// </para>
///
/// <para>
/// <b>BỘ NHỚ:</b> service scoped, chỉ giữ 1 object nhỏ trong RAM của scope.
/// Dữ liệu được cache theo TTL ngắn (mặc định 60s) để không query DB
/// liên tục khi nhiều trang cùng hiển thị.
/// </para>
/// </summary>
public sealed class ShopContactService
{
    /// <summary>Thời gian cache tối đa (giây).</summary>
    private const int CacheTtlSeconds = 60;

    private readonly IShopContactRepository _repo;
    private readonly ILogger<ShopContactService> _logger;

    private ShopContactInfo? _cached;
    private DateTime _cachedAtUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ShopContactService(
        IShopContactRepository repo,
        ILogger<ShopContactService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>
    /// Lấy thông tin liên hệ (có cache TTL ngắn).
    /// Trả về object rỗng nếu DB lỗi ⇒ UI vẫn render được (không crash).
    /// </summary>
    public async Task<ShopContactInfo> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null && (DateTime.UtcNow - _cachedAtUtc).TotalSeconds < CacheTtlSeconds)
        {
            return _cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null && (DateTime.UtcNow - _cachedAtUtc).TotalSeconds < CacheTtlSeconds)
            {
                return _cached;
            }

            var contact = await _repo.GetShopContactSettingsAsync(ct);

            _cached = new ShopContactInfo
            {
                Address = Get(contact, "ShopAddress"),
                Hotline = Get(contact, "ShopHotline"),
                Email = Get(contact, "ShopEmail"),
                WorkingHours = Get(contact, "ShopWorkingHours"),
            };

            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[ShopContactService] Không đọc được SystemSettings: {ex.GetType().Name} {ex.Message}");

            // Trả về cache cũ nếu có ⇒ UI không bị trống.
            return _cached ?? new ShopContactInfo();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Get(IReadOnlyDictionary<string, string> source, string key) =>
        source.TryGetValue(key, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
}

/// <summary>Thông tin liên hệ của shop (đọc từ <c>SystemSettings</c>).</summary>
public sealed class ShopContactInfo
{
    public string Address { get; set; } = string.Empty;
    public string Hotline { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string WorkingHours { get; set; } = string.Empty;

    /// <summary>True khi có ít nhất một trường liên hệ đã được cấu hình.</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Address)
        || !string.IsNullOrWhiteSpace(Hotline)
        || !string.IsNullOrWhiteSpace(Email)
        || !string.IsNullOrWhiteSpace(WorkingHours);
}
