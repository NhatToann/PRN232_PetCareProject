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
/// </summary>
public static class SelfHttpClientExtensions
{
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
            configure?.Invoke(client);
        });
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
                configure?.Invoke(client);
            })
            .AddHttpMessageHandler<SpaAuthHeaderHandler>();
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
