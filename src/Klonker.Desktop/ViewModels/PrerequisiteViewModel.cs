using Klonker.Core.Templates;

namespace Klonker.Desktop.ViewModels;

public sealed class PrerequisiteViewModel
{
    public PrerequisiteViewModel(TemplatePrerequisite prerequisite)
    {
        Prerequisite = prerequisite;
    }

    public TemplatePrerequisite Prerequisite { get; }

    public string Name => Prerequisite.Name;

    public string Description => Prerequisite.Description;

    public string RequiredFor =>
        $"Needed to {Prerequisite.RequiredFor} the generated project";
}
