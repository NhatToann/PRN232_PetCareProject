using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho <see cref="ChatbotIntentCache"/> — cache intent của chatbot.
///
/// <para>
/// <b>Bối cảnh:</b> mỗi tin nhắn chatbot tốn <b>2 request</b> Gemini
/// (1 phân tích intent + 1 sinh câu trả lời). Gemini free tier chỉ cho
/// <b>20 request/ngày</b> ⇒ người dùng hỏi 10 câu là hết quota cả ngày.
/// Cache intent theo nội dung (đã chuẩn hoá) giúp câu hỏi lặp lại không tốn
/// thêm request.
/// </para>
/// </summary>
public class ChatbotIntentCacheTests
{
    [Fact]
    public void Get_KhongCoTrongCache_TraNull()
    {
        var cache = new ChatbotIntentCache();

        Assert.Null(cache.Get("chưa có gì"));
    }

    [Fact]
    public void Set_RoiGet_TraDungIntent()
    {
        var cache = new ChatbotIntentCache();
        var intent = new AiIntentResult { Intent = "product_search" };

        cache.Set("giá thức ăn", intent);

        var cached = cache.Get("giá thức ăn");
        Assert.NotNull(cached);
        Assert.Equal("product_search", cached!.Intent);
    }

    [Fact]
    public void Normalize_CauHoiTuongDong_ChungKey()
    {
        Assert.Equal(
            ChatbotIntentCache.Normalize("Giá thức ăn cho chó?"),
            ChatbotIntentCache.Normalize("  giá thức ăn cho chó  "));
    }

    [Fact]
    public void Get_CauHoiTuongDong_LayTrenCache()
    {
        var cache = new ChatbotIntentCache();
        cache.Set(
            ChatbotIntentCache.Normalize("giá thức ăn cho chó?"),
            new AiIntentResult { Intent = "price_inquiry" });

        var cached = cache.Get(ChatbotIntentCache.Normalize("GIÁ THỨC ĂN CHO CHÓ"));

        Assert.NotNull(cached);
        Assert.Equal("price_inquiry", cached!.Intent);
    }

    [Fact]
    public void Set_VuotGioiHan_PhaiXoaMucCuNhat_DeKhongPhinhRam()
    {
        var cache = new ChatbotIntentCache();

        // Nhồi hơn MaxEntries mục.
        for (var i = 0; i < ChatbotIntentCache.MaxEntries + 10; i++)
        {
            cache.Set($"cau hoi {i}", new AiIntentResult { Intent = "unknown" });
        }

        // Mục cũ nhất phải bị xóa; mục mới nhất vẫn còn.
        Assert.Null(cache.Get("cau hoi 0"));
        Assert.NotNull(cache.Get($"cau hoi {ChatbotIntentCache.MaxEntries + 9}"));
    }

    [Fact]
    public void Set_ChuoiRong_PhaiBoQua()
    {
        var cache = new ChatbotIntentCache();

        cache.Set(string.Empty, new AiIntentResult { Intent = "greeting" });

        Assert.Null(cache.Get(string.Empty));
    }
}
