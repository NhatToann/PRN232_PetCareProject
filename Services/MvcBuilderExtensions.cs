using Microsoft.Extensions.DependencyInjection;

namespace PetShop.Services;

public static class MvcBuilderExtensions
{
    /// <summary>
    /// Bật XML output formatter để hỗ trợ content negotiation
    /// (client gửi "Accept: application/xml" sẽ nhận response dạng XML).
    ///
    /// Dùng formatter tự viết thay vì formatter XML có sẵn của ASP.NET Core vì các DTO
    /// của dự án là positional record — chúng chỉ có init-only setter nên XmlSerializer
    /// (cần constructor không tham số) và DataContractSerializer (cần setter thật)
    /// đều không serialize được.
    ///
    /// ASP.NET Core 8 đã đóng gói sẵn Microsoft.AspNetCore.Mvc.Formatters.Xml trong
    /// shared framework, nên không cần thêm package riêng.
    /// </summary>
    public static IMvcBuilder AddPetShopXmlFormatters(this IMvcBuilder builder)
    {
        builder.AddMvcOptions(options =>
        {
            // Append (không Insert 0) để JSON vẫn là formatter mặc định khi client
            // không gửi header Accept.
            options.OutputFormatters.Add(new XmlOutputFormatter());

            // Chuẩn REST: nếu client yêu cầu media type mà server không hỗ trợ
            // thì trả 406 Not Acceptable thay vì trả 200 sai định dạng.
            options.ReturnHttpNotAcceptable = true;
        });

        return builder;
    }
}
