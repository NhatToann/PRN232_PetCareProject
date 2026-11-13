
GO
USE [SHOP_PET_Database]
GO
/* =====================================================================
   CHUẨN HOÁ TIỀN TỆ VND — chạy tự động sau khi restore seed.

   Dữ liệu seed gốc của OJT dùng giá "kiểu USD" cho một số bảng
   (15.99, 24.99, 8.99, 6.99, 12.50, 55.47, 1.00 ...) trong khi phần
   lớn dữ liệu (Products.price, PetService.price,
   BoardingRoom.price_per_day) đã là VND thật ⇒ hệ thống hiển thị
   lẫn lộn "15.990 ₫" (thực chất là 15.99 USD).

   Cách xử lý (KHÔNG dùng tỷ giá quy đổi cho phần có bản ghi thật —
   dùng GIÁ THẬT của sản phẩm):
     1. Order_Detail.unit_price ← Products.price (fallback PetService.price)
     2. Order.total_amount      ← SUM(quantity × unit_price)
     3. Đơn/chi tiết mồ côi     ← nhân tỷ giá fallback 25.000 (USD→VND)
     4. Payment.amount           ← đồng bộ theo total_amount của đơn
     5. StaffSalary.HourlyRate   ← đặt lại theo median các staff khác
     6. PayrollRecords.HourlyRate← đồng bộ theo StaffSalary

   Idempotent: chạy lại nhiều lần vẫn cho kết quả giống nhau.
   KHÔNG xoá bất kỳ dòng dữ liệu nào.
   ===================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

/* 1. Order_Detail.unit_price ← giá VND thật của sản phẩm/dịch vụ */
UPDATE od
SET od.unit_price = COALESCE(p.price, ps.price, od.unit_price)
FROM dbo.Order_Detail od
LEFT JOIN dbo.Products p ON p.product_id = od.product_id
LEFT JOIN dbo.PetService ps ON ps.service_id = od.service_id
WHERE od.unit_price < 1000
  AND (p.price IS NOT NULL OR ps.price IS NOT NULL);

/* 2. Order.total_amount ← SUM(quantity × unit_price) */
UPDATE o
SET o.total_amount = x.total
FROM dbo.[Order] o
CROSS APPLY (
    SELECT ISNULL(SUM(od.quantity * od.unit_price), 0) AS total
    FROM dbo.Order_Detail od
    WHERE od.order_id = o.order_id
) x
WHERE o.total_amount < 1000
  AND x.total > 0;

/* 3. Tỷ lệ quy đổi fallback cho nhóm dữ liệu seed cũ (USD→VND) */
DECLARE @ratio DECIMAL(18,6);

SELECT @ratio = CASE
        WHEN MIN(o2.total_amount) > 0 THEN AVG(CAST(o2.total_amount AS FLOAT)) / AVG(CAST(o1.total_amount AS FLOAT))
        ELSE NULL
    END
FROM dbo.[Order] o1
JOIN dbo.[Order] o2 ON o2.order_id = o1.order_id
WHERE o1.total_amount < 1000
  AND o2.total_amount >= 1000;

IF @ratio IS NULL OR @ratio <= 0
    SET @ratio = 25000;

/* 3a. Đơn KHÔNG có Order_Detail */
UPDATE dbo.[Order]
SET total_amount = ROUND(total_amount * @ratio, 0)
WHERE total_amount < 1000
  AND NOT EXISTS (SELECT 1 FROM dbo.Order_Detail d WHERE d.order_id = dbo.[Order].order_id);

/* 3b. Order_Detail mồ côi (product/service không tồn tại) */
UPDATE od
SET od.unit_price = ROUND(od.unit_price * @ratio, 0)
FROM dbo.Order_Detail od
WHERE od.unit_price < 1000
  AND NOT EXISTS (SELECT 1 FROM dbo.Products p WHERE p.product_id = od.product_id)
  AND NOT EXISTS (SELECT 1 FROM dbo.PetService ps WHERE ps.service_id = od.service_id);

/* 3c. Tính lại total_amount cho đơn chứa dòng chi tiết mồ côi */
UPDATE o
SET o.total_amount = x.total
FROM dbo.[Order] o
CROSS APPLY (
    SELECT ISNULL(SUM(od.quantity * od.unit_price), 0) AS total
    FROM dbo.Order_Detail od
    WHERE od.order_id = o.order_id
) x
WHERE x.total > 0
  AND o.total_amount < 1000;

/* 4. Payment.amount ← đồng bộ theo total_amount của đơn */
UPDATE p
SET p.amount = o.total_amount
FROM dbo.Payment p
JOIN dbo.[Order] o
    ON o.order_id = p.reference_id
   AND p.payment_type = 'order'
WHERE p.amount < 1000
  AND o.total_amount > 0;

/* 4b. Payment mồ côi (reference_id không có trong Order) — giữ nguyên dòng */
UPDATE p
SET p.amount = ROUND(p.amount * @ratio, 0)
FROM dbo.Payment p
WHERE p.amount < 1000
  AND p.payment_type = 'order'
  AND NOT EXISTS (
      SELECT 1 FROM dbo.[Order] o
      WHERE o.order_id = p.reference_id
  );

/* 5. StaffSalary.HourlyRate — giá "kiểu USD" → VND */
DECLARE @medianRate DECIMAL(18,2);

SELECT @medianRate = AVG(CAST(HourlyRate AS FLOAT))
FROM (
    SELECT HourlyRate,
           ROW_NUMBER() OVER (ORDER BY HourlyRate) AS rn,
           COUNT(*) OVER () AS cnt
    FROM dbo.StaffSalary
    WHERE HourlyRate >= 1000
) t
WHERE rn IN ((cnt + 1) / 2, (cnt + 2) / 2);

IF @medianRate IS NULL OR @medianRate <= 0
    SET @medianRate = 19000;

UPDATE dbo.StaffSalary
SET HourlyRate = @medianRate
WHERE HourlyRate < 1000;

/* 6. PayrollRecords.HourlyRate ← đồng bộ theo StaffSalary */
UPDATE pr
SET pr.HourlyRate = ss.HourlyRate
FROM dbo.PayrollRecords pr
JOIN dbo.StaffSalary ss ON ss.StaffId = pr.StaffID
WHERE pr.HourlyRate IS NOT NULL
  AND pr.HourlyRate < 1000;

COMMIT TRANSACTION;
GO
