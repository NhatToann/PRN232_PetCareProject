namespace PetShop.Services;

/// <summary>
/// Phát hiện intent CỤC BỘ (không gọi AI) cho các câu hỏi có mẫu rõ ràng.
///
/// <para>
/// <b>VÌ SAO CẦN (tối ưu quota + độ trễ):</b> Gemini free tier chỉ cho
/// <b>20 request/ngày</b>. Mỗi tin nhắn chatbot trước đây tốn <b>2 request</b>:
/// 1 phân tích intent + 1 sinh câu trả lời ⇒ hết quota sau ~10 câu.
/// Với câu hỏi có mẫu rõ (chào hỏi, hỏi số lượng sản phẩm, hỏi dịch vụ,
/// hỏi trạng thái đơn, hỏi thanh toán) ta có thể nhận diện intent ngay trong
/// code ⇒ <b>bỏ qua 1 request AI</b>.
/// </para>
///
/// <para>
/// <b>Nguyên tắc an toàn:</b> chỉ trả về intent khi mẫu khớp <b>chắc chắn</b>;
/// còn lại trả <c>null</c> ⇒ gọi AI như trước. Không đoán bùa.
/// </para>
///
/// <para>
/// <b>So khớp không phân biệt dấu (sửa lỗi 11/11/2026):</b> khách gõ
/// "chỉ 1 môn?" (thiếu dấu sắc) — trước đây không khớp "món" ⇒ rơi xuống
/// Gemini ⇒ hết quota ⇒ chatbot trả nhầm danh sách dịch vụ ⇒ người dùng
/// tưởng chatbot bị hardcode. Nay mọi từ khoá được so khớp trên chuỗi đã
/// <see cref="TextNormalizer.Fold"/> (bỏ dấu) nên "môn"/"món", "san pham"/
/// "sản phẩm" đều khớp.
/// </para>
///
/// <para>
/// <b>RAM:</b> không lưu trạng thái, chỉ là hàm thuần túy ⇒ không tốn bộ nhớ.
/// </para>
/// </summary>
public static class LocalIntentDetector
{
    /// <summary>Các từ khoá dịch vụ (spa, grooming, khám bệnh, tiêm vaccine...).</summary>
    private static readonly string[] ServiceKeywords =
    {
        "dịch vụ", "spa", "grooming", "khám bệnh", "tiêm", "vaccine", "tẩy giun",
        "làm đẹp", "cắt tỉa", "spa mỹ", "chăm sóc da", "nhổ móng", "tiêm phòng"
    };

    /// <summary>Các từ khoá về đơn hàng / trạng thái.</summary>
    private static readonly string[] OrderKeywords =
    {
        "đơn hàng", "đơn của tôi", "trạng thái đơn", "vận chuyển", "ship",
        "giao hàng", "theo dõi đơn", "đơn hàng của"
    };

    /// <summary>Các từ khoá về thanh toán.</summary>
    private static readonly string[] PaymentKeywords =
    {
        "thanh toán", "payment", "payos", "cod", "cổng thanh toán", "trả tiền",
        "hóa đơn", "hoa don", "phương thức thanh toán"
    };

    /// <summary>Các từ khoá về đặt lịch.</summary>
    private static readonly string[] BookingKeywords =
    {
        "đặt lịch", "dat lich", "booking", "hẹn lịch", "lịch hẹn", "đặt hẹn",
        "lịch khám", "giờ mở cửa", "giờ làm việc"
    };

    /// <summary>Các từ khoá về số lượng sản phẩm.</summary>
    private static readonly string[] CountKeywords =
    {
        "có bao nhiêu", "bao nhiêu sản phẩm", "số lượng", "còn bao nhiêu", "how many",
        "có mấy", "mấy món", "mấy sản phẩm", "mấy môn"
    };

