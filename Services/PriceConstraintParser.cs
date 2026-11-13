using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PetShop.Services;

/// <summary>
/// Ràng buộc giá trích xuất được từ câu hỏi tiếng Việt của khách.
///
/// <para>
/// <b>VÌ SAO CẦN (lỗi thật 11/11/2026):</b> khách hỏi "món nào dưới 20k" /
/// "có mấy món dưới 50k" nhưng chatbot KHÔNG hề lọc theo giá ⇒ đếm ra tổng số
/// sản phẩm trong shop (42) và trả về câu template cố định
/// "🐾 Hiện cửa hàng đang có 42 sản phẩm phù hợp với nhu cầu của bạn."
/// ⇒ người dùng tưởng chatbot bị hardcode / bịa số liệu.
/// </para>
///
/// <para>
/// Nay ràng buộc giá được parse <b>cục bộ</b> (không tốn request Gemini) và áp
/// dụng trực tiếp vào truy vấn SQL ⇒ câu trả lời luôn khớp dữ liệu thật.
/// </para>
/// </summary>
/// <param name="Min">Giá thấp nhất (inclusive). <c>null</c> = không giới hạn dưới.</param>
/// <param name="Max">Giá cao nhất. <c>null</c> = không giới hạn trên.</param>
/// <param name="MaxInclusive">
/// <c>true</c> = "tối đa X" / "từ A đến B" (bao gồm cả X);
/// <c>false</c> = "dưới X" (nghiêm ngặt &lt; X).
/// </param>
public sealed record PriceConstraint(decimal? Min, decimal? Max, bool MaxInclusive)
{
    /// <summary>Không có ràng buộc giá nào.</summary>
    public static PriceConstraint None { get; } = new(null, null, true);

    /// <summary>Có ràng buộc giá hay không.</summary>
    public bool HasValue => Min.HasValue || Max.HasValue;

    /// <summary>Mô tả bằng tiếng Việt để đưa vào câu trả lời / prompt.</summary>
    public string Describe()
    {
        if (Min.HasValue && Max.HasValue)
        {
            return $"từ {Vnd(Min.Value)} đến {Vnd(Max.Value)}";
        }

        if (Max.HasValue)
        {
            return MaxInclusive ? $"tối đa {Vnd(Max.Value)}" : $"dưới {Vnd(Max.Value)}";
        }

        if (Min.HasValue)
        {
            return $"từ {Vnd(Min.Value)} trở lên";
        }

        return string.Empty;
    }

    private static string Vnd(decimal value) => PetShop.Components.Shared.Format.Vnd(value);
}

