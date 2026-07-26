using System.Collections.Immutable;
using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;

namespace Klonker.Core.Generation;

public static class GenerationExecutor
{
    public static async Task<GenerationResult> ExecuteAsync(
        GenerationPlan plan,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var destinationValidation = GenerationDestinationValidator.Validate(destinationPath);
        if (!destinationValidation.IsSuccess)
        {
            return new GenerationResult(
                GenerationStatus.Rejected,
                destinationValidation.Issues[0].Message,
                destinationValidation.Issues);
        }

        var destination = destinationValidation.Value!;
        var parent = Directory.GetParent(destination)!;
        var destinationExists = Directory.Exists(destination);
        var planValidation = ValidatePlan(plan);
        if (planValidation.Length > 0)
        {
            return new GenerationResult(
                GenerationStatus.Rejected,
                "The generation plan contains unsafe or conflicting paths.",
                planValidation);
        }

        var stagingName = $".klonker-{Path.GetFileName(destination)}-{Guid.NewGuid():N}.staging";
        var staging = Path.Combine(parent.FullName, stagingName);
        var removedEmptyDestination = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(staging);

            foreach (var directory in plan.Directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolved = SafePath.ResolveUnderRoot(staging, directory);
                if (!resolved.IsSuccess)
                {
                    return new GenerationResult(
                        GenerationStatus.Rejected,
                        "The generation plan contains an unsafe directory path.",
                        resolved.Issues);
                }

                Directory.CreateDirectory(resolved.Value!);
            }

            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolved = SafePath.ResolveUnderRoot(staging, file.RelativePath);
                if (!resolved.IsSuccess)
                {
                    return new GenerationResult(
                        GenerationStatus.Rejected,
                        "The generation plan contains an unsafe file path.",
                        resolved.Issues);
                }

                var containingDirectory = Path.GetDirectoryName(resolved.Value!);
                if (containingDirectory is not null)
                {
                    Directory.CreateDirectory(containingDirectory);
                }

                await using var output = new FileStream(
                    resolved.Value!,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                await output.WriteAsync(file.Content.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (destinationExists)
            {
                if (Directory.EnumerateFileSystemEntries(destination).Any())
                {
                    return Rejected(
                        "destination.changed",
                        "The destination became non-empty while generation was in progress.");
                }

                Directory.Delete(destination);
                removedEmptyDestination = true;
            }

            Directory.Move(staging, destination);
            return new GenerationResult(
                GenerationStatus.Succeeded,
                $"Generated {plan.Files.Length} files successfully.",
                []);
        }
        catch (OperationCanceledException)
        {
            return new GenerationResult(
                GenerationStatus.Cancelled,
                "Generation was cancelled before the project was installed.",
                []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new GenerationResult(
                GenerationStatus.Failed,
                "Klonker could not write the generated project. No partial project was installed.",
                [],
                exception);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            if (removedEmptyDestination && !Directory.Exists(destination))
            {
                Directory.CreateDirectory(destination);
            }
        }
    }

    private static ImmutableArray<ValidationIssue> ValidatePlan(GenerationPlan plan)
    {
        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in plan.Directories)
        {
            var pathResult = SafePath.NormalizeRelative(directory);
            issues.AddRange(pathResult.Issues);
        }

        foreach (var file in plan.Files)
        {
            var pathResult = SafePath.NormalizeRelative(file.RelativePath);
            issues.AddRange(pathResult.Issues);
            if (!files.Add(file.RelativePath))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "plan.duplicate_destination",
                    $"The plan contains duplicate destination '{file.RelativePath}'.",
                    Path: file.RelativePath));
            }

            foreach (var segment in GetParentPaths(file.RelativePath))
            {
                if (files.Contains(segment))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "plan.file_directory_collision",
                        $"'{segment}' is planned as both a file and a directory.",
                        Path: segment));
                }
            }
        }

        foreach (var file in files)
        {
            foreach (var parent in GetParentPaths(file))
            {
                if (files.Contains(parent))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "plan.file_directory_collision",
                        $"'{parent}' is planned as both a file and a directory.",
                        Path: parent));
                }
            }
        }

        return issues.ToImmutable();
    }

    private static IEnumerable<string> GetParentPaths(string path)
    {
        var segments = path.Replace('\\', '/').Split('/');
        for (var length = 1; length < segments.Length; length++)
        {
            yield return string.Join('/', segments.Take(length));
        }
    }

    private static GenerationResult Rejected(string code, string message) =>
        new(
            GenerationStatus.Rejected,
            message,
            [new ValidationIssue(ValidationSeverity.Error, code, message)]);
}
