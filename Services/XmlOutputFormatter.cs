using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace PetShop.Services;

/// <summary>
/// Output formatter trả về XML khi client gửi header "Accept: application/xml".
///
/// Vì sao không dùng formatter XML có sẵn của ASP.NET Core:
/// các DTO của dự án là positional record — chúng chỉ có init-only setter nên
/// XmlSerializer (cần constructor không tham số) và DataContractSerializer
/// (cần setter thật) đều không serialize được. Formatter này tự dựng XML từ
/// các thuộc tính public, nên hoạt động được với mọi DTO hiện có.
/// </summary>
public sealed class XmlOutputFormatter : TextOutputFormatter
{
    public XmlOutputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/xml"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/xml"));
        SupportedEncodings.Add(Encoding.UTF8);
    }

    protected override bool CanWriteType(Type? type) => type is not null && type != typeof(string);

    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        var httpContext = context.HttpContext;
        var settings = new XmlWriterSettings
        {
            Encoding = selectedEncoding,
            Indent = true,
            OmitXmlDeclaration = false
        };

        await using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            var rootName = ResolveRootName(context.Object?.GetType());
            writer.WriteStartElement(rootName);

            if (context.Object is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    WriteItem(writer, item);
                }
            }
            else if (context.Object is not null)
            {
                WriteItem(writer, context.Object);
            }

            writer.WriteEndElement();
        }

        httpContext.Response.ContentLength = stream.Length;
        stream.Position = 0;
        await stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
    }

    private static void WriteItem(XmlWriter writer, object item)
    {
        writer.WriteStartElement("Item");
        WriteProperties(writer, item);
        writer.WriteEndElement();
    }

    private static void WriteProperties(XmlWriter writer, object item)
    {
        foreach (var property in GetProperties(item.GetType()))
        {
            var value = property.GetValue(item);

            // Bỏ qua thuộc tính null để XML gọn và tránh rỗng.
            if (value is null)
            {
                continue;
            }

            writer.WriteStartElement(property.Name);

            if (value is IEnumerable nested and not string)
            {
                foreach (var child in nested)
                {
                    if (child is null)
                    {
                        continue;
                    }

                    writer.WriteStartElement("Item");
                    WriteProperties(writer, child);
                    writer.WriteEndElement();
                }
            }
            else
            {
                writer.WriteString(FormatValue(value));
            }

            writer.WriteEndElement();
        }
    }

    private static IEnumerable<PropertyInfo> GetProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => IsSerializable(p.PropertyType));
    }

    /// <summary>
    /// Bỏ qua thuộc tính kiểu collection phức tạp (IReadOnlyList với phần tử object)
    /// vì XML formatter chỉ xử lý tốt các giá trị scalar và collection phẳng.
    /// </summary>
    private static bool IsSerializable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string) || underlying == typeof(decimal) || underlying == typeof(DateTime)
            || underlying == typeof(DateOnly) || underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid))
        {
            return true;
        }

        if (underlying.IsPrimitive || underlying.IsEnum)
        {
            return true;
        }

        // Collection phẳng của scalar là serialize được.
        if (typeof(IEnumerable).IsAssignableFrom(underlying))
        {
            var elementType = GetElementType(underlying);
            return elementType is not null && IsScalar(elementType);
        }

        return false;
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying == typeof(string) || underlying == typeof(decimal) || underlying == typeof(DateTime)
            || underlying == typeof(DateOnly) || underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid) || underlying.IsPrimitive || underlying.IsEnum;
    }

    private static Type? GetElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.GetInterfaces()
            .Concat(new[] { type })
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string ResolveRootName(Type? type)
    {
        if (type is null)
        {
            return "Root";
        }

        if (type.IsArray || (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string)))
        {
            return "ArrayOf" + ResolveElementName(type);
        }

        return ResolveElementName(type);
    }

    private static string ResolveElementName(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType()?.Name ?? "Item";
        }

        var elementType = GetElementType(type);
        return elementType?.Name ?? type.Name;
    }
}