/// <summary>
/// Parse ràng buộc giá từ câu hỏi tiếng Việt (không cần AI).
///
/// <para>Các dạng được hỗ trợ:</para>
/// <list type="bullet">
///   <item>"dưới 20k", "dưới 20.000", "dưới 20 nghìn" ⇒ Max = 20.000 (exclusive)</item>
///   <item>"tối đa 30k", "không quá 30k" ⇒ Max = 30.000 (inclusive)</item>
///   <item>"trên 100k", "hơn 100k", "từ 100k trở lên" ⇒ Min = 100.000</item>
///   <item>"từ 20k đến 50k", "20k - 50k" ⇒ Min = 20.000, Max = 50.000 (inclusive)</item>
///   <item>"1,5 triệu" ⇒ 1.500.000 · "500k" ⇒ 500.000 · "2tr" ⇒ 2.000.000</item>
/// </list>
/// </summary>
public static class PriceConstraintParser
{
    /// <summary>
    /// Regex bắt số tiền + đơn vị. Đơn vị: k/ng/nghìn/ngàn (nghìn), tr/triệu (triệu),
    /// tỷ/ty (tỷ). "20.000" (dấu chấm là dấu phân cách nghìn kiểu VN) ⇒ 20.000.
    /// </summary>
    private static readonly Regex AmountPattern = new(
        @"(?<num>\d{1,3}(?:[.,]\d{3})+(?:\.\d+)?|\d+(?:[.,]\d+)?)\s*(?<unit>nghìn|ngàn|triệu|ng|tr|ty|tỷ|k)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Các từ khoá ⇒ trần giá (Max) + cách so sánh.</summary>
    private static readonly (string Word, bool Inclusive)[] MaxWords =
    {
        ("duoi", false),       // dưới
        ("nho hon", false),    // nhỏ hơn
        ("it hon", false),     // ít hơn
        ("be hon", false),     // bé hơn
        ("toi da", true),      // tối đa
        ("khong qua", true),   // không quá
        ("gia toi da", true),  // giá tối đa
    };

    /// <summary>Các từ khoá ⇒ sàn giá (Min).</summary>
    private static readonly string[] MinWords =
    {
        "tren",       // trên
        "lon hon",    // lớn hơn
        "toi thieu",  // tối thiểu
        "it nhat",    // ít nhất
        "gia tu",     // giá từ
    };

    /// <summary>Các từ nối ⇒ khoảng giá (Min..Max) — so khớp theo ranh giới từ.</summary>
    private static readonly string[] RangeWords = { "den", "toi", "va" };

    /// <summary>
    /// Parse ràng buộc giá từ câu hỏi.
    /// </summary>
    /// <param name="message">Câu hỏi của khách (có thể null).</param>
    /// <returns>Ràng buộc giá; <see cref="PriceConstraint.None"/> nếu câu không nhắc giá.</returns>
    public static PriceConstraint Parse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return PriceConstraint.None;
        }

        var text = message.ToLowerInvariant();
        var flat = StripDiacriticsKeepLength(text);

        var matches = AmountPattern.Matches(text);
        if (matches.Count == 0)
        {
            return PriceConstraint.None;
        }

        // Lấy các món tiền tìm được (kèm vị trí) — bỏ qua số không phải tiền
        // (vd "1 món", "2 con") bằng cách yêu cầu có đơn vị HOẶC số >= 1000.
        var amounts = new List<(decimal Value, int Start, int End, bool HasUnit)>();
        foreach (Match m in matches)
        {
            var value = ParseAmount(m);
            if (value is null)
            {
                continue;
            }

            amounts.Add((value.Value, m.Index, m.Index + m.Length, m.Groups["unit"].Success));
        }

        if (amounts.Count == 0)
        {
            return PriceConstraint.None;
        }

        decimal? min = null;
        decimal? max = null;
        var maxInclusive = true;

        for (var i = 0; i < amounts.Count; i++)
        {
            var (value, start, end, hasUnit) = amounts[i];

            // 1. Khoảng giá: "từ A đến B" / "A - B" ⇒ A là Min, B là Max.
            if (i + 1 < amounts.Count)
            {
                var between = flat[end..amounts[i + 1].Start];
                if (RangeWords.Any(w => ContainsWord(between, w)) || between.Contains('-'))
                {
                    min ??= value;
                    max ??= amounts[i + 1].Value;
                    maxInclusive = true;
                    continue;
                }
            }

            // 2. Ngữ cảnh trước số ⇒ trần / sàn giá.
            var before = flat[Math.Max(0, start - 30)..start];
            var after = flat[end..Math.Min(flat.Length, end + 20)];

            var isMax = false;
            var inclusive = true;

            foreach (var (word, inc) in MaxWords)
            {
                if (ContainsWord(before, word))
                {
                    isMax = true;
                    inclusive = inc;
                    break;
                }
            }

            if (isMax)
            {
                max ??= value;
                maxInclusive = inclusive;
                continue;
            }

            if (MinWords.Any(w => ContainsWord(before, w)))
            {
                min ??= value;
                continue;
            }

            // 3. "từ 100k trở lên" — từ khoá sàn nằm SAU số.
            if (ContainsWord(after, "tro len") || ContainsWord(after, "tro len"))
            {
                min ??= value;
                continue;
            }

            // 4. "khoảng X" / "tầm X" ⇒ coi như trần (inclusive).
            if (ContainsWord(before, "khoang") || ContainsWord(before, "tam"))
            {
                max ??= value;
                maxInclusive = true;
                continue;
            }

            // 5. Số trần trị có đơn vị mà không có từ khoá định hướng
            //    (vd "món nào 20k") ⇒ coi như trần giá.
            if (hasUnit && min is null && max is null)
            {
                max ??= value;
                maxInclusive = true;
            }
        }

