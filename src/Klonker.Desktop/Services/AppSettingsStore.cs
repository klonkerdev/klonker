using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Klonker.Core.Diagnostics;
using Klonker.Core.Registry;

namespace Klonker.Desktop.Services;

public sealed class AppSettingsStore
{
    public const int SupportedSchemaVersion = 0;
    public const int DefaultRegistryDownloadTimeoutSeconds = 20;

    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Lock syncRoot = new();
    private readonly string applicationDataRoot;

    public AppSettingsStore(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        this.applicationDataRoot = Path.GetFullPath(applicationDataRoot);
    }

    public static AppSettingsStore CreateDefault() =>
        new(FavoriteStore.GetDefaultApplicationDataRoot());

    public string ApplicationDataRoot => applicationDataRoot;

    public string StoragePath => Path.Combine(applicationDataRoot, "settings.json");

    public OperationResult<AppSettingsSnapshot> Load()
    {
        lock (syncRoot)
        {
            try
            {
                Directory.CreateDirectory(applicationDataRoot);
                if (!File.Exists(StoragePath))
                {
                    return SaveCore(CreateDefaultSnapshot());
                }

                var dto = JsonSerializer.Deserialize<SettingsDto>(
                    File.ReadAllText(StoragePath),
                    ReadOptions);
                if (dto is null)
                {
                    return Failure(
                        "settings.empty",
                        "The local application settings file is empty.");
                }

                var issues = new List<ValidationIssue>();
                if (dto.SchemaVersion != SupportedSchemaVersion)
                {
                    issues.Add(Error(
                        "settings.schema_unsupported",
                        $"Application settings schema version {dto.SchemaVersion} is not supported."));
                }

                if (!Enum.TryParse<AppAppearance>(
                        dto.Appearance,
                        ignoreCase: false,
                        out var appearance))
                {
                    issues.Add(Error(
                        "settings.appearance_invalid",
                        $"Appearance '{dto.Appearance}' is not supported."));
                }

                if (!Enum.TryParse<DiagnosticLogLevel>(
                        dto.DiagnosticLogLevel,
                        ignoreCase: false,
                        out var logLevel))
                {
                    issues.Add(Error(
                        "settings.log_level_invalid",
                        $"Diagnostic log level '{dto.DiagnosticLogLevel}' is not supported."));
                }

                if (dto.RegistryDownloadTimeoutSeconds is < 5 or > 120)
                {
                    issues.Add(Error(
                        "settings.registry_timeout_invalid",
                        "Registry download timeout must be between 5 and 120 seconds."));
                }

                if (!Enum.TryParse<RegistryVersionPreference>(
                        dto.RegistryVersionPreference,
                        ignoreCase: false,
                        out var versionPreference))
                {
                    issues.Add(Error(
                        "settings.registry_version_preference_invalid",
                        "The registry version preference is not supported."));
                }

                if (!Enum.TryParse<RegistryDuplicateSourcePolicy>(
                        dto.RegistryDuplicateSourcePolicy,
                        ignoreCase: false,
                        out var duplicateSourcePolicy))
                {
                    issues.Add(Error(
                        "settings.registry_duplicate_policy_invalid",
                        "The duplicate registry source policy is not supported."));
                }

                if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
                {
                    return new OperationResult<AppSettingsSnapshot>(null, issues);
                }

                return new OperationResult<AppSettingsSnapshot>(
                    new AppSettingsSnapshot(
                        StoragePath,
                        appearance,
                        dto.DiagnosticLoggingEnabled,
                        logLevel,
                        dto.PrerequisiteProbesEnabled,
                        dto.RegistryDownloadTimeoutSeconds,
                        versionPreference,
                        (dto.RegistryVersionPins ??
                         new Dictionary<string, string>())
                            .Where(pair =>
                                !string.IsNullOrWhiteSpace(pair.Key) &&
                                !string.IsNullOrWhiteSpace(pair.Value))
                            .ToImmutableDictionary(
                                pair => pair.Key,
                                pair => pair.Value,
                                StringComparer.Ordinal),
                        duplicateSourcePolicy),
                    issues);
            }
            catch (JsonException exception)
            {
                return Failure(
                    "settings.json_invalid",
                    $"The local application settings are invalid JSON: {exception.Message}");
            }
            catch (Exception exception) when (
                exception is IOException or
                    UnauthorizedAccessException or
                    ArgumentException)
            {
                return Failure(
                    "settings.read_failed",
                    $"The local application settings could not be read: {exception.Message}");
            }
        }
    }

