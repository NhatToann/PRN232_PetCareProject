using System.Globalization;

namespace PetShop.Components.Shared;

/// <summary>
/// Helper định dạng dùng chung cho toàn bộ UI.
///
/// <para>
/// <b>VỀ VIỆT CHUẨN HOÁ TIỀN TỆ VND:</b> trước đây mỗi trang tự định dạng tiền
/// theo 4 kiểu khác nhau (<c>:C0</c>, <c>:N0₫</c>, <c>:N0 ₫</c>, <c>:N0 đ</c>) và
/// <c>ChatbotService</c> còn dùng <c>:N0</c> với culture **mặc định của server**
/// (tiếng Anh) ⇒ dấu phân cách nghìn kiểu Mỹ ("35,000") lẫn với kiểu Việt ("35.000").
/// </para>
///
/// <para>
/// Giờ <b>toàn bộ</b> giá tiền đều đi qua <see cref="Vnd"/> ⇒ luôn có dấu phân cách
/// nghìn kiểu Việt và ký hiệu <c>₫</c>, không bao giờ lẫn lộn.
/// </para>
/// </summary>
public static class Format
{
    /// <summary>Culture Việt Nam — dùng <c>,</c> làm dấu phân cách thập phân và <c>.</c> làm dấu phân cách nghìn.</summary>
    public static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>
    /// Định dạng số tiền theo chuẩn VND: <c>35.000 ₫</c> (làm tròn về đồng, không phần thập phân).
    /// </summary>
    public static string Vnd(decimal value) =>
        string.Create(Vi, $"{Math.Round(value, 0, MidpointRounding.AwayFromZero):N0} ₫");

    /// <summary>
    /// Định dạng số tiền kèm hậu tố (mặc định <c>₫</c>) — dùng khi cần chèn thêm
    /// chữ (vd <c>"/ngày"</c>) ngay sau ký hiệu tiền tệ.
    /// </summary>
    public static string Vnd(decimal value, string suffix) =>
        string.Create(Vi, $"{Math.Round(value, 0, MidpointRounding.AwayFromZero):N0} ₫{suffix}");

    /// <summary>
    /// Định dạng số tiền KHÔNG ký hiệu (chỉ phần số có dấu phân cách nghìn kiểu Việt) —
    /// dùng khi nơi hiển thị đã có sẵn ký hiệu <c>₫</c> bên cạnh.
    /// </summary>
    public static string VndNumber(decimal value) =>
        string.Create(Vi, $"{Math.Round(value, 0, MidpointRounding.AwayFromZero):N0}");

    public static string DateTimeVi(DateTime value) =>
        value.ToString("dd/MM/yyyy HH:mm", Vi);

    public static string DateOnlyVi(DateOnly value) =>
        value.ToString("dd/MM/yyyy", Vi);
}
