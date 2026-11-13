using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho <see cref="LocalIntentDetector"/> — fast-path intent cục bộ.
///
/// <para>
/// <b>Bối cảnh:</b> Gemini free tier chỉ 20 request/ngày. Trước khi có fast-path,
/// mỗi tin nhắn chatbot tốn 2 request (intent + trả lời) ⇒ hết quota sau ~10 câu.
/// Fast-path nhận diện intent ngay trong code cho các câu có mẫu rõ ⇒ tiết kiệm
/// 50% quota và giảm độ trễ.
/// </para>
/// </summary>
public class LocalIntentDetectorTests
{
    [Fact]
    public void Detect_LoiChao_TraIntroduction()
    {
        var result = LocalIntentDetector.Detect("xin chào");

        Assert.NotNull(result);
        Assert.Equal("introduction", result!.Intent);
    }

    [Fact]
    public void Detect_CauHoiGioiThieu_TraIntroduction()
    {
        var result = LocalIntentDetector.Detect("bạn là ai");

        Assert.NotNull(result);
        Assert.Equal("introduction", result!.Intent);
    }

    [Fact]
    public void Detect_CauHoiGioiThieu_CoDauHoi_TraIntroduction()
    {
        // "Bạn là ai?" là câu gợi ý mặc định trong widget ⇒ phải nhận diện được.
        var result = LocalIntentDetector.Detect("Bạn là ai?");

        Assert.NotNull(result);
        Assert.Equal("introduction", result!.Intent);
    }

    [Fact]
    public void Detect_HoiSoLuongSanPham_TraProductCount()
    {
        var result = LocalIntentDetector.Detect("cửa hàng có bao nhiêu sản phẩm thức ăn cho chó");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
        Assert.Equal("thức ăn", result.Parameters["product_type"]);
        Assert.Equal("chó", result.Parameters["pet"]);
    }

    [Fact]
    public void Detect_HoiDichVu_TraServiceInquiry()
    {
        var result = LocalIntentDetector.Detect("shop có những dịch vụ gì");

        Assert.NotNull(result);
        Assert.Equal("service_inquiry", result!.Intent);
    }

    [Fact]
    public void Detect_HoiDonHang_TraOrderStatus()
    {
        var result = LocalIntentDetector.Detect("trạng thái đơn hàng của tôi thế nào");

        Assert.NotNull(result);
        Assert.Equal("order_status", result!.Intent);
    }

    [Fact]
    public void Detect_HoiThanhToan_TraPayment()
    {
        var result = LocalIntentDetector.Detect("thanh toán bằng payos được không");

        Assert.NotNull(result);
        Assert.Equal("payment", result!.Intent);
    }

    [Fact]
    public void Detect_HoiDatLich_TraBooking()
    {
        var result = LocalIntentDetector.Detect("tôi muốn đặt lịch chăm sóc");

        Assert.NotNull(result);
        Assert.Equal("booking", result!.Intent);
    }

    [Fact]
    public void Detect_CauHoiChungKhongMauRang_TraNull_DeGoiAI()
    {
        // Câu hỏi mở ⇒ không nhận diện chắc chắn ⇒ phải gọi AI.
        var result = LocalIntentDetector.Detect("cho tôi hỏi giá thức ăn cho chó là bao nhiêu");

        Assert.Null(result);
    }

    [Fact]
    public void Detect_LoiChaoKemCauHoi_KhongPhaiIntroduction()
    {
        // "xin chào, cho tôi hỏi giá..." — KHÔNG được coi là lời chào thuần.
        var result = LocalIntentDetector.Detect("xin chào, cho tôi hỏi thức ăn cho chó giá bao nhiêu?");

        Assert.Null(result);
    }

    [Fact]
    public void Detect_ChuoiRong_TraNull()
    {
        Assert.Null(LocalIntentDetector.Detect(string.Empty));
        Assert.Null(LocalIntentDetector.Detect(null));
    }

    [Fact]
    public void Detect_CauHoiNoiTiep_ChiMon_TraProductCount()
    {
        // Lỗi thật 11/11/2026: khách gõ "chỉ 1 môn?" (thiếu dấu sắc) ⇒ trước đây
        // không khớp "món" ⇒ rơi xuống Gemini ⇒ hết quota ⇒ trả nhầm danh sách
        // dịch vụ (câu trả lời "lạc đề") ⇒ người dùng tưởng chatbot hardcode.
        var result = LocalIntentDetector.Detect("chỉ 1 môn?");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
    }