    public OperationResult<AppSettingsSnapshot> Save(
        AppSettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (syncRoot)
        {
            if (settings.RegistryDownloadTimeoutSeconds is < 5 or > 120)
            {
                return Failure(
                    "settings.registry_timeout_invalid",
                    "Registry download timeout must be between 5 and 120 seconds.");
            }

            return SaveCore(settings with { StoragePath = StoragePath });
        }
    }

    public OperationResult<AppSettingsSnapshot> Reset()
    {
        lock (syncRoot)
        {
            return SaveCore(CreateDefaultSnapshot());
        }
    }

    private AppSettingsSnapshot CreateDefaultSnapshot() =>
        new(
            StoragePath,
            AppAppearance.System,
            DiagnosticLoggingEnabled: false,
            DiagnosticLogLevel.Information,
            PrerequisiteProbesEnabled: false,
            DefaultRegistryDownloadTimeoutSeconds,
            RegistryVersionPreference.LatestStable,
            RegistryVersionPins: null,
            RegistryDuplicateSourcePolicy.PreferFirstConfiguredSource);

    private OperationResult<AppSettingsSnapshot> SaveCore(
        AppSettingsSnapshot settings)
    {
        var dto = new SettingsDto
        {
            SchemaVersion = SupportedSchemaVersion,
            Appearance = settings.Appearance.ToString(),
            DiagnosticLoggingEnabled = settings.DiagnosticLoggingEnabled,
            DiagnosticLogLevel = settings.DiagnosticLogLevel.ToString(),
            PrerequisiteProbesEnabled = settings.PrerequisiteProbesEnabled,
            RegistryDownloadTimeoutSeconds =
                settings.RegistryDownloadTimeoutSeconds,
            RegistryVersionPreference =
                settings.RegistryVersionPreference.ToString(),
            RegistryVersionPins = settings.RegistryVersionPins?
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal),
            RegistryDuplicateSourcePolicy =
                settings.RegistryDuplicateSourcePolicy.ToString(),
        };

        try
        {
            Directory.CreateDirectory(applicationDataRoot);
            WriteAtomically(
                StoragePath,
                JsonSerializer.Serialize(dto, WriteOptions) + Environment.NewLine);
            return new OperationResult<AppSettingsSnapshot>(
                settings with { StoragePath = StoragePath },
                []);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
        {
            return Failure(
                "settings.write_failed",
                $"The local application settings could not be saved: {exception.Message}");
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, Utf8);
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

    private OperationResult<AppSettingsSnapshot> Failure(
        string code,
        string message) =>
        new(null, [Error(code, message)]);

    private ValidationIssue Error(string code, string message) =>
        new(ValidationSeverity.Error, code, message, Path: StoragePath);

    private sealed class SettingsDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("appearance")]
        public string? Appearance { get; set; }

        [JsonPropertyName("diagnostic_logging_enabled")]
        public bool DiagnosticLoggingEnabled { get; set; }

        [JsonPropertyName("diagnostic_log_level")]
        public string? DiagnosticLogLevel { get; set; }

        [JsonPropertyName("prerequisite_probes_enabled")]
        public bool PrerequisiteProbesEnabled { get; set; }

        [JsonPropertyName("registry_download_timeout_seconds")]
        public int RegistryDownloadTimeoutSeconds { get; set; } =
            DefaultRegistryDownloadTimeoutSeconds;

        [JsonPropertyName("registry_version_preference")]
        public string? RegistryVersionPreference { get; set; } =
            Klonker.Core.Registry.RegistryVersionPreference
                .LatestStable
                .ToString();

        [JsonPropertyName("registry_version_pins")]
        public Dictionary<string, string>? RegistryVersionPins { get; set; } =
            new(StringComparer.Ordinal);

        [JsonPropertyName("registry_duplicate_source_policy")]
        public string? RegistryDuplicateSourcePolicy { get; set; } =
            Klonker.Core.Registry.RegistryDuplicateSourcePolicy
                .PreferFirstConfiguredSource
                .ToString();
    }
}
