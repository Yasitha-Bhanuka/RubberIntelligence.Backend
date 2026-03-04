namespace RubberIntelligence.API.Modules.Dpp.DTOs
{
    /// <summary>
    /// Returned ONLY to exporters with an APPROVED AccessRequest.
    /// Contains decrypted field values — never stored in DB.
    /// </summary>
    public class ConfidentialFieldDto
    {
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// Plaintext value, decrypted in service layer only.
        /// Never persisted. Never exposed without an approved request.
        /// </summary>
        public string DecryptedValue { get; set; } = string.Empty;
    }
}
