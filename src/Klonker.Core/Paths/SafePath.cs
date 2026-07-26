using System.Collections.Immutable;
using Klonker.Core.Diagnostics;

namespace Klonker.Core.Paths;

public static class SafePath
{
    private static readonly ImmutableHashSet<string> ReservedDeviceNames =
        new[]
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        }.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    public static OperationResult<string> NormalizeRelative(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var issues = new List<ValidationIssue>();
        if (path.Length == 0)
        {
            issues.Add(Error("path.empty", "A rendered path cannot be empty.", path));
            return new OperationResult<string>(null, issues);
        }

        if (path.Contains('\0', StringComparison.Ordinal))
        {
            issues.Add(Error("path.nul", "Paths cannot contain NUL characters.", path));
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
        {
            issues.Add(Error("path.unc", "UNC paths are not allowed.", path));
        }

        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            issues.Add(Error("path.drive_qualified", "Drive-qualified paths are not allowed.", path));
        }

        if (path[0] is '/' or '\\' || Path.IsPathRooted(path))
        {
            issues.Add(Error("path.rooted", "Rooted paths are not allowed.", path));
        }

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment.Length == 0))
        {
            issues.Add(Error("path.empty_segment", "Paths cannot contain empty segments.", path));
        }

        foreach (var segment in segments)
        {
            ValidateSegment(segment, path, issues);
        }

        if (issues.Count > 0)
        {
            return new OperationResult<string>(null, issues);
        }

        return new OperationResult<string>(string.Join('/', segments), issues);
    }

    public static OperationResult<string> ResolveUnderRoot(string rootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var normalized = NormalizeRelative(relativePath);
        if (!normalized.IsSuccess)
        {
            return normalized;
        }

        var fullRoot = Path.GetFullPath(rootPath);
        var segments = normalized.Value!.Split('/');
        var candidate = Path.GetFullPath(Path.Combine([fullRoot, .. segments]));
        var relativeToRoot = Path.GetRelativePath(fullRoot, candidate);

        if (Path.IsPathRooted(relativeToRoot) ||
            relativeToRoot.Equals("..", StringComparison.Ordinal) ||
            relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativeToRoot.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return new OperationResult<string>(
                null,
                [Error("path.outside_root", "The path resolves outside its intended root.", relativePath)]);
        }

        return new OperationResult<string>(candidate, normalized.Issues);
    }

    private static void ValidateSegment(
        string segment,
        string originalPath,
        List<ValidationIssue> issues)
    {
        if (segment is "." or "..")
        {
            issues.Add(Error("path.traversal", "Path traversal segments are not allowed.", originalPath));
            return;
        }

        if (segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            issues.Add(Error(
                "path.trailing_character",
                "Windows path segments cannot end with a space or period.",
                originalPath));
        }

        if (segment.Any(character =>
                character < 32 ||
                character is '<' or '>' or ':' or '"' or '|' or '?' or '*'))
        {
            issues.Add(Error(
                "path.invalid_character",
                "The path contains a character that is invalid on Windows.",
                originalPath));
        }

        var deviceCandidate = segment.Split('.', 2)[0].TrimEnd(' ', '.');
        if (ReservedDeviceNames.Contains(deviceCandidate))
        {
            issues.Add(Error(
                "path.reserved_name",
                $"'{segment}' is a reserved Windows device name.",
                originalPath));
        }
    }

    private static ValidationIssue Error(string code, string message, string path) =>
        new(ValidationSeverity.Error, code, message, Path: path);
}
