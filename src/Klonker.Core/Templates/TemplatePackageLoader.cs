using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;
using Tomlyn;
using Tomlyn.Model;

namespace Klonker.Core.Templates;

public static partial class TemplatePackageLoader
{
    public const int SupportedSchemaVersion = 0;

    public static OperationResult<TemplatePackage> Load(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        var issues = new List<ValidationIssue>();
        var fullRoot = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(fullRoot))
        {
            issues.Add(Error(
                "package.not_found",
                "The template package directory does not exist.",
                path: fullRoot));
            return new OperationResult<TemplatePackage>(null, issues);
        }

        var manifestPath = Path.Combine(fullRoot, "template.toml");
        if (!File.Exists(manifestPath))
        {
            issues.Add(Error(
                "manifest.not_found",
                "The template package does not contain template.toml.",
                path: manifestPath));
            return new OperationResult<TemplatePackage>(null, issues);
        }

        var manifest = ParseManifest(File.ReadAllText(manifestPath), issues);
        var contentPath = Path.Combine(fullRoot, "content");
        if (!Directory.Exists(contentPath))
        {
            issues.Add(Error(
                "package.content_not_found",
                "The template package does not contain a content directory.",
                path: contentPath));
        }

        var sourceFiles = Directory.Exists(contentPath)
            ? EnumerateContent(contentPath, issues)
            : [];

        if (manifest is null || issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<TemplatePackage>(null, issues);
        }

