namespace Klonker.Desktop.Services;

public sealed class WindowsPrerequisiteProbeService : IPrerequisiteProbeService
{
    private static readonly Dictionary<string, string[]> PathCandidates =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["cmake"] = ["cmake.exe", "cmake"],
            ["cpp_toolchain"] =
                ["cl.exe", "clang++.exe", "g++.exe", "clang++", "g++"],
            ["dotnet_sdk"] = ["dotnet.exe", "dotnet"],
            ["git"] = ["git.exe", "git"],
            ["make"] = ["make.exe", "make", "mingw32-make.exe"],
            ["ninja"] = ["ninja.exe", "ninja"],
            ["xmake"] = ["xmake.exe", "xmake"],
        };

    public Task<PrerequisiteProbeResult> ProbeAsync(
        string prerequisiteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prerequisiteId);
        cancellationToken.ThrowIfCancellationRequested();

        if (PathCandidates.TryGetValue(prerequisiteId, out var candidates))
        {
            var found = FindOnPath(candidates);
            return Task.FromResult(found is null
                ? new PrerequisiteProbeResult(
                    PrerequisiteProbeState.NotFound,
                    "No matching executable was found on the current PATH.")
                : new PrerequisiteProbeResult(
                    PrerequisiteProbeState.Found,
                    $"Found '{found}' on PATH. Version compatibility was not executed or inferred."));
        }

        if (prerequisiteId == "gof2_pc")
        {
            var installation = FindGof2Installation();
            return Task.FromResult(installation is null
                ? new PrerequisiteProbeResult(
                    PrerequisiteProbeState.NotFound,
                    "Galaxy on Fire 2 was not found in the known Steam or GOG folders.")
                : new PrerequisiteProbeResult(
                    PrerequisiteProbeState.Found,
                    $"Found a game installation at '{installation}'."));
        }

        if (prerequisiteId == "kaamoclub_modapi")
        {
            return Task.FromResult(new PrerequisiteProbeResult(
                PrerequisiteProbeState.Unknown,
                "Klonker cannot identify every valid ModAPI installation safely. Check the selected game directory manually."));
        }

        return Task.FromResult(new PrerequisiteProbeResult(
            PrerequisiteProbeState.Unknown,
            "No host-owned read-only probe is available for this prerequisite."));
    }

    private static string? FindOnPath(IEnumerable<string> candidates)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(candidate, directory);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                        NotSupportedException or
                        PathTooLongException)
                {
                }
            }
        }

        return null;
    }

    private static string? FindGof2Installation()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        }
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var candidates = new[]
            {
                Path.Combine(
                    root,
                    "Steam",
                    "steamapps",
                    "common",
                    "Galaxy on Fire 2 Full HD"),
                Path.Combine(
                    root,
                    "GOG Galaxy",
                    "Games",
                    "Galaxy on Fire 2 Full HD"),
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
