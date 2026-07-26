using Klonker.Core.Templates;

namespace Klonker.Desktop.ViewModels;

public sealed class TemplateListItemViewModel : ViewModelBase
{
    public TemplateListItemViewModel(TemplatePackage package)
    {
        Package = package;
    }

    public TemplatePackage Package { get; }

    public string Name => Package.Manifest.Name;

    public string Description => Package.Manifest.Description;

    public string Version => $"v{Package.Manifest.Version}";

    public string Metadata =>
        $"{Package.Manifest.TargetOs} · {Package.Manifest.BuildSystem}";
}
