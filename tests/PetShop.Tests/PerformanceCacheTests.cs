using Microsoft.Extensions.Logging.Abstractions;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho <see cref="ProductNameCache"/> — cache danh sách TÊN sản phẩm
/// dùng chung cho mọi circuit Blazor.
///
/// <para>
/// Hành vi cần bảo vệ (gắn với vấn đề hiệu năng/RAM đã gặp):
/// </para>
/// <list type="number">
/// <item>Trong TTL: chỉ query DB đúng 1 lần, các lần sau trả kết quả đã cache.</item>
/// <item>Hết TTL: query lại (dữ liệu không bị "đơ" quá lâu).</item>
/// <item><c>Invalidate()</c> (sau khi thêm/sửa/xoá sản phẩm): lần gọi kế tiếp phải query lại.</item>
/// <item>Gọi song song (thundering herd): chỉ query DB đúng 1 lần.</item>
/// </list>
/// </summary>
public sealed class ProductNameCacheTests
{
    private static ProductNameCache CreateCache() => new(NullLogger<ProductNameCache>.Instance);

    [Fact]
    public async Task GetAsync_TrongTTL_ChiQueryMotLan()
    {
        var cache = CreateCache();
        var calls = 0;

        Task<IReadOnlyList<string>> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult<IReadOnlyList<string>>(new[] { "A", "B" });
        }

        var first = await cache.GetAsync(Factory, CancellationToken.None);
        var second = await cache.GetAsync(Factory, CancellationToken.None);
        var third = await cache.GetAsync(Factory, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(first, second);
        Assert.Equal(second, third);
        Assert.Equal(new[] { "A", "B" }, first);
    }

    [Fact]
    public async Task Invalidate_LanGoiKeTiepPhaiQueryLai()
    {
        var cache = CreateCache();
        var calls = 0;

        Task<IReadOnlyList<string>> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult<IReadOnlyList<string>>(new[] { $"V{calls}" });
        }

        var first = await cache.GetAsync(Factory, CancellationToken.None);
        Assert.Equal("V1", first[0]);

        cache.Invalidate();

        var second = await cache.GetAsync(Factory, CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.Equal("V2", second[0]);
    }

    [Fact]
    public async Task GetAsync_GoiSongSong_ChiQueryMotLan()
    {
        var cache = CreateCache();
        var calls = 0;

        async Task<IReadOnlyList<string>> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            // Trễ nhẹ để các lời gọi song song thực sự cạnh tranh nhau.
            await Task.Delay(50);
            return new[] { "X" };
        }

        var tasks = new[]
        {
            cache.GetAsync(Factory, CancellationToken.None),
            cache.GetAsync(Factory, CancellationToken.None),
            cache.GetAsync(Factory, CancellationToken.None),
            cache.GetAsync(Factory, CancellationToken.None),
        };

        await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetAsync_FactoryTraNull_TraVeMangRongKhongLamLoi()
    {
        var cache = CreateCache();

        Task<IReadOnlyList<string>> Factory(CancellationToken _) =>
            Task.FromResult<IReadOnlyList<string>>(null!);

        var result = await cache.GetAsync(Factory, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAsync_KhongCoUserId_TraVeMangRong()
    {
        var cache = CreateCache();
        var result = await cache.GetAsync(_ => Task.FromResult<IReadOnlyList<string>>(new[] { "Z" }), CancellationToken.None);
        Assert.Single(result);
    }
}

/// <summary>
/// Unit test cho <see cref="CartStateService"/> — chống gọi JS interop lặp lại.
///
/// <para>
/// Vấn đề: <c>MainLayout.razor</c> và <c>Header.razor</c> cùng gọi
/// <c>LoadAsync()</c> ⇒ 2 lần <c>cartStorage.read</c> mỗi lần mở trang.
/// </para>
/// </summary>
public sealed class CartStateServiceTests
{
    [Fact]
    public async Task LoadAsync_GoiNhieuLan_ChiDongJSInteropMotLan()
    {
        var js = new FakeJsRuntime();
        var service = new CartStateService(js, NullLogger<CartStateService>.Instance);

        await service.LoadAsync();
        await service.LoadAsync();
        await service.LoadAsync();

        Assert.Equal(1, js.ReadCount);
    }

    [Fact]
    public async Task LoadAsync_DocDungDuLieuTuStorage()
    {
        var js = new FakeJsRuntime
        {
            StoredJson = "[{\"id\":7,\"name\":\"Cho Corgi\",\"price\":150000,\"quantity\":2,\"image\":\"a.jpg\",\"stockQuantity\":10}]"
        };

        var service = new CartStateService(js, NullLogger<CartStateService>.Instance);
        await service.LoadAsync();

        Assert.Single(service.Items);
        Assert.Equal(7, service.Items[0].ProductId);
        Assert.Equal(2, service.Count);
        Assert.Equal(300000m, service.Total);
    }

    [Fact]
    public async Task AddAsync_SanPhamHetHang_Throw()
    {
        var js = new FakeJsRuntime();
        var service = new CartStateService(js, NullLogger<CartStateService>.Instance);
        await service.LoadAsync();

        var product = new ProductDto(1, "Hết hàng", 1000m, 0, null, null, null, null, null, null, 0d, 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(product, 1));
    }

    [Fact]
    public async Task AddAsync_ClampSoLuongTheoTonKho()
    {
        var js = new FakeJsRuntime();
        var service = new CartStateService(js, NullLogger<CartStateService>.Instance);
        await service.LoadAsync();

        var product = new ProductDto(1, "Bóng", 1000m, 3, null, null, null, null, null, null, 0d, 0);

        await service.AddAsync(product, 99);

        // Count là TỔNG số lượng, nên bằng số lượng bị clamp về tồn kho (3).
        Assert.Equal(3, service.Count);
        Assert.Equal(3, service.Items[0].Quantity);
    }

    [Fact]
    public async Task ResetLoadedState_ChoPhepDocLai()
    {
        var js = new FakeJsRuntime();
        var service = new CartStateService(js, NullLogger<CartStateService>.Instance);

        await service.LoadAsync();
        service.ResetLoadedState();
        await service.LoadAsync();

        Assert.Equal(2, js.ReadCount);
    }

    /// <summary>JS runtime giả lập — chỉ hỗ trợ <c>cartStorage.read</c> / <c>cartStorage.write</c>.</summary>
    private sealed class FakeJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public int ReadCount { get; private set; }
        public string? StoredJson { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "cartStorage.read")
            {
                ReadCount++;
                var json = StoredJson;
                var result = json is null
                    ? default(TValue)!
                    : (TValue)(object)System.Text.Json.JsonSerializer.Deserialize<List<CartStateService.CartStorageItem>>(
                        json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                return ValueTask.FromResult(result);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }
}
