using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Klonker.Core.Diagnostics;

namespace Klonker.Core.Registry;

public static partial class RegistrySignatureVerifier
{
    public const int SupportedSchemaVersion = 0;
    public const string RsaPkcs1Sha256 = "rsa-pkcs1-sha256";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static OperationResult<VerifiedRegistrySignature> Verify(
        ReadOnlySpan<byte> indexBytes,
        string signatureJson,
        RegistryTrustPolicy trustPolicy,
        string sourceDescription)
    {
        ArgumentNullException.ThrowIfNull(signatureJson);
        ArgumentNullException.ThrowIfNull(trustPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);

        SignatureDto? signature;
        try
        {
            signature = JsonSerializer.Deserialize<SignatureDto>(
                signatureJson,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            return Failure(
                "registry.signature_json_invalid",
                $"The registry signature is invalid JSON: {exception.Message}",
                sourceDescription);
        }

        if (signature is null)
        {
            return Failure(
                "registry.signature_empty",
                "The registry signature file is empty.",
                sourceDescription);
        }

        if (signature.SchemaVersion != SupportedSchemaVersion)
        {
            return Failure(
                "registry.signature_schema_unsupported",
                $"Registry signature schema version {signature.SchemaVersion} is not supported.",
                sourceDescription);
        }

        if (!string.Equals(
                signature.PublisherId,
                trustPolicy.PublisherId,
                StringComparison.Ordinal))
        {
            return Failure(
                "registry.publisher_untrusted",
                $"The registry claims publisher '{signature.PublisherId ?? "(missing)"}', " +
                $"but source trust is pinned to '{trustPolicy.PublisherId}'.",
                sourceDescription);
        }

        if (string.IsNullOrWhiteSpace(signature.KeyId))
        {
            return Failure(
                "registry.signature_key_missing",
                "The registry signature does not identify a publisher key.",
                sourceDescription);
        }

        var keys = trustPolicy.Keys
            .Where(key => string.Equals(
                key.KeyId,
                signature.KeyId,
                StringComparison.Ordinal))
            .ToArray();
        if (keys.Length != 1)
        {
            return Failure(
                "registry.publisher_key_untrusted",
                $"Publisher key '{signature.KeyId}' is not uniquely trusted for " +
                $"'{trustPolicy.PublisherId}'.",
                sourceDescription);
        }

        var key = keys[0];
        if (key.Revoked)
        {
            return Failure(
                "registry.publisher_key_revoked",
                $"Publisher key '{key.KeyId}' has been revoked locally.",
                sourceDescription);
        }

        if (!string.Equals(
                signature.Algorithm,
                RsaPkcs1Sha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                key.Algorithm,
                RsaPkcs1Sha256,
                StringComparison.Ordinal))
        {
            return Failure(
                "registry.signature_algorithm_unsupported",
                $"Registry signature algorithm '{signature.Algorithm ?? "(missing)"}' is not supported.",
                sourceDescription);
        }

        var actualHash = SHA256.HashData(indexBytes);
        if (string.IsNullOrWhiteSpace(signature.IndexSha256) ||
            !Sha256Regex().IsMatch(signature.IndexSha256))
        {
            return Failure(
                "registry.signature_hash_invalid",
                "The registry signature must declare a 64-character index SHA-256.",
                sourceDescription);
        }

        byte[] declaredHash;
        try
        {
            declaredHash = Convert.FromHexString(signature.IndexSha256);
        }
        catch (FormatException)
        {
            return Failure(
                "registry.signature_hash_invalid",
                "The registry signature index SHA-256 is malformed.",
                sourceDescription);
        }

        if (!CryptographicOperations.FixedTimeEquals(actualHash, declaredHash))
        {
            return Failure(
                "registry.signature_hash_mismatch",
                "The registry index bytes do not match the signed SHA-256.",
                sourceDescription);
        }

        byte[] publicKey;
        byte[] signatureBytes;
        try
        {
            publicKey = Convert.FromBase64String(key.PublicKeySpki);
            signatureBytes = Convert.FromBase64String(signature.Signature ?? string.Empty);
        }
        catch (FormatException)
        {
            return Failure(
                "registry.signature_encoding_invalid",
                "The publisher key or signature is not valid Base64.",
                sourceDescription);
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length || rsa.KeySize < 2048)
            {
                return Failure(
                    "registry.publisher_key_invalid",
                    "Trusted RSA publisher keys must be at least 2048 bits and use SPKI encoding.",
                    sourceDescription);
            }

            if (!rsa.VerifyHash(
                    actualHash,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1))
            {
                return Failure(
                    "registry.signature_invalid",
                    "The registry signature does not verify with the trusted publisher key.",
                    sourceDescription);
            }
        }
        catch (CryptographicException)
        {
            return Failure(
                "registry.publisher_key_invalid",
                "The trusted publisher key is not a valid RSA SPKI key.",
                sourceDescription);
        }

        return new OperationResult<VerifiedRegistrySignature>(
            new VerifiedRegistrySignature(
                signature.PublisherId!,
                signature.KeyId,
                signature.Algorithm!,
                Convert.ToHexStringLower(actualHash)),
            [
                new ValidationIssue(
                    ValidationSeverity.Information,
                    "registry.signature_verified",
                    $"Verified registry publisher '{signature.PublisherId}' " +
                    $"with trusted key '{signature.KeyId}'.",
                    Path: sourceDescription),
            ]);
    }

    private static OperationResult<VerifiedRegistrySignature> Failure(
        string code,
        string message,
        string path) =>
        new(
            null,
            [new ValidationIssue(ValidationSeverity.Error, code, message, Path: path)]);

    [GeneratedRegex(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    private sealed class SignatureDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("publisher_id")]
        public string? PublisherId { get; init; }

        [JsonPropertyName("key_id")]
        public string? KeyId { get; init; }

        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; init; }

        [JsonPropertyName("index_sha256")]
        public string? IndexSha256 { get; init; }

        [JsonPropertyName("signature")]
        public string? Signature { get; init; }
    }
}
