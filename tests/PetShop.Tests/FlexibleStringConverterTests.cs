using System.Text.Json;
using System.Text.Json.Serialization;
using PetShop.Models;

namespace PetShop.Tests;

/// <summary>
/// Test cho <see cref="FlexibleStringConverter"/> và việc deserialize envelope webhook PayOS.
///
/// <para>
/// <b>Bối cảnh (lỗi thật đã tái hiện):</b> webhook PayOS có thể gửi <c>code</c> dạng
/// <b>chuỗi</c> (<c>"00"</c> — chuẩn tài liệu) hoặc dạng <b>số</c> (<c>0</c> — biến thể
/// sandbox/legacy). DTO khai báo <c>string? Code</c> ⇒ payload dạng số làm
/// <c>JsonException</c> ⇒ webhook bị từ chối ⇒ đơn hàng không bao giờ được đánh dấu
/// "đã thanh toán" trong DB.
/// </para>
/// </summary>
public class FlexibleStringConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new FlexibleStringConverter() }
    };

    // -----------------------------------------------------------------
    // Converter trực tiếp
    // -----------------------------------------------------------------

    [Fact]
    public void Read_Chuoi_TrảNguyênGiaTri()
    {
        var value = JsonSerializer.Deserialize<string?>("\"00\"", Options);

        Assert.Equal("00", value);
    }

    [Fact]
    public void Read_So_TrảChuoiDangText()
    {
        var value = JsonSerializer.Deserialize<string?>("0", Options);

        Assert.Equal("0", value);
    }

    [Fact]
    public void Read_SoThuc_TrảNguyênRawText()
    {
        var value = JsonSerializer.Deserialize<string?>("12.50", Options);

        Assert.Equal("12.50", value);
    }

    [Fact]
    public void Read_Null_TrảNull()
    {
        var value = JsonSerializer.Deserialize<string?>("null", Options);

        Assert.Null(value);
    }

    [Fact]
    public void Read_Boolean_TrảChuoi()
    {
        Assert.Equal("true", JsonSerializer.Deserialize<string?>("true", Options));
        Assert.Equal("false", JsonSerializer.Deserialize<string?>("false", Options));
    }

    [Fact]
    public void Write_Null_GhiNull()
    {
        Assert.Equal("null", JsonSerializer.Serialize<string?>(null, Options));
    }

    [Fact]
    public void Write_KhongNull_GhiChuoi()
    {
        Assert.Equal("\"00\"", JsonSerializer.Serialize<string?>("00", Options));
    }

    // -----------------------------------------------------------------
    // Envelope webhook PayOS
    // -----------------------------------------------------------------

    /// <summary>Envelope chuẩn tài liệu: code là chuỗi "00".</summary>
    private const string EnvelopeStringCode =
        "{\"code\":\"00\",\"desc\":\"success\",\"success\":true," +
        "\"data\":{\"orderCode\":123456789,\"amount\":3000,\"code\":\"00\",\"desc\":\"Thành công\"}}";

    /// <summary>Biến thể sandbox/legacy: code là SỐ 0.</summary>
    private const string EnvelopeNumericCode =
        "{\"code\":0,\"desc\":\"success\",\"success\":true," +
        "\"data\":{\"orderCode\":123456789,\"amount\":3000,\"code\":0,\"desc\":\"Thành công\"}}";

    [Fact]
    public void Deserialize_EnvelopeCodeChuoi_ThanhCong()
    {
        var payload = JsonSerializer.Deserialize<PayOSWebhookPayload>(EnvelopeStringCode, Options);

        Assert.NotNull(payload);
        Assert.Equal("00", payload!.Code);
        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal(123456789, payload.Data!.OrderCode);
        Assert.Equal(3000, payload.Data.Amount);
        Assert.Equal("00", payload.Data.Code);
    }

    [Fact]
    public void Deserialize_EnvelopeCodeSo_KhongThrow()
    {
        // REGRESSION: trước khi có converter ⇒ JsonException ⇒ "Invalid payload"
        // ⇒ thanh toán thành công KHÔNG được ghi vào DB.
        var payload = JsonSerializer.Deserialize<PayOSWebhookPayload>(EnvelopeNumericCode, Options);

        Assert.NotNull(payload);
        Assert.Equal("0", payload!.Code);
        Assert.NotNull(payload.Data);
        Assert.Equal("0", payload.Data!.Code);
        Assert.Equal(123456789, payload.Data.OrderCode);
    }

    [Fact]
    public void Deserialize_EnvelopeDataStatusSo_KhongThrow()
    {
        const string body =
            "{\"code\":\"00\",\"success\":true,\"data\":{\"orderCode\":42,\"status\":0}}";

        var payload = JsonSerializer.Deserialize<PayOSWebhookPayload>(body, Options);

        Assert.NotNull(payload?.Data);
        Assert.Equal("0", payload!.Data!.Status);
    }
}