    /// <summary>
    /// Các từ khoá cho thấy khách đang hỏi về <b>sản phẩm</b> (dùng cùng với
    /// ràng buộc giá để lọc danh sách sản phẩm).
    ///
    /// <para>
    /// So khớp theo ranh giới TỪ (không phân biệt dấu) ⇒ "môn"/"món", "đồ"/
    /// "đồ chơi"… đều nhận diện được, nhưng "do" trong "dờ" hay "doanh" không
    /// bị khớp nhầm.
    /// </para>
    /// </summary>
    private static readonly string[] ProductQueryKeywords =
    {
        "món", "sản phẩm", "hàng", "loại", "mẫu", "thức ăn", "đồ", "phụ kiện", "thuốc"
    };

    /// <summary>
    /// Các từ khoá hỏi về chi tiết sản phẩm (giá / tồn kho / có sẵn không).
    ///
    /// <para>
    /// Cặp với <see cref="ProductQueryKeywords"/> để nhận diện câu hỏi có thể trả
    /// lời thẳng từ DB ⇒ tiết kiệm quota Gemini free tier (chỉ 20 request/ngày).
    /// </para>
    /// </summary>
    internal static readonly string[] ProductDetailKeywords =
    {
        "giá", "bao nhiêu", "còn", "tồn", "có sẵn", "có món", "có sản phẩm",
        "tư vấn", "gợi ý", "nên mua", "mua", "bán"
    };

    /// <summary>Các từ khoá về giá.</summary>
    private static readonly string[] PriceKeywords =
    {
        "giá", "bao nhiêu tiền", "cost", "price", "rẻ nhất", "đắt nhất"
    };

    /// <summary>
    /// Các từ khoá về thông tin liên hệ của shop.
    ///
    /// <para>
    /// Fast-path này giúp câu hỏi "địa chỉ shop ở đâu / hotline là số nào" được
    /// trả lời NGAY từ <c>SystemSettings</c> mà KHÔNG tốn request Gemini —
    /// quan trọng khi Gemini free tier đã hết quota (20 req/ngày).
    /// </para>
    /// </summary>
    private static readonly string[] ContactKeywords =
    {
        "địa chỉ", "dia chi", "hotline", "số điện thoại", "so dien thoai", "liên hệ",
        "lien he", "email", "thời gian mở cửa", "giờ mở cửa", "mở cửa", "địa điểm",
        "ở đâu", "o dau", "cửa hàng ở", "cua hang o", "trụ sở", "dia diem"
    };

    /// <summary>Các cụm tự giới thiệu — KHÔNG phụ thuộc dấu "?" (chắc chắn là intent introduction).</summary>
    private static readonly string[] SelfIntroKeywords =
    {
        "bạn là ai", "là ai", "giới thiệu", "tên gì", "giúp việc gì", "bạn là bot"
    };

    /// <summary>Các lời chào đơn giản.</summary>
    private static readonly string[] GreetingKeywords =
    {
        "xin chào", "chào hỏi", "hello", "chào bạn", "hey", "good morning"
    };