    [Fact]
    public void Detect_CauHoiNoiTiep_MonKhongDau_TraProductCount()
    {
        var result = LocalIntentDetector.Detect("chỉ 1 món?");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
    }

    [Fact]
    public void Detect_HoiSoLuongKhongDau_TraProductCount()
    {
        // "có mấy môn dưới 50k" — khách gõ thiếu dấu.
        var result = LocalIntentDetector.Detect("có mấy môn dưới 50k");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
        Assert.Equal("50000", result.Parameters["max_price"]);
        Assert.Equal("false", result.Parameters["max_price_inclusive"]);
    }

    [Fact]
    public void Detect_SoTienKhongPhaiSoLuong_KhongPhaiProductCount()
    {
        // "1,5 triệu" là GIÁ, không phải số lượng ⇒ không được coi là product_count.
        var result = LocalIntentDetector.Detect("1,5 triệu có mấy món?");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
        Assert.Equal(1500000m, decimal.Parse(result.Parameters["max_price"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Detect_KhiNao_KhongPhaiLoiChao()
    {
        // "hi " trong "khi nào" ⇒ trước đây bị hiểu thành lời chào ⇒ trả nhầm
        // câu giới thiệu thay vì trả lời câu hỏi.
        var result = LocalIntentDetector.Detect("khi nào shop mở cửa");

        Assert.NotNull(result);
        Assert.NotEqual("introduction", result!.Intent);
        Assert.Equal("shop_contact", result.Intent);
    }

    [Fact]
    public void Detect_GioiTuChoToi_KhongPhaiLocTheoCho()
    {
        // LỖI THẬT 11/11/2026: "bạn cho tôi biết món nào dưới 20k" — sau khi bỏ
        // dấu, "cho" (giới từ) trùng với "chó" (loài thú) ⇒ bị lọc pet=chó ⇒
        // chỉ còn 1 sản phẩm thay vì 3.
        var result = LocalIntentDetector.Detect("bạn cho tôi biết món nào dưới 20k");

        Assert.NotNull(result);
        Assert.Equal("product_price_filter", result!.Intent);
        Assert.Equal("20000", result.Parameters["max_price"]);
        Assert.False(result.Parameters.ContainsKey("pet"));
        Assert.False(result.Parameters.ContainsKey("product_type"));
    }

    [Fact]
    public void Detect_ChoToi_Ban_Khach_DeuKhongPhaiCho()
    {
        // "cho" đứng trước đại từ/người ⇒ là giới từ, không phải "chó".
        Assert.False(TextNormalizer.MentionsDog(TextNormalizer.Fold("cho tôi")));
        Assert.False(TextNormalizer.MentionsDog(TextNormalizer.Fold("cho bạn")));
        Assert.False(TextNormalizer.MentionsDog(TextNormalizer.Fold("cho khách")));
    }

    [Fact]
    public void Detect_MentionsCho_LaThu_ThiRaCho()
    {
        Assert.True(TextNormalizer.MentionsDog(TextNormalizer.Fold("bóng tennis cho chó")));
        Assert.True(TextNormalizer.MentionsDog(TextNormalizer.Fold("cho chó nhé")));
    }

    [Fact]
    public void Detect_BanLaAi_TraIntroduction()
    {
        // "bạn là ai" phải ra câu tự giới thiệu DỰA TRÊN DB, không hardcode.
        var result = LocalIntentDetector.Detect("bạn là ai");

        Assert.NotNull(result);
        Assert.Equal("introduction", result!.Intent);
    }

    [Fact]
    public void Detect_CoMayMonDuoi50k_CoRangBuocGia()
    {
        var result = LocalIntentDetector.Detect("có mấy món dưới 50k");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
        Assert.Equal("50000", result.Parameters["max_price"]);
    }

    [Fact]
    public void Detect_ChoToiKhongPhaiPet_NhungCoCauHoiVeSanPham()
    {
        // "cho tôi biết có mấy món dưới 20k" — vẫn phải lọc theo giá, không lọc chó.
        var result = LocalIntentDetector.Detect("cho tôi biết có mấy món dưới 20k");

        Assert.NotNull(result);
        Assert.Equal("product_count", result!.Intent);
        Assert.Equal("20000", result.Parameters["max_price"]);
        Assert.False(result.Parameters.ContainsKey("pet"));
    }
}
