using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;

namespace PetShop.Services;

/// <summary>
/// LS-10 / SL-04 — Custom Input Formatter (slide PRN232 Ch03 S17).
///
/// <para>
/// Slide Ch03 S17 yêu cầu: "để deserializing data received from the client, tạo một input
/// formatter class", kế thừa lớp formatter input của ASP.NET Core, override
/// <c>CanReadType</c> và <c>ReadRequestBodyAsync</c>, rồi đăng ký vào
/// <c>MvcOptions.InputFormatters</c>.
/// </para>
///
/// <para>
/// Hành vi của formatter này: khi client gửi body JSON, mọi giá trị chuỗi trong body
/// được chuyển sang CHỮ HOA trước khi deserialize thành model. Mục đích là demo cơ chế
/// custom input formatter của ASP.NET Core Web API — dùng cho endpoint demo search sản phẩm.
/// </para>
/// </summary>
public sealed class UpperCaseTextInputFormatter : InputFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<UpperCaseTextInputFormatter> _logger;

    /// <summary>
    /// Danh sách model type mà formatter được phép xử lý.
    /// </summary>
    /// <remarks>
    /// Chỉ giới hạn cho các model của endpoint demo. Nếu áp dụng cho mọi model JSON
    /// thì tên sản phẩm gửi lên qua <c>api/products-temp/create</c> cũng bị chuyển HOA
    /// ⇒ phá vỡ hành vi CRUD hiện có.
    /// </remarks>
    private static readonly HashSet<Type> AllowedModelTypes =
    [
        typeof(PetShop.Controllers.FormatterDemoRequest)
    ];

    /// <summary>
    /// Khởi tạo formatter.
    /// </summary>
    /// <param name="logger">
    /// Logger để ghi lại lỗi khi đọc body (AGENTS.md §2.2).
    /// Có thể bỏ trống khi formatter được đăng ký trực tiếp vào
    /// <c>MvcOptions.InputFormatters</c> trong <c>Program.cs</c> (chỗ đó chưa có DI container).
    /// </param>
    public UpperCaseTextInputFormatter(ILogger<UpperCaseTextInputFormatter>? logger = null)
    {
        _logger = logger ?? NullLogger<UpperCaseTextInputFormatter>.Instance;

        // Formatter này chỉ nhận JSON — không cạnh tranh với SystemTextJsonInputFormatter
        // cho các content-type khác (form-urlencoded, multipart...).
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/*+json"));
    }

    /// <summary>
    /// Chỉ xử lý các model type nằm trong danh sách cho phép.
    /// </summary>
    protected override bool CanReadType(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        // Xử lý trực tiếp chuỗi thuần (text/plain đã được chuẩn hoá hoa).
        if (underlying == typeof(string))
        {
            return true;
        }

        return AllowedModelTypes.Contains(underlying);
    }

    /// <summary>
    /// Đọc body request, chuyển các giá trị chuỗi sang HOA rồi trả về cho model binding.
    /// </summary>
    public override async Task<InputFormatterResult> ReadRequestBodyAsync(
        InputFormatterContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var request = context.HttpContext.Request;

        try
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var raw = await reader.ReadToEndAsync(context.HttpContext.RequestAborted);

            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogWarning(
                    "UpperCaseTextInputFormatter nhận body rỗng cho {Path}.",
                    request.Path.Value);

                return await InputFormatterResult.FailureAsync();
            }

            var upperCased = UpperCaseJsonStringValues(raw);

            // Deserialize trực tiếp thành model: formatter trả về object đã typed,
            // nếu trả về chuỗi thô thì MVC sẽ ép kiểu string → model và ném InvalidCastException.
            var model = JsonSerializer.Deserialize(
                upperCased,
                context.ModelType,
                SerializerOptions);

            if (model is null)
            {
                _logger.LogWarning(
                    "UpperCaseTextInputFormatter deserialize được null cho {Path}.",
                    request.Path.Value);

                return await InputFormatterResult.FailureAsync();
            }

            _logger.LogInformation(
                "UpperCaseTextInputFormatter đã chuẩn hoá body của {Path} sang chữ hoa.",
                request.Path.Value);

            return await InputFormatterResult.SuccessAsync(model);
        }
        catch (OperationCanceledException)
        {
            // Client huỷ request — không phải lỗi hệ thống.
            _logger.LogDebug("UpperCaseTextInputFormatter bị huỷ bởi client.");
            throw;
        }
        catch (JsonException ex)
        {
            // Không nuốt lỗi: log rồi trả Failure để MVC trả 400 + thông báo cho client.
            _logger.LogError(ex, "UpperCaseTextInputFormatter nhận body JSON không hợp lệ của {Path}.", request.Path.Value);
            return await InputFormatterResult.FailureAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpperCaseTextInputFormatter không đọc được body của {Path}.", request.Path.Value);
            return await InputFormatterResult.FailureAsync();
        }
    }

    /// <summary>
    /// Chuyển mọi giá trị chuỗi trong JSON thành chữ hoa mà không phá vỡ cấu trúc JSON.
    /// </summary>
    private static string UpperCaseJsonStringValues(string json)
    {
        var builder = new StringBuilder(json.Length);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];

            if (inString)
            {
                if (escaped)
                {
                    // Ký tự đã được escape (\" \\ \/ \u ...) thì giữ nguyên.
                    builder.Append(ch);
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    builder.Append(ch);
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    builder.Append(ch);
                    inString = false;
                    continue;
                }

                builder.Append(char.ToUpperInvariant(ch));
                continue;
            }

            if (ch == '"')
            {
                builder.Append(ch);
                inString = true;
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
