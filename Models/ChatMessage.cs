using System;
using System.Collections.Generic;

namespace PetShop.Models
{
    public partial class ChatMessage
    {
        public int MessageId { get; set; }

        public int CustomerId { get; set; }

        public int? StaffId { get; set; }

        public string SenderType { get; set; } = null!;

        public string Message { get; set; } = null!;

        public DateTime? SentAt { get; set; }

        public bool? IsRead { get; set; }

        /// <summary>
        /// Tên hiển thị của người gửi.
        ///
        /// <para>
        /// <b>VÌ SAO CẦN (13/11/2026):</b> luồng chat khách ↔ nhân viên nay là
        /// <b>HỘP THƯ CHUNG</b> — mọi nhân viên đều thấy tin của cùng 1 khách ⇒
        /// tin nhắn không còn thuộc về "1 đối tác" mà thuộc về "1 người gửi" ⇒
        /// phải lưu tên để hiển thị đúng trên từng bubble.
        /// </para>
        /// </summary>
        public string? SenderName { get; set; }

        /// <summary>
        /// Loại thật của người gửi: <c>staff</c> / <c>admin</c> / <c>doctor</c> /
        /// <c>customer</c>.
        ///
        /// <para>
        /// Cột <c>SenderType</c> bị CHECK constraint giới hạn chỉ còn
        /// <c>staff</c> / <c>customer</c> ⇒ không phân biệt được nhân viên với
        /// bác sĩ / quản trị (id các bảng lại trùng nhau) ⇒ cột này lưu nhãn
        /// đầy đủ để tra đúng tên người gửi.
        /// </para>
        /// </summary>
        public string? SenderKind { get; set; }

        public virtual Customer Customer { get; set; } = null!;

        public virtual Admin? Staff { get; set; }
    }
}
