using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Paths;
using Klonker.Core.Templates;

namespace Klonker.Core.Authoring;

public static partial class TemplateAuthoringPlanner
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git",
        ".vs",
        ".idea",
        "bin",
        "build",
        "dist",
        "node_modules",
        "obj",
    ];
    private const int MaximumImportedFiles = 10_000;
    private const long MaximumImportedFileBytes = 64L * 1024 * 1024;
    private const long MaximumImportedBytes = 512L * 1024 * 1024;

    public static OperationResult<GenerationPlan> CreatePlan(
        TemplateAuthoringRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<ValidationIssue>();
        ValidateRequest(request, issues);
        var destination = TemplateAuthoringDestinationValidator.Validate(
            request.DestinationPath,
            request.ExistingContentPath);
        issues.AddRange(destination.Issues);
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<GenerationPlan>(null, issues);
        }

        var files = new List<PlannedFile>
        {
            TextFile("package.toml", BuildPackageToml(request)),
        };
        var directories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "content",
            "variants",
        };

        if (!string.IsNullOrWhiteSpace(request.ExistingContentPath))
        {
            ImportExistingContent(
                request.ExistingContentPath!,
                files,
                directories,
                issues);
        }

        AddSeedFiles(request.SeedFiles, files, directories, issues);
        if (request.CreateReadme &&
            !files.Any(file => file.RelativePath.Equals(
                "content/README.md",
                StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.Equals(
                    "content/README.md.sbn",
                    StringComparison.OrdinalIgnoreCase)))
        {
            files.Add(TextFile(
                "content/README.md.sbn",
                BuildReadme(request)));
        }

        foreach (var platform in request.Platforms.Order(StringComparer.Ordinal))
        {
            foreach (var buildSystem in request.BuildSystems.Order(
                         StringComparer.Ordinal))
            {
                var variantId = GetVariantId(platform, buildSystem);
                var variantRoot = $"variants/{variantId}";
                directories.Add(variantRoot);
                directories.Add($"{variantRoot}/content");
                files.Add(TextFile(
                    $"{variantRoot}/variant.toml",
                    BuildVariantToml(
                        request,
                        platform,
                        buildSystem,
                        variantId)));

                foreach (var seed in request.SeedFiles
                             .Where(seed =>
                                 seed.VariantSpecific &&
                                 (seed.BuildSystem is null ||
                                  seed.BuildSystem.Equals(
                                      buildSystem,
                                      StringComparison.Ordinal)))
                             .OrderBy(
                                 seed => seed.RelativePath,
                                 StringComparer.Ordinal))
                {
                    AddTextFile(
                        $"{variantRoot}/content/{seed.RelativePath}",
                        seed.Content,
                        files,
                        directories,
                        issues);
                }
            }
        }

        ValidatePlannedPaths(files, directories, issues);
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<GenerationPlan>(null, issues);
        }

        var orderedFiles = files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToImmutableArray();
        var identity = new TemplateIdentity(
            "authoring",
            $"{request.NamespaceId}.{request.PackageId}",
            $"{request.NamespaceId}.{request.PackageId}",
            request.Platforms.Length == 1 &&
            request.BuildSystems.Length == 1
                ? GetVariantId(
                    request.Platforms[0],
                    request.BuildSystems[0])
                : "multiple",
            request.Version);
        var messages = issues.ToImmutableArray();
        return new OperationResult<GenerationPlan>(
            new GenerationPlan(
                identity,
                directories.Order(StringComparer.Ordinal).ToImmutableArray(),
                orderedFiles,
                messages),
            messages);
    }

    private static void ValidateRequest(
        TemplateAuthoringRequest request,
        List<ValidationIssue> issues)
    {
        ValidateId(request.NamespaceId, "namespace", issues);
        ValidateId(request.PackageId, "package", issues);
        ValidateId(request.Language, "language", issues);

        if (request.BuildSystems.IsDefaultOrEmpty)
        {
            issues.Add(Error(
                "authoring.build_system_required",
                "Select at least one build system."));
        }
        else
        {
            var buildSystems = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var buildSystem in request.BuildSystems)
            {
                ValidateId(buildSystem, "build system", issues);
                if (!buildSystems.Add(buildSystem))
                {
                    issues.Add(Error(
                        "authoring.build_system_duplicate",
                        $"Build system '{buildSystem}' is selected more than once."));
                }
            }

            if (buildSystems.Contains("none") && buildSystems.Count > 1)
            {
                issues.Add(Error(
                    "authoring.build_system_none_exclusive",
                    "'No build system' cannot be combined with another build system."));
            }
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            issues.Add(Error(
                "authoring.name_required",
                "Enter a human-readable package name."));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            issues.Add(Error(
                "authoring.description_required",
                "Describe what projects this template creates."));
        }

        if (!VersionPattern().IsMatch(request.Version))
        {
            issues.Add(Error(
                "authoring.version_invalid",
                "Version must use semantic form such as 0.1.0."));
        }

        if (string.IsNullOrWhiteSpace(request.SourceLicense) ||
            string.IsNullOrWhiteSpace(request.LicenseSummary))
        {
            issues.Add(Error(
                "authoring.license_required",
                "Choose a source license or the explicit None option."));
        }

        if (request.Platforms.IsDefaultOrEmpty)
        {
            issues.Add(Error(
                "authoring.platform_required",
                "Select at least one target platform."));
        }
        else
        {
            var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var platform in request.Platforms)
            {
                ValidateId(platform, "platform", issues);
                if (!platforms.Add(platform))
                {
                    issues.Add(Error(
                        "authoring.platform_duplicate",
                        $"Platform '{platform}' is selected more than once."));
                }
            }

            if (platforms.Contains("any") && platforms.Count > 1)
            {
                issues.Add(Error(
                    "authoring.platform_any_exclusive",
                    "'Any platform' cannot be combined with a platform-specific target."));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ExistingContentPath))
        {
            string contentRoot;
            try
            {
                contentRoot = Path.GetFullPath(request.ExistingContentPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
            {
                issues.Add(Error(
                    "authoring.source_invalid",
                    $"The source folder path is invalid: {exception.Message}"));
                return;
            }

            if (!Directory.Exists(contentRoot))
            {
                issues.Add(Error(
                    "authoring.source_missing",
                    "The source content folder does not exist.",
                    contentRoot));
            }
            else if (new DirectoryInfo(contentRoot).Attributes.HasFlag(
                         FileAttributes.ReparsePoint))
            {
                issues.Add(Error(
                    "authoring.source_reparse",
                    "The source content folder cannot be a symbolic link or reparse point.",
                    contentRoot));
            }
        }
    }

    private static void ImportExistingContent(
        string sourceRoot,
        List<PlannedFile> files,
        HashSet<string> directories,
        List<ValidationIssue> issues)
    {
        var root = new DirectoryInfo(Path.GetFullPath(sourceRoot));
        var pending = new Stack<DirectoryInfo>();
        var importedFiles = 0;
        long importedBytes = 0;
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
                        "authoring.source_reparse",
                        "Symbolic links and reparse points are not copied into templates.",
                        entry.FullName));
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    if (ExcludedDirectoryNames.Contains(
                            child.Name,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning,
                            "authoring.source_excluded",
                            $"Excluded generated or tool-owned directory '{child.Name}'.",
                            Path: child.FullName));
                        continue;
                    }

                    pending.Push(child);
                    continue;
                }

                if (entry is not FileInfo file)
                {
                    continue;
                }

                importedFiles++;
                if (importedFiles > MaximumImportedFiles)
                {
                    issues.Add(Error(
                        "authoring.source_file_limit",
                        $"The source contains more than {MaximumImportedFiles} files. Remove generated or vendored content before importing.",
                        root.FullName));
                    return;
                }

                if (file.Length > MaximumImportedFileBytes)
                {
                    issues.Add(Error(
                        "authoring.source_file_too_large",
                        $"File '{file.Name}' exceeds the 64 MiB authoring limit.",
                        file.FullName));
                    continue;
                }

                importedBytes += file.Length;
                if (importedBytes > MaximumImportedBytes)
                {
                    issues.Add(Error(
                        "authoring.source_size_limit",
                        "The selected source exceeds the 512 MiB authoring limit. Remove generated or vendored content before importing.",
                        root.FullName));
                    return;
                }

                var relative = Path.GetRelativePath(root.FullName, file.FullName)
                    .Replace('\\', '/');
                var destination = $"content/{relative}";
                var bytes = File.ReadAllBytes(file.FullName).ToImmutableArray();
                string? text = null;
                try
                {
                    text = StrictUtf8.GetString(bytes.AsSpan());
                }
                catch (DecoderFallbackException)
                {
                    // Binary files remain byte-for-byte payload.
                }

                files.Add(new PlannedFile(
                    destination,
                    bytes,
                    text is not null,
                    text,
                    file.FullName));
                AddParentDirectories(destination, directories);
            }
        }
    }

    private static void AddSeedFiles(
        IEnumerable<TemplateAuthoringSeedFile> seeds,
        List<PlannedFile> files,
        HashSet<string> directories,
        List<ValidationIssue> issues)
    {
        foreach (var seed in seeds
                     .Where(seed => !seed.VariantSpecific)
                     .OrderBy(seed => seed.RelativePath, StringComparer.Ordinal))
        {
            AddTextFile(
                $"content/{seed.RelativePath}",
                seed.Content,
                files,
                directories,
                issues);
        }
    }

    private static void AddTextFile(
        string relativePath,
        string content,
        List<PlannedFile> files,
        HashSet<string> directories,
        List<ValidationIssue> issues)
    {
        if (files.Any(file => file.RelativePath.Equals(
                relativePath,
                StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "authoring.seed_conflict",
                $"Skipped generated starter file '{relativePath}' because imported content already uses that path.",
                Path: relativePath));
            return;
        }

        files.Add(TextFile(relativePath, content));
        AddParentDirectories(relativePath, directories);
    }

    private static void ValidatePlannedPaths(
        IEnumerable<PlannedFile> files,
        IEnumerable<string> directories,
        List<ValidationIssue> issues)
    {
        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            issues.AddRange(SafePath.NormalizeRelative(directory).Issues);
        }

        foreach (var file in files)
        {
            issues.AddRange(SafePath.NormalizeRelative(file.RelativePath).Issues);
            if (!filePaths.Add(file.RelativePath))
            {
                issues.Add(Error(
                    "authoring.path_duplicate",
                    $"More than one file would be written to '{file.RelativePath}'.",
                    file.RelativePath));
            }
        }

        foreach (var file in filePaths)
        {
            foreach (var parent in GetParents(file))
            {
                if (filePaths.Contains(parent))
                {
                    issues.Add(Error(
                        "authoring.path_collision",
                        $"'{parent}' would be both a file and a directory.",
                        parent));
                }
            }
        }
    }

    private static string BuildPackageToml(TemplateAuthoringRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("schema_version = 0");
        builder.AppendLine();
        AppendTomlString(builder, "namespace", request.NamespaceId);
        AppendTomlString(builder, "id", request.PackageId);
        builder.AppendLine();
        AppendTomlString(builder, "name", request.Name);
        AppendTomlString(builder, "description", request.Description);
        builder.AppendLine();
        AppendTomlString(builder, "language", request.Language);
        AppendTomlString(builder, "source_license", request.SourceLicense);
        AppendTomlString(builder, "license_summary", request.LicenseSummary);
        var tags = request.Tags.IsDefaultOrEmpty
            ? ImmutableArray.Create(request.Language, "starter")
            : request.Tags
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray();
        builder
            .Append("tags = [")
            .Append(string.Join(", ", tags.Select(QuoteToml)))
            .AppendLine("]");

        var parameters = request.Parameters.IsDefaultOrEmpty
            ?
            [
                new TemplateParameterDefinition(
                    "project_name",
                    TemplateParameterType.Text,
                    "Project name",
                    "Name used by generated project files.",
                    Required: true,
                    DefaultValue: "MyProject",
                    Validation: null,
                    Values: []),
            ]
            : request.Parameters;
        foreach (var parameter in parameters)
        {
            builder.AppendLine();
            builder.AppendLine("[[parameters]]");
            AppendTomlString(builder, "id", parameter.Id);
            AppendTomlString(
                builder,
                "type",
                parameter.Type switch
                {
                    TemplateParameterType.Text => "string",
                    TemplateParameterType.Boolean => "boolean",
                    TemplateParameterType.Choice => "choice",
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(request),
                        $"Unsupported parameter type '{parameter.Type}'."),
                });
            AppendTomlString(builder, "label", parameter.Label);
            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                AppendTomlString(
                    builder,
                    "description",
                    parameter.Description);
            }

            builder.Append("required = ")
                .AppendLine(parameter.Required ? "true" : "false");
            if (parameter.DefaultValue is bool booleanDefault)
            {
                builder.Append("default = ")
                    .AppendLine(booleanDefault ? "true" : "false");
            }
            else if (parameter.DefaultValue is not null)
            {
                AppendTomlString(
                    builder,
                    "default",
                    Convert.ToString(
                        parameter.DefaultValue,
                        System.Globalization.CultureInfo.InvariantCulture) ??
                    string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(parameter.Validation))
            {
                AppendTomlString(
                    builder,
                    "validation",
                    parameter.Validation);
            }

            if (!parameter.Values.IsDefaultOrEmpty)
            {
                builder
                    .Append("values = [")
                    .Append(string.Join(
                        ", ",
                        parameter.Values.Select(QuoteToml)))
                    .AppendLine("]");
            }
        }

        return builder.ToString();
    }

    private static string BuildVariantToml(
        TemplateAuthoringRequest request,
        string platform,
        string buildSystem,
        string variantId)
    {
        var builder = new StringBuilder();
        builder.AppendLine("schema_version = 0");
        builder.AppendLine();
        AppendTomlString(builder, "id", variantId);
        AppendTomlString(
            builder,
            "description",
            $"{request.Name} for {platform} using {buildSystem}.");
        AppendTomlString(builder, "version", request.Version);
        builder.AppendLine();
        AppendTomlString(builder, "target_os", platform);
        AppendTomlString(builder, "build_system", buildSystem);
        foreach (var prerequisite in request.Prerequisites.IsDefault
                     ? []
                     : request.Prerequisites)
        {
            builder.AppendLine();
            builder.AppendLine("[[prerequisites]]");
            AppendTomlString(builder, "id", prerequisite.Id);
            AppendTomlString(builder, "name", prerequisite.Name);
            AppendTomlString(
                builder,
                "description",
                prerequisite.Description);
            AppendTomlString(
                builder,
                "required_for",
                prerequisite.RequiredFor);
        }

        return builder.ToString();
    }

    private static string BuildReadme(TemplateAuthoringRequest request) =>
        "# {{ project_name }}\n\n" +
        $"Generated from the {request.Name} Klonker template.\n\n" +
        "## Requirements\n\n" +
        $"- Platform: {string.Join(", ", request.Platforms)}\n" +
        $"- Language: {request.Language}\n" +
        $"- Build system: {string.Join(", ", request.BuildSystems)}\n\n" +
        $"Generated source license: {request.SourceLicense}\n";

    private static PlannedFile TextFile(string path, string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return new PlannedFile(
            path,
            StrictUtf8.GetBytes(normalized).ToImmutableArray(),
            IsText: true,
            normalized,
            "[template wizard]");
    }

    private static void AddParentDirectories(
        string relativePath,
        HashSet<string> directories)
    {
        foreach (var parent in GetParents(relativePath))
        {
            directories.Add(parent);
        }
    }

    private static IEnumerable<string> GetParents(string relativePath)
    {
        var segments = relativePath.Split('/');
        for (var length = 1; length < segments.Length; length++)
        {
            yield return string.Join('/', segments.Take(length));
        }
    }

    private static string GetVariantId(
        string platform,
        string buildSystem) =>
        buildSystem == "none"
            ? platform
            : $"{platform}-{buildSystem}";

    private static void ValidateId(
        string value,
        string label,
        List<ValidationIssue> issues)
    {
        if (!IdPattern().IsMatch(value))
        {
            issues.Add(Error(
                $"authoring.{label.Replace(' ', '_')}_invalid",
                $"{char.ToUpperInvariant(label[0])}{label[1..]} ID '{value}' must start with a lowercase letter and contain only lowercase letters, numbers, and hyphens."));
        }
    }

    private static void AppendTomlString(
        StringBuilder builder,
        string key,
        string value)
    {
        builder
            .Append(key)
            .Append(" = ")
            .AppendLine(QuoteToml(value));
    }

    private static string QuoteToml(string value) =>
        $"\"{value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)}\"";

    private static ValidationIssue Error(
        string code,
        string message,
        string? path = null) =>
        new(ValidationSeverity.Error, code, message, Path: path);

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
