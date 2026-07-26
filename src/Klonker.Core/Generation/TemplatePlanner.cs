using System.Collections.Immutable;
using System.Text;
using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;
using Klonker.Core.Templates;

namespace Klonker.Core.Generation;

public static class TemplatePlanner
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<OperationResult<GenerationPlan>> CreatePlanAsync(
        TemplatePackage package,
        IReadOnlyDictionary<string, object?>? suppliedValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var parameterResult = ParameterResolver.Resolve(package.Manifest, suppliedValues);
        if (!parameterResult.IsSuccess)
        {
            return new OperationResult<GenerationPlan>(null, parameterResult.Issues);
        }

        var issues = new List<ValidationIssue>(parameterResult.Issues);
        var plannedFiles = new List<PlannedFile>();

        foreach (var source in package.SourceFiles.OrderBy(
                     file => file.RelativePath,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceResolution = SafePath.ResolveUnderRoot(
                package.ContentPath,
                source.RelativePath);
            issues.AddRange(sourceResolution.Issues);
            if (!sourceResolution.IsSuccess)
            {
                continue;
            }

            var sourcePath = sourceResolution.Value!;
            var sourceInfo = new FileInfo(sourcePath);
            if (!sourceInfo.Exists)
            {
                issues.Add(Error(
                    "package.source_missing",
                    $"Template content file '{source.RelativePath}' no longer exists.",
                    source.RelativePath));
                continue;
            }

            if (sourceInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                issues.Add(Error(
                    "package.reparse_point",
                    "Template content cannot contain symbolic links or reparse points.",
                    source.RelativePath));
                continue;
            }

            var renderedPathResult = RenderPath(
                source.RelativePath,
                source.IsTextTemplate,
                parameterResult.Value!);
            issues.AddRange(renderedPathResult.Issues);
            if (!renderedPathResult.IsSuccess)
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);
            if (source.IsTextTemplate)
            {
                string templateText;
                try
                {
                    templateText = StrictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    issues.Add(Error(
                        "template.utf8",
                        $"Text template '{source.RelativePath}' is not valid UTF-8.",
                        source.RelativePath));
                    continue;
                }

                var renderResult = RestrictedTemplateRenderer.Render(
                    templateText,
                    source.RelativePath,
                    parameterResult.Value!);
                issues.AddRange(renderResult.Issues);
                if (!renderResult.IsSuccess)
                {
                    continue;
                }

                var textContent = renderResult.Value!;
                plannedFiles.Add(new PlannedFile(
                    renderedPathResult.Value!,
                    StrictUtf8.GetBytes(textContent).ToImmutableArray(),
                    IsText: true,
                    textContent,
                    source.RelativePath));
            }
            else
            {
                plannedFiles.Add(new PlannedFile(
                    renderedPathResult.Value!,
                    bytes.ToImmutableArray(),
                    IsText: false,
                    TextContent: null,
                    source.RelativePath));
            }
        }

        ValidateDestinationCollisions(plannedFiles, issues);
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<GenerationPlan>(null, issues);
        }

        var orderedFiles = plannedFiles
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToImmutableArray();
        var directories = GetDirectories(orderedFiles);
        var identity = new TemplateIdentity(
            package.RegistryId,
            package.Manifest.Id,
            package.Manifest.FamilyId,
            package.Manifest.VariantId,
            package.Manifest.Version);
        var messages = issues.ToImmutableArray();

        return new OperationResult<GenerationPlan>(
            new GenerationPlan(identity, directories, orderedFiles, messages),
            messages);
    }

    private static OperationResult<string> RenderPath(
        string sourceRelativePath,
        bool isTextTemplate,
        ResolvedParameters parameters)
    {
        var issues = new List<ValidationIssue>();
        var renderedSegments = new List<string>();
        var sourceSegments = sourceRelativePath.Split('/');

        for (var index = 0; index < sourceSegments.Length; index++)
        {
            var sourceSegment = sourceSegments[index];
            var segmentResult = RestrictedTemplateRenderer.Render(
                sourceSegment,
                $"{sourceRelativePath} (path segment {index + 1})",
                parameters);
            issues.AddRange(segmentResult.Issues);
            if (!segmentResult.IsSuccess)
            {
                continue;
            }

            var renderedSegment = segmentResult.Value!;
            if (renderedSegment.Contains('/') || renderedSegment.Contains('\\'))
            {
                issues.Add(Error(
                    "path.injected_separator",
                    "A rendered path segment cannot introduce a directory separator.",
                    sourceRelativePath));
                continue;
            }

            renderedSegments.Add(renderedSegment);
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<string>(null, issues);
        }

        var renderedPath = string.Join('/', renderedSegments);
        if (isTextTemplate &&
            renderedPath.EndsWith(".sbn", StringComparison.Ordinal))
        {
            renderedPath = renderedPath[..^4];
        }

        var normalized = SafePath.NormalizeRelative(renderedPath);
        issues.AddRange(normalized.Issues);
        return new OperationResult<string>(
            normalized.IsSuccess ? normalized.Value : null,
            issues);
    }

    private static void ValidateDestinationCollisions(
        IEnumerable<PlannedFile> files,
        List<ValidationIssue> issues)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!paths.Add(file.RelativePath))
            {
                issues.Add(Error(
                    "plan.duplicate_destination",
                    $"More than one template file renders to '{file.RelativePath}'.",
                    file.RelativePath));
            }
        }

        foreach (var filePath in paths)
        {
            foreach (var parent in GetParentPaths(filePath))
            {
                if (paths.Contains(parent))
                {
                    issues.Add(Error(
                        "plan.file_directory_collision",
                        $"'{parent}' is planned as both a file and a directory.",
                        parent));
                }
            }
        }
    }

    private static ImmutableArray<string> GetDirectories(IEnumerable<PlannedFile> files)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            foreach (var parent in GetParentPaths(file.RelativePath))
            {
                directories.Add(parent);
            }
        }

        return directories
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static IEnumerable<string> GetParentPaths(string relativePath)
    {
        var segments = relativePath.Split('/');
        for (var length = 1; length < segments.Length; length++)
        {
            yield return string.Join('/', segments.Take(length));
        }
    }

    private static ValidationIssue Error(string code, string message, string path) =>
        new(ValidationSeverity.Error, code, message, Path: path);
}
