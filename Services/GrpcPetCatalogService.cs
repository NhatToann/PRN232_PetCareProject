using Grpc.Core;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Protos;

namespace PetShop.Services;

/// <summary>
/// LS-09 — gRPC service cho catalog sản phẩm (slide PRN232 Ch09).
///
/// <para>
/// Lớp này kế thừa <c>PetCatalog.PetCatalogBase</c> do Grpc.Tools sinh ra từ
/// <c>Protos/petcatalog.proto</c>, nên chỉ cần override 4 method tương ứng 4 loại RPC:
/// unary, server streaming, client streaming và bidirectional streaming.
/// </para>
///
/// <para>
/// Nguyên tắc 3-layer của dự án được giữ: service chỉ gọi <see cref="IProductService"/>,
/// không inject <c>DbContext</c> trực tiếp.
/// </para>
/// </summary>
public sealed class GrpcPetCatalogService : PetCatalog.PetCatalogBase
{
    private readonly IProductService _productService;
    private readonly ILogger<GrpcPetCatalogService> _logger;

    /// <summary>
    /// Khởi tạo gRPC service.
    /// </summary>
    /// <param name="productService">Lớp nghiệp vụ sản phẩm (tầng Service của 3-layer).</param>
    /// <param name="logger">Logger để ghi lại lỗi — không nuốt lỗi (AGENTS.md §2.2).</param>
    public GrpcPetCatalogService(
        IProductService productService,
        ILogger<GrpcPetCatalogService> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    /// <summary>
    /// RPC unary — lấy danh sách sản phẩm theo điều kiện lọc.
    /// </summary>
    public override async Task<GetProductsReply> GetProducts(
        GetProductsRequest request,
        ServerCallContext context)
    {
        try
        {
            var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword;
            var categoryId = request.CategoryId > 0 ? request.CategoryId : (int?)null;

            var items = await _productService.GetAllAsync(keyword, categoryId, context.CancellationToken);

            // take/skip chỉ áp dụng sau khi lấy từ tầng Service, giữ nguyên quy tắc phân trang.
            var page = ApplyPaging(items, request.Skip, request.Take);

            var reply = new GetProductsReply
            {
                TotalCount = items.Count
            };

            reply.Products.AddRange(page.Select(ToProductReply));

            _logger.LogInformation(
                "gRPC GetProducts: keyword={Keyword}, categoryId={CategoryId}, trả về {Count}/{Total} bản ghi.",
                keyword,
                categoryId,
                page.Count,
                items.Count);

            return reply;
        }
        catch (OperationCanceledException)
        {
            // Client huỷ request — không phải lỗi hệ thống, chỉ ghi mức Debug.
            _logger.LogDebug("gRPC GetProducts bị huỷ bởi client.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC GetProducts thất bại.");
            throw new RpcException(new Status(StatusCode.Internal, "Không lấy được danh sách sản phẩm."));
        }
    }

    /// <summary>
    /// RPC server streaming — phát từng sản phẩm một qua luồng response.
    /// </summary>
    public override async Task StreamProducts(
        GetProductsRequest request,
        IServerStreamWriter<ProductReply> responseStream,
        ServerCallContext context)
    {
        try
        {
            var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword;
            var categoryId = request.CategoryId > 0 ? request.CategoryId : (int?)null;

            var items = await _productService.GetAllAsync(keyword, categoryId, context.CancellationToken);
            var page = ApplyPaging(items, request.Skip, request.Take);

            var sent = 0;
            foreach (var item in page)
            {
                await responseStream.WriteAsync(ToProductReply(item));
                sent++;
            }

            _logger.LogInformation(
                "gRPC StreamProducts: đã phát {Count}/{Total} sản phẩm qua server stream.",
                sent,
                items.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("gRPC StreamProducts bị huỷ bởi client.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC StreamProducts thất bại.");
            throw new RpcException(new Status(StatusCode.Internal, "Không phát được luồng sản phẩm."));
        }
    }

    /// <summary>
    /// RPC client streaming — đọc hết stream do client gửi lên rồi trả về 1 response tổng hợp.
    /// </summary>
    public override async Task<UploadSummaryReply> UploadProducts(
        IAsyncStreamReader<ProductUploadRequest> requestStream,
        ServerCallContext context)
    {
        var summary = new UploadSummaryReply();
        var received = 0;

        try
        {
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                received++;

                var item = requestStream.Current;
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    summary.RejectedCount++;
                    summary.Messages.Add($"Sản phẩm #{received}: thiếu tên, đã bỏ qua.");
                    continue;
                }

                if (item.Price < 0 || item.StockQuantity < 0)
                {
                    summary.RejectedCount++;
                    summary.Messages.Add($"Sản phẩm '{item.Name}': giá hoặc số lượng không hợp lệ, đã bỏ qua.");
                    continue;
                }

                var created = await _productService.CreateAsync(
                    new ProductCreateRequest(
                        Name: item.Name,
                        CategoryId: item.CategoryId,
                        Price: (decimal)item.Price,
                        StockQuantity: item.StockQuantity,
                        SupplierName: null,
                        Description: string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
                        ImageUrl: string.IsNullOrWhiteSpace(item.ImageUrl) ? null : item.ImageUrl),
                    context.CancellationToken);

                if (created is null)
                {
                    summary.RejectedCount++;
                    summary.Messages.Add($"Sản phẩm '{item.Name}': không tạo được trong CSDL.");
                }
                else
                {
                    summary.AcceptedCount++;
                    summary.Messages.Add($"Đã tạo sản phẩm '{created.Name}' (ID {created.ProductId}).");
                }
            }

            summary.ReceivedCount = received;

            _logger.LogInformation(
                "gRPC UploadProducts: nhận {Received}, chấp nhận {Accepted}, từ chối {Rejected}.",
                summary.ReceivedCount,
                summary.AcceptedCount,
                summary.RejectedCount);

            return summary;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("gRPC UploadProducts bị huỷ bởi client.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC UploadProducts thất bại sau {Received} bản ghi.", received);
            throw new RpcException(new Status(StatusCode.Internal, "Không nhập được danh sách sản phẩm."));
        }
    }

