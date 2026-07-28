using System.Collections.Immutable;
using System.Text;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Paths;
using Klonker.Core.Templates;

namespace Klonker.Core.Modules;

public static class ModulePlanner
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<OperationResult<ModuleGenerationPlan>> CreatePlanAsync(
        ModulePackage package,
        IReadOnlyDictionary<string, object?>? suppliedValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        suppliedValues ??= new Dictionary<string, object?>();

        var issues = new List<ValidationIssue>();
        var slotValues = ResolveSlots(package.Manifest, suppliedValues, issues);
        var parameterManifest = new TemplateManifest(
            0,
            package.Manifest.Id,
            "module",
            "module",
            package.Manifest.Name,
            package.Manifest.Description,
            package.Manifest.Version,
            "any",
            "none",
            package.Manifest.SourceLicense,
            package.Manifest.Parameters.AddRange(
                package.Manifest.Slots.Select(slot =>
                    new TemplateParameterDefinition(
                        slot.Id,
                        TemplateParameterType.Text,
                        slot.Label,
                        slot.Description,
                        slot.Required,
                        slot.DefaultPath,
                        null,
                        []))),
            Tags: package.Manifest.Tags,
            Language: package.Manifest.Language);

        var resolved = ParameterResolver.Resolve(parameterManifest, suppliedValues);
        issues.AddRange(resolved.Issues);
        if (!resolved.IsSuccess ||
            issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<ModuleGenerationPlan>(null, issues);
        }

        foreach (var parameter in package.Manifest.Parameters
                     .Where(parameter => parameter.Type == TemplateParameterType.Text))
        {
            if (resolved.Value!.Values.TryGetValue(parameter.Id, out var raw) &&
                raw is string text &&
                (text.Contains('/') || text.Contains('\\')))
            {
                issues.Add(Error(
                    "module.parameter_separator",
                    $"Parameter '{parameter.Label}' cannot introduce path separators. Use a module slot for configurable paths.",
                    parameter.Id));
            }
        }

        var plannedFiles = new List<PlannedFile>();
        foreach (var source in package.SourceFiles
                     .OrderBy(file => file.RelativePath, StringComparer.Ordinal))
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

            var info = new FileInfo(sourceResolution.Value!);
            if (!info.Exists ||
                info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                issues.Add(Error(
                    "module.source_invalid",
                    $"Module content file '{source.RelativePath}' is missing or is a reparse point.",
                    path: source.RelativePath));
                continue;
            }

            var renderedPath = RestrictedTemplateRenderer.Render(
                source.RelativePath,
                $"{source.RelativePath} (module path)",
                resolved.Value!);
            issues.AddRange(renderedPath.Issues);
            if (!renderedPath.IsSuccess)
            {
                continue;
            }

            var outputPath = renderedPath.Value!;
            if (source.IsTextTemplate &&
                outputPath.EndsWith(".sbn", StringComparison.Ordinal))
            {
                outputPath = outputPath[..^4];
            }

            var normalized = SafePath.NormalizeRelative(outputPath);
            issues.AddRange(normalized.Issues);
            if (!normalized.IsSuccess)
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(
                    sourceResolution.Value!,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!source.IsTextTemplate)
            {
                plannedFiles.Add(new PlannedFile(
                    normalized.Value!,
                    bytes.ToImmutableArray(),
                    false,
                    null,
                    source.RelativePath));
                continue;
            }

            string sourceText;
            try
            {
                sourceText = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                issues.Add(Error(
                    "module.utf8",
                    $"Text module file '{source.RelativePath}' is not valid UTF-8.",
                    path: source.RelativePath));
                continue;
            }

            var rendered = RestrictedTemplateRenderer.Render(
                sourceText,
                source.RelativePath,
                resolved.Value!);
            issues.AddRange(rendered.Issues);
            if (rendered.IsSuccess)
            {
                plannedFiles.Add(new PlannedFile(
                    normalized.Value!,
                    StrictUtf8.GetBytes(rendered.Value!).ToImmutableArray(),
                    true,
                    rendered.Value,
                    source.RelativePath));
            }
        }

        ValidateCollisions(plannedFiles, issues);
        string? instructions = null;
        if (!string.IsNullOrWhiteSpace(package.Manifest.PostGenerationInstructions))
        {
            var rendered = RestrictedTemplateRenderer.Render(
                package.Manifest.PostGenerationInstructions,
                "post_generation_instructions",
                resolved.Value!);
            issues.AddRange(rendered.Issues);
            instructions = rendered.Value?.Trim();
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<ModuleGenerationPlan>(null, issues);
        }

        var files = plannedFiles
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToImmutableArray();
        var directories = files
            .SelectMany(file => Parents(file.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var identity = new TemplateIdentity(
            package.RegistryId,
            package.Manifest.Id,
            "module",
            "module",
            package.Manifest.Version);
        var messages = issues.ToImmutableArray();
        var filePlan = new GenerationPlan(identity, directories, files, messages);

        return new OperationResult<ModuleGenerationPlan>(
            new ModuleGenerationPlan(
                package.RegistryId,
                package.Manifest.Id,
                package.Manifest.Version,
                filePlan,
                slotValues,
                new ModuleLicenseReport(
                    package.Manifest.SourceLicense,
                    package.Manifest.Dependencies),
                instructions,
                messages),
            messages);
    }

    private static ImmutableDictionary<string, string> ResolveSlots(
        ModuleManifest manifest,
        IReadOnlyDictionary<string, object?> suppliedValues,
        List<ValidationIssue> issues)
    {
        var result = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        foreach (var slot in manifest.Slots)
        {
            var value = suppliedValues.TryGetValue(slot.Id, out var supplied)
                ? supplied as string
                : slot.DefaultPath;
            if (string.IsNullOrWhiteSpace(value))
            {
                if (slot.Required)
                {
                    issues.Add(Error(
                        "module.slot_required",
                        $"Slot '{slot.Label}' requires a relative destination path.",
                        slot.Id));
                }

                continue;
            }

            var normalized = SafePath.NormalizeRelative(value);
            if (!normalized.IsSuccess)
            {
                issues.AddRange(normalized.Issues.Select(issue =>
                    issue with
                    {
                        Code = "module.slot_path_invalid",
                        ParameterId = slot.Id,
                    }));
                continue;
            }

            result[slot.Id] = normalized.Value!;
        }

        return result.ToImmutable();
    }

    private static void ValidateCollisions(
        IEnumerable<PlannedFile> plannedFiles,
        List<ValidationIssue> issues)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plannedFiles)
        {
            if (!files.Add(file.RelativePath))
            {
                issues.Add(Error(
                    "module.duplicate_destination",
                    $"More than one module file renders to '{file.RelativePath}'.",
                    path: file.RelativePath));
            }
        }

        foreach (var file in files)
        {
            foreach (var parent in Parents(file))
            {
                if (files.Contains(parent))
                {
                    issues.Add(Error(
                        "module.file_directory_collision",
                        $"'{parent}' is planned as both a file and a directory.",
                        path: parent));
                }
            }
        }
    }

    private static IEnumerable<string> Parents(string path)
    {
        var segments = path.Replace('\\', '/').Split('/');
        for (var length = 1; length < segments.Length; length++)
        {
            yield return string.Join('/', segments.Take(length));
        }
    }

    private static ValidationIssue Error(
        string code,
        string message,
        string? parameterId = null,
        string? path = null) =>
        new(ValidationSeverity.Error, code, message, parameterId, path);
}
