namespace PetShop.Models
{
    public class ChatbotOptions
    {
        public const string SectionName = "Chatbot";
        public const string DefaultProvider = "gemini";

        public string DefaultModel { get; set; } = "gemini-3.5-flash";
        public string DefaultProviderName { get; set; } = DefaultProvider;
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 1000;
        public bool EnableDatabaseQuery { get; set; } = true;
        public string SystemPromptPath { get; set; } = string.Empty;

        public Dictionary<string, AiProviderConfig> Providers { get; set; } = new();
    }

    public class AiProviderConfig
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string DefaultModel { get; set; } = string.Empty;
        public string FallbackModel { get; set; } = "gemini-3.5-flash-lite";
        public int MaxTokens { get; set; } = 2048;
        public double Temperature { get; set; } = 0.7;
        public int TimeoutSeconds { get; set; } = 60;
        public bool Enabled { get; set; } = true;
    }
}
