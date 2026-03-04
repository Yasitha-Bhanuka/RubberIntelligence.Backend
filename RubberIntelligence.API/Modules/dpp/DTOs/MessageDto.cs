namespace RubberIntelligence.API.Modules.Dpp.DTOs
{
    public class MessageDto
    {
        public string Id { get; set; } = string.Empty;
        public string LotId { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public string ReceiverId { get; set; } = string.Empty;
        // Always plaintext — decryption handled exclusively in MessageService
        public string Content { get; set; } = string.Empty;
        public bool IsConfidential { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SendMessageRequest
    {
        public string ReceiverId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsConfidential { get; set; }
    }
}
