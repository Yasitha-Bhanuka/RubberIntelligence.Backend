using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Dpp.DTOs;
using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Secure lot-linked messaging service.
    /// Confidential messages are encrypted with AES-256-CBC (random IV) before persistence.
    /// Non-confidential messages are stored as plaintext in EncryptedContent.
    /// Decryption happens ONLY here — never in the controller.
    /// </summary>
    public class MessageService
    {
        private readonly IMessageRepository _messageRepository;
        private readonly FieldEncryptionService _fieldEncryption;

        public MessageService(IMessageRepository messageRepository, FieldEncryptionService fieldEncryption)
        {
            _messageRepository = messageRepository;
            _fieldEncryption   = fieldEncryption;
        }

        public async Task<MessageDto> SendMessage(
            string lotId,
            string senderId,
            string receiverId,
            string content,
            bool isConfidential)
        {
            Message message;

            if (isConfidential)
            {
                // Encrypt content with AES-256-CBC; store ciphertext + IV
                var encrypted = _fieldEncryption.Encrypt(content);
                message = new Message
                {
                    LotId            = lotId,
                    SenderId         = senderId,
                    ReceiverId       = receiverId,
                    EncryptedContent = encrypted.EncryptedValue,
                    IV               = encrypted.IV,
                    IsConfidential   = true
                };
            }
            else
            {
                // Store plaintext directly in EncryptedContent; IV left empty
                message = new Message
                {
                    LotId            = lotId,
                    SenderId         = senderId,
                    ReceiverId       = receiverId,
                    EncryptedContent = content,
                    IV               = string.Empty,
                    IsConfidential   = false
                };
            }

            await _messageRepository.CreateAsync(message);

            // Return DTO with plaintext content to the caller
            return ToDto(message, content);
        }

        public async Task<List<MessageDto>> GetMessages(string lotId, string userId)
        {
            var messages = await _messageRepository.GetByLotIdAsync(lotId);

            // Only expose messages where this user is a participant
            return messages
                .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .Select(m => ToDto(m, DecryptIfNeeded(m)))
                .ToList();
        }

        public async Task<long> GetUnreadCount(string userId)
            => await _messageRepository.GetUnreadCountAsync(userId);

        // ── Private Helpers ──────────────────────────────────────────────

        private string DecryptIfNeeded(Message m) =>
            m.IsConfidential
                ? _fieldEncryption.Decrypt(m.EncryptedContent, m.IV)
                : m.EncryptedContent;

        private static MessageDto ToDto(Message m, string plainContent) => new()
        {
            Id             = m.Id,
            LotId          = m.LotId,
            SenderId       = m.SenderId,
            ReceiverId     = m.ReceiverId,
            Content        = plainContent,
            IsConfidential = m.IsConfidential,
            CreatedAt      = m.CreatedAt
        };
    }
}
