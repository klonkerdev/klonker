namespace Klonker.Desktop.Services;

public static class DevelopmentSampleRegistryLocator
{
    private const string RelativeRegistryPath = "samples/local-registry/registry.json";

    public static string? FindRegistryIndex()
    {
        var starts = new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        };

        foreach (var start in starts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                var candidate = Path.Combine(
                    current.FullName,
                    RelativeRegistryPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return null;
    }
}
