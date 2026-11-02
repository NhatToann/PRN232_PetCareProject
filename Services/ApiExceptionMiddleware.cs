using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace PetShop.Services;

/// <summary>
/// Middleware chuyển exception thành <see cref="ProblemDetails"/> theo chuẩn RFC 7807.
/// ASP.NET Core dùng ProblemDetails cho lỗi 400+; middleware này giữ cùng format cho
/// các lỗi phát sinh trong controller/service nên client luôn parse được nhất quán.
/// </summary>
public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client đã ngắt kết nối — không phải lỗi server, không cần ghi log.
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        var (status, title, detail) = MapException(ex);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LineError(ex, $"[API] {context.Request.Method} {context.Request.Path} -> {status}");
        }
        else
        {
            _logger.LineWarning($"[API] {context.Request.Method} {context.Request.Path} -> {status}: {detail}");
        }

        if (context.Response.HasStarted)
        {
            // Response đã bắt đầu gửi — không thể ghi thêm body.
            return;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            context.RequestAborted);
    }

    private static (int Status, string Title, string Detail) MapException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Không được phép", ex.Message),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Không tìm thấy dữ liệu", ex.Message),
            ArgumentException => (StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ", ex.Message),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Thao tác không thể thực hiện", ex.Message),
            NotImplementedException => (StatusCodes.Status501NotImplemented, "Chưa hỗ trợ", "Tính năng đang được phát triển."),
            _ => (StatusCodes.Status500InternalServerError, "Lỗi hệ thống", "Đã xảy ra lỗi không mong muốn. Vui lòng thử lại sau.")
        };
    }
}
