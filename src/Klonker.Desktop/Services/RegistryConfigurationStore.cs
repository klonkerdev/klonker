using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Klonker.Core.Diagnostics;
using Klonker.Core.Registry;

namespace Klonker.Desktop.Services;

public sealed class RegistryConfigurationStore
{
    public const int SupportedSchemaVersion = 0;
    public const string OfficialRegistryUrl =
        "https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json";
    public const string OfficialRegistryEnvironmentVariable =
        "KLONKER_OFFICIAL_REGISTRY_URL";
    public const string OfficialPublisherId = "klonker.official";
    public const string OfficialSigningKeyId = "2026-primary";
    public const string OfficialPublicKeySpki =
        "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA0kmjbeOoLw9Q1ZdbOxhJ9SG8fMkGrDUv1lBuJBPvjGqUFgE458CFI1YUnu4bmuehAswaNpapnWebGwTpHXmX9+v+xwjhSprymWfwJ4hL4cj3kz/3vllOoOb3Y16KMtFzr22xP1PGu5Sy/ZX6WMfcM+r3f3ugqQuMerCNZ5XHdZ0D0BEQhWN0k4MeO7VbFEFKi+QMcLlNXlioO1QcEqPAFHFExLr/Q65StQyMIQV9LSE95+bshCV0OtfJtbE966Gu8ggTPzVOxHvG045tXA9zxiI6grgL7n4NArDbcZt4CS7ARqK+VgI/qeLoOjObGKJGheEzRgo7N438SejyXwCTd6x/1ep6E3J2/tYL5aMqyJFQmsy61KSsx9qvESNlDxm7sfkE8eHi5TVXRv9ecsex634lb9CZkZ2rQwAvZD2rCT9xF91RyC4v1lFB4jCnpAs4Ommg9kj/Q/R4zzGiNq/NRIPtUfrZgNHgR8cGYBxxxzHVnsq24OuIHJhzhaaJ7Q3lAgMBAAE=";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string applicationDataRoot;
    private readonly string? developmentRegistryPath;
    private readonly string? officialRegistryUrl;
    private readonly bool seedDevelopmentRegistry;

    public string ApplicationDataRoot => applicationDataRoot;

    public string ConfigurationPath =>
        Path.Combine(applicationDataRoot, "registries.json");

    public string CacheRoot => Path.Combine(applicationDataRoot, "cache");

    public RegistryConfigurationStore(
        string applicationDataRoot,
        string? developmentRegistryPath = null,
        string? officialRegistryUrl = null,
        bool seedDevelopmentRegistry = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        this.applicationDataRoot = Path.GetFullPath(applicationDataRoot);
        this.developmentRegistryPath = developmentRegistryPath;
        this.officialRegistryUrl = officialRegistryUrl;
        this.seedDevelopmentRegistry = seedDevelopmentRegistry;
    }

