using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho <see cref="PriceConstraintParser"/> và intent lọc theo giá.
///
/// <para>
/// <b>Bối cảnh (lỗi thật 11/11/2026):</b> khách hỏi "món nào dưới 20k" /
/// "có mấy món dưới 50k" nhưng chatbot KHÔNG lọc theo giá ⇒ đếm ra 42 sản phẩm
/// (kể cả sản phẩm đã xoá mềm) và trả về câu template cố định
/// "🐾 Hiện cửa hàng đang có 42 sản phẩm phù hợp với nhu cầu của bạn."
/// ⇒ người dùng tưởng chatbot bị hardcode / bịa số liệu.
/// </para>
/// </summary>
public class PriceConstraintParserTests
{
    [Fact]
    public void Parse_Duoi20k_TraTranGia20k()
    {
        var result = PriceConstraintParser.Parse("bạn cho tôi biết món nào dưới 20k");

        Assert.True(result.HasValue);
        Assert.Null(result.Min);
        Assert.Equal(20_000m, result.Max);
        Assert.False(result.MaxInclusive);
    }

    [Fact]
    public void Parse_Duoi20Nghin_TraTranGia20k()
    {
        var result = PriceConstraintParser.Parse("có món nào dưới 20 nghìn không");

        Assert.Equal(20_000m, result.Max);
    }

    [Fact]
    public void Parse_Duoi20000_TraTranGia20k()
    {
        // "20.000" dấu chấm là phân cách nghìn kiểu VN.
        var result = PriceConstraintParser.Parse("sản phẩm dưới 20.000 đồng");

        Assert.Equal(20_000m, result.Max);
    }

    [Fact]
    public void Parse_Tu20kDen50k_TraKhoangGia()
    {
        var result = PriceConstraintParser.Parse("có sản phẩm nào từ 20k đến 50k không");

        Assert.Equal(20_000m, result.Min);
        Assert.Equal(50_000m, result.Max);
        Assert.True(result.MaxInclusive);
    }

    [Fact]
    public void Parse_Tren100k_TraSanGia()
    {
        var result = PriceConstraintParser.Parse("sản phẩm trên 100k");

        Assert.Equal(100_000m, result.Min);
        Assert.Null(result.Max);
    }

    [Fact]
    public void Parse_Tu100kTroLen_TraSanGia()
    {
        var result = PriceConstraintParser.Parse("có món nào từ 100k trở lên không");

        Assert.Equal(100_000m, result.Min);
        Assert.Null(result.Max);
    }

    [Fact]
    public void Parse_ToiDa30k_TraTranInclusive()
    {
        var result = PriceConstraintParser.Parse("giá tối đa 30k");

        Assert.Equal(30_000m, result.Max);
        Assert.True(result.MaxInclusive);
    }

    [Fact]
    public void Parse_1ComTrieu_TraGia1Trieu()
    {
        var result = PriceConstraintParser.Parse("dưới 1,5 triệu");

        Assert.Equal(1_500_000m, result.Max);
    }

    [Fact]
    public void Parse_KhongNhacGia_TraNone()
    {
        Assert.False(PriceConstraintParser.Parse("bạn là ai").HasValue);
        Assert.False(PriceConstraintParser.Parse("xin chào").HasValue);
        Assert.False(PriceConstraintParser.Parse(null).HasValue);
        Assert.False(PriceConstraintParser.Parse("   ").HasValue);
    }

    [Fact]
    public void Parse_SoLuongKhongPhaiTien_TraNone()
    {
        // "1 món" / "2 con" là số lượng, KHÔNG phải tiền.
        Assert.False(PriceConstraintParser.Parse("chỉ 1 món thôi").HasValue);
        Assert.False(PriceConstraintParser.Parse("tôi mua 2 con chó").HasValue);
    }

    [Fact]
    public void Describe_TraNhanMoTaTiengViet()
    {
        var duoi20k = PriceConstraintParser.Parse("dưới 20k");
        Assert.Contains("dưới", duoi20k.Describe());
        Assert.Contains("20.000", duoi20k.Describe());

        var tu20kDen50k = PriceConstraintParser.Parse("từ 20k đến 50k");
        Assert.Contains("từ", tu20kDen50k.Describe());
        Assert.Contains("50.000", tu20kDen50k.Describe());
    }

    // =======================================================================
    // LocalIntentDetector — fast-path lọc theo giá.
    // =======================================================================

    [Fact]
    public void Detect_MonNaoDuoi20k_TraProductPriceFilter()
    {
        var result = LocalIntentDetector.Detect("bạn cho tôi biết món nào dưới 20k");

        Assert.NotNull(result);
        Assert.Equal("product_price_filter", result!.Intent);
        Assert.Equal("20000", result.Parameters["max_price"]);
        Assert.Equal("false", result.Parameters["max_price_inclusive"]);
    }

    [Fact]
    public void Detect_CoMayMonDuoi50k_TraProductCountKemGia()
    {
        // "có mấy" là từ khoá đếm số lượng ⇒ intent product_count, nhưng PHẢI kèm
        // ràng buộc giá để không đếm ra tổng số sản phẩm của shop.
        var result = LocalIntentDetector.Detect("có mấy món dưới 50k");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
        Assert.Equal("50000", result.Parameters["max_price"]);
    }

    [Fact]
    public void Detect_HoiSoLuong_TraProductCountKhongCoGia()
    {
        var result = LocalIntentDetector.Detect("cửa hàng có bao nhiêu sản phẩm");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
        Assert.False(result.Parameters.ContainsKey("max_price"));
    }

    [Fact]
    public void Detect_HoiGiaKhongPhaiLoc_TraNull()
    {
        // "cho tôi hỏi giá thức ăn cho chó là bao nhiêu" — không có ràng buộc giá
        // cụ thể ⇒ vẫn phải gọi AI như trước (giữ nguyên hành vi cũ).
        var result = LocalIntentDetector.Detect("cho tôi hỏi giá thức ăn cho chó là bao nhiêu");

        Assert.Null(result);
    }
}
