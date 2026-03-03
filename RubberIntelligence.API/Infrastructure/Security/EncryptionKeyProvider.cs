using System.Text;
using Microsoft.Extensions.Options;

namespace RubberIntelligence.API.Infrastructure.Security
{
    /// <summary>
    /// Central key-resolution service for all AES / HMAC keys used in the DPP pipeline.
    ///
    /// Resolution order (first non-null win):
    ///   1. OS environment variable (injected by your deploy pipeline / Key Vault reference)
    ///   2. IConfiguration value (appsettings section — suitable for dev overrides only)
    ///   3. Compile-time fallback — ONLY permitted when ASPNETCORE_ENVIRONMENT == "Development".
    ///      In any other environment the service throws at startup, forcing an explicit secret.
    ///
    /// Key-version tagging:
    ///   Every encrypted value is prefixed "v{version}:" so that a future key-rotation can
    ///   still decrypt old ciphertexts during the migration window.
    /// </summary>
    public sealed class EncryptionKeyProvider
    {
        // ── Internal fallback dev keys (intentionally weak and public — dev ONLY) ────
        private const string DevFieldKey = "DevFieldEncryptKey256Bit!!!!!!!!"; // 32 chars
        private const string DevFileKey  = "DevFileEncryptKey256Bit!!!!!!!!!"; // 32 chars
        private const string DevHmacKey  = "DevHmacBlindIndexKey256Bit!!!!!!"; // 32 chars

        private readonly byte[] _fieldKey;
        private readonly byte[] _fileKey;
        private readonly byte[] _hmacKey;
        private readonly int    _keyVersion;
        private readonly ILogger<EncryptionKeyProvider> _logger;

        public int CurrentKeyVersion => _keyVersion;

        public EncryptionKeyProvider(
            IConfiguration config,
            IWebHostEnvironment env,
            IOptions<EncryptionKeyOptions> opts,
            ILogger<EncryptionKeyProvider> logger)
        {
            _logger     = logger;
            _keyVersion = opts.Value.CurrentKeyVersion;

            bool isDev = env.IsDevelopment();

            _fieldKey = ResolveKey(
                opts.Value.FieldKeyEnvVar,
                opts.Value.FieldKeyConfigPath,
                isDev ? DevFieldKey : null,
                config, isDev, env.EnvironmentName,
                "Field AES-256");

            _fileKey = ResolveKey(
                opts.Value.FileKeyEnvVar,
                opts.Value.FileKeyConfigPath,
                isDev ? DevFileKey : null,
                config, isDev, env.EnvironmentName,
                "File AES-256");

            _hmacKey = ResolveKey(
                opts.Value.HmacKeyEnvVar,
                opts.Value.HmacKeyConfigPath,
                isDev ? DevHmacKey : null,
                config, isDev, env.EnvironmentName,
                "HMAC Blind-Index");
        }

        /// <summary>Returns the AES-256 key for per-field encryption (32 bytes).</summary>
        public byte[] GetFieldAesKey()  => _fieldKey;

        /// <summary>Returns the AES-256 key for file-level encryption (32 bytes).</summary>
        public byte[] GetFileAesKey()   => _fileKey;

        /// <summary>Returns the HMAC key for blind-index generation (≥ 32 bytes).</summary>
        public byte[] GetHmacKey()      => _hmacKey;

        // ─────────────────────────────────────────────────────────────────────
        private byte[] ResolveKey(
            string envVar, string configPath,
            string? devFallback,
            IConfiguration config,
            bool isDev, string envName,
            string keyLabel)
        {
            // 1. Environment variable (highest priority — production path)
            var raw = Environment.GetEnvironmentVariable(envVar);

            if (string.IsNullOrWhiteSpace(raw))
            {
                // 2. IConfiguration (dev override in appsettings.Development.json etc.)
                raw = config[configPath];
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (!isDev || devFallback == null)
                {
                    // Production with no key → hard fail at startup (safe by design)
                    throw new InvalidOperationException(
                        $"[Security] {keyLabel} key not found. " +
                        $"Set '{envVar}' environment variable or configure '{configPath}' in appsettings. " +
                        $"Environment: {envName}. " +
                        $"In production, use Azure Key Vault or a secrets manager — " +
                        $"never store keys in source-controlled files.");
                }

                // Development fallback — warn loudly
                _logger.LogWarning(
                    "[Security][DEV ONLY] {KeyLabel} key falling back to hardcoded dev key. " +
                    "Set env var '{EnvVar}' before deploying to any non-development environment.",
                    keyLabel, envVar);

                raw = devFallback;
            }

            var keyBytes = Encoding.UTF8.GetBytes(raw);

            if (keyBytes.Length < 32)
                throw new InvalidOperationException(
                    $"[Security] {keyLabel} key is too short ({keyBytes.Length} bytes). " +
                    $"Minimum is 32 bytes (256 bits). Env var: '{envVar}'.");

            // Exactly 32 bytes for AES-256 — truncate or pad would silently weaken the key,
            // so we enforce the exact length contract instead.
            if (keyLabel.Contains("AES") && keyBytes.Length != 32)
                throw new InvalidOperationException(
                    $"[Security] {keyLabel} key must be exactly 32 characters for AES-256. " +
                    $"Got {keyBytes.Length} bytes. Env var: '{envVar}'.");

            return keyBytes;
        }
    }
}
