using Microsoft.AspNetCore.Mvc;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Controllers
{
    [ApiController]
    [Route("api/chatbot")]
    public sealed class ChatbotController : ControllerBase
    {
        private readonly IChatbotService _chatbotService;
        private readonly ILogger<ChatbotController> _logger;

        public ChatbotController(
            IChatbotService chatbotService,
            ILogger<ChatbotController> logger)
        {
            _chatbotService = chatbotService;
            _logger = logger;
        }

        [HttpPost("message")]
        public async Task<ActionResult<ChatMessageResponseDto>> SendMessage(
            [FromBody] ChatMessageRequestDto request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new ApiErrorDto { Error = "Message cannot be empty." });
            }

            if (request.Message.Length > 2000)
            {
                return BadRequest(new ApiErrorDto { Error = "Message is too long. Maximum 2000 characters allowed." });
            }

            try
            {
                _logger.LogInformation(
                    "Chatbot request: CustomerId={CustomerId}, MessageLength={Length}",
                    request.CustomerId, request.Message.Length);

                var response = await _chatbotService.GetResponseAsync(request, ct);

                _logger.LogInformation(
                    "Chatbot response: Source={Source}, ReplyLength={Length}",
                    response.Source, response.Reply.Length);

                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Chatbot configuration error");
                return StatusCode(503, new ApiErrorDto
                {
                    Error = "Chatbot service is not available.",
                    Details = "No AI provider is configured. Please contact the administrator."
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "AI provider HTTP error");
                return StatusCode(502, new ApiErrorDto
                {
                    Error = "AI service temporarily unavailable.",
                    Details = "Please try again in a few moments."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected chatbot error");
                return StatusCode(500, new ApiErrorDto
                {
                    Error = "An unexpected error occurred.",
                    Details = "Please try again or contact support."
                });
            }
        }

        [HttpPost("intent")]
        public async Task<ActionResult<AiIntentResult>> DetectIntent(
            [FromBody] DetectIntentRequestDto request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new ApiErrorDto { Error = "Message cannot be empty." });
            }

            try
            {
                var intent = await _chatbotService.AnalyzeIntentWithAi(request.Message, ct);
                return Ok(intent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Intent detection error");
                return StatusCode(500, new ApiErrorDto { Error = "Failed to detect intent." });
            }
        }

        [HttpGet("context/{customerId:int}")]
        public async Task<ActionResult<ConversationContextDto>> GetContext(
            int customerId,
            CancellationToken ct)
        {
            try
            {
                var context = await _chatbotService.GetContextAsync(customerId, ct);
                return Ok(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Context retrieval error for customer {CustomerId}", customerId);
                return StatusCode(500, new ApiErrorDto { Error = "Failed to retrieve context." });
            }
        }

        [HttpGet("health")]
        public async Task<ActionResult<ChatbotHealthDto>> GetHealth(CancellationToken ct)
        {
            try
            {
                var context = await _chatbotService.GetContextAsync(null, ct);
                return Ok(new ChatbotHealthDto
                {
                    Status = "healthy",
                    Message = "Chatbot service is running.",
                    Timestamp = DateTime.UtcNow,
                    AiProviderConfigured = true,
                    DatabaseConnected = true
                });
            }
            catch
            {
                return Ok(new ChatbotHealthDto
                {
                    Status = "degraded",
                    Message = "Chatbot service is running but may have limited functionality.",
                    Timestamp = DateTime.UtcNow,
                    AiProviderConfigured = false,
                    DatabaseConnected = false
                });
            }
        }
    }

    public class DetectIntentRequestDto
    {
        public string Message { get; set; } = string.Empty;
    }

    public class ChatbotHealthDto
    {
        public string Status { get; set; } = "unknown";
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool AiProviderConfigured { get; set; }
        public bool DatabaseConnected { get; set; }
    }
}
