using System.Collections.Immutable;
using System.IO.Compression;
using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;

namespace Klonker.Core.Registry;

public static class PackageArchiveExtractor
{
    private const int MaximumEntries = 10_000;
    private const long MaximumFileBytes = 64L * 1024 * 1024;
    private const long MaximumExpandedBytes = 512L * 1024 * 1024;

    public static async Task<OperationResult<string>> ExtractAsync(
        string archivePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var archiveFullPath = Path.GetFullPath(archivePath);
        var destination = Path.GetFullPath(destinationPath);
        var parent = Directory.GetParent(destination);
        if (parent is null)
        {
            return Failure(
                "registry.cache_path_invalid",
                "The package cache destination has no parent directory.",
                destination);
        }

        Directory.CreateDirectory(parent.FullName);
        var staging = Path.Combine(
            parent.FullName,
            $".{Path.GetFileName(destination)}-{Guid.NewGuid():N}.staging");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = ValidateArchive(archiveFullPath);
            if (!validation.IsSuccess)
            {
                return new OperationResult<string>(null, validation.Issues);
            }

            Directory.CreateDirectory(staging);
            using var archive = ZipFile.OpenRead(archiveFullPath);
            foreach (var item in validation.Value!.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolved = SafePath.ResolveUnderRoot(staging, item.RelativePath);
                if (!resolved.IsSuccess)
                {
                    return new OperationResult<string>(null, resolved.Issues);
                }

                if (item.IsDirectory)
                {
                    Directory.CreateDirectory(resolved.Value!);
                    continue;
                }

                var outputDirectory = Path.GetDirectoryName(resolved.Value!);
                if (outputDirectory is not null)
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var entry = archive.Entries[item.EntryIndex];
                await using var input = entry.Open();
                await using var output = new FileStream(
                    resolved.Value!,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination))
            {
                if (new DirectoryInfo(destination).Attributes.HasFlag(
                        FileAttributes.ReparsePoint))
                {
                    return Failure(
                        "registry.cache_reparse_point",
                        "The package cache destination cannot be a symbolic link or reparse point.",
                        destination);
                }

                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(staging, destination);
            return new OperationResult<string>(destination, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
        {
            return Failure(
                "registry.package_extract_failed",
                $"The cached package could not be extracted safely: {exception.Message}",
                archiveFullPath);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static OperationResult<ArchiveLayout> ValidateArchive(
        string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count > MaximumEntries)
            {
                return Failure<ArchiveLayout>(
                    "registry.package_too_many_entries",
                    $"Package archives may contain at most {MaximumEntries} entries.",
                    archivePath);
            }

            var issues = new List<ValidationIssue>();
            var items = ImmutableArray.CreateBuilder<ArchiveItem>();
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;

            for (var index = 0; index < archive.Entries.Count; index++)
            {
                var entry = archive.Entries[index];
                if (IsLinkOrReparsePoint(entry))
                {
                    issues.Add(Error(
                        "registry.package_link",
                        "Package archives cannot contain symbolic links or reparse points.",
                        entry.FullName));
                    continue;
                }

                var isDirectory =
                    entry.FullName.EndsWith('/') ||
                    entry.FullName.EndsWith('\\');
                var candidate = isDirectory
                    ? entry.FullName.TrimEnd('/', '\\')
                    : entry.FullName;
                if (candidate.Length == 0)
                {
                    continue;
                }

                var normalized = SafePath.NormalizeRelative(candidate);
                issues.AddRange(normalized.Issues);
                if (!normalized.IsSuccess)
                {
                    continue;
                }

                var relativePath = normalized.Value!;
                if (isDirectory)
                {
                    if (!directories.Add(relativePath))
                    {
                        continue;
                    }

                    if (files.Contains(relativePath))
                    {
                        issues.Add(Collision(relativePath));
                    }
                }
                else
                {
                    if (entry.Length > MaximumFileBytes)
                    {
                        issues.Add(Error(
                            "registry.package_file_too_large",
                            $"Archive entry '{relativePath}' exceeds the {MaximumFileBytes} byte limit.",
                            relativePath));
                    }

                    expandedBytes = checked(expandedBytes + entry.Length);
                    if (expandedBytes > MaximumExpandedBytes)
                    {
                        issues.Add(Error(
                            "registry.package_expanded_too_large",
                            $"The expanded package exceeds the {MaximumExpandedBytes} byte limit.",
                            archivePath));
                    }

                    if (!files.Add(relativePath))
                    {
                        issues.Add(Error(
                            "registry.package_duplicate_path",
                            $"Archive destination '{relativePath}' appears more than once.",
                            relativePath));
                    }

                    if (directories.Contains(relativePath))
                    {
                        issues.Add(Collision(relativePath));
                    }
                }

                foreach (var parent in GetParentPaths(relativePath))
                {
                    if (files.Contains(parent))
                    {
                        issues.Add(Collision(parent));
                    }

                    directories.Add(parent);
                }

                items.Add(new ArchiveItem(index, relativePath, isDirectory));
            }

            foreach (var file in files)
            {
                if (directories.Contains(file))
                {
                    issues.Add(Collision(file));
                }
            }

            if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
            {
                return new OperationResult<ArchiveLayout>(null, issues);
            }

            return new OperationResult<ArchiveLayout>(
                new ArchiveLayout(items.ToImmutable()),
                issues);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
        {
            return Failure<ArchiveLayout>(
                "registry.package_archive_invalid",
                $"The package archive is invalid: {exception.Message}",
                archivePath);
        }
    }

    private static bool IsLinkOrReparsePoint(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixMode == UnixSymbolicLink ||
               windowsAttributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static IEnumerable<string> GetParentPaths(string path)
    {
        var segments = path.Split('/');
        for (var length = 1; length < segments.Length; length++)
        {
            yield return string.Join('/', segments.Take(length));
        }
    }

    private static ValidationIssue Collision(string path) =>
        Error(
            "registry.package_file_directory_collision",
            $"Archive destination '{path}' is both a file and a directory.",
            path);

    private static ValidationIssue Error(string code, string message, string path) =>
        new(ValidationSeverity.Error, code, message, Path: path);

    private static OperationResult<string> Failure(
        string code,
        string message,
        string path) =>
        Failure<string>(code, message, path);

    private static OperationResult<T> Failure<T>(
        string code,
        string message,
        string path)
        where T : class =>
        new(null, [Error(code, message, path)]);

    private sealed record ArchiveItem(
        int EntryIndex,
        string RelativePath,
        bool IsDirectory);

    private sealed record ArchiveLayout(ImmutableArray<ArchiveItem> Items);
}
