using System;
using System.Collections.Generic;

namespace PetShop.Models
{
    public class ChatMessageRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public int? CustomerId { get; set; }
        public List<ChatHistoryItemDto>? History { get; set; }
    }

    public class ChatHistoryItemDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class ChatMessageResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? Intent { get; set; }
        public Dictionary<string, object>? Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class ConversationContextDto
    {
        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public List<string> AvailablePets { get; set; } = new();
        public List<string> RecentOrders { get; set; } = new();
        public List<string> RecentBookings { get; set; } = new();

        // Shop data
        public List<string> StaffList { get; set; } = new();
        public List<string> DoctorList { get; set; } = new();
        public List<string> Products { get; set; } = new();
        public List<string> Services { get; set; } = new();
        public List<string> ProductCategories { get; set; } = new();
        public List<string> Species { get; set; } = new();
        public List<string> BoardingRooms { get; set; } = new();
    }

    public class ChatIntentDto
    {
        public string Intent { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
    }

    public class ApiErrorDto
    {
        public string Error { get; set; } = string.Empty;
        public string? Details { get; set; }
    }

    public class AiIntentResult
    {
        public string Intent { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
    }
}
