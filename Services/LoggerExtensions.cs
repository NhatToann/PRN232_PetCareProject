using Microsoft.Extensions.Logging;

namespace PetShop.Services
{
    /// <summary>
    /// Helper ghi log dạng "dòng thông báo" — thay thế cho <c>Console.WriteLine</c>.
    ///
    /// Vì sao không dùng thẳng <c>_logger.LogInformation("... {x} ...")</c>:
    /// nhiều chỗ trong dự án ghép chuỗi động (vd <c>$"[PayOS] Order #{orderId} ..."</c>).
    /// Nếu truyền thẳng chuỗi đó làm message template, các khoảng <c>{...}</c> trong dữ liệu
    /// sẽ bị hiểu nhầm là placeholder. Hàm này coi toàn bộ chuỗi là một giá trị đơn lẻ,
    /// nên luôn in đúng nguyên văn.
    /// </summary>
    public static class LoggerExtensions
    {
        public static void Line(this ILogger logger, string message)
        {
            logger.LogInformation("{Line}", message);
        }

        /// <summary>Ghi log mức Warning cho các tình huống bất thường nhưng không làm hỏng luồng.</summary>
        public static void LineWarning(this ILogger logger, string message)
        {
            logger.LogWarning("{Line}", message);
        }

        /// <summary>Ghi log mức Error kèm exception.</summary>
        public static void LineError(this ILogger logger, Exception exception, string message)
        {
            logger.LogError(exception, "{Line}", message);
        }
    }
}
