using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;

namespace PetShop.Repositories;

/// <summary>
/// Truy vấn cấu hình liên hệ của shop từ bảng <c>SystemSettings</c>.
/// Tách riêng khỏi service (3-layer: Controller/Component → Service → Repository).
///
/// <para>
/// <b>VÌ SAO DÙNG IDbContextFactory:</b> TopBar + Footer + Contact đều gọi
/// service này, và chúng được render SONG SONG với page hiện tại trong cùng
/// circuit Blazor Server. Nếu dùng chung <c>DbContext</c> scoped ⇒ EF Core ném
/// <c>"A second operation was started on this context instance"</c> ⇒ trang
/// bị crash (rõ ràng ở <c>/boarding</c>). Mỗi lần đọc tạo context riêng.
/// </para>
///
/// <para>
/// <b>SỬA BUG DI (11/11/2026) — KHÔNG được inject scoped vào singleton:</b>
/// <c>IDbContextFactory&lt;ShopPetDatabaseContext&gt;</c> được đăng ký với
/// lifetime <c>Scoped</c> (xem <c>Program.cs</c>). Nếu repository này được đăng ký
/// là <c>Singleton</c> mà inject trực tiếp factory scoped ⇒ DI container ném
/// <c>InvalidOperationException: "Cannot consume scoped service ... from singleton"</c>
/// NGAY LÚC khởi tạo host ⇒ toàn bộ app KHÔNG START ĐƯỢC trong test
/// (<c>WebApplicationFactory</c>) vì DI validation bật.
///
/// ⇒ Nay repository tự tạo scope riêng qua <see cref="IServiceScopeFactory"/>
/// (đăng ký Singleton ⇒ hợp lệ) rồi resolve factory scoped BÊN TRONG scope đó.
/// Kết quả: repository vẫn Singleton (như service) mà không vi phạm DI.
/// </para>
/// </summary>
public sealed class ShopContactRepository : IShopContactRepository
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ShopContactRepository(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetShopContactSettingsAsync(CancellationToken ct = default)
    {
        // Mỗi lần đọc tạo scope riêng ⇒ context riêng ⇒ không đụng nhau,
        // đồng thời vẫn an toàn khi repository là Singleton.
        using var scope = _scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ShopPetDatabaseContext>>();

        await using var context = await factory.CreateDbContextAsync(ct);
        var keys = new[] { "ShopAddress", "ShopHotline", "ShopEmail", "ShopWorkingHours" };

        var rows = await context.SystemSettings
            .AsNoTracking()
            .Where(s => keys.Contains(s.SettingKey))
            .ToListAsync(ct);

        return rows
            .GroupBy(s => s.SettingKey)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.SettingValue).FirstOrDefault() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }
}
