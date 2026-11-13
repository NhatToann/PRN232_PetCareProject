using Microsoft.AspNetCore.Http;

namespace PetShop.Services;

/// <summary>
/// Extension đăng ký typed <see cref="HttpClient"/> gọi chính API của ứng dụng đang chạy.
///
/// <para>
/// Đặc thù của đồ án: các trang Blazor không gọi service trực tiếp mà thông qua
/// HTTP client tới chính endpoint trong app (mô phỏng kiến trúc frontend ↔ backend tách rời).
/// Vì vậy <c>BaseAddress</c> phải trỏ về origin hiện tại — lấy từ
/// <see cref="HttpContext.Request"/> nếu có, không thì fallback về cấu hình <c>App:BaseUrl</c>.
/// </para>
///
/// <para>
/// Trước đây khối này bị lặp 9 lần trong <c>Program.cs</c>; nay gom về một chỗ.
/// </para>
///
/// <para>
/// <b>TỐI ƯU RAM:</b> mọi client đều dùng chung cấu hình handler tối ưu
/// (xem <see cref="ConfigureSelfHttpHandler"/>):
/// </para>
/// <list type="bullet">
/// <item>
/// <c>MaxConnectionsPerServer = 16</c>: giới hạn số TCP connection mà mỗi client
/// giữ tới chính server. Mặc định là <c>int.MaxValue</c> ⇒ mỗi circuit có thể mở
/// hàng chục connection, mỗi connection tốn vài chục KB buffer ⇒ tổng RAM bị "ăn"
/// nhanh khi mở nhiều tab.
/// </item>
/// <item>
/// <c>PooledConnectionLifetime = 2 phút</c>: recycle connection định kỳ ⇒ không
/// giữ connection "chết" (stale) trong pool, tránh rò rỉ.
/// </item>
/// <item>
/// <c>EnableMultipleHttp2Connections / Http2</c>: không bật vì self-HTTP chạy
/// HTTP/1.1 trên cổng 5286 (xem cấu hình Kestrel trong Program.cs).
/// </item>
/// </list>
/// </summary>
public static class SelfHttpClientExtensions
{
    /// <summary>Số kết nối tối đa mỗi client giữ tới chính server.</summary>
    private const int MaxConnectionsPerServer = 16;

    /// <summary>Thời gian sống của một connection trong pool trước khi được recycle.</summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>Đăng ký typed client trỏ về chính ứng dụng.</summary>
    /// <typeparam name="TClient">Interface của client (vd <c>IOrderClient</c>).</typeparam>
    /// <typeparam name="TImplementation">Class hiện thực (vd <c>OrderClient</c>).</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Tuỳ chọn cấu hình thêm cho client.</param>
    public static IHttpClientBuilder AddSelfHttpClient<TClient, TImplementation>(
        this IServiceCollection services,
        Action<HttpClient>? configure = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        return services.AddHttpClient<TClient, TImplementation>((sp, client) =>
        {
            client.BaseAddress = ResolveBaseAddress(sp);
            client.Timeout = TimeSpan.FromSeconds(30);
            configure?.Invoke(client);
        })
        .ConfigureSelfHttpHandler();
    }

    /// <summary>
    /// Đăng ký typed client trỏ về chính ứng dụng, kèm <see cref="SpaAuthHeaderHandler"/>
    /// để gắn header phiên đăng nhập (<c>X-User-Id</c>, <c>X-Customer-Id</c>, <c>X-User-Role</c>).
    /// </summary>
    public static IHttpClientBuilder AddSelfHttpClientWithAuth<TClient, TImplementation>(
        this IServiceCollection services,
        Action<HttpClient>? configure = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        return services.AddHttpClient<TClient, TImplementation>((sp, client) =>
        {
            client.BaseAddress = ResolveBaseAddress(sp);
            client.Timeout = TimeSpan.FromSeconds(30);
            configure?.Invoke(client);
        })
        .ConfigureSelfHttpHandler()
        .AddHttpMessageHandler<SpaAuthHeaderHandler>();
    }

    /// <summary>
    /// Cấu hình handler cho self-HTTP — tối ưu RAM (giới hạn connection pool).
    /// </summary>
    private static IHttpClientBuilder ConfigureSelfHttpHandler(this IHttpClientBuilder builder)
    {
        return builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // GIỚI HẠN connection pool — tác động trực tiếp tới RAM.
            MaxConnectionsPerServer = MaxConnectionsPerServer,

            // Recycle connection định kỳ để không giữ connection chết trong pool.
            PooledConnectionLifetime = PooledConnectionLifetime,

            // Không tự động giải nén: response từ chính API nằm trong RAM của
            // cùng tiến trình ⇒ nén chỉ tốn CPU mà không tiết kiệm băng thông.
            AutomaticDecompression = System.Net.DecompressionMethods.None,

            // Self-HTTP chạy HTTP/1.1 (cổng 5286) ⇒ tắt HTTP/2 (tránh thêm
            // state machine + buffer cho mỗi connection).
            EnableMultipleHttp2Connections = false,

            // Không giữ connection tới server đã tắt.
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),

            ConnectTimeout = TimeSpan.FromSeconds(10),
        });
    }

    /// <summary>
    /// Xác định <c>BaseAddress</c> của client:
    /// ưu tiên origin của request hiện tại, không có thì fallback về <c>App:BaseUrl</c>.
    /// </summary>
    private static Uri ResolveBaseAddress(IServiceProvider sp)
    {
        var context = sp.GetService<IHttpContextAccessor>()?.HttpContext;

        if (context is not null && context.Request.Host.HasValue)
        {
            return new Uri($"{context.Request.Scheme}://{context.Request.Host}/", UriKind.Absolute);
        }

        var configured = sp.GetService<IConfiguration>()?["App:BaseUrl"];
        return new Uri(
            string.IsNullOrWhiteSpace(configured) ? "http://localhost:5286/" : configured,
            UriKind.Absolute);
    }
}
