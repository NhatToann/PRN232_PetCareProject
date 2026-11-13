namespace PetShop.Services;

/// <summary>
/// Danh mục CSS theo nhóm / theo trang — nguồn sự thật duy nhất cho việc
/// tách CSS theo layout.
///
/// <para>
/// <b>Bối cảnh:</b> trước đây <c>App.razor</c> <c>&lt;link&gt;</c> toàn bộ 20 file
/// CSS (~380 KB) ở mọi trang. Nay mỗi trang chỉ nạp CSS nó cần.
/// </para>
///
/// <para>
/// <b>Cách dùng:</b> layout gọi <c>CssBundle.ForPage(...)</c> rồi truyền cho
/// <c>&lt;PageStyles /&gt;</c>.
/// </para>
/// </summary>
public static class CssBundle
{
    // -----------------------------------------------------------------
    // NHÓM DÙNG CHUNG — nạp ở mọi layout.
    //
    // Được rút ra từ phân tích usage thực tế: các rule này được dùng bởi
    // Header / TopBar / Footer / Navigation / ProductCard — tức là xuất hiện
    // trên hầu hết các trang.
    // -----------------------------------------------------------------
    public static readonly IReadOnlyList<string> Common = new[]
    {
        "app.css",
        "css/template/index.css",
        "css/template/App.css",
        "css/template/home.css",
    };

    /// <summary>CSS của các component dùng chung (header/topbar/footer/chat/bell).</summary>
    public static readonly IReadOnlyList<string> SharedWidgets = new[]
    {
        "css/template/auth.css",
        "css/template/pageContextBar.css",
        "css/template/cartDrawer.css",
        "css/template/notificationBell.css",
        "css/template/chatWidget.css",
    };

    // -----------------------------------------------------------------
    // THEO TRANG.
    //
    // Danh sách lấy từ phân tích selector-vs-usage trên toàn bộ file .razor
    // (script _analyze_css.py). Mỗi rule được gán cho trang nào thực sự
    // dùng class tương ứng.
    // -----------------------------------------------------------------
    public static readonly IReadOnlyList<string> Home = new[]
    {
        "css/template/home.css",
    };

    public static readonly IReadOnlyList<string> Products = new[]
    {
        "css/template/searchProducts.css",
    };

    public static readonly IReadOnlyList<string> ProductDetail = new[]
    {
        "css/template/productDetails.css",
        "css/template/searchProducts.css",
    };

    public static readonly IReadOnlyList<string> Cart = new[]
    {
        "css/template/cart.css",
        "css/spa/cart-react.css",
    };

    public static readonly IReadOnlyList<string> Checkout = new[]
    {
        "css/template/checkout.css",
        "css/template/cart.css",
        "css/spa/cart-react.css",
    };

    public static readonly IReadOnlyList<string> CheckoutResult = new[]
    {
        "css/template/checkoutResult.css",
    };

    public static readonly IReadOnlyList<string> CodConfirmation = new[]
    {
        "css/template/codConfirmation.css",
    };

    public static readonly IReadOnlyList<string> Invoice = new[]
    {
        "css/template/invoice.css",
        "css/template/spa.css",
    };

    public static readonly IReadOnlyList<string> Profile = new[]
    {
        "css/template/profile.css",
    };

    public static readonly IReadOnlyList<string> Auth = new[]
    {
        "css/template/auth.css",
    };

    public static readonly IReadOnlyList<string> About = new[]
    {
        "css/template/about.css",
    };

    public static readonly IReadOnlyList<string> Spa = new[]
    {
        "css/template/spa.css",
        "css/spa/service-react.css",
    };

    public static readonly IReadOnlyList<string> SpaCart = new[]
    {
        "css/template/spa.css",
        "css/spa/service-react.css",
        "css/spa/cart-react.css",
    };

    public static readonly IReadOnlyList<string> SpaInvoice = new[]
    {
        "css/template/invoice.css",
        "css/template/spa.css",
        "css/spa/service-react.css",
    };

    public static readonly IReadOnlyList<string> Orders = new[]
    {
        "css/template/spa.css",
        "css/spa/service-react.css",
    };

    public static readonly IReadOnlyList<string> Search = new[]
    {
        "css/template/searchProducts.css",
    };

    // ----- Staff workspace -----
    public static readonly IReadOnlyList<string> StaffWorkspace = new[]
    {
        "css/template/systemPage.css",
        "css/template/ordersStaff.css",
        "css/template/createProduct.css",
    };

    // -----------------------------------------------------------------
    // Hàm gộp nhiều nhóm, loại trùng lặp, giữ thứ tự.
    // -----------------------------------------------------------------
    public static IReadOnlyList<string> Merge(params IReadOnlyList<string>[] groups)
    {
        var result = new List<string>();

        foreach (var group in groups)
        {
            foreach (var file in group)
            {
                if (!result.Contains(file))
                {
                    result.Add(file);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// CSS dùng chung cho trang KHÁCH HÀNG (MainLayout).
    /// </summary>
    public static IReadOnlyList<string> CustomerCommon => Merge(Common, SharedWidgets);

    /// <summary>
    /// CSS dùng chung cho trang NHÂN VIÊN (StaffWorkspaceLayout).
    /// </summary>
    public static IReadOnlyList<string> StaffCommon => Merge(Common, SharedWidgets, StaffWorkspace);
}