        return new OperationResult<TemplatePackage>(
            new TemplatePackage(fullRoot, contentPath, manifest, sourceFiles),
            issues);
    }

    private static TemplateManifest? ParseManifest(
        string text,
        List<ValidationIssue> issues)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ??
                throw new InvalidOperationException("Tomlyn returned no manifest model.");
        }
        catch (TomlException exception)
        {
            issues.Add(Error(
                "manifest.toml_invalid",
                $"template.toml is not valid TOML: {exception.Message}"));
            return null;
        }

        var schemaVersion = GetInteger(table, "schema_version", issues);
        var id = GetString(table, "id", issues);
        var familyId = GetString(table, "family_id", issues);
        var variantId = GetString(table, "variant_id", issues);
        var name = GetString(table, "name", issues);
        var description = GetString(table, "description", issues);
        var version = GetString(table, "version", issues);
        var targetOs = GetString(table, "target_os", issues);
        var buildSystem = GetString(table, "build_system", issues);
        var sourceLicense = GetString(table, "source_license", issues);

        if (schemaVersion is not null && schemaVersion != SupportedSchemaVersion)
        {
            issues.Add(Error(
                "manifest.schema_unsupported",
                $"Schema version {schemaVersion} is not supported; expected {SupportedSchemaVersion}."));
        }

        var parameters = ParseParameters(table, issues);

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return null;
        }

        return new TemplateManifest(
            schemaVersion!.Value,
            id!,
            familyId!,
            variantId!,
            name!,
            description!,
            version!,
            targetOs!,
            buildSystem!,
            sourceLicense!,
            parameters);
    }

    private static ImmutableArray<TemplateParameterDefinition> ParseParameters(
        TomlTable table,
        List<ValidationIssue> issues)
    {
        if (!table.TryGetValue("parameters", out var rawParameters) ||
            rawParameters is not TomlTableArray parameterTables)
        {
            return [];
        }

        var parameters = ImmutableArray.CreateBuilder<TemplateParameterDefinition>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < parameterTables.Count; index++)
        {
            var parameterTable = parameterTables[index];
            var context = $"parameters[{index}]";
            var id = GetString(parameterTable, "id", issues, context);
            var typeText = GetString(parameterTable, "type", issues, context);
            var label = GetString(parameterTable, "label", issues, context);
            var description = GetOptionalString(parameterTable, "description", issues, context);
            var required = GetBoolean(parameterTable, "required", issues, context);
            var validation = GetOptionalString(parameterTable, "validation", issues, context);
            var values = GetStringArray(parameterTable, "values", issues, context);

            TemplateParameterType? type = typeText switch
            {
                "string" => TemplateParameterType.Text,
                "boolean" => TemplateParameterType.Boolean,
                "choice" => TemplateParameterType.Choice,
                null => null,
                _ => null,
            };

            if (typeText is not null && type is null)
            {
                issues.Add(Error(
                    "parameter.type_unsupported",
                    $"Parameter '{id ?? context}' has unsupported type '{typeText}'.",
                    id));
            }

            if (id is not null)
            {
                if (!ParameterIdPattern().IsMatch(id))
                {
                    issues.Add(Error(
                        "parameter.id_invalid",
                        $"Parameter ID '{id}' must be a valid template identifier.",
                        id));
                }

                if (!seenIds.Add(id))
                {
                    issues.Add(Error(
                        "parameter.id_duplicate",
                        $"Parameter ID '{id}' is declared more than once.",
                        id));
                }
            }

            if (validation is not null && validation != "cpp_identifier")
            {
                issues.Add(Error(
                    "parameter.validation_unsupported",
                    $"Parameter '{id ?? context}' uses unsupported validation '{validation}'.",
                    id));
            }

            object? defaultValue = null;
            if (parameterTable.TryGetValue("default", out var rawDefault))
            {
                defaultValue = rawDefault;
                if (type == TemplateParameterType.Boolean && rawDefault is not bool)
                {
                    issues.Add(Error(
                        "parameter.default_type",
                        $"Default for boolean parameter '{id ?? context}' must be a boolean.",
                        id));
                }
                else if (type is TemplateParameterType.Text or TemplateParameterType.Choice &&
                         rawDefault is not string)
                {
                    issues.Add(Error(
                        "parameter.default_type",
                        $"Default for parameter '{id ?? context}' must be a string.",
                        id));
                }
            }

            if (type == TemplateParameterType.Choice)
            {
                if (values.IsDefaultOrEmpty)
                {
                    issues.Add(Error(
                        "parameter.choice_values_required",
                        $"Choice parameter '{id ?? context}' must declare at least one value.",
                        id));
                }
                else if (defaultValue is string choiceDefault &&
                         !values.Contains(choiceDefault, StringComparer.Ordinal))
                {
                    issues.Add(Error(
                        "parameter.choice_default_invalid",
                        $"Default '{choiceDefault}' is not an allowed value for parameter '{id ?? context}'.",
                        id));
                }
            }

            if (id is not null &&
                type is not null &&
                label is not null &&
                required is not null)
            {
                parameters.Add(new TemplateParameterDefinition(
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

        return parameters.ToImmutable();
    }

    private static ImmutableArray<TemplateSourceFile> EnumerateContent(
        string contentPath,
        List<ValidationIssue> issues)
    {
        var files = ImmutableArray.CreateBuilder<TemplateSourceFile>();
        var pending = new Stack<DirectoryInfo>();
        var root = new DirectoryInfo(contentPath);

        if (root.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            issues.Add(Error(
                "package.reparse_point",
                "The content directory cannot be a symbolic link or reparse point.",
                path: contentPath));
            return [];
        }

        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    issues.Add(Error(
                        "package.reparse_point",
                        "Template content cannot contain symbolic links or reparse points.",
                        path: entry.FullName));
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push(childDirectory);
                    continue;
                }

                if (entry is not FileInfo file)
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(contentPath, file.FullName)
                    .Replace('\\', '/');
                var normalized = SafePath.NormalizeRelative(relativePath);
                issues.AddRange(normalized.Issues);
                var resolved = normalized.IsSuccess
                    ? SafePath.ResolveUnderRoot(contentPath, normalized.Value!)
                    : new OperationResult<string>(null, []);
                issues.AddRange(resolved.Issues);

                if (resolved.IsSuccess)
                {
                    files.Add(new TemplateSourceFile(
                        normalized.Value!,
                        resolved.Value!,
                        relativePath.EndsWith(".sbn", StringComparison.Ordinal)));
                }
            }
        }

        return files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string? GetString(
        TomlTable table,
        string property,
        List<ValidationIssue> issues,
        string? context = null)
    {
        if (!table.TryGetValue(property, out var value))
        {
            issues.Add(MissingProperty(property, context));
            return null;
        }

        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        issues.Add(Error(
            "manifest.property_type",
            $"{PropertyName(property, context)} must be a non-empty string."));
        return null;
    }

    private static string? GetOptionalString(
        TomlTable table,
        string property,
        List<ValidationIssue> issues,
        string? context)
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
            "manifest.property_type",
            $"{PropertyName(property, context)} must be a string."));
        return null;
    }

    private static int? GetInteger(
        TomlTable table,
        string property,
        List<ValidationIssue> issues)
    {
        if (!table.TryGetValue(property, out var value))
        {
            issues.Add(MissingProperty(property, null));
            return null;
        }

        if (value is long number &&
            number is >= int.MinValue and <= int.MaxValue)
        {
            return Convert.ToInt32(number, CultureInfo.InvariantCulture);
        }

        issues.Add(Error(
            "manifest.property_type",
            $"{PropertyName(property, null)} must be an integer."));
        return null;
    }

    private static bool? GetBoolean(
        TomlTable table,
        string property,
        List<ValidationIssue> issues,
        string context)
    {
        if (!table.TryGetValue(property, out var value))
        {
            issues.Add(MissingProperty(property, context));
            return null;
        }

        if (value is bool boolean)
        {
            return boolean;
        }

        issues.Add(Error(
            "manifest.property_type",
            $"{PropertyName(property, context)} must be a boolean."));
        return null;
    }

    private static ImmutableArray<string> GetStringArray(
        TomlTable table,
        string property,
        List<ValidationIssue> issues,
        string context)
    {
        if (!table.TryGetValue(property, out var value))
        {
            return [];
        }

        if (value is not TomlArray array || array.Any(item => item is not string))
        {
            issues.Add(Error(
                "manifest.property_type",
                $"{PropertyName(property, context)} must be an array of strings."));
            return [];
        }

        return array.Cast<string>().ToImmutableArray();
    }

    private static ValidationIssue MissingProperty(string property, string? context) =>
        Error(
            "manifest.property_required",
            $"{PropertyName(property, context)} is required.");

    private static string PropertyName(string property, string? context) =>
        context is null ? $"Property '{property}'" : $"Property '{context}.{property}'";

    private static ValidationIssue Error(
        string code,
        string message,
        string? parameterId = null,
        string? path = null) =>
        new(ValidationSeverity.Error, code, message, parameterId, path);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterIdPattern();
}
