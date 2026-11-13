using Microsoft.Extensions.Logging;

namespace PetShop.Services;

/// <summary>
/// Cache phía server cho danh sách TÊN sản phẩm dùng cho ô tìm kiếm ở Header.
///
/// <para>
/// <b>Vì sao cần (vừa tối ưu RAM vừa tối ưu độ trễ):</b>
/// Header nằm ở MỌI trang. Trước đây mỗi lần điều hướng nó gọi
/// <c>api/products</c> để lấy toàn bộ danh mục rồi mới chỉ giữ lại cột
/// <c>Name</c>. Hệ quả:
/// </para>
/// <list type="bullet">
/// <item>Query DB + serialize hàng trăm KB JSON mỗi lần điều hướng ⇒ lag.</item>
/// <item>
/// Mỗi circuit Blazor giữ riêng một mảng string/ProductDto trong RAM ⇒
/// càng nhiều tab mở càng tốn RAM. Với 1 bản cache dùng chung (singleton)
/// thì toàn server chỉ giữ đúng 1 bản.
/// </item>
/// </list>
///
/// <para>
/// <b>TTL ngắn (30s)</b> để dữ liệu vẫn gần với DB (sản phẩm thêm/xoá không
/// bị "đơ" quá lâu), đồng thời hủy cache ngay khi có thay đổi sản phẩm
/// (xem <see cref="Invalidate"/>).
/// </para>
/// </summary>
public sealed class ProductNameCache
{
    /// <summary>Thời gian sống của cache (giây).</summary>
    private const int TtlSeconds = 30;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<ProductNameCache> _logger;

    private IReadOnlyList<string> _names = Array.Empty<string>();
    private DateTime _expiresUtc = DateTime.MinValue;
    private bool _hasValue;

    public ProductNameCache(ILogger<ProductNameCache> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách tên sản phẩm, dùng cache nếu còn hạn.
    /// </summary>
    /// <param name="factory">Hàm query DB (chỉ được gọi khi cache hết hạn).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<string>> GetAsync(Func<CancellationToken, Task<IReadOnlyList<string>>> factory, CancellationToken ct)
    {
        if (_hasValue && DateTime.UtcNow < _expiresUtc)
        {
            return _names;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Kiểm tra lần nữa: có thể một request khác đã nạp xong trong lúc chờ.
            if (_hasValue && DateTime.UtcNow < _expiresUtc)
            {
                return _names;
            }

            var fetched = await factory(ct).ConfigureAwait(false);

            // Gán trước khi trả về để các request song song dùng chung 1 mảng.
            _names = fetched ?? Array.Empty<string>();
            _expiresUtc = DateTime.UtcNow.AddSeconds(TtlSeconds);
            _hasValue = true;

            return _names;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Xoá cache — gọi khi có thay đổi sản phẩm (thêm/sửa/xoá) để ô tìm kiếm
    /// không gợi ý tên cũ.
    /// </summary>
    public void Invalidate()
    {
        _hasValue = false;
        _expiresUtc = DateTime.MinValue;
        _logger.LogDebug("[ProductNameCache] Đã vô hiệu hoá cache tên sản phẩm.");
    }
}
