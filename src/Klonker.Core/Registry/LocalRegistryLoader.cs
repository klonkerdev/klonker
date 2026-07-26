using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;

namespace Klonker.Core.Registry;

public static class LocalRegistryLoader
{
    public const int SupportedSchemaVersion = 0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static OperationResult<LocalRegistryCatalog> Load(string registryJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryJsonPath);

        var fullPath = Path.GetFullPath(registryJsonPath);
        if (!File.Exists(fullPath))
        {
            return Failure(
                "registry.not_found",
                "The local registry index could not be found.",
                fullPath);
        }

        RegistryDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RegistryDto>(
                File.ReadAllText(fullPath),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            return Failure(
                "registry.json_invalid",
                $"The local registry index is invalid JSON: {exception.Message}",
                fullPath);
        }

        if (dto is null)
        {
            return Failure(
                "registry.empty",
                "The local registry index is empty.",
                fullPath);
        }

        var issues = new List<ValidationIssue>();
        if (dto.SchemaVersion != SupportedSchemaVersion)
        {
            issues.Add(Error(
                "registry.schema_unsupported",
                $"Registry schema version {dto.SchemaVersion} is not supported.",
                fullPath));
        }

        ValidateRequired(dto.RegistryId, "registry_id", fullPath, issues);
        ValidateRequired(dto.DisplayName, "display_name", fullPath, issues);
        var entries = ImmutableArray.CreateBuilder<RegistryTemplateEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var registryRoot = Path.GetDirectoryName(fullPath)!;

        for (var index = 0; index < dto.Templates.Count; index++)
        {
            var item = dto.Templates[index];
            var context = $"templates[{index}]";
            ValidateRequired(item.FamilyId, $"{context}.family_id", fullPath, issues);
            ValidateRequired(item.VariantId, $"{context}.variant_id", fullPath, issues);
            ValidateRequired(item.TemplateId, $"{context}.template_id", fullPath, issues);
            ValidateRequired(item.Name, $"{context}.name", fullPath, issues);
            ValidateRequired(item.Description, $"{context}.description", fullPath, issues);
            ValidateRequired(item.Version, $"{context}.version", fullPath, issues);
            ValidateRequired(item.TargetOs, $"{context}.target_os", fullPath, issues);
            ValidateRequired(item.BuildSystem, $"{context}.build_system", fullPath, issues);
            ValidateRequired(item.PackagePath, $"{context}.package_path", fullPath, issues);
            ValidateRequired(item.LicenseSummary, $"{context}.license_summary", fullPath, issues);

            if (!string.IsNullOrWhiteSpace(item.TemplateId) && !ids.Add(item.TemplateId))
            {
                issues.Add(Error(
                    "registry.template_duplicate",
                    $"Template ID '{item.TemplateId}' appears more than once in this registry.",
                    fullPath));
            }

            var packageResolution = string.IsNullOrWhiteSpace(item.PackagePath)
                ? new OperationResult<string>(null, [])
                : SafePath.ResolveUnderRoot(registryRoot, item.PackagePath);
            issues.AddRange(packageResolution.Issues);
            if (packageResolution.IsSuccess && !Directory.Exists(packageResolution.Value))
            {
                issues.Add(Error(
                    "registry.package_not_found",
                    $"Package directory '{item.PackagePath}' does not exist.",
                    item.PackagePath ?? fullPath));
            }

            if (HasRequiredProperties(item) && packageResolution.IsSuccess)
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
                    item.PackagePath!,
                    item.LicenseSummary!));
            }
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<LocalRegistryCatalog>(null, issues);
        }

        return new OperationResult<LocalRegistryCatalog>(
            new LocalRegistryCatalog(
                dto.SchemaVersion,
                dto.RegistryId!,
                dto.DisplayName!,
                registryRoot,
                entries.ToImmutable()),
            issues);
    }

    public static OperationResult<string> ResolvePackagePath(
        LocalRegistryCatalog registry,
        RegistryTemplateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(entry);
        return SafePath.ResolveUnderRoot(registry.RootPath, entry.PackagePath);
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
        !string.IsNullOrWhiteSpace(item.LicenseSummary);

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

    private static OperationResult<LocalRegistryCatalog> Failure(
        string code,
        string message,
        string path) =>
        new(null, [Error(code, message, path)]);

    private static ValidationIssue Error(string code, string message, string path) =>
        new(ValidationSeverity.Error, code, message, Path: path);

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
    }
}
