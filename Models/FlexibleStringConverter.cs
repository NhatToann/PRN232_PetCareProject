using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetShop.Models;

/// <summary>
/// Converter đọc được cả <b>số</b> lẫn <b>chuỗi</b> cho các field dạng string
/// của webhook PayOS (<c>code</c>, <c>desc</c>, <c>status</c>).
///
/// <para>
/// <b>VÌ SAO CẦN (lỗi thật đã tái hiện):</b> tài liệu PayOS dùng
/// <c>"code": "00"</c> (chuỗi), nhưng biến thể sandbox/legacy gửi
/// <c>"code": 0</c> (số). Với DTO khai báo <c>string? Code</c>, payload có số
/// ⇒ <c>JsonException</c> ⇒ <c>HandleWebhookAsync</c> trả "Invalid payload"
/// ⇒ thanh toán thành công KHÔNG được ghi vào DB (mất đồng bộ trạng thái).
/// </para>
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.Number:
                // Giữ nguyên dạng số gốc (vd "00" không mất, "0" giữ "0").
                return Encoding.UTF8.GetString(reader.ValueSpan);

            case JsonTokenType.True:
                return "true";

            case JsonTokenType.False:
                return "false";

            case JsonTokenType.Null:
                return null;

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
