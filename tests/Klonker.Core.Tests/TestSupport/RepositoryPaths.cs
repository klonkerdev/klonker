namespace Klonker.Core.Tests.TestSupport;

internal static class RepositoryPaths
{
    public static string SampleRegistry
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
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

            throw new DirectoryNotFoundException(
                "Could not locate samples/local-registry/registry.json.");
        }
    }

    public static string SamplePackage => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(SampleRegistry)!,
        "packages",
        "official.cpp-cli.windows-cmake");
}
