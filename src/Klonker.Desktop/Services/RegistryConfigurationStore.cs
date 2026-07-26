using System.Collections.Immutable;
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

    public RegistryConfigurationStore(
        string applicationDataRoot,
        string? developmentRegistryPath = null,
        string? officialRegistryUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        this.applicationDataRoot = Path.GetFullPath(applicationDataRoot);
        this.developmentRegistryPath = developmentRegistryPath;
        this.officialRegistryUrl = officialRegistryUrl;
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
                : configuredOfficialRegistryUrl);
    }

    public OperationResult<RegistryConfigurationSnapshot> Load()
    {
        var configurationPath = Path.Combine(applicationDataRoot, "registries.json");
        var cacheRoot = Path.Combine(applicationDataRoot, "cache");
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

            var issues = new List<ValidationIssue>();
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
                    var location = kind == RegistrySourceKind.Local
                        ? Path.GetFullPath(
                            source.Location,
                            Path.GetDirectoryName(configurationPath)!)
                        : source.Location;
                    sources.Add(new RegistrySource(
                        source.Name,
                        kind.Value,
                        location,
                        source.Enabled));
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
                    cacheRoot,
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

    private void WriteInitialConfiguration(string configurationPath)
    {
        var sources = new List<SourceDto>();
        if (!string.IsNullOrWhiteSpace(developmentRegistryPath))
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
        public List<SourceDto> Sources { get; set; } = [];
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
    }
}
