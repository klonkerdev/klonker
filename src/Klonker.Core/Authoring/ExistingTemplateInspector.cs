using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;
using Klonker.Core.Templates;
using Tomlyn;
using Tomlyn.Model;

namespace Klonker.Core.Authoring;

public static partial class ExistingTemplateInspector
{
    public static ExistingTemplateInspection Inspect(string rootPath)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            issues.Add(Error(
                "authoring.inspect_path_required",
                "Choose an existing project or template folder."));
            return Result(
                rootPath,
                ExistingTemplateKind.ProjectFolder,
                "No folder selected.",
                rootPath,
                metadata: null,
                [],
                issues);
        }

        string root;
        try
        {
            root = Path.GetFullPath(rootPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            issues.Add(Error(
                "authoring.inspect_path_invalid",
                $"The selected folder path is invalid: {exception.Message}"));
            return Result(
                rootPath,
                ExistingTemplateKind.ProjectFolder,
                "The selected path is invalid.",
                rootPath,
                metadata: null,
                [],
                issues);
        }

        if (!Directory.Exists(root))
        {
            issues.Add(Error(
                "authoring.inspect_folder_missing",
                "The selected folder does not exist.",
                root));
            return Result(
                root,
                ExistingTemplateKind.ProjectFolder,
                "The selected folder does not exist.",
                root,
                metadata: null,
                [],
                issues);
        }

        try
        {
            if (new DirectoryInfo(root).Attributes.HasFlag(
                    FileAttributes.ReparsePoint))
            {
                issues.Add(Error(
                    "authoring.inspect_reparse",
                    "The selected folder cannot be a symbolic link or reparse point.",
                    root));
            }

            var files = InspectFiles(root, issues);
            var runtimeManifest = Path.Combine(root, "template.toml");
            if (File.Exists(runtimeManifest))
            {
                return InspectRuntimePackage(root, files, issues);
            }

            var sourceManifest = Path.Combine(root, "package.toml");
            if (File.Exists(sourceManifest))
            {
                return InspectRegistrySource(root, files, issues);
            }

            if (files.Any(path => path.EndsWith(
                    ".toml",
                    StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "authoring.inspect_unknown_toml",
                    "TOML files were found, but neither package.toml nor template.toml exists at the folder root. Move or rename the appropriate manifest before refreshing.",
                    Path: root));
            }

            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "authoring.inspect_manifest_missing",
                "No Klonker manifest was found. The wizard can copy this folder into a new registry source package without changing the original.",
                Path: root));
            return Result(
                root,
                ExistingTemplateKind.ProjectFolder,
                $"Ordinary project folder with {files.Length} discoverable files.",
                root,
                metadata: null,
                files,
                issues);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(Error(
                "authoring.inspect_failed",
                $"The folder could not be inspected: {exception.Message}",
                root));
            return Result(
                root,
                ExistingTemplateKind.ProjectFolder,
                "Folder inspection failed.",
                root,
                metadata: null,
                [],
                issues);
        }
    }

    private static ExistingTemplateInspection InspectRuntimePackage(
        string root,
        ImmutableArray<string> files,
        List<ValidationIssue> issues)
    {
        var loaded = TemplatePackageLoader.Load(root);
        issues.AddRange(loaded.Issues);
        ExistingTemplateMetadata? metadata = null;
        if (loaded.Value is not null)
        {
            var manifest = loaded.Value.Manifest;
            var separator = manifest.FamilyId.IndexOf('.');
            var namespaceId = separator > 0
                ? manifest.FamilyId[..separator]
                : "local";
            var packageId = separator > 0
                ? manifest.FamilyId[(separator + 1)..]
                : manifest.FamilyId;
            metadata = new ExistingTemplateMetadata(
                namespaceId,
                packageId,
                manifest.Name,
                manifest.Description,
                manifest.Version,
                manifest.Language,
                [manifest.BuildSystem],
                manifest.SourceLicense,
                [manifest.TargetOs]);
        }

        return Result(
            root,
            ExistingTemplateKind.RuntimePackage,
            loaded.IsSuccess
                ? "Valid runtime template package. It can be converted into editable registry source layout."
                : "Runtime package detected, but its manifest or content needs attention.",
            Path.Combine(root, "content"),
            metadata,
            files,
            issues);
    }

    private static ExistingTemplateInspection InspectRegistrySource(
        string root,
        ImmutableArray<string> files,
        List<ValidationIssue> issues)
    {
        var manifestPath = Path.Combine(root, "package.toml");
        var package = ParseToml(manifestPath, "package.toml", issues);
        ExistingTemplateMetadata? metadata = null;
        if (package is not null)
        {
            RequireSchema(package, manifestPath, issues);
            var namespaceId = GetRequiredString(
                package,
                "namespace",
                manifestPath,
                issues);
            var packageId = GetRequiredString(
                package,
                "id",
                manifestPath,
                issues);
            var name = GetRequiredString(
                package,
                "name",
                manifestPath,
                issues);
            var description = GetRequiredString(
                package,
                "description",
                manifestPath,
                issues);
            var language = GetRequiredString(
                package,
                "language",
                manifestPath,
                issues);
            var sourceLicense = GetRequiredString(
                package,
                "source_license",
                manifestPath,
                issues);
            _ = GetRequiredString(
                package,
                "license_summary",
                manifestPath,
                issues);
            ValidateId(namespaceId, "namespace", manifestPath, issues);
            ValidateId(packageId, "package", manifestPath, issues);
            ValidateId(language, "language", manifestPath, issues);
            RejectFavorite(package, manifestPath, issues);

            if (packageId is not null &&
                !Path.GetFileName(root).Equals(
                    packageId,
                    StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "authoring.package_folder_mismatch",
                    $"Package ID '{packageId}' does not match folder name '{Path.GetFileName(root)}'. The official registry publisher requires them to match.",
                    Path: manifestPath));
            }

            if (namespaceId is not null)
            {
                var namespaceFolder = Directory.GetParent(root)?.Name;
                if (!namespaceId.Equals(
                        namespaceFolder,
                        StringComparison.Ordinal))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "authoring.namespace_folder_mismatch",
                        $"Namespace '{namespaceId}' does not match parent folder '{namespaceFolder}'. Use templates/<namespace>/<package> when publishing.",
                        Path: manifestPath));
                }
            }

            var variants = InspectVariants(root, issues);
            if (namespaceId is not null &&
                packageId is not null &&
                name is not null &&
                description is not null &&
                language is not null &&
                sourceLicense is not null &&
                variants.Count > 0)
            {
                metadata = new ExistingTemplateMetadata(
                    namespaceId,
                    packageId,
                    name,
                    description,
                    variants[0].Version,
                    language,
                    variants
                        .Select(variant => variant.BuildSystem)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToImmutableArray(),
                    sourceLicense,
                    variants
                        .Select(variant => variant.Platform)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToImmutableArray());
            }
        }

        return Result(
            root,
            ExistingTemplateKind.RegistrySourcePackage,
            issues.Any(issue => issue.Severity == ValidationSeverity.Error)
                ? "Registry source package detected, with errors to fix before publication."
                : "Registry source package structure is valid. Warnings are optional improvements.",
            Path.Combine(root, "content"),
            metadata,
            files,
            issues);
    }

    private static List<VariantMetadata> InspectVariants(
        string root,
        List<ValidationIssue> issues)
    {
        var result = new List<VariantMetadata>();
        var variantsRoot = Path.Combine(root, "variants");
        if (!Directory.Exists(variantsRoot))
        {
            issues.Add(Error(
                "authoring.variants_missing",
                "Registry source packages require a variants directory.",
                variantsRoot));
            return result;
        }

        var variantDirectories = Directory.GetDirectories(variantsRoot)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (variantDirectories.Length == 0)
        {
            issues.Add(Error(
                "authoring.variant_required",
                "Add at least one variants/<variant>/variant.toml definition.",
                variantsRoot));
            return result;
        }

        foreach (var variantRoot in variantDirectories)
        {
            var variantPath = Path.Combine(variantRoot, "variant.toml");
            if (!File.Exists(variantPath))
            {
                issues.Add(Error(
                    "authoring.variant_manifest_missing",
                    "Each variant folder requires variant.toml.",
                    variantPath));
                continue;
            }

            var variant = ParseToml(
                variantPath,
                "variant.toml",
                issues);
            if (variant is null)
            {
                continue;
            }

            RequireSchema(variant, variantPath, issues);
            var id = GetRequiredString(variant, "id", variantPath, issues);
            _ = GetRequiredString(
                variant,
                "description",
                variantPath,
                issues);
            var version = GetRequiredString(
                variant,
                "version",
                variantPath,
                issues);
            var platform = GetRequiredString(
                variant,
                "target_os",
                variantPath,
                issues);
            var buildSystem = GetRequiredString(
                variant,
                "build_system",
                variantPath,
                issues);
            ValidateId(id, "variant", variantPath, issues);
            ValidateId(platform, "platform", variantPath, issues);
            ValidateId(buildSystem, "build system", variantPath, issues);
            if (version is not null &&
                !VersionPattern().IsMatch(version))
            {
                issues.Add(Error(
                    "authoring.version_invalid",
                    $"Version '{version}' must use semantic form such as 0.1.0.",
                    variantPath));
            }

            RejectFavorite(variant, variantPath, issues);
            if (id is not null &&
                !Path.GetFileName(variantRoot).Equals(
                    id,
                    StringComparison.Ordinal))
            {
                issues.Add(Error(
                    "authoring.variant_folder_mismatch",
                    $"Variant ID '{id}' must match folder name '{Path.GetFileName(variantRoot)}'.",
                    variantPath));
            }

            if (version is not null &&
                platform is not null &&
                buildSystem is not null)
            {
                result.Add(new VariantMetadata(
                    version,
                    platform,
                    buildSystem));
            }
        }

        return result;
    }

    private static ImmutableArray<string> InspectFiles(
        string root,
        List<ValidationIssue> issues)
    {
        var files = ImmutableArray.CreateBuilder<string>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootInfo = new DirectoryInfo(root);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootInfo);
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
                        "authoring.inspect_reparse",
                        "Template trees cannot contain symbolic links or reparse points.",
                        entry.FullName));
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    if (child.Name.Equals(
                            ".git",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    pending.Push(child);
                    continue;
                }

                if (entry is FileInfo file)
                {
                    var relative = Path.GetRelativePath(root, file.FullName)
                        .Replace('\\', '/');
                    var normalized = SafePath.NormalizeRelative(relative);
                    issues.AddRange(normalized.Issues);
                    if (normalized.IsSuccess)
                    {
                        if (!paths.Add(normalized.Value!))
                        {
                            issues.Add(Error(
                                "authoring.path_duplicate",
                                $"The tree contains case-colliding path '{normalized.Value}'.",
                                file.FullName));
                            continue;
                        }

                        files.Add(normalized.Value!);
                    }
                }
            }
        }

        foreach (var path in paths)
        {
            var segments = path.Split('/');
            for (var length = 1; length < segments.Length; length++)
            {
                var parent = string.Join('/', segments.Take(length));
                if (paths.Contains(parent))
                {
                    issues.Add(Error(
                        "authoring.path_collision",
                        $"'{parent}' is both a file and a directory.",
                        path));
                }
            }
        }

        return files.Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static TomlTable? ParseToml(
        string path,
        string label,
        List<ValidationIssue> issues)
    {
        try
        {
            return TomlSerializer.Deserialize<TomlTable>(
                File.ReadAllText(path));
        }
        catch (TomlException exception)
        {
            issues.Add(Error(
                "authoring.toml_invalid",
                $"{label} is not valid TOML: {exception.Message}",
                path));
            return null;
        }
    }

    private static void RequireSchema(
        TomlTable table,
        string path,
        List<ValidationIssue> issues)
    {
        if (!table.TryGetValue("schema_version", out var value) ||
            value is not long schema ||
            schema != 0)
        {
            issues.Add(Error(
                "authoring.schema_invalid",
                "schema_version must be the integer 0.",
                path));
        }
    }

    private static string? GetRequiredString(
        TomlTable table,
        string property,
        string path,
        List<ValidationIssue> issues)
    {
        if (table.TryGetValue(property, out var value) &&
            value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        issues.Add(Error(
            "authoring.property_required",
            $"Property '{property}' must be a non-empty string.",
            path));
        return null;
    }

    private static void ValidateId(
        string? value,
        string label,
        string path,
        List<ValidationIssue> issues)
    {
        if (value is not null && !IdPattern().IsMatch(value))
        {
            issues.Add(Error(
                "authoring.id_invalid",
                $"{char.ToUpperInvariant(label[0])}{label[1..]} ID '{value}' must start with a lowercase letter and contain only lowercase letters, numbers, and hyphens.",
                path));
        }
    }

    private static void RejectFavorite(
        TomlTable table,
        string path,
        List<ValidationIssue> issues)
    {
        if (table.ContainsKey("favorite"))
        {
            issues.Add(Error(
                "authoring.favorite_forbidden",
                "Favorite state is app-local and cannot be declared in package metadata.",
                path));
        }
    }

    private static ExistingTemplateInspection Result(
        string root,
        ExistingTemplateKind kind,
        string summary,
        string contentSourcePath,
        ExistingTemplateMetadata? metadata,
        ImmutableArray<string> files,
        IEnumerable<ValidationIssue> issues) =>
        new(
            root,
            kind,
            summary,
            contentSourcePath,
            metadata,
            files,
            issues.ToImmutableArray());

    private static ValidationIssue Error(
        string code,
        string message,
        string? path = null) =>
        new(ValidationSeverity.Error, code, message, Path: path);

    private sealed record VariantMetadata(
        string Version,
        string Platform,
        string BuildSystem);

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
