namespace Klonker.Desktop.ViewModels;

public sealed class TemplateWizardPageViewModel
{
    public TemplateWizardPageViewModel(
        TemplateWizardViewModel wizard,
        TemplateWizardStepKind kind)
    {
        Wizard = wizard;
        Kind = kind;
    }

    public TemplateWizardViewModel Wizard { get; }

    public TemplateWizardStepKind Kind { get; }

    public bool IsWelcome => Kind == TemplateWizardStepKind.Welcome;

    public bool IsCatalogTemplate =>
        Kind == TemplateWizardStepKind.CatalogTemplate;

    public bool IsExistingFolder =>
        Kind == TemplateWizardStepKind.ExistingFolder;

    public bool IsInspection => Kind == TemplateWizardStepKind.Inspection;

    public bool IsDestination => Kind == TemplateWizardStepKind.Destination;

    public bool IsBasics => Kind == TemplateWizardStepKind.Basics;

    public bool IsTechnology => Kind == TemplateWizardStepKind.Technology;

    public bool IsMetadata => Kind == TemplateWizardStepKind.Metadata;

    public bool IsPreview => Kind == TemplateWizardStepKind.Preview;
}
