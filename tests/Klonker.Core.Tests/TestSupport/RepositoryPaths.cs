using System.Runtime.CompilerServices;

namespace Klonker.Core.Tests.TestSupport;

internal static class RepositoryPaths
{
    public static string SampleRegistry
    {
        get
        {
            var registry = FindRegistryFrom(AppContext.BaseDirectory) ??
                FindRegistryFrom(Environment.CurrentDirectory) ??
                FindRegistryFrom(GetSourceDirectory());
            if (registry is not null)
            {
                return registry;
            }

            throw new DirectoryNotFoundException(
                "Could not locate samples/local-registry/registry.json.");
        }
    }

    public static string SamplePackage => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(SampleRegistry)!,
        "packages",
        "std.cpp-cli.windows-cmake");

    private static string? FindRegistryFrom(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            var candidate = System.IO.Path.Combine(
                current.FullName,
                "samples",
                "local-registry",
                "registry.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string GetSourceDirectory(
        [CallerFilePath] string sourceFilePath = "") =>
        Path.GetDirectoryName(sourceFilePath)!;
}
