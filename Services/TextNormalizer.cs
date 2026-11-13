using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PetShop.Services;

/// <summary>
/// Chuẩn hoá văn bản tiếng Việt để so khớp từ khoá bất chấp khác biệt dấu.
///
/// <para>
/// <b>VÌ SAO CẦN (lỗi chatbot 11/11/2026):</b> khách gõ "chỉ 1 môn?" (thiếu
/// dấu sắc) trong khi code chỉ nhận diện "món" ⇒ câu hỏi rơi xuống Gemini ⇒ khi
/// hết quota, chatbot trả nhầm danh sách dịch vụ ⇒ người dùng thấy câu trả lời
/// "lạc đề", tưởng chatbot bị hardcode.
/// </para>
///
/// <para>
/// Hàm <see cref="Fold"/> bỏ toàn bộ dấu thanh/dấu phụ và chuyển chữ thường, giữ
/// nguyên chữ cái (kể cả <c>đ</c> ⇒ <c>d</c>) để so khớp từ khoá.
/// </para>
/// </summary>
public static class TextNormalizer
{
    /// <summary>Regex so khớp TỪ (theo ranh giới từ) trên chuỗi đã bỏ dấu.</summary>
    private static readonly Regex WordPattern = new(@"\b\w+\b", RegexOptions.Compiled);

    /// <summary>
    /// Bỏ dấu tiếng Việt + chuyển chữ thường.
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        // "đ"/"Đ" không có dạng phân rã ⇒ thay thủ công.
        return sb.ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .ToLowerInvariant();
    }

    /// <summary>
    /// Kiểm tra chuỗi (đã bỏ dấu) có chứa TỪ theo ranh giới từ hay không.
    /// </summary>
    public static bool ContainsWord(string foldedText, string foldedWord)
    {
        if (string.IsNullOrEmpty(foldedText) || string.IsNullOrEmpty(foldedWord))
        {
            return false;
        }

        foreach (Match m in WordPattern.Matches(foldedText))
        {
            if (string.Equals(m.Value, foldedWord, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Kiểm tra chuỗi (đã bỏ dấu) có chứa CỤM TỪ theo ranh giới từ hay không.
    ///
    /// <para>
    /// Cần cho các từ khoá đa từ ("sản phẩm", "thức ăn") và để tránh khớp
    /// nhầm với từ có chứa chuỗi đó (vd "đồ" ⇔ "do" trong "dờ", "doanh").
    /// </para>
    /// </summary>
    public static bool ContainsPhrase(string foldedText, string foldedPhrase)
    {
        if (string.IsNullOrEmpty(foldedText) || string.IsNullOrEmpty(foldedPhrase))
        {
            return false;
        }

        var index = foldedText.IndexOf(foldedPhrase, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(foldedText[index - 1]);
            var endIndex = index + foldedPhrase.Length;
            var afterOk = endIndex >= foldedText.Length || !char.IsLetterOrDigit(foldedText[endIndex]);

            if (beforeOk && afterOk)
            {
                return true;
            }

            index = foldedText.IndexOf(foldedPhrase, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Câu hỏi có nhắc tới LOÀI CHÓ không?
    ///
    /// <para>
    /// <b>VÌ SAO CẦN KIỂM TRA RIÊNG:</b> khi bỏ dấu, "chó" ⇒ "cho" — trùng với
    /// giới từ "cho" ("cho tôi biết…") ⇒ chatbot lọc nhầm sản phẩm cho chó.
    /// Vì vậy "cho" chỉ được coi là loài thú khi KHÔNG đi kèm đại từ
    /// ("cho tôi", "cho bạn", "cho mình", "cho khách").
    /// </para>
    /// </summary>
    public static bool MentionsDog(string foldedText)
    {
        if (ContainsWord(foldedText, "cun"))
        {
            return true;
        }

        foreach (Match m in WordPattern.Matches(foldedText))
        {
            if (!string.Equals(m.Value, "cho", StringComparison.Ordinal))
            {
                continue;
            }

            // Xem từ kế tiếp có phải đại từ/giới từ không.
            var rest = foldedText[(m.Index + m.Length)..];
            var nextWord = WordPattern.Match(rest).Value;

            if (nextWord is "toi" or "ban" or "minh" or "khach" or "nguoi" or "anh" or "chi")
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>Câu hỏi có nhắc tới LOÀI MÈO không ("mèo"/"meo")?</summary>
    public static bool MentionsCat(string foldedText) => ContainsWord(foldedText, "meo");
}
