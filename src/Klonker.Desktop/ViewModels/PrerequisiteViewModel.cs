using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Core.Templates;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class PrerequisiteViewModel : ViewModelBase
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

    public string Id => Prerequisite.Id;

    public bool HasProbeResult => ProbeResult is not null;

    public bool ProbeFound =>
        ProbeResult?.State == PrerequisiteProbeState.Found;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProbeResult))]
    [NotifyPropertyChangedFor(nameof(ProbeFound))]
    public partial PrerequisiteProbeResult? ProbeResult { get; set; }
}