    /// <summary>
    /// RPC bidirectional streaming — đọc và ghi song song hai chiều:
    /// mỗi bản ghi client gửi lên sẽ được xử lý và trả kết quả ngay trên cùng luồng.
    /// </summary>
    public override async Task SyncProducts(
        IAsyncStreamReader<ProductSyncRequest> requestStream,
        IServerStreamWriter<ProductSyncReply> responseStream,
        ServerCallContext context)
    {
        try
        {
            var processed = 0;

            while (await requestStream.MoveNext(context.CancellationToken))
            {
                processed++;
                var request = requestStream.Current;
                var operation = (request.Operation ?? string.Empty).Trim().ToLowerInvariant();

                ProductSyncReply reply;

                if (operation == "delete")
                {
                    var productId = request.Product?.ProductId ?? 0;
                    var deleted = await _productService.SoftDeleteAsync(productId, context.CancellationToken);

                    reply = new ProductSyncReply
                    {
                        Success = deleted,
                        ProductId = productId,
                        Message = deleted
                            ? $"Đã xoá mềm sản phẩm ID {productId}."
                            : $"Không tìm thấy sản phẩm ID {productId} để xoá."
                    };
                }
                else if (operation == "upsert")
                {
                    var item = request.Product;
                    if (item is null || string.IsNullOrWhiteSpace(item.Name))
                    {
                        reply = new ProductSyncReply
                        {
                            Success = false,
                            ProductId = item?.ProductId ?? 0,
                            Message = "Bản ghi upsert thiếu tên sản phẩm."
                        };
                    }
                    else if (item.ProductId > 0)
                    {
                        var updated = await _productService.UpdateAsync(
                            new ProductUpdateRequest(
                                ProductId: item.ProductId,
                                Name: item.Name,
                                CategoryId: item.CategoryId,
                                Price: (decimal)item.Price,
                                StockQuantity: item.StockQuantity,
                                SupplierName: null,
                                Description: string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
                                ImageUrl: string.IsNullOrWhiteSpace(item.ImageUrl) ? null : item.ImageUrl),
                            context.CancellationToken);

                        reply = new ProductSyncReply
                        {
                            Success = updated is not null,
                            ProductId = item.ProductId,
                            Message = updated is null
                                ? $"Không tìm thấy sản phẩm ID {item.ProductId} để cập nhật."
                                : $"Đã cập nhật sản phẩm '{updated.Name}'."
                        };
                    }
                    else
                    {
                        var created = await _productService.CreateAsync(
                            new ProductCreateRequest(
                                Name: item.Name,
                                CategoryId: item.CategoryId,
                                Price: (decimal)item.Price,
                                StockQuantity: item.StockQuantity,
                                SupplierName: null,
                                Description: string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
                                ImageUrl: string.IsNullOrWhiteSpace(item.ImageUrl) ? null : item.ImageUrl),
                            context.CancellationToken);

                        reply = new ProductSyncReply
                        {
                            Success = created is not null,
                            ProductId = created?.ProductId ?? 0,
                            Message = created is null
                                ? $"Không tạo được sản phẩm '{item.Name}'."
                                : $"Đã tạo sản phẩm '{created.Name}' (ID {created.ProductId})."
                        };
                    }
                }
                else
                {
                    reply = new ProductSyncReply
                    {
                        Success = false,
                        ProductId = request.Product?.ProductId ?? 0,
                        Message = $"Thao tác '{request.Operation}' không hợp lệ (chỉ nhận 'upsert' hoặc 'delete')."
                    };
                }

                await responseStream.WriteAsync(reply);
            }

            _logger.LogInformation("gRPC SyncProducts: đã xử lý {Processed} bản ghi hai chiều.", processed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("gRPC SyncProducts bị huỷ bởi client.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC SyncProducts thất bại.");
            throw new RpcException(new Status(StatusCode.Internal, "Không đồng bộ được danh sách sản phẩm."));
        }
    }

    /// <summary>
    /// Áp dụng phân trang (skip/take) cho kết quả đã lấy từ tầng Service.
    /// </summary>
    private static IReadOnlyList<ProductDto> ApplyPaging(
        IReadOnlyList<ProductDto> items,
        int skip,
        int take)
    {
        IEnumerable<ProductDto> query = items;

        if (skip > 0)
        {
            query = query.Skip(skip);
        }

        if (take > 0)
        {
            query = query.Take(take);
        }

        return query.ToList();
    }

    /// <summary>
    /// Chuyển DTO của tầng Service sang message gRPC.
    /// </summary>
    private static ProductReply ToProductReply(ProductDto dto)
    {
        var reply = new ProductReply
        {
            ProductId = dto.ProductId,
            Name = dto.Name,
            Price = (double)dto.Price,
            StockQuantity = dto.StockQuantity,
            AvgRating = dto.AvgRating,
            ReviewCount = dto.ReviewCount
        };

        // Proto3 `optional` dùng cơ chế presence: property C# là nullable, nhưng setter
        // của Google.Protobuf KHÔNG chấp nhận null (CheckNotNull) — null được biểu diễn
        // bằng trạng thái "không set" (HasXxx = false). Vì vậy chỉ gán khi có giá trị.
        if (dto.Description is not null)
        {
            reply.Description = dto.Description;
        }

        if (dto.ImageUrl is not null)
        {
            reply.ImageUrl = dto.ImageUrl;
        }

        if (dto.CategoryId.HasValue)
        {
            reply.CategoryId = dto.CategoryId.Value;
        }

        if (dto.CategoryName is not null)
        {
            reply.CategoryName = dto.CategoryName;
        }

        if (dto.SupplierId.HasValue)
        {
            reply.SupplierId = dto.SupplierId.Value;
        }

        if (dto.SupplierName is not null)
        {
            reply.SupplierName = dto.SupplierName;
        }

        return reply;
    }
}
