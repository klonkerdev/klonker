using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;

namespace Klonker.Core.Registry;

public static partial class RegistryIndexLoader
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static OperationResult<RegistryIndex> Parse(
        string json,
        string sourceDescription)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);

        RegistryDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RegistryDto>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            return Failure(
                "registry.json_invalid",
                $"The registry index is invalid JSON: {exception.Message}",
                sourceDescription);
        }

        if (dto is null)
        {
            return Failure(
                "registry.empty",
                "The registry index is empty.",
                sourceDescription);
        }

        var issues = new List<ValidationIssue>();
        if (dto.SchemaVersion != SupportedSchemaVersion)
        {
            issues.Add(Error(
                "registry.schema_unsupported",
                $"Registry schema version {dto.SchemaVersion} is not supported. " +
                $"Expected version {SupportedSchemaVersion}.",
                sourceDescription));
        }

        ValidateRequired(dto.RegistryId, "registry_id", sourceDescription, issues);
        ValidateRequired(dto.DisplayName, "display_name", sourceDescription, issues);

        var entries = ImmutableArray.CreateBuilder<RegistryTemplateEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < dto.Templates.Count; index++)
        {
            var item = dto.Templates[index];
            var context = $"templates[{index}]";
            ValidateRequired(item.FamilyId, $"{context}.family_id", sourceDescription, issues);
            ValidateRequired(item.VariantId, $"{context}.variant_id", sourceDescription, issues);
            ValidateRequired(item.TemplateId, $"{context}.template_id", sourceDescription, issues);
            ValidateRequired(item.Name, $"{context}.name", sourceDescription, issues);
            ValidateRequired(item.Description, $"{context}.description", sourceDescription, issues);
            ValidateRequired(item.Version, $"{context}.version", sourceDescription, issues);
            ValidateRequired(item.TargetOs, $"{context}.target_os", sourceDescription, issues);
            ValidateRequired(item.BuildSystem, $"{context}.build_system", sourceDescription, issues);
            ValidateRequired(item.PackagePath, $"{context}.package_path", sourceDescription, issues);
            ValidateRequired(
                item.LicenseSummary,
                $"{context}.license_summary",
                sourceDescription,
                issues);
            ValidateRequired(
                item.PackageSha256,
                $"{context}.package_sha256",
                sourceDescription,
                issues);

            if (!string.IsNullOrWhiteSpace(item.TemplateId) && !ids.Add(item.TemplateId))
            {
                issues.Add(Error(
                    "registry.template_duplicate",
                    $"Template ID '{item.TemplateId}' appears more than once in this registry.",
                    sourceDescription));
            }

            if (!string.IsNullOrWhiteSpace(item.PackagePath))
            {
                var packagePath = SafePath.NormalizeRelative(item.PackagePath);
                issues.AddRange(packagePath.Issues.Select(issue =>
                    issue with { Path = $"{context}.package_path" }));
            }

            if (!string.IsNullOrWhiteSpace(item.PackageSha256) &&
                !Sha256Regex().IsMatch(item.PackageSha256))
            {
                issues.Add(Error(
                    "registry.package_checksum_invalid",
                    $"Registry property '{context}.package_sha256' must be a 64-character SHA-256 value.",
                    sourceDescription));
            }

            if (item.PackageSizeBytes <= 0)
            {
                issues.Add(Error(
                    "registry.package_size_invalid",
                    $"Registry property '{context}.package_size_bytes' must be greater than zero.",
                    sourceDescription));
            }

            if (HasRequiredProperties(item) &&
                Sha256Regex().IsMatch(item.PackageSha256!) &&
                item.PackageSizeBytes > 0)
            {
                entries.Add(new RegistryTemplateEntry(
                    item.FamilyId!,
                    item.VariantId!,
                    item.TemplateId!,
                    item.Name!,
                    item.Description!,
                    item.Version!,
                    item.TargetOs!,
                    item.BuildSystem!,
                    item.PackagePath!.Replace('\\', '/'),
                    item.LicenseSummary!,
                    item.PackageSha256!.ToLowerInvariant(),
                    item.PackageSizeBytes));
            }
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<RegistryIndex>(null, issues);
        }

        return new OperationResult<RegistryIndex>(
            new RegistryIndex(
                dto.SchemaVersion,
                dto.RegistryId!,
                dto.DisplayName!,
                entries.ToImmutable()),
            issues);
    }

    private static bool HasRequiredProperties(RegistryTemplateDto item) =>
        !string.IsNullOrWhiteSpace(item.FamilyId) &&
        !string.IsNullOrWhiteSpace(item.VariantId) &&
        !string.IsNullOrWhiteSpace(item.TemplateId) &&
        !string.IsNullOrWhiteSpace(item.Name) &&
        !string.IsNullOrWhiteSpace(item.Description) &&
        !string.IsNullOrWhiteSpace(item.Version) &&
        !string.IsNullOrWhiteSpace(item.TargetOs) &&
        !string.IsNullOrWhiteSpace(item.BuildSystem) &&
        !string.IsNullOrWhiteSpace(item.PackagePath) &&
        !string.IsNullOrWhiteSpace(item.LicenseSummary) &&
        !string.IsNullOrWhiteSpace(item.PackageSha256);

    private static void ValidateRequired(
        string? value,
        string property,
        string path,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Error(
                "registry.property_required",
                $"Registry property '{property}' is required.",
                path));
        }
    }

    private static OperationResult<RegistryIndex> Failure(
        string code,
        string message,
        string path) =>
        new(null, [Error(code, message, path)]);

    private static ValidationIssue Error(string code, string message, string path) =>
        new(ValidationSeverity.Error, code, message, Path: path);

    [GeneratedRegex(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    private sealed class RegistryDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("registry_id")]
        public string? RegistryId { get; init; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("templates")]
        public List<RegistryTemplateDto> Templates { get; init; } = [];
    }

    private sealed class RegistryTemplateDto
    {
        [JsonPropertyName("family_id")]
        public string? FamilyId { get; init; }

        [JsonPropertyName("variant_id")]
        public string? VariantId { get; init; }

        [JsonPropertyName("template_id")]
        public string? TemplateId { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("target_os")]
        public string? TargetOs { get; init; }

        [JsonPropertyName("build_system")]
        public string? BuildSystem { get; init; }

        [JsonPropertyName("package_path")]
        public string? PackagePath { get; init; }

        [JsonPropertyName("license_summary")]
        public string? LicenseSummary { get; init; }

        [JsonPropertyName("package_sha256")]
        public string? PackageSha256 { get; init; }

        [JsonPropertyName("package_size_bytes")]
        public long PackageSizeBytes { get; init; }
    }
}
