using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using PetShop.Models;

namespace PetShop.Models
{
    /// <summary>
    /// Dựng EDM model cho OData (slide PRN232 - Ch04).
    /// EDM = "Entity Data Model": bản mô tả dữ liệu mà OData dùng để hiểu cấu trúc
    /// và biết cách dịch $filter / $select / $orderby / $expand sang LINQ.
    ///
    /// Vì entity trong project dùng tên key không chuẩn (ProductId, ServiceId, Id...)
    /// nên phải khai báo key tường minh cho từng entity — nếu không,
    /// ODataConventionModelBuilder sẽ báo lỗi "has no keys defined".
    /// </summary>
    public static class PetShopEdmModel
    {
        /// <summary>
        /// Ba entity set được mở ra cho client truy vấn động:
        /// Products, PetServices, Pets.
        /// </summary>
        public static IEdmModel Build()
        {
            var builder = new ODataConventionModelBuilder();

            builder.EntityType<Product>().HasKey(p => p.ProductId);
            builder.EntityType<ProductCategory>().HasKey(c => c.CategoryId);
            builder.EntityType<Supplier>().HasKey(s => s.SupplierId);
            builder.EntityType<Admin>().HasKey(a => a.AdminId);
            builder.EntityType<PetService>().HasKey(s => s.ServiceId);
            builder.EntityType<Pet>().HasKey(p => p.Id);
            builder.EntityType<Breed>().HasKey(b => b.BreedId);
            builder.EntityType<Species>().HasKey(s => s.SpeciesId);

            // Bỏ các navigation property dạng collection.
            // Lý do: mỗi request $expand sẽ nhân bản dòng (row multiplication),
            // khiến câu SQL nặng và $count/$top sai số lượng.
            // Giữ lại navigation property dạng đơn (Category, Supplier, Breed)
            // vì chúng là demo $expand kinh điển trong slide.
            builder.EntityType<Product>().Ignore(p => p.OrderDetails);
            builder.EntityType<Pet>().Ignore(p => p.Bookings);
            builder.EntityType<Pet>().Ignore(p => p.MedicalRecords);
            builder.EntityType<PetService>().Ignore(s => s.BookingServices);

            builder.EntitySet<Product>("Products");
            builder.EntitySet<PetService>("PetServices");
            builder.EntitySet<Pet>("Pets");

            return builder.GetEdmModel();
        }
    }
}
