using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PetShop.Models;

namespace PetShop.Services;

public sealed class CartStateService
{
    private const string KeyCart = "cart";

    public event Action? Changed;

    private List<FrontendCartItem> _items = new();
    public IReadOnlyList<FrontendCartItem> Items => _items;

    public int Count => _items.Sum(item => item.Quantity);
    public decimal Total => _items.Sum(item => item.Price * item.Quantity);

    private IJSRuntime? _js;
    private readonly ILogger<CartStateService> _logger;

    /// <summary>
    /// Task đọc giỏ hàng đang chạy — chia sẻ giữa các caller song song.
    ///
    /// <para>
    /// Thay cho cờ <c>_loaded</c> cũ: cờ chỉ được set <b>sau</b> khi interop xong
    /// nên các caller chạy song song vẫn gọi interop lặp lại ⇒ gây giẽn nhau
    /// (TaskCanceledException) ⇒ chết circuit. Giờ mọi caller await cùng 1 task.
    /// </para>
    /// </summary>
    private Task? _loadTask;

    public CartStateService(IJSRuntime js, ILogger<CartStateService> logger)
    {
        _js = js;
        _logger = logger;
    }

    public void AttachRuntime(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Đọc giỏ hàng từ <c>localStorage</c> (qua JS interop).
    ///
    /// <para>
    /// <b>Guard "đã tải" (idempotent):</b> trước đây <c>MainLayout.razor</c> và
    /// <c>Header.razor</c> CÙNG gọi <c>LoadAsync()</c> ⇒ 2 lần JS interop
    /// <c>cartStorage.read</c> cho cùng một dữ liệu mỗi lần mở trang.
    /// Mỗi lần gọi interop là một chuyến đi về browser ⇒ độ trễ + giữ message
    /// trong hàng đợi SignalR của circuit ⇒ tốn RAM.
    /// Nay lần gọi thứ 2 trở đi là no-op ⇒ chỉ 1 interop duy nhất.
    /// </para>
    ///
    /// <para>
    /// <b>SỬA BUG (11/11/2026) — TaskCanceledException làm chết circuit:</b>
    /// MainLayout + Header + CartDrawer + Home + Cart + Checkout đều gọi
    /// <c>LoadAsync()</c> gần như đồng thời. Guard <c>_loaded</c> CHỈ được set
    /// <b>sau</b> khi interop hoàn tất ⇒ các caller chạy song song đều thấy
    /// <c>_loaded == false</c> ⇒ cùng gọi <c>cartStorage.read</c> ⇒ các lần
    /// sau bị hủy (circuit ngắt kết nối) ⇒ <c>TaskCanceledException</c> ném ra
    /// ngoài ⇒ component crash ⇒ circuit chết.
    ///
    /// ⇒ Dùng <c>_loadTask</c> để CHIA SẺ đúng một lần đọc giữa mọi caller
    /// (giống cách <c>UserSessionService.LoadAsync</c> đã làm).
    /// </para>
    /// </summary>
    public async Task LoadAsync()
    {
        // Share the in-flight load across concurrent callers.
        if (_loadTask is null)
        {
            _loadTask = LoadCoreAsync();
        }

        try
        {
            await _loadTask;
        }
        catch (OperationCanceledException)
        {
            // Circuit bị ngắt giữa chừng ⇒ cho phép thử lại ở lần render sau.
            _loadTask = null;
            throw;
        }
    }

    private async Task LoadCoreAsync()
    {
        _items = new List<FrontendCartItem>();
        if (_js is null) return;
        try
        {
            var parsed = await _js.InvokeAsync<List<CartStorageItem>?>("cartStorage.read");
            if (parsed is not null)
            {
                _items = parsed.Select(p => new FrontendCartItem(
                    p.id, p.name ?? "", p.price, p.quantity, p.image, p.stockQuantity)).ToList();
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // Circuit may not be ready, prerender phase, or localStorage payload malformed.
            // Re-load on reconnect or retry will be handled by the caller.
            _logger.Line($"[CartStateService] LoadAsync skipped: {ex.GetType().Name} {ex.Message}");
        }
    }

    public async Task AddAsync(ProductDto product, int quantity = 1)
    {
        if (product.StockQuantity <= 0)
            throw new InvalidOperationException("Sản phẩm đã hết hàng.");

        var nextQuantity = Math.Clamp(quantity, 1, product.StockQuantity);
        var idx = _items.FindIndex(item => item.ProductId == product.ProductId);
        if (idx >= 0)
        {
            var current = _items[idx];
            _items[idx] = current with { Quantity = Math.Min(product.StockQuantity, current.Quantity + nextQuantity), StockQuantity = product.StockQuantity };
        }
        else
        {
            _items.Add(new FrontendCartItem(product.ProductId, product.Name, product.Price, nextQuantity, product.ImageUrl, product.StockQuantity));
        }

        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task UpdateQuantityAsync(int productId, int quantity)
    {
        var idx = _items.FindIndex(item => item.ProductId == productId);
        if (idx < 0) return;

        if (quantity <= 0)
            _items.RemoveAt(idx);
        else
        {
            var item = _items[idx];
            _items[idx] = item with { Quantity = Math.Min(quantity, Math.Max(1, item.StockQuantity)) };
        }

        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task RemoveAsync(int productId)
    {
        _items.RemoveAll(item => item.ProductId == productId);
        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        _items.Clear();
        await SaveAsync();
        Changed?.Invoke();
    }

    /// <summary>
    /// Đặt lại cờ "đã tải" — dùng khi circuit mới được tạo lại (sau khi đăng nhập
    /// lại / đăng xuất) để giỏ hàng được đọc lại từ <c>localStorage</c>.
    /// </summary>
    public void ResetLoadedState()
    {
        _loadTask = null;
    }

    private async Task SaveAsync()
    {
        if (_js is null) return;
        try
        {
            var dto = _items.Select(i => new CartStorageItem(
                i.ProductId, i.Name, i.Price, i.Quantity, i.ImageUrl, i.StockQuantity)).ToList();
            await _js.InvokeVoidAsync("cartStorage.write", dto);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException)
        {
            // Circuit disconnected; persistence skipped. Data remains in memory.
            _logger.Line($"[CartStateService] SaveAsync skipped: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>Shape của item giỏ hàng lưu trong <c>localStorage</c> (phải khớp key JS).</summary>
    public sealed record CartStorageItem(int id, string? name, decimal price, int quantity, string? image, int stockQuantity);
}