    /// <summary>
    /// Mẫu câu hỏi đếm số lượng theo dạng "chỉ 1 món?", "còn 2 sản phẩm nữa".
    ///
    /// <para>
    /// Đây là câu hỏi NỐI TIẾP: không chứa ràng buộc giá ⇒ phải lấy từ lịch sử
    /// hội thoại (do <c>ChatbotService</c> gộp thêm) nếu không chatbot sẽ đếm
    /// toàn bộ sản phẩm trong shop ⇒ câu trả lời trông như template cố định.
    /// </para>
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex CountWithNumberPattern = new(
        @"(?<lead>\bchi\b[^0-9a-z]{0,12})?(?<num>\d+)\s*(?<noun>mon|san pham|hang|loai|mau)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Nhận diện intent cục bộ.
    /// </summary>
    /// <param name="message">Câu hỏi của người dùng.</param>
    /// <returns>
    /// <c>AiIntentResult</c> nếu nhận diện chắc chắn; <c>null</c> nếu không chắc
    /// ⇒ caller nên gọi AI.
    /// </returns>
    public static Models.AiIntentResult? Detect(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var text = message.Trim().ToLowerInvariant();

        // 0. Ràng buộc giá (parse cục bộ, KHÔNG tốn request Gemini).
        //
        //    "món nào dưới 20k" / "có mấy món dưới 50k" / "từ 20k đến 50k" …
        //    ⇒ phải LỌC theo giá, nếu không chatbot đếm ra tổng số sản phẩm
        //    trong shop và trả về câu template cố định ⇒ người dùng tưởng bị hardcode.
        // Parse giá trên bản GỐC (chưa bỏ dấu) vì đơn vị "nghìn/triệu" có dấu.
        var price = PriceConstraintParser.Parse(text);

        // 1. Tự giới thiệu — luôn nhận diện (kể cả khi có dấu "?").
        if (ContainsAny(text, SelfIntroKeywords))
        {
            return new Models.AiIntentResult { Intent = "introduction" };
        }

        // 2. Lời chào đơn giản — KHÔNG phải lời chào nếu câu có dấu "?" hoặc chứa "hỏi"
        //    (vd "xin chào, cho tôi hỏi giá thức ăn") ⇒ bỏ qua fast-path này.
        //
        //    "hi " phải khớp theo ranh giới từ, nếu không "khi nào" cũng bị hiểu
        //    thành lời chào "hi" ⇒ trả nhầm "giới thiệu".
        if (IsGreeting(text)
            && !text.Contains('?')
            && !text.Contains("hỏi", StringComparison.Ordinal))
        {
            return new Models.AiIntentResult { Intent = "introduction" };
        }

        // 2a. Số lượng sản phẩm — trả lời trực tiếp từ DB, không cần AI sinh câu trả lời.
        //
        //     LƯU Ý: nếu câu hỏi nhắc "dịch vụ" (vd "có bao nhiêu dịch vụ?") thì phải
        //     ưu tiên intent dịch vụ, nếu không chatbot sẽ đếm SẢN PHẨM ⇒ lạc đề.
        if ((ContainsAny(text, CountKeywords) || IsCountWithNumber(text))
            && !ContainsAny(text, ServiceKeywords))
        {
            var (productType, pet) = ExtractProductTypeAndPet(text);

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (productType is not null)
            {
                parameters["product_type"] = productType;
            }

            if (pet is not null)
            {
                parameters["pet"] = pet;
            }

            AppendPriceParameters(parameters, price);

            return new Models.AiIntentResult { Intent = "product_count", Parameters = parameters };
        }

        // 2b. Lọc sản phẩm theo giá (vd "món nào dưới 20k", "có sản phẩm nào từ 20k đến 50k").
        //     Trả lời THẲNG từ DB ⇒ chính xác tuyệt đối, không bị Gemini bịa giá.
        if (price.HasValue && ContainsAny(text, ProductQueryKeywords))
        {
            var (productType, pet) = ExtractProductTypeAndPet(text);

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (productType is not null)
            {
                parameters["product_type"] = productType;
            }

            if (pet is not null)
            {
                parameters["pet"] = pet;
            }

            AppendPriceParameters(parameters, price);

            return new Models.AiIntentResult { Intent = "product_price_filter", Parameters = parameters };
        }

        // 2c. Thông tin liên hệ của shop — trả lời trực tiếp từ SystemSettings,
        //     không tốn request Gemini (quan trọng khi đã hết quota).
        if (ContainsAny(text, ContactKeywords))
        {
            return new Models.AiIntentResult { Intent = "shop_contact" };
        }

        // 3. Dịch vụ — có đường xử lý tĩnh (GetActiveServicesAsync) trong service.
        if (ContainsAny(text, ServiceKeywords) && !ContainsAny(text, PriceKeywords))
        {
            return new Models.AiIntentResult { Intent = "service_inquiry" };
        }

        // 4. Đơn hàng.
        if (ContainsAny(text, OrderKeywords))
        {
            return new Models.AiIntentResult { Intent = "order_status" };
        }

        // 5. Thanh toán.
        if (ContainsAny(text, PaymentKeywords))
        {
            return new Models.AiIntentResult { Intent = "payment" };
        }

        // 6. Đặt lịch.
        if (ContainsAny(text, BookingKeywords))
        {
            return new Models.AiIntentResult { Intent = "booking" };
        }

        // Không chắc chắn ⇒ để AI quyết định.
        return null;
    }

    /// <summary>
    /// Lời chào đơn giản — "hi"/"hello" chỉ khớp khi đứng RIÊNG (theo ranh giới từ),
    /// tránh "khi nào" bị hiểu thành "hi".
    /// </summary>
    private static bool IsGreeting(string text)
    {
        var folded = TextNormalizer.Fold(text);

        if (ContainsAny(folded, GreetingKeywords))
        {
            return true;
        }

        // "hi" / "hello" đứng riêng (sau khi bỏ dấu).
        var index = folded.IndexOf("hi", StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetter(folded[index - 1]);
            var endIndex = index + 2;
            var afterOk = endIndex >= folded.Length || !char.IsLetter(folded[endIndex]);

            if (beforeOk && afterOk)
            {
                return true;
            }

            index = folded.IndexOf("hi", index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Kiểm tra câu có chứa bất kỳ từ khoá nào không (không phân biệt dấu,
    /// theo ranh giới từ).
    /// </summary>
    private static bool ContainsAny(string text, string[] keywords)
    {
        var haystack = TextNormalizer.Fold(text);

        foreach (var keyword in keywords)
        {
            if (TextNormalizer.ContainsPhrase(haystack, TextNormalizer.Fold(keyword)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Câu hỏi dạng "chỉ 1 món?", "còn 2 sản phẩm nữa?" ⇒ intent đếm số lượng.
    ///
    /// <para>
    /// Chỉ chấp nhận khi số đi kèm DANH TỪ SẢN PHẨM ("món/sản phẩm/hàng/loại/mẫu")
    /// và số đó là số nhỏ (1..99) — tránh nhầm với câu hỏi về giá ("1,5 triệu").
    /// </para>
    /// </summary>
    private static bool IsCountWithNumber(string text)
    {
        var folded = TextNormalizer.Fold(text);

        foreach (System.Text.RegularExpressions.Match m in CountWithNumberPattern.Matches(folded))
        {
            if (!int.TryParse(m.Groups["num"].Value, out var num) || num is < 1 or > 99)
            {
                continue;
            }

            // "chỉ 1 món" ⇒ chắc chắn là hỏi số lượng.
            if (m.Groups["lead"].Success && m.Groups["lead"].Value.Trim().Length > 0)
            {
                return true;
            }

            // "1 món" / "2 sản phẩm" mà không có "chỉ" ⇒ vẫn coi là hỏi số lượng
            // khi câu ngắn & có dấu hỏi.
            if (text.Contains('?'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ghi ràng buộc giá vào <c>parameters</c> của intent (dạng chuỗi để tương thích
    /// với <see cref="Models.AiIntentResult.Parameters"/> là <c>Dictionary&lt;string,string&gt;</c>).
    /// </summary>
    private static void AppendPriceParameters(Dictionary<string, string> parameters, PriceConstraint price)
    {
        if (price.Min.HasValue)
        {
            parameters["min_price"] = price.Min.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (price.Max.HasValue)
        {
            parameters["max_price"] = price.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            parameters["max_price_inclusive"] = price.MaxInclusive ? "true" : "false";
        }
    }

    /// <summary>
    /// Trích loại sản phẩm + loài thú từ câu hỏi (đơn giản, không cần AI).
    /// </summary>
    private static (string? ProductType, string? Pet) ExtractProductTypeAndPet(string text)
    {
        var folded = TextNormalizer.Fold(text);

        string? productType = null;
        string? pet = null;

        if (ContainsAny(folded, new[] { "thức ăn", "pate", "hạt" }))
        {
            productType = "thức ăn";
        }
        else if (ContainsAny(folded, new[] { "đồ chơi" }))
        {
            productType = "đồ chơi";
        }
        else if (ContainsAny(folded, new[] { "phụ kiện", "dây cổ", "áo cho" }))
        {
            productType = "phụ kiện";
        }
        else if (ContainsAny(folded, new[] { "thuốc", "vitamin" }))
        {
            productType = "thuốc";
        }

        if (TextNormalizer.MentionsDog(folded))
        {
            pet = "chó";
        }
        else if (TextNormalizer.MentionsCat(folded))
        {
            pet = "mèo";
        }

        return (productType, pet);
    }
}
