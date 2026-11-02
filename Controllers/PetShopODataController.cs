using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Models;

namespace PetShop.Controllers
{
    /// <summary>
    /// OData endpoint (slide PRN232 - Ch04).
    ///
    /// Trả về IQueryable để middleware OData tự áp dụng các query option
    /// $filter / $select / $orderby / $top / $skip / $expand / $count.
    /// Nhờ vậy client tự do truy vấn động mà không phải viết endpoint mới cho từng bộ lọc.
    ///
    /// Dùng OData attribute routing (route template theo cú pháp OData) thay vì
    /// convention routing: convention routing gắn entity set theo TÊN CONTROLLER,
    /// mà các tên controller hiện có (ProductsController, PetsController,
    /// PetServicesController) đã bị chiếm bởi API REST cũ.
    /// </summary>
    [ODataRouteComponent("PetShop")]
    public sealed class PetShopODataController : ODataController
    {
        private readonly ShopPetDatabaseContext _db;

        public PetShopODataController(ShopPetDatabaseContext db)
        {
            _db = db;
        }

        // ---------------- Products ----------------

        /// <summary>GET /PetShop/Products</summary>
        [HttpGet("PetShop/Products")]
        [EnableQuery(PageSize = 50, MaxTop = 200)]
        public IActionResult GetProducts()
        {
            return Ok(_db.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted));
        }

        /// <summary>GET /PetShop/Products({key})</summary>
        [HttpGet("PetShop/Products({key})")]
        [EnableQuery]
        public IActionResult GetProduct([FromRoute] int key)
        {
            var product = _db.Products
                .AsNoTracking()
                .Where(p => p.ProductId == key && !p.IsDeleted);

            return Ok(SingleResult.Create(product));
        }

        /// <summary>GET /PetShop/Products/$count — trả về tổng số bản ghi (OData V4).</summary>
        [HttpGet("PetShop/Products/$count")]
        public IActionResult GetProductsCount()
        {
            var count = _db.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Count();

            return Ok(count);
        }

        // ---------------- PetServices ----------------

        /// <summary>GET /PetShop/PetServices</summary>
        [HttpGet("PetShop/PetServices")]
        [EnableQuery(PageSize = 50, MaxTop = 200)]
        public IActionResult GetPetServices()
        {
            return Ok(_db.PetServices.AsNoTracking());
        }

        /// <summary>GET /PetShop/PetServices({key})</summary>
        [HttpGet("PetShop/PetServices({key})")]
        [EnableQuery]
        public IActionResult GetPetService([FromRoute] int key)
        {
            var service = _db.PetServices
                .AsNoTracking()
                .Where(s => s.ServiceId == key);

            return Ok(SingleResult.Create(service));
        }

        /// <summary>GET /PetShop/PetServices/$count — trả về tổng số bản ghi (OData V4).</summary>
        [HttpGet("PetShop/PetServices/$count")]
        public IActionResult GetPetServicesCount()
        {
            var count = _db.PetServices.AsNoTracking().Count();

            return Ok(count);
        }

        // ---------------- Pets ----------------

        /// <summary>GET /PetShop/Pets</summary>
        [HttpGet("PetShop/Pets")]
        [EnableQuery(PageSize = 50, MaxTop = 200)]
        public IActionResult GetPets()
        {
            return Ok(_db.Pets
                .AsNoTracking()
                .Where(p => p.DeletedAt == null));
        }

        /// <summary>GET /PetShop/Pets({key})</summary>
        [HttpGet("PetShop/Pets({key})")]
        [EnableQuery]
        public IActionResult GetPet([FromRoute] int key)
        {
            var pet = _db.Pets
                .AsNoTracking()
                .Where(p => p.Id == key && p.DeletedAt == null);

            return Ok(SingleResult.Create(pet));
        }

        /// <summary>GET /PetShop/Pets/$count — trả về tổng số bản ghi (OData V4).</summary>
        [HttpGet("PetShop/Pets/$count")]
        public IActionResult GetPetsCount()
        {
            var count = _db.Pets
                .AsNoTracking()
                .Where(p => p.DeletedAt == null)
                .Count();

            return Ok(count);
        }
    }
}
