using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;
using Klonker.Core.Templates;
using Tomlyn;
using Tomlyn.Model;

namespace Klonker.Core.Modules;

public static partial class ModulePackageLoader
{
    public const int SupportedSchemaVersion = 0;

    public static OperationResult<ModulePackage> Load(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(packageRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                "module.path_invalid",
                $"The module package path is invalid: {exception.Message}",
                packageRoot);
        }

        if (!Directory.Exists(fullRoot))
        {
            return Failure(
                "module.not_found",
                "The module package directory does not exist.",
                fullRoot);
        }

        if (new DirectoryInfo(fullRoot).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return Failure(
                "module.reparse_point",
                "The module package directory cannot be a symbolic link or reparse point.",
                fullRoot);
        }

        var issues = new List<ValidationIssue>();
        var manifestPath = Path.Combine(fullRoot, "module.toml");
        if (!File.Exists(manifestPath))
        {
            return Failure(
                "module.manifest_not_found",
                "The module package does not contain module.toml.",
                manifestPath);
        }

        ModuleManifest? manifest;
        try
        {
            manifest = ParseManifest(File.ReadAllText(manifestPath), issues);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "module.manifest_read_failed",
                $"module.toml could not be read: {exception.Message}",
                manifestPath);
        }

        var contentPath = Path.Combine(fullRoot, "content");
        if (!Directory.Exists(contentPath))
        {
            issues.Add(Error(
                "module.content_not_found",
                "The module package does not contain a content directory.",
                contentPath));
        }

        var files = Directory.Exists(contentPath)
            ? EnumerateContent(contentPath, issues)
            : [];

        if (manifest is null ||
            issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<ModulePackage>(null, issues);
        }

        return new OperationResult<ModulePackage>(
            new ModulePackage(fullRoot, contentPath, manifest, files),
            issues);
    }

    private static ModuleManifest? ParseManifest(
        string text,
        List<ValidationIssue> issues)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ??
                throw new InvalidOperationException("Tomlyn returned no module manifest model.");
        }
        catch (TomlException exception)
        {
            issues.Add(Error(
                "module.toml_invalid",
                $"module.toml is not valid TOML: {exception.Message}"));
            return null;
        }

        var schemaVersion = Integer(table, "schema_version", issues);
        var id = String(table, "id", issues);
        var name = String(table, "name", issues);
        var description = String(table, "description", issues);
        var version = String(table, "version", issues);
        var language = String(table, "language", issues);
        var sourceLicense = String(table, "source_license", issues);
        var tags = StringArray(table, "tags", issues, required: false);
        var instructions = OptionalString(table, "post_generation_instructions", issues);

        if (schemaVersion is not null && schemaVersion != SupportedSchemaVersion)
        {
            issues.Add(Error(
                "module.schema_unsupported",
                $"Module schema version {schemaVersion} is not supported; expected {SupportedSchemaVersion}."));
        }

        if (id is not null && !ModuleIdPattern().IsMatch(id))
        {
            issues.Add(Error(
                "module.id_invalid",
                "Module IDs must use lowercase dot-separated identifiers such as 'cpp.cmake-submodule'."));
        }

        if (language is not null && !LanguageIdPattern().IsMatch(language))
        {
            issues.Add(Error(
                "module.language_invalid",
                "Module language IDs must start with a lowercase letter and contain lowercase letters, numbers, or hyphens."));
        }

        var slots = ParseSlots(table, issues);
        var parameters = ParseParameters(table, issues);
        var dependencies = ParseDependencies(table, issues);
        var declaredIds = new HashSet<string>(
            slots.Select(slot => slot.Id),
            StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (!declaredIds.Add(parameter.Id))
            {
                issues.Add(Error(
                    "module.value_id_duplicate",
                    $"'{parameter.Id}' is declared as both a slot and a parameter.",
                    parameterId: parameter.Id));
            }
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return null;
        }

        return new ModuleManifest(
            schemaVersion!.Value,
            id!,
            name!,
            description!,
            version!,
            language!,
            sourceLicense!,
            tags,
            slots,
            parameters,
            dependencies,
            instructions);
    }

    private static ImmutableArray<ModuleSlotDefinition> ParseSlots(
        TomlTable table,
        List<ValidationIssue> issues)
    {
        if (!table.TryGetValue("slots", out var value))
        {
            return [];
        }

        if (value is not TomlTableArray slotTables)
        {
            issues.Add(Error(
                "module.property_type",
                "Property 'slots' must be an array of tables."));
            return [];
        }

        var result = ImmutableArray.CreateBuilder<ModuleSlotDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < slotTables.Count; index++)
        {
            var slot = slotTables[index];
            var context = $"slots[{index}]";
            var id = String(slot, "id", issues, context);
            var label = String(slot, "label", issues, context);
            var description = String(slot, "description", issues, context);
            var required = Boolean(slot, "required", issues, context);
            var defaultPath = OptionalString(slot, "default", issues, context);
            if (id is not null &&
                (!IdentifierPattern().IsMatch(id) || !ids.Add(id)))
            {
                issues.Add(Error(
                    ids.Contains(id)
                        ? "module.slot_duplicate"
                        : "module.slot_id_invalid",
                    ids.Contains(id)
                        ? $"Module slot '{id}' is declared more than once."
                        : $"Module slot ID '{id}' is invalid.",
                    parameterId: id));
            }

            if (defaultPath is not null)
            {
                var path = SafePath.NormalizeRelative(defaultPath);
                issues.AddRange(path.Issues.Select(issue =>
                    issue with
                    {
                        Code = "module.slot_default_invalid",
                        ParameterId = id,
                    }));
            }

            if (id is not null &&
                label is not null &&
                description is not null &&
                required is not null)
            {
                result.Add(new ModuleSlotDefinition(
                    id,
                    label,
                    description,
                    required.Value,
                    defaultPath?.Replace('\\', '/')));
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<TemplateParameterDefinition> ParseParameters(
        TomlTable table,
        List<ValidationIssue> issues)
    {
        if (!table.TryGetValue("parameters", out var value))
        {
            return [];
        }

        if (value is not TomlTableArray parameterTables)
        {
            issues.Add(Error(
                "module.property_type",
                "Property 'parameters' must be an array of tables."));
            return [];
        }

        var result = ImmutableArray.CreateBuilder<TemplateParameterDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < parameterTables.Count; index++)
        {
            var parameter = parameterTables[index];
            var context = $"parameters[{index}]";
            var id = String(parameter, "id", issues, context);
            var typeText = String(parameter, "type", issues, context);
            var label = String(parameter, "label", issues, context);
            var description = OptionalString(parameter, "description", issues, context);
            var required = Boolean(parameter, "required", issues, context);
            var validation = OptionalString(parameter, "validation", issues, context);
            var values = StringArray(parameter, "values", issues, required: false, context);
            var type = typeText switch
            {
                "string" => TemplateParameterType.Text,
                "boolean" => TemplateParameterType.Boolean,
                "choice" => TemplateParameterType.Choice,
                _ => (TemplateParameterType?)null,
            };

            if (id is not null &&
                (!IdentifierPattern().IsMatch(id) || !ids.Add(id)))
            {
                issues.Add(Error(
                    ids.Contains(id)
                        ? "module.parameter_duplicate"
                        : "module.parameter_id_invalid",
                    ids.Contains(id)
                        ? $"Module parameter '{id}' is declared more than once."
                        : $"Module parameter ID '{id}' is invalid.",
                    parameterId: id));
            }

            if (typeText is not null && type is null)
            {
                issues.Add(Error(
                    "module.parameter_type_unsupported",
                    $"Module parameter '{id ?? context}' has unsupported type '{typeText}'.",
                    parameterId: id));
            }

            if (validation is not null && validation != "cpp_identifier")
            {
                issues.Add(Error(
                    "module.parameter_validation_unsupported",
                    $"Module parameter '{id ?? context}' uses unsupported validation '{validation}'.",
                    parameterId: id));
            }

            object? defaultValue = null;
            if (parameter.TryGetValue("default", out var rawDefault))
            {
                defaultValue = rawDefault;
                if (type == TemplateParameterType.Boolean && rawDefault is not bool ||
                    type is TemplateParameterType.Text or TemplateParameterType.Choice &&
                    rawDefault is not string)
                {
                    issues.Add(Error(
                        "module.parameter_default_type",
                        $"Default for module parameter '{id ?? context}' has the wrong type.",
                        parameterId: id));
                }
            }

            if (type == TemplateParameterType.Choice &&
                (values.IsDefaultOrEmpty ||
                 defaultValue is string choice &&
                 !values.Contains(choice, StringComparer.Ordinal)))
            {
                issues.Add(Error(
                    "module.parameter_choice_invalid",
                    $"Choice parameter '{id ?? context}' must have choices and a valid default.",
                    parameterId: id));
            }

            if (id is not null &&
                type is not null &&
                label is not null &&
                required is not null)
            {
                result.Add(new TemplateParameterDefinition(
                    id,
                    type.Value,
                    label,
                    description,
                    required.Value,
                    defaultValue,
                    validation,
                    values));
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<ModuleDependency> ParseDependencies(
        TomlTable table,
        List<ValidationIssue> issues)
    {
        if (!table.TryGetValue("dependencies", out var value))
        {
            return [];
        }

        if (value is not TomlTableArray dependencyTables)
        {
            issues.Add(Error(
                "module.property_type",
                "Property 'dependencies' must be an array of tables."));
            return [];
        }

        var result = ImmutableArray.CreateBuilder<ModuleDependency>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < dependencyTables.Count; index++)
        {
            var dependency = dependencyTables[index];
            var context = $"dependencies[{index}]";
            var id = String(dependency, "id", issues, context);
            var name = String(dependency, "name", issues, context);
            var version = String(dependency, "version", issues, context);
            var license = String(dependency, "license", issues, context);
            var projectUrl = OptionalString(dependency, "project_url", issues, context);

            if (id is not null && !ids.Add(id))
            {
                issues.Add(Error(
                    "module.dependency_duplicate",
                    $"Module dependency '{id}' is declared more than once."));
            }

            if (projectUrl is not null &&
                (!Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) ||
                 uri.Scheme is not ("https" or "http")))
            {
                issues.Add(Error(
                    "module.dependency_url_invalid",
                    $"Dependency '{id ?? context}' project_url must be an absolute HTTP(S) URL."));
            }

            if (id is not null &&
                name is not null &&
                version is not null &&
                license is not null)
            {
                result.Add(new ModuleDependency(id, name, version, license, projectUrl));
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<TemplateSourceFile> EnumerateContent(
        string contentRoot,
        List<ValidationIssue> issues)
    {
        var result = ImmutableArray.CreateBuilder<TemplateSourceFile>();
        var pending = new Stack<DirectoryInfo>();
        var root = new DirectoryInfo(contentRoot);
        if (root.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            issues.Add(Error(
                "module.reparse_point",
                "The module content directory cannot be a symbolic link or reparse point.",
                contentRoot));
            return [];
        }

        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory
                         .EnumerateFileSystemInfos()
                         .OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    issues.Add(Error(
                        "module.reparse_point",
                        "Module content cannot contain symbolic links or reparse points.",
                        entry.FullName));
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    pending.Push(child);
                    continue;
                }

                if (entry is FileInfo file)
                {
                    var relative = Path.GetRelativePath(contentRoot, file.FullName)
                        .Replace('\\', '/');
                    var normalized = SafePath.NormalizeRelative(relative);
                    issues.AddRange(normalized.Issues);
                    if (normalized.IsSuccess)
                    {
                        result.Add(new TemplateSourceFile(
                            normalized.Value!,
                            file.FullName,
                            relative.EndsWith(".sbn", StringComparison.Ordinal)));
                    }
                }
            }
        }

        return result
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static int? Integer(
        TomlTable table,
        string property,
        List<ValidationIssue> issues)
    {
        if (table.TryGetValue(property, out var value) &&
            value is long number &&
            number is >= int.MinValue and <= int.MaxValue)
        {
            return Convert.ToInt32(number, CultureInfo.InvariantCulture);
        }

        issues.Add(Error(
            "module.property_required",
            $"Property '{property}' must be an integer."));
        return null;
    }

    private static string? String(
        TomlTable table,
        string property,
        List<ValidationIssue> issues,
        string? context = null)
    {
        if (table.TryGetValue(property, out var value) &&
            value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        issues.Add(Error(
            "module.property_required",
            $"{Property(context, property)} must be a non-empty string."));
        return null;
    }

    private static string? OptionalString(
        TomlTable table,
        string property,
        List<ValidationIssue> issues,
        string? context = null)
    {
        if (!table.TryGetValue(property, out var value))
        {
            return null;
        }

        if (value is string text)
        {
            return text;
        }

        issues.Add(Error(
            "module.property_type",
            $"{Property(context, property)} must be a string."));
        return null;
    }

    private static bool? Boolean(
        TomlTable table,
        string property,
        List<ValidationIssue> issues,
        string context)
    {
        if (table.TryGetValue(property, out var value) && value is bool result)
        {
            return result;
        }

        issues.Add(Error(
            "module.property_required",
            $"{Property(context, property)} must be a boolean."));
        return null;
    }

    private static ImmutableArray<string> StringArray(
        TomlTable table,
        string property,
        List<ValidationIssue> issues,
        bool required,
        string? context = null)
    {
        if (!table.TryGetValue(property, out var value))
        {
            if (required)
            {
                issues.Add(Error(
                    "module.property_required",
                    $"{Property(context, property)} must be an array of strings."));
            }

            return [];
        }

        if (value is not TomlArray array || array.Any(item => item is not string))
        {
            issues.Add(Error(
                "module.property_type",
                $"{Property(context, property)} must be an array of strings."));
            return [];
        }

        return array
            .Cast<string>()
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static string Property(string? context, string property) =>
        context is null
            ? $"Property '{property}'"
            : $"Property '{context}.{property}'";

    private static OperationResult<ModulePackage> Failure(
        string code,
        string message,
        string path) =>
        new(null, [Error(code, message, path)]);

    private static ValidationIssue Error(
        string code,
        string message,
        string? path = null,
        string? parameterId = null) =>
        new(ValidationSeverity.Error, code, message, parameterId, path);

    [GeneratedRegex(
        @"\A[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)+\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex ModuleIdPattern();

    [GeneratedRegex(
        @"\A[A-Za-z_][A-Za-z0-9_]*\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(
        @"\A[a-z][a-z0-9-]*\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex LanguageIdPattern();
}
