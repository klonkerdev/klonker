using System.Reflection;
using System.Runtime.InteropServices;

namespace Klonker.Desktop.ViewModels;

public sealed class AboutViewModel
{
    public const string AuthorName = "@SleathCobra";
    public const string AuthorUrl = "https://github.com/SleathCobra";
    public const string RepositoryUrl =
        "https://github.com/klonkerdev/klonker";
    public const string DocumentationUrl =
        "https://github.com/klonkerdev/klonker/tree/main/docs";
    public const string LicenseUrl =
        "https://github.com/klonkerdev/klonker/blob/main/LICENSE";

    public AboutViewModel()
        : this(typeof(AboutViewModel).Assembly)
    {
    }

    public AboutViewModel(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        FullVersion = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "unknown"
            : informationalVersion;
        Version = FullVersion.Split('+', 2)[0];
        BuildChannel = Version.Contains(
            "nightly",
            StringComparison.OrdinalIgnoreCase)
            ? "Nightly"
            : "Preview";
        Runtime = RuntimeInformation.FrameworkDescription;
        Platform =
            $"{RuntimeInformation.OSDescription} · {RuntimeInformation.ProcessArchitecture}";
        Copyright = "© 2026 SleathCobra";
    }

    public string Version { get; }

    public string FullVersion { get; }

    public string DisplayVersion => $"Version {Version}";

    public string BuildChannel { get; }

    public string Runtime { get; }

    public string Platform { get; }

    public string Copyright { get; }
}
