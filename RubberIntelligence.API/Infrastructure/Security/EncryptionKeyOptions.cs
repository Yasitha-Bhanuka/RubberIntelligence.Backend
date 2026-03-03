namespace RubberIntelligence.API.Infrastructure.Security
{
    /// <summary>
    /// Strongly-typed options for all AES / HMAC keys used by the DPP pipeline.
    /// Bind from appsettings section "Encryption" — actual secrets are always
    /// injected via environment variables or Azure Key Vault at runtime.
    /// </summary>
    public sealed class EncryptionKeyOptions
    {
        public const string SectionName = "Encryption";

        // ── Environment variable names ───────────────────────────────────────
        /// <summary>Env-var name for the AES-256 field-level key (32 chars).</summary>
        public string FieldKeyEnvVar     { get; set; } = "DPP_FIELD_ENCRYPTION_KEY";

        /// <summary>Env-var name for the AES-256 file-level key (32 chars).</summary>
        public string FileKeyEnvVar      { get; set; } = "DPP_FILE_ENCRYPTION_KEY";

        /// <summary>Env-var name for the HMAC-SHA256 blind-index key (32+ chars).</summary>
        public string HmacKeyEnvVar      { get; set; } = "DPP_BLIND_INDEX_HMAC_KEY";

        // ── Config fallback paths (config values must never hold real secrets in prod) ──
        public string FieldKeyConfigPath { get; set; } = "Encryption:FieldKey";
        public string FileKeyConfigPath  { get; set; } = "Encryption:FileKey";
        public string HmacKeyConfigPath  { get; set; } = "Encryption:HmacKey";

        // ── Key-version header ───────────────────────────────────────────────
        /// <summary>
        /// Monotonic version tag. Bump when you rotate keys so old ciphertexts
        /// can still be decrypted by the previous key during a migration window.
        /// Stored as a prefix in EncryptedValue: "v1:<base64>".
        /// </summary>
        public int CurrentKeyVersion { get; set; } = 1;
    }
}
