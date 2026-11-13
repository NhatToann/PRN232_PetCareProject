using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Repositories;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho HỘP THƯ CHUNG (13/11/2026).
///
/// <para>
/// <b>YÊU CẦU:</b> "tất cả nhân viên đều xem được tin của 1 customer" ⇒ luồng
/// chat khách ↔ nhân viên KHÔNG còn là 1-1 mà là hộp thư theo khách:
/// mọi nhân viên đều đọc được (và ghi vào) cùng một tập tin theo <c>CustomerID</c>.
/// </para>
///
/// <para>
/// <b>VÌ SAO CẦN TEST:</b> trước đây khoá luồng là cặp
/// <c>(CustomerID, StaffID)</c> ⇒ nhân viên A không thấy tin nhân viên B đã
/// trả lời ⇒ khách bị "bỏ rơi" nếu nhân viên trả lời sai người. Test bên dưới
/// chốt lại hành vi mới để không regression.
/// </para>
/// </summary>
public class ChatPoolThreadTests
{
    /// <summary>
    /// Khách mở hộp thư nhân viên ⇒ khoá luồng là CHÍNH khách đó.
    /// </summary>
    [Fact]
    public void Khach_MoHopThuNhanVien_KhoaLuongLaChinhKhach()
    {
        var customerId = ChatRepository.ResolvePoolCustomerId(61, "customer", 61, "pool");

        Assert.Equal(61, customerId);
    }

    /// <summary>
    /// Nhân viên mở hộp thư của khách ⇒ khoá luồng là id KHÁCH (không phải
    /// id nhân viên) ⇒ mọi nhân viên đều đọc được cùng 1 tập tin.
    /// </summary>
    [Theory]
    [InlineData("staff")]
    [InlineData("admin")]
    [InlineData("doctor")]
    public void NhanVien_MoHopThuCuaKhach_KhoaLuongLaIdKhach(string staffKind)
    {
        // Nhân viên id = 4 mở hộp thư của khách id = 61.
        var customerId = ChatRepository.ResolvePoolCustomerId(4, staffKind, 61, "customer");

        Assert.Equal(61, customerId);
    }

    /// <summary>
    /// 2 nhân viên khác nhau mở hộp thư của CÙNG 1 khách ⇒ phải ra CÙNG khoá
    /// luồng ⇒ cả hai đều thấy tin của nhau.
    /// </summary>
    [Fact]
    public void HaiNhanVien_MoCungMotKhach_PhaiCungKhoaLuong()
    {
        var staffA = ChatRepository.ResolvePoolCustomerId(4, "staff", 61, "customer");
        var staffB = ChatRepository.ResolvePoolCustomerId(8, "admin", 61, "customer");
        var doctorC = ChatRepository.ResolvePoolCustomerId(2, "doctor", 61, "customer");

        Assert.Equal(staffA, staffB);
        Assert.Equal(staffA, doctorC);
        Assert.Equal(61, staffA);
    }

    /// <summary>
    /// Nhân viên mở hộp thư bằng <c>peerKind = "pool"</c> cũng phải tra được
    /// id khách (client có thể gửi theo kiểu pool).
    /// </summary>
    [Fact]
    public void NhanVien_MoHopThuKieuPool_TraDungIdKhach()
    {
        var customerId = ChatRepository.ResolvePoolCustomerId(4, "staff", 61, "pool");

        Assert.Equal(61, customerId);
    }

    /// <summary>
    /// Nhân viên ↔ nhân viên (không có khách trong luồng) ⇒ không hợp lệ ⇒ 0.
    /// </summary>
    [Fact]
    public void NhanVien_TrongNhanVien_KhongCoKhoaLuong()
    {
        var customerId = ChatRepository.ResolvePoolCustomerId(4, "staff", 8, "staff");

        Assert.Equal(0, customerId);
    }

    /// <summary>
    /// Loại "pool" phải được nhận diện là hộp thư của khách (không phải
    /// "không xác định") — nếu quên ⇒ lịch sử hộp thư trả về rỗng.
    /// </summary>
    [Fact]
    public void PeerKindPool_KhongPhaiLaKhach_HayNhanVien()
    {
        Assert.False(ChatRepository.IsCustomerKind("pool"));
        Assert.False(ChatRepository.IsCustomerKind("staff"));
        Assert.True(ChatRepository.IsCustomerKind("customer"));
        Assert.True(ChatRepository.IsCustomerKind("user"));
    }

    /// <summary>
    /// Hằng số loại hộp thư chung phải tồn tại và đúng giá trị (client + server
    /// dùng chung qua JSON ⇒ sai 1 ký tự là hỏng cả luồng).
    /// </summary>
    [Fact]
    public void HangSoPool_DungGiaTri()
    {
        Assert.Equal("pool", ChatRepository.KindPool);
        Assert.Equal("pool", ChatRepositoryKind.Pool);
    }
}
