using System.Collections;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace PetShop.Services;

/// <summary>
/// Output formatter trả về CSV khi client gửi header "Accept: text/csv".
/// Chỉ nhận các collection phẳng (IEnumerable của object/record) — không đệ quy vào
/// nested object để giữ định dạng đơn giản, đúng mục đích demo content negotiation.
/// </summary>
public sealed class CsvOutputFormatter : TextOutputFormatter
{
    public CsvOutputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/csv"));
        SupportedEncodings.Add(Encoding.UTF8);
    }

    protected override bool CanWriteType(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (!typeof(IEnumerable).IsAssignableFrom(type) || type == typeof(string))
        {
            return false;
        }

        // Dictionary không có thuộc tính công khai để lấy tên cột.
        return !IsDictionaryLike(type);
    }

    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        var items = context.Object as IEnumerable;
        if (items is null)
        {
            return;
        }

        var list = items.Cast<object?>().Where(x => x is not null).ToList();
        var httpContext = context.HttpContext;

        if (list.Count == 0)
        {
            await httpContext.Response.WriteAsync(string.Empty, selectedEncoding);
            return;
        }

        var properties = GetScalarProperties(list[0]!.GetType());
        var builder = new StringBuilder();

        builder.AppendLine(string.Join(",", properties.Select(p => EscapeCsv(p.Name))));
        foreach (var item in list)
        {
            var values = properties.Select(p => EscapeCsv(FormatValue(p.GetValue(item))));
            builder.AppendLine(string.Join(",", values));
        }

        await httpContext.Response.WriteAsync(builder.ToString(), selectedEncoding);
    }

    private static bool IsDictionaryLike(Type type)
    {
        return type.GetInterfaces()
            .Concat(new[] { type })
            .Any(i => i.IsGenericType &&
                       i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
    }

    private static List<System.Reflection.PropertyInfo> GetScalarProperties(Type type)
    {
        return type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => IsScalar(p.PropertyType))
            .ToList();
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string) || underlying == typeof(decimal) || underlying == typeof(DateTime))
        {
            return true;
        }

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(TimeSpan)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(Guid);
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Luôn bọc trong dấu nháy để tránh lỗi khi chứa dấu phẩy, xuống dòng hoặc dấu nháy.
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
