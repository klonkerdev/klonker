using CommunityToolkit.Mvvm.ComponentModel;

namespace Klonker.Desktop.ViewModels;

public sealed partial class TemplateWizardStepViewModel : ViewModelBase
{
    public TemplateWizardStepViewModel(
        int number,
        TemplateWizardStepKind kind,
        string title,
        string description)
    {
        Number = number;
        Kind = kind;
        Title = title;
        Description = description;
    }

    public int Number { get; }

    public TemplateWizardStepKind Kind { get; }

    public string Title { get; }

    public string Description { get; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsComplete { get; set; }
}