        if (min is null && max is null)
        {
            return PriceConstraint.None;
        }

        return new PriceConstraint(min, max, maxInclusive);
    }

    /// <summary>
    /// Kiểm tra chuỗi có chứa TỪ (theo ranh giới từ) hay không — tránh "va" khớp
    /// nhầm bên trong "van" hay "toi" khớp nhầm trong "toida".
    /// </summary>
    private static bool ContainsWord(string haystack, string word)
    {
        if (string.IsNullOrEmpty(haystack))
        {
            return false;
        }

        var index = haystack.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetter(haystack[index - 1]);
            var endIndex = index + word.Length;
            var afterOk = endIndex >= haystack.Length || !char.IsLetter(haystack[endIndex]);

            if (beforeOk && afterOk)
            {
                return true;
            }

            index = haystack.IndexOf(word, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Đọc số tiền từ match: "20.000" ⇒ 20000 · "1,5" ⇒ 1.5 · "500k" ⇒ 500000.
    /// </summary>
    private static decimal? ParseAmount(Match m)
    {
        var raw = m.Groups["num"].Value;

        decimal number;
        if (raw.Contains(','))
        {
            // Dấu phẩy:
            //   - 3 chữ số sau dấu phẩy ⇒ dấu phân cách NGHÌN kiểu VN ("1,500" = 1500)
            //   - 1-2 chữ số sau dấu phẩy ⇒ dấu THẬP PHÂN kiểu VN ("1,5" = 1.5)
            var commaIndex = raw.IndexOf(',');
            var decimalsAfterComma = raw.Length - commaIndex - 1;

            if (decimalsAfterComma == 3)
            {
                number = decimal.Parse(raw.Replace(",", string.Empty), CultureInfo.InvariantCulture);
            }
            else
            {
                // Chuẩn hoá sang dấu thập phân '.' để parse theo invariant culture.
                if (!decimal.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
                {
                    return null;
                }
            }
        }
        else if (raw.Contains('.'))
        {
            var lastDot = raw.LastIndexOf('.');
            var decimals = raw.Length - lastDot - 1;

            if (decimals == 3)
            {
                // Dấu chấm = phân cách nghìn ⇒ 20.000 = 20000.
                number = decimal.Parse(raw.Replace(".", string.Empty), CultureInfo.InvariantCulture);
            }
            else if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return null;
            }
        }
        else if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
        {
            return null;
        }

        var unit = m.Groups["unit"].Success ? m.Groups["unit"].Value : string.Empty;
        var multiplier = unit switch
        {
            "k" => 1_000m,
            "ng" => 1_000m,
            "nghìn" or "ngàn" => 1_000m,
            "tr" => 1_000_000m,
            "triệu" => 1_000_000m,
            "ty" or "tỷ" => 1_000_000_000m,
            _ => 1m,
        };

        // Số không có đơn vị và nhỏ hơn 1000 (vd "1", "2") ⇒ nhiều khả năng là
        // số lượng chứ không phải tiền ⇒ bỏ qua.
        if (!m.Groups["unit"].Success && number < 1000)
        {
            return null;
        }

        return number * multiplier;
    }

    /// <summary>
    /// Bỏ dấu tiếng Việt nhưng GIỮ NGUYÊN độ dài chuỗi (mỗi ký tự 1:1) để vị trí
    /// (index) trong chuỗi đã bỏ dấu vẫn khớp với chuỗi gốc.
    /// </summary>
    private static string StripDiacriticsKeepLength(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(text.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        // "đ"/"Đ" không có dạng phân rã ⇒ thay thủ công (giữ độ dài).
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D');
    }
}
