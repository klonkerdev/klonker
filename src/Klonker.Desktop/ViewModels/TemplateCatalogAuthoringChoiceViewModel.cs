using Klonker.Core.Registry;

namespace Klonker.Desktop.ViewModels;

public sealed class TemplateCatalogAuthoringChoiceViewModel
{
    public TemplateCatalogAuthoringChoiceViewModel(
        RegistryTemplatePackage template)
    {
        Template = template;
    }

    public RegistryTemplatePackage Template { get; }

    public string Name => Template.Package.Manifest.Name;

    public string Description => Template.Package.Manifest.Description;

    public string QualifiedId => Template.QualifiedId;

    public string Technology =>
        $"{Template.Package.Manifest.Language} · " +
        $"{Template.Package.Manifest.TargetOs} · " +
        $"{Template.Package.Manifest.BuildSystem}";
}
