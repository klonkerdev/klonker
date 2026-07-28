using System.Collections.Immutable;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Paths;

namespace Klonker.Core.Modules;

public static class ModuleGenerationExecutor
{
    public static async Task<GenerationResult> ExecuteAsync(
        ModuleGenerationPlan plan,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string destination;
        try
        {
            destination = Path.GetFullPath(destinationPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Rejected(
                "module.destination_invalid",
                $"The module destination is invalid: {exception.Message}");
        }

        if (!Directory.Exists(destination))
        {
            return Rejected(
                "module.destination_missing",
                "The module destination must be an existing directory.");
        }

        var preflight = Preflight(plan, destination);
        if (!preflight.IsDefaultOrEmpty)
        {
            return new GenerationResult(
                GenerationStatus.Rejected,
                "The module cannot be generated until the destination conflicts are resolved.",
                preflight);
        }

        var parent = Directory.GetParent(destination);
        if (parent is null)
        {
            return Rejected(
                "module.destination_root",
                "Modules cannot be generated directly into a filesystem root.");
        }

        var staging = Path.Combine(
            parent.FullName,
            $".klonker-module-{Guid.NewGuid():N}.staging");
        var installedFiles = new List<string>();
        var createdDirectories = new List<string>();
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var file in plan.FilePlan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var staged = SafePath.ResolveUnderRoot(staging, file.RelativePath);
                if (!staged.IsSuccess)
                {
                    return new GenerationResult(
                        GenerationStatus.Rejected,
                        "The module plan contains an unsafe path.",
                        staged.Issues);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(staged.Value!)!);
                await File.WriteAllBytesAsync(
                        staged.Value!,
                        file.Content.ToArray(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var secondPreflight = Preflight(plan, destination);
            if (!secondPreflight.IsDefaultOrEmpty)
            {
                return new GenerationResult(
                    GenerationStatus.Rejected,
                    "The destination changed while the module was being prepared. Resolve the conflicts and retry.",
                    secondPreflight);
            }

            foreach (var directory in plan.FilePlan.Directories
                         .OrderBy(path => path.Count(character => character == '/'))
                         .ThenBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var finalDirectory = SafePath.ResolveUnderRoot(destination, directory);
                if (!finalDirectory.IsSuccess)
                {
                    return new GenerationResult(
                        GenerationStatus.Rejected,
                        "The module plan contains an unsafe directory.",
                        finalDirectory.Issues);
                }

                if (!Directory.Exists(finalDirectory.Value!))
                {
                    Directory.CreateDirectory(finalDirectory.Value!);
                    createdDirectories.Add(finalDirectory.Value!);
                }
            }

            foreach (var file in plan.FilePlan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var staged = SafePath.ResolveUnderRoot(staging, file.RelativePath).Value!;
                var final = SafePath.ResolveUnderRoot(destination, file.RelativePath).Value!;
                File.Move(staged, final);
                installedFiles.Add(final);

                var installedBytes = await File.ReadAllBytesAsync(final, cancellationToken)
                    .ConfigureAwait(false);
                if (!installedBytes.AsSpan().SequenceEqual(file.Content.AsSpan()))
                {
                    throw new IOException(
                        $"Generated module file '{file.RelativePath}' could not be verified.");
                }
            }

            var instructionSuffix = string.IsNullOrWhiteSpace(
                plan.PostGenerationInstructions)
                ? string.Empty
                : " Review the post-generation instructions shown by Klonker.";
            return new GenerationResult(
                GenerationStatus.Succeeded,
                $"Generated {plan.FilePlan.Files.Length} module files and verified them successfully.{instructionSuffix}",
                []);
        }
        catch (OperationCanceledException)
        {
            RollBack(installedFiles, createdDirectories);
            return new GenerationResult(
                GenerationStatus.Cancelled,
                "Module generation was cancelled and newly written files were removed.",
                []);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            RollBack(installedFiles, createdDirectories);
            return new GenerationResult(
                GenerationStatus.Failed,
                "Klonker could not install the module. Files written by this attempt were removed.",
                [],
                exception);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    public static ImmutableArray<ValidationIssue> Preflight(
        ModuleGenerationPlan plan,
        string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        string destination;
        try
        {
            destination = Path.GetFullPath(destinationPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            issues.Add(Issue(
                "module.destination_invalid",
                $"The module destination is invalid: {exception.Message}",
                destinationPath));
            return issues.ToImmutable();
        }

        if (!Directory.Exists(destination))
        {
            issues.Add(Issue(
                "module.destination_missing",
                "The module destination must be an existing directory.",
                destination));
            return issues.ToImmutable();
        }

        if (new DirectoryInfo(destination).Attributes.HasFlag(
                FileAttributes.ReparsePoint))
        {
            issues.Add(Issue(
                "module.destination_reparse",
                "The module destination cannot be a symbolic link or reparse point.",
                destination));
            return issues.ToImmutable();
        }

        foreach (var directory in plan.FilePlan.Directories)
        {
            var resolved = SafePath.ResolveUnderRoot(destination, directory);
            issues.AddRange(resolved.Issues);
            if (!resolved.IsSuccess)
            {
                continue;
            }

            if (File.Exists(resolved.Value!))
            {
                issues.Add(Issue(
                    "module.directory_conflict",
                    $"A file already occupies planned directory '{directory}'. Remove or rename it, then retry.",
                    directory));
            }
            else
            {
                ValidateExistingChain(destination, resolved.Value!, issues);
            }
        }

        foreach (var file in plan.FilePlan.Files)
        {
            var resolved = SafePath.ResolveUnderRoot(destination, file.RelativePath);
            issues.AddRange(resolved.Issues);
            if (!resolved.IsSuccess)
            {
                continue;
            }

            if (File.Exists(resolved.Value!) || Directory.Exists(resolved.Value!))
            {
                issues.Add(Issue(
                    "module.file_conflict",
                    $"Planned file '{file.RelativePath}' already exists. Klonker will not overwrite it; remove or rename it, then retry.",
                    file.RelativePath));
            }

            ValidateExistingChain(destination, Path.GetDirectoryName(resolved.Value!)!, issues);
        }

        return issues
            .DistinctBy(issue => (issue.Code, issue.Path))
            .ToImmutableArray();
    }

    private static void ValidateExistingChain(
        string root,
        string candidate,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        var current = new DirectoryInfo(candidate);
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        while (current is not null)
        {
            var currentPath =
                Path.TrimEndingDirectorySeparator(current.FullName);
            if (current.Exists &&
                current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                issues.Add(Issue(
                    "module.destination_reparse",
                    $"Existing destination directory '{current.FullName}' is a symbolic link or reparse point.",
                    current.FullName));
            }

            if (string.Equals(
                    currentPath,
                    rootPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = current.Parent;
        }
    }

    private static void RollBack(
        IEnumerable<string> files,
        IEnumerable<string> directories)
    {
        foreach (var file in files.Reverse())
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // Best-effort rollback; the diagnostic exception remains on the result.
            }
        }

        foreach (var directory in directories
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception)
            {
                // Best-effort rollback.
            }
        }
    }

    private static GenerationResult Rejected(string code, string message) =>
        new(GenerationStatus.Rejected, message, [Issue(code, message)]);

    private static ValidationIssue Issue(
        string code,
        string message,
        string? path = null) =>
        new(ValidationSeverity.Error, code, message, Path: path);
}
