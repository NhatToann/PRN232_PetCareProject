using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using PetShop.Protos;

namespace PetShop.Services;

/// <summary>
/// LS-09 — gRPC client dùng để demo 4 loại RPC (slide PRN232 Ch09).
///
/// <para>
/// Client dùng <c>Grpc.Net.Client</c> với <c>GrpcChannel.ForAddress</c>. Vì chạy trong
/// cùng tiến trình với server (self-call) nên HTTP/2 không mã hoá (h2c) được dùng;
/// <c>AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)</c>
/// là bắt buộc cho các channel không TLS.
/// </para>
///
/// <para>
/// Cổng 5287: Kestrel cấu hình endpoint này chỉ nhận HTTP/2 thuần (h2c) — điều kiện bắt
/// buộc để gRPC hoạt động. Cổng 5286 phục vụ web/REST với HTTP/1.1 + HTTP/2.
/// </para>
/// </summary>
public sealed class GrpcPetCatalogClient : IDisposable
{
    /// <summary>Cổng HTTP/2 thuần (h2c) do Kestrel lắng nghe cho gRPC.</summary>
    public const int GrpcPort = 5287;

    private readonly GrpcChannel _channel;
    private readonly PetCatalog.PetCatalogClient _client;
    private readonly ILogger<GrpcPetCatalogClient> _logger;
    private bool _disposed;

    /// <summary>
    /// Khởi tạo client gRPC trỏ về endpoint của ứng dụng đang chạy.
    /// </summary>
    /// <param name="logger">Logger để ghi lại lỗi — không nuốt lỗi (AGENTS.md §2.2).</param>
    public GrpcPetCatalogClient(ILogger<GrpcPetCatalogClient> logger)
    {
        _logger = logger;

        // Cho phép kết nối HTTP/2 không mã hoá (h2c) — server Kestrel của app chạy cùng cổng.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        _channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}");
        _client = new PetCatalog.PetCatalogClient(_channel);
    }

    /// <summary>
    /// RPC unary — trả về danh sách sản phẩm theo điều kiện lọc.
    /// </summary>
    public async Task<GetProductsReply> GetProductsAsync(
        string? keyword,
        int? categoryId,
        int take = 0,
        CancellationToken ct = default)
    {
        try
        {
            var request = new GetProductsRequest
            {
                Keyword = keyword ?? string.Empty,
                Take = take
            };

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                request.CategoryId = categoryId.Value;
            }

            var reply = await _client.GetProductsAsync(request, cancellationToken: ct);
            _logger.LogInformation("gRPC client GetProductsAsync: nhận {Count} sản phẩm.", reply.Products.Count);
            return reply;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC client GetProductsAsync thất bại: {Status}.", ex.StatusCode);
            throw;
        }
    }

    /// <summary>
    /// RPC server streaming — đọc luồng sản phẩm do server phát ra.
    /// </summary>
    public async Task<List<ProductReply>> StreamProductsAsync(
        string? keyword,
        int? categoryId,
        CancellationToken ct = default)
    {
        var result = new List<ProductReply>();

        try
        {
            var request = new GetProductsRequest
            {
                Keyword = keyword ?? string.Empty
            };

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                request.CategoryId = categoryId.Value;
            }

            using var stream = _client.StreamProducts(request, cancellationToken: ct);

            while (await stream.ResponseStream.MoveNext(ct))
            {
                result.Add(stream.ResponseStream.Current);
            }

            _logger.LogInformation("gRPC client StreamProductsAsync: đọc được {Count} sản phẩm.", result.Count);
            return result;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC client StreamProductsAsync thất bại: {Status}.", ex.StatusCode);
            throw;
        }
    }

    /// <summary>
    /// RPC client streaming — gửi nhiều sản phẩm lên server và nhận 1 response tổng hợp.
    /// </summary>
    public async Task<UploadSummaryReply> UploadProductsAsync(
        IEnumerable<ProductUploadRequest> products,
        CancellationToken ct = default)
    {
        try
        {
            using var stream = _client.UploadProducts(cancellationToken: ct);

            foreach (var product in products)
            {
                await stream.RequestStream.WriteAsync(product);
            }

            await stream.RequestStream.CompleteAsync();

            var reply = await stream.ResponseAsync;
            _logger.LogInformation(
                "gRPC client UploadProductsAsync: nhận {Accepted}/{Received} sản phẩm hợp lệ.",
                reply.AcceptedCount,
                reply.ReceivedCount);

            return reply;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC client UploadProductsAsync thất bại: {Status}.", ex.StatusCode);
            throw;
        }
    }

    /// <summary>
    /// RPC bidirectional streaming — gửi từng bản ghi và đọc kết quả trả về ngay.
    /// </summary>
    public async Task<List<ProductSyncReply>> SyncProductsAsync(
        IEnumerable<ProductSyncRequest> requests,
        CancellationToken ct = default)
    {
        var replies = new List<ProductSyncReply>();

        try
        {
            using var stream = _client.SyncProducts(cancellationToken: ct);

            foreach (var request in requests)
            {
                await stream.RequestStream.WriteAsync(request);
            }

            await stream.RequestStream.CompleteAsync();

            while (await stream.ResponseStream.MoveNext(ct))
            {
                replies.Add(stream.ResponseStream.Current);
            }

            _logger.LogInformation("gRPC client SyncProductsAsync: nhận {Count} kết quả.", replies.Count);
            return replies;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC client SyncProductsAsync thất bại: {Status}.", ex.StatusCode);
            throw;
        }
    }

    /// <summary>
    /// Giải phóng channel khi service bị dispose.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Dispose();
    }
}