    public static RegistryConfigurationStore CreateDefault()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var applicationDataRoot = Path.Combine(localApplicationData, "Klonker");
        var configuredOfficialRegistryUrl = Environment.GetEnvironmentVariable(
            OfficialRegistryEnvironmentVariable);
        return new RegistryConfigurationStore(
            applicationDataRoot,
            DevelopmentSampleRegistryLocator.FindRegistryIndex(),
            string.IsNullOrWhiteSpace(configuredOfficialRegistryUrl)
                ? OfficialRegistryUrl
                : configuredOfficialRegistryUrl,
            seedDevelopmentRegistry: false);
    }

    public OperationResult<RegistryConfigurationSnapshot> Load()
    {
        var configurationPath = ConfigurationPath;
        try
        {
            Directory.CreateDirectory(applicationDataRoot);
            if (!File.Exists(configurationPath))
            {
                WriteInitialConfiguration(configurationPath);
            }

            var dto = JsonSerializer.Deserialize<ConfigurationDto>(
                File.ReadAllText(configurationPath),
                ReadOptions);
            if (dto is null)
            {
                return Failure(
                    "registry.configuration_empty",
                    "The registry configuration file is empty.",
                    configurationPath);
            }

            if (dto.Sources is null)
            {
                return Failure(
                    "registry.configuration_sources_required",
                    "Registry configuration property 'sources' must be an array.",
                    configurationPath);
            }

            var issues = new List<ValidationIssue>();
            if (RemoveLegacyDevelopmentSource(
                    dto,
                    configurationPath))
            {
                WriteAtomically(
                    configurationPath,
                    JsonSerializer.Serialize(dto, WriteOptions) +
                    Environment.NewLine);
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Information,
                    "registry.legacy_development_source_removed",
                    "The old automatically added development-samples registry " +
                    "was removed. Development registries can be added explicitly " +
                    "from Settings or the registry workspace wizard.",
                    Path: configurationPath));
            }

            if (dto.SchemaVersion != SupportedSchemaVersion)
            {
                issues.Add(Error(
                    "registry.configuration_schema_unsupported",
                    $"Registry configuration schema version {dto.SchemaVersion} is not supported.",
                    configurationPath));
            }

            var sources = ImmutableArray.CreateBuilder<RegistrySource>();
            for (var index = 0; index < dto.Sources.Count; index++)
            {
                var source = dto.Sources[index];
                var context = $"sources[{index}]";
                if (string.IsNullOrWhiteSpace(source.Name))
                {
                    issues.Add(Error(
                        "registry.configuration_name_required",
                        $"Registry configuration property '{context}.name' is required.",
                        configurationPath));
                }

                if (string.IsNullOrWhiteSpace(source.Location))
                {
                    issues.Add(Error(
                        "registry.configuration_location_required",
                        $"Registry configuration property '{context}.location' is required.",
                        configurationPath));
                }

                RegistrySourceKind? kind = source.Kind switch
                {
                    "local" => RegistrySourceKind.Local,
                    "remote" => RegistrySourceKind.Remote,
                    _ => null,
                };
                if (kind is null)
                {
                    issues.Add(Error(
                        "registry.configuration_kind_invalid",
                        $"Registry configuration property '{context}.kind' must be 'local' or 'remote'.",
                        configurationPath));
                }

                if (kind is not null &&
                    !string.IsNullOrWhiteSpace(source.Name) &&
                    !string.IsNullOrWhiteSpace(source.Location))
                {
                    if (kind == RegistrySourceKind.Remote &&
                        (!Uri.TryCreate(
                            source.Location,
                            UriKind.Absolute,
                            out var remoteUri) ||
                         !remoteUri.Scheme.Equals(
                             Uri.UriSchemeHttps,
                             StringComparison.OrdinalIgnoreCase)))
                    {
                        issues.Add(Error(
                            "registry.configuration_remote_url_invalid",
                            $"Registry configuration property '{context}.location' " +
                            "must be an absolute HTTPS URL for a remote source.",
                            configurationPath));
                    }

                    var location = kind == RegistrySourceKind.Local
                        ? Path.GetFullPath(
                            source.Location,
                            Path.GetDirectoryName(configurationPath)!)
                        : source.Location;
                    var trustPolicy = ParseTrustPolicy(
                        source,
                        kind.Value,
                        context,
                        configurationPath,
                        issues);
                    sources.Add(new RegistrySource(
                        source.Name,
                        kind.Value,
                        location,
                        source.Enabled,
                        trustPolicy));
                }
            }

            if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
            {
                return new OperationResult<RegistryConfigurationSnapshot>(
                    null,
                    issues);
            }

            return new OperationResult<RegistryConfigurationSnapshot>(
                new RegistryConfigurationSnapshot(
                    configurationPath,
                    CacheRoot,
                    dto.Offline,
                    sources.ToImmutable()),
                issues);
        }
        catch (JsonException exception)
        {
            return Failure(
                "registry.configuration_json_invalid",
                $"The registry configuration is invalid JSON: {exception.Message}",
                configurationPath);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
        {
            return Failure(
                "registry.configuration_read_failed",
                $"The registry configuration could not be read: {exception.Message}",
                configurationPath);
        }
    }

    public OperationResult<RegistryConfigurationSnapshot> Save(
        bool offline,
        IEnumerable<RegistrySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var sourceArray = sources.ToArray();
        var validationIssues = ValidateSourcesForSave(sourceArray);
        if (validationIssues.Count > 0)
        {
            return new OperationResult<RegistryConfigurationSnapshot>(
                null,
                validationIssues);
        }

        var dto = new ConfigurationDto
        {
            SchemaVersion = SupportedSchemaVersion,
            Offline = offline,
            Sources = sourceArray
                .Select(source => new SourceDto
                {
                    Name = source.Name,
                    Kind = source.Kind switch
                    {
                        RegistrySourceKind.Local => "local",
                        RegistrySourceKind.Remote => "remote",
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(sources),
                            $"Unsupported registry source kind '{source.Kind}'."),
                    },
                    Location = source.Location,
                    Enabled = source.Enabled,
                    RequireSignature = source.TrustPolicy?.RequireSignature ?? false,
                    PublisherId = source.TrustPolicy?.PublisherId,
                    TrustedKeys = source.TrustPolicy?.Keys
                        .OrderBy(key => key.KeyId, StringComparer.Ordinal)
                        .Select(key => new TrustedKeyDto
                        {
                            KeyId = key.KeyId,
                            Algorithm = key.Algorithm,
                            PublicKeySpki = key.PublicKeySpki,
                            Revoked = key.Revoked,
                        })
                        .ToList() ?? [],
                })
                .ToList(),
        };

        try
        {
            Directory.CreateDirectory(applicationDataRoot);
            WriteAtomically(
                ConfigurationPath,
                JsonSerializer.Serialize(dto, WriteOptions) + Environment.NewLine);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
        {
            return Failure(
                "registry.configuration_write_failed",
                $"The registry configuration could not be saved: {exception.Message}",
                ConfigurationPath);
        }

        return Load();
    }

    private List<ValidationIssue> ValidateSourcesForSave(
        RegistrySource[] sources)
    {
        var issues = new List<ValidationIssue>();
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
            var context = $"sources[{index}]";
            if (string.IsNullOrWhiteSpace(source.Name))
            {
                issues.Add(Error(
                    "registry.configuration_name_required",
                    $"Registry configuration property '{context}.name' is required.",
                    ConfigurationPath));
            }

            if (string.IsNullOrWhiteSpace(source.Location))
            {
                issues.Add(Error(
                    "registry.configuration_location_required",
                    $"Registry configuration property '{context}.location' is required.",
                    ConfigurationPath));
            }
            else if (!locations.Add(source.Location))
            {
                issues.Add(Error(
                    "registry.configuration_location_duplicate",
                    $"Registry location '{source.Location}' is configured more than once.",
                    ConfigurationPath));
            }

            if (source.Kind == RegistrySourceKind.Remote &&
                (!Uri.TryCreate(
                    source.Location,
                    UriKind.Absolute,
                    out var remoteUri) ||
                 !remoteUri.Scheme.Equals(
                     Uri.UriSchemeHttps,
                     StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Error(
                    "registry.configuration_remote_url_invalid",
                    $"Registry configuration property '{context}.location' must " +
                    "be an absolute HTTPS URL for a remote source.",
                    ConfigurationPath));
            }

            if (source.Kind == RegistrySourceKind.Local &&
                source.TrustPolicy is not null)
            {
                issues.Add(Error(
                    "registry.configuration_trust_remote_only",
                    $"Registry configuration property '{context}.trust' is " +
                    "supported only for remote sources.",
                    ConfigurationPath));
            }

            if (source.TrustPolicy is not null)
            {
                ValidateTrustPolicyForSave(
                    source,
                    source.TrustPolicy,
                    context,
                    issues);
            }
        }

        return issues;
    }

    private void ValidateTrustPolicyForSave(
        RegistrySource source,
        RegistryTrustPolicy trustPolicy,
        string context,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(trustPolicy.PublisherId))
        {
            issues.Add(Error(
                "registry.configuration_publisher_required",
                $"Registry configuration property '{context}.publisher_id' is required.",
                ConfigurationPath));
        }

        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in trustPolicy.Keys)
        {
            if (string.IsNullOrWhiteSpace(key.KeyId) || !keyIds.Add(key.KeyId))
            {
                issues.Add(Error(
                    "registry.configuration_key_id_invalid",
                    $"Registry source '{source.Name}' has a missing or duplicate key ID.",
                    ConfigurationPath));
            }

            if (!string.Equals(
                    key.Algorithm,
                    RegistrySignatureVerifier.RsaPkcs1Sha256,
                    StringComparison.Ordinal))
            {
                issues.Add(Error(
                    "registry.configuration_key_algorithm_invalid",
                    $"Registry key '{key.KeyId}' must use " +
                    $"'{RegistrySignatureVerifier.RsaPkcs1Sha256}'.",
                    ConfigurationPath));
            }

            if (!IsValidPublicKey(key.PublicKeySpki))
            {
                issues.Add(Error(
                    "registry.configuration_public_key_invalid",
                    $"Registry key '{key.KeyId}' must contain a Base64 RSA SPKI " +
                    "key of at least 2048 bits.",
                    ConfigurationPath));
            }
        }

        if (trustPolicy.RequireSignature &&
            !trustPolicy.Keys.Any(key => !key.Revoked))
        {
            issues.Add(Error(
                "registry.configuration_active_key_required",
                $"Registry source '{source.Name}' requires an active trusted key.",
                ConfigurationPath));
        }
    }

    public OperationResult<RegistryConfigurationSnapshot> Reset()
    {
        try
        {
            if (File.Exists(ConfigurationPath))
            {
                File.Delete(ConfigurationPath);
            }

            return Load();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "registry.configuration_reset_failed",
                $"The registry configuration could not be reset: {exception.Message}",
                ConfigurationPath);
        }
    }

    private void WriteInitialConfiguration(string configurationPath)
    {
        var sources = new List<SourceDto>();
        if (seedDevelopmentRegistry &&
            !string.IsNullOrWhiteSpace(developmentRegistryPath))
        {
            sources.Add(new SourceDto
            {
                Name = "Development samples",
                Kind = "local",
                Location = Path.GetFullPath(developmentRegistryPath),
                Enabled = true,
            });
        }

        if (!string.IsNullOrWhiteSpace(officialRegistryUrl))
        {
            sources.Add(new SourceDto
            {
                Name = "Klonker official templates",
                Kind = "remote",
                Location = officialRegistryUrl,
                Enabled = true,
                RequireSignature = true,
                PublisherId = OfficialPublisherId,
                TrustedKeys =
                [
                    new TrustedKeyDto
                    {
                        KeyId = OfficialSigningKeyId,
                        Algorithm = RegistrySignatureVerifier.RsaPkcs1Sha256,
                        PublicKeySpki = OfficialPublicKeySpki,
                    },
                ],
            });
        }

        var dto = new ConfigurationDto
        {
            SchemaVersion = SupportedSchemaVersion,
            Offline = false,
            Sources = sources,
        };
        File.WriteAllText(
            configurationPath,
            JsonSerializer.Serialize(dto, WriteOptions) + Environment.NewLine);
    }

    private bool RemoveLegacyDevelopmentSource(
        ConfigurationDto configuration,
        string configurationPath)
    {
        if (seedDevelopmentRegistry ||
            string.IsNullOrWhiteSpace(developmentRegistryPath) ||
            configuration.Sources is null ||
            !configuration.Sources.Any(source =>
                string.Equals(source.Kind, "remote", StringComparison.Ordinal) &&
                (string.Equals(
                     source.PublisherId,
                     OfficialPublisherId,
                     StringComparison.Ordinal) ||
                 string.Equals(
                     source.Location,
                     officialRegistryUrl,
                     StringComparison.Ordinal))))
        {
            return false;
        }

        var expectedPath = Path.GetFullPath(developmentRegistryPath);
        var configurationDirectory =
            Path.GetDirectoryName(configurationPath)!;
        return configuration.Sources.RemoveAll(source =>
        {
            if (!string.Equals(
                    source.Name,
                    "Development samples",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    source.Kind,
                    "local",
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(source.Location))
            {
                return false;
            }

            try
            {
                var sourcePath = Path.GetFullPath(
                    source.Location,
                    configurationDirectory);
                return string.Equals(
                    sourcePath,
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
            {
                return false;
            }
        }) > 0;
    }

    private static RegistryTrustPolicy? ParseTrustPolicy(
        SourceDto source,
        RegistrySourceKind kind,
        string context,
        string configurationPath,
        List<ValidationIssue> issues)
    {
        var hasTrustConfiguration =
            source.RequireSignature ||
            !string.IsNullOrWhiteSpace(source.PublisherId) ||
            (source.TrustedKeys?.Count ?? 0) > 0;
        if (!hasTrustConfiguration)
        {
            return null;
        }

        if (kind != RegistrySourceKind.Remote)
        {
            issues.Add(Error(
                "registry.configuration_trust_remote_only",
                $"Registry configuration property '{context}.require_signature' " +
                "is supported only for remote sources.",
                configurationPath));
        }

        if (string.IsNullOrWhiteSpace(source.PublisherId))
        {
            issues.Add(Error(
                "registry.configuration_publisher_required",
                $"Registry configuration property '{context}.publisher_id' is " +
                "required when publisher trust is configured.",
                configurationPath));
        }

        var keys = ImmutableArray.CreateBuilder<RegistryTrustedKey>();
        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        var trustedKeys = source.TrustedKeys ?? [];
        for (var keyIndex = 0; keyIndex < trustedKeys.Count; keyIndex++)
        {
            var key = trustedKeys[keyIndex];
            var keyContext = $"{context}.trusted_keys[{keyIndex}]";
            if (string.IsNullOrWhiteSpace(key.KeyId) || !keyIds.Add(key.KeyId))
            {
                issues.Add(Error(
                    "registry.configuration_key_id_invalid",
                    $"Registry configuration property '{keyContext}.key_id' must " +
                    "be non-empty and unique.",
                    configurationPath));
            }

            if (!string.Equals(
                    key.Algorithm,
                    RegistrySignatureVerifier.RsaPkcs1Sha256,
                    StringComparison.Ordinal))
            {
                issues.Add(Error(
                    "registry.configuration_key_algorithm_invalid",
                    $"Registry configuration property '{keyContext}.algorithm' " +
                    $"must be '{RegistrySignatureVerifier.RsaPkcs1Sha256}'.",
                    configurationPath));
            }

            if (!IsValidPublicKey(key.PublicKeySpki))
            {
                issues.Add(Error(
                    "registry.configuration_public_key_invalid",
                    $"Registry configuration property '{keyContext}.public_key_spki' " +
                    "must be a Base64 RSA SPKI key of at least 2048 bits.",
                    configurationPath));
            }

            if (!string.IsNullOrWhiteSpace(key.KeyId) &&
                !string.IsNullOrWhiteSpace(key.Algorithm) &&
                !string.IsNullOrWhiteSpace(key.PublicKeySpki))
            {
                keys.Add(new RegistryTrustedKey(
                    key.KeyId,
                    key.Algorithm,
                    key.PublicKeySpki,
                    key.Revoked));
            }
        }

        if (source.RequireSignature &&
            !keys.Any(key => !key.Revoked))
        {
            issues.Add(Error(
                "registry.configuration_active_key_required",
                $"Registry source '{source.Name}' requires a signature but has no " +
                "active trusted publisher key.",
                configurationPath));
        }

        return string.IsNullOrWhiteSpace(source.PublisherId)
            ? null
            : new RegistryTrustPolicy(
                source.PublisherId,
                keys.ToImmutable(),
                source.RequireSignature);
    }

    private static bool IsValidPublicKey(string? encodedKey)
    {
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(encodedKey);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(bytes, out var bytesRead);
            return bytesRead == bytes.Length && rsa.KeySize >= 2048;
        }
        catch (Exception exception) when (
            exception is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static void WriteAtomically(string path, string contents)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                contents,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static OperationResult<RegistryConfigurationSnapshot> Failure(
        string code,
        string message,
        string path) =>
        new(null, [Error(code, message, path)]);

    private static ValidationIssue Error(string code, string message, string path) =>
        new(ValidationSeverity.Error, code, message, Path: path);

    private sealed class ConfigurationDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("offline")]
        public bool Offline { get; set; }

        [JsonPropertyName("sources")]
        public List<SourceDto>? Sources { get; set; } = [];
    }

    private sealed class SourceDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("require_signature")]
        public bool RequireSignature { get; set; }

        [JsonPropertyName("publisher_id")]
        public string? PublisherId { get; set; }

        [JsonPropertyName("trusted_keys")]
        public List<TrustedKeyDto>? TrustedKeys { get; set; } = [];
    }

    private sealed class TrustedKeyDto
    {
        [JsonPropertyName("key_id")]
        public string? KeyId { get; set; }

        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; set; }

        [JsonPropertyName("public_key_spki")]
        public string? PublicKeySpki { get; set; }

        [JsonPropertyName("revoked")]
        public bool Revoked { get; set; }
    }
}
