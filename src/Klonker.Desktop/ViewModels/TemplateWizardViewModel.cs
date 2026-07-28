using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Klonker.Core.Authoring;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Registry;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class TemplateWizardViewModel : ViewModelBase
{
    private readonly TemplateAuthoringOptions options;
    private readonly ITemplateAuthoringService authoringService;
    private readonly ITemplateAuthoringFolderPicker folderPicker;
    private TemplateWizardStepKind[] stepKinds =
        [TemplateWizardStepKind.Welcome];
    private int currentStepIndex;
    private bool updatingExclusiveChoices;

    public TemplateWizardViewModel(
        TemplateAuthoringOptions options,
        ITemplateAuthoringService authoringService,
        ITemplateAuthoringFolderPicker folderPicker,
        IEnumerable<RegistryTemplatePackage>? catalogTemplates = null)
    {
        this.options = options;
        this.authoringService = authoringService;
        this.folderPicker = folderPicker;

        Licenses = options.Licenses;
        Languages = new ObservableCollection<TemplateLanguageOption>(
            options.Languages);
        CatalogTemplates = new ObservableCollection<
            TemplateCatalogAuthoringChoiceViewModel>(
            (catalogTemplates ?? [])
                .OrderBy(
                    template => template.Package.Manifest.Name,
                    StringComparer.Ordinal)
                .ThenBy(
                    template => template.Package.Manifest.VariantId,
                    StringComparer.Ordinal)
                .Select(template =>
                    new TemplateCatalogAuthoringChoiceViewModel(template)));
        Platforms = new ObservableCollection<TemplatePlatformChoiceViewModel>(
            options.Platforms.Select(option =>
                new TemplatePlatformChoiceViewModel(option)));
        foreach (var platform in Platforms)
        {
            AttachPlatform(platform);
        }

        SelectedLicense = Licenses.FirstOrDefault(option => option.Id == "mit") ??
            Licenses[0];
        SelectedLanguage = Languages[0];
        (Platforms.FirstOrDefault(platform => platform.Id == "any") ??
         Platforms[0]).IsSelected = true;
        BuildSteps();
    }

    public ImmutableArray<TemplateLicenseOption> Licenses { get; }

    public ObservableCollection<TemplateLanguageOption> Languages { get; }

    public ObservableCollection<TemplateBuildSystemChoiceViewModel>
        AvailableBuildSystems
    { get; } = [];

    public ObservableCollection<TemplatePlatformChoiceViewModel>
        Platforms
    { get; }

    public ObservableCollection<TemplateCatalogAuthoringChoiceViewModel>
        CatalogTemplates
    { get; }

    public bool HasCatalogTemplates => CatalogTemplates.Count > 0;

    public ObservableCollection<TemplateWizardStepViewModel> Steps { get; } = [];

    public ObservableCollection<TemplateInspectionIssueViewModel>
        InspectionIssues
    { get; } = [];

    public bool IsNewTemplate => Mode == TemplateWizardMode.NewTemplate;

    public bool IsExistingFolder => Mode == TemplateWizardMode.ExistingFolder;

    public bool IsCatalogTemplate => Mode == TemplateWizardMode.CatalogTemplate;

    public bool HasInspection => Inspection is not null;

    public bool HasInspectionFiles => Inspection?.Files.Length > 0;

    public string InspectionFilesSummary =>
        Inspection is null
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                Inspection.Files.Take(40)) +
              (Inspection.Files.Length > 40
                  ? $"{Environment.NewLine}… and {Inspection.Files.Length - 40} more"
                  : string.Empty);

    public bool HasIssues => InspectionIssues.Count > 0;

    public bool InspectionHasErrors => Inspection?.HasErrors == true;

    public bool IsAlreadyRegistrySource =>
        Inspection?.IsAlreadyRegistrySource == true;

    public bool CanConvertInspection =>
        HasInspection &&
        !InspectionHasErrors &&
        !IsAlreadyRegistrySource;

    public string InspectionSummary =>
        Inspection?.Summary ?? "Choose a folder and run inspection.";

    public string InspectionStatus =>
        Inspection is null
            ? "Not inspected"
            : InspectionHasErrors
                ? "Needs changes"
                : IsAlreadyRegistrySource
                    ? "Ready for publication"
                    : "Ready to convert";

    public string CurrentStepTitle =>
        Steps.Count == 0 ? "Template wizard" : Steps[currentStepIndex].Title;

    public string CurrentStepDescription =>
        Steps.Count == 0
            ? string.Empty
            : Steps[currentStepIndex].Description;

    public bool CanGoBack => currentStepIndex > 0 && !IsGenerating;

    public bool CanGoNext =>
        CurrentPage is not null &&
        !IsGenerating &&
        CurrentPage.Kind switch
        {
            TemplateWizardStepKind.Welcome => false,
            TemplateWizardStepKind.CatalogTemplate =>
                SelectedCatalogTemplate is not null,
            TemplateWizardStepKind.ExistingFolder =>
                !string.IsNullOrWhiteSpace(ExistingFolderPath),
            TemplateWizardStepKind.Inspection => CanConvertInspection,
            TemplateWizardStepKind.Destination => DestinationIsValid,
            TemplateWizardStepKind.Basics => SelectedLicense is not null,
            TemplateWizardStepKind.Technology =>
                SelectedLanguage is not null &&
                AvailableBuildSystems.Any(buildSystem =>
                    buildSystem.IsSelected) &&
                Platforms.Any(platform => platform.IsSelected),
            TemplateWizardStepKind.Metadata =>
                !string.IsNullOrWhiteSpace(NamespaceId) &&
                !string.IsNullOrWhiteSpace(PackageId) &&
                !string.IsNullOrWhiteSpace(PackageName) &&
                !string.IsNullOrWhiteSpace(Description) &&
                !string.IsNullOrWhiteSpace(Version),
            _ => false,
        };

    public bool IsPreviewStep =>
        CurrentPage?.Kind == TemplateWizardStepKind.Preview;

    public bool HasPlan => Preview is not null;

    public bool DestinationHasError =>
        DestinationValidationVisible && !DestinationIsValid;

    public bool DestinationHasSuccess =>
        DestinationValidationVisible && DestinationIsValid;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasGenerationError =>
        GenerationResult is not null && !GenerationResult.Succeeded;

    public bool GenerationSucceeded => GenerationResult?.Succeeded == true;

    public string GenerationMessage =>
        GenerationResult?.Message ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewTemplate))]
    [NotifyPropertyChangedFor(nameof(IsExistingFolder))]
    [NotifyPropertyChangedFor(nameof(IsCatalogTemplate))]
    public partial TemplateWizardMode? Mode { get; set; }

    [ObservableProperty]
    public partial TemplateWizardPageViewModel CurrentPage { get; set; } =
        null!;

    [ObservableProperty]
    public partial bool IsTransitionReversed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string ExistingFolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string DestinationPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(DestinationHasError))]
    [NotifyPropertyChangedFor(nameof(DestinationHasSuccess))]
    public partial bool DestinationIsValid { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationHasError))]
    [NotifyPropertyChangedFor(nameof(DestinationHasSuccess))]
    public partial bool DestinationValidationVisible { get; set; }

    [ObservableProperty]
    public partial string DestinationValidationMessage { get; set; } =
        string.Empty;

    [ObservableProperty]
    public partial bool CreateReadme { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial TemplateLicenseOption? SelectedLicense { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial TemplateLanguageOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial TemplateCatalogAuthoringChoiceViewModel?
        SelectedCatalogTemplate
    { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string NamespaceId { get; set; } = "local";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string PackageId { get; set; } = "my-template";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string PackageName { get; set; } = "My Template";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string Description { get; set; } =
        "A reusable project starter.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial string Version { get; set; } = "0.1.0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInspection))]
    [NotifyPropertyChangedFor(nameof(HasInspectionFiles))]
    [NotifyPropertyChangedFor(nameof(InspectionFilesSummary))]
    [NotifyPropertyChangedFor(nameof(InspectionHasErrors))]
    [NotifyPropertyChangedFor(nameof(IsAlreadyRegistrySource))]
    [NotifyPropertyChangedFor(nameof(CanConvertInspection))]
    [NotifyPropertyChangedFor(nameof(InspectionSummary))]
    [NotifyPropertyChangedFor(nameof(InspectionStatus))]
    public partial ExistingTemplateInspection? Inspection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    public partial GenerationPreviewViewModel? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; } =
        "Choose how you want to begin.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    public partial bool IsGenerating { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGenerationError))]
    [NotifyPropertyChangedFor(nameof(GenerationSucceeded))]
    [NotifyPropertyChangedFor(nameof(GenerationMessage))]
    public partial GenerationResult? GenerationResult { get; set; }

    [RelayCommand]
    private void ChooseNewTemplate()
    {
        Mode = TemplateWizardMode.NewTemplate;
        ExistingFolderPath = string.Empty;
        Inspection = null;
        BuildSteps();
        MoveTo(1, reverse: false);
        StatusMessage = "Choose a new or empty folder for the authoring package.";
    }

    [RelayCommand]
    private void ChooseExistingFolder()
    {
        Mode = TemplateWizardMode.ExistingFolder;
        BuildSteps();
        MoveTo(1, reverse: false);
        StatusMessage = "Choose the code or template folder to inspect.";
    }

    [RelayCommand]
    private void ChooseCatalogTemplate()
    {
        Mode = TemplateWizardMode.CatalogTemplate;
        BuildSteps();
        MoveTo(1, reverse: false);
        StatusMessage = HasCatalogTemplates
            ? "Choose a catalog template to use as the starting point."
            : "No catalog templates are loaded. Close the wizard, refresh the catalog, and try again.";
    }

    [RelayCommand]
    private async Task BrowseExistingFolderAsync()
    {
        var selected = await folderPicker.PickAsync(
            "Choose existing code or template folder");
        if (selected is not null)
        {
            ExistingFolderPath = selected;
            InspectExisting();
        }
    }

    [RelayCommand]
    private async Task BrowseDestinationAsync()
    {
        var selected = await folderPicker.PickAsync(
            "Choose new or empty template package folder");
        if (selected is not null)
        {
            DestinationPath = selected;
        }
    }

    [RelayCommand]
    private void RefreshInspection() => InspectExisting();

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => MoveTo(currentStepIndex - 1, reverse: true);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (CurrentPage.Kind == TemplateWizardStepKind.ExistingFolder)
        {
            InspectExisting();
        }

        if (CurrentPage.Kind == TemplateWizardStepKind.Metadata &&
            !BuildPreview())
        {
            return;
        }

        MoveTo(currentStepIndex + 1, reverse: false);
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (Preview is null)
        {
            return;
        }

        IsGenerating = true;
        GenerationResult = null;
        StatusMessage = "Creating the template package transactionally…";
        try
        {
            GenerationResult = await authoringService.GenerateAsync(
                Preview.Plan,
                DestinationPath);
            StatusMessage = GenerationResult.Succeeded
                ? "Template package created. The original source was not changed."
                : "The package was not installed; review the reported problem.";
        }
        finally
        {
            IsGenerating = false;
            GenerateCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanGenerate() =>
        Preview is not null &&
        !IsGenerating &&
        GenerationResult?.Succeeded != true;

    private void BuildSteps()
    {
        stepKinds = Mode switch
        {
            TemplateWizardMode.ExistingFolder =>
            [
                TemplateWizardStepKind.Welcome,
                TemplateWizardStepKind.ExistingFolder,
                TemplateWizardStepKind.Inspection,
                TemplateWizardStepKind.Destination,
                TemplateWizardStepKind.Basics,
                TemplateWizardStepKind.Technology,
                TemplateWizardStepKind.Metadata,
                TemplateWizardStepKind.Preview,
            ],
            TemplateWizardMode.NewTemplate =>
            [
                TemplateWizardStepKind.Welcome,
                TemplateWizardStepKind.Destination,
                TemplateWizardStepKind.Basics,
                TemplateWizardStepKind.Technology,
                TemplateWizardStepKind.Metadata,
                TemplateWizardStepKind.Preview,
            ],
            TemplateWizardMode.CatalogTemplate =>
            [
                TemplateWizardStepKind.Welcome,
                TemplateWizardStepKind.CatalogTemplate,
                TemplateWizardStepKind.Destination,
                TemplateWizardStepKind.Basics,
                TemplateWizardStepKind.Technology,
                TemplateWizardStepKind.Metadata,
                TemplateWizardStepKind.Preview,
            ],
            _ => [TemplateWizardStepKind.Welcome],
        };

        Steps.Clear();
        for (var index = 0; index < stepKinds.Length; index++)
        {
            var (title, description) = DescribeStep(stepKinds[index]);
            Steps.Add(new TemplateWizardStepViewModel(
                index + 1,
                stepKinds[index],
                title,
                description));
        }

        currentStepIndex = 0;
        UpdateStepState();
    }

    private void MoveTo(int index, bool reverse)
    {
        if (index < 0 || index >= stepKinds.Length)
        {
            return;
        }

        IsTransitionReversed = reverse;
        currentStepIndex = index;
        UpdateStepState();
    }

    private void UpdateStepState()
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].IsCurrent = index == currentStepIndex;
            Steps[index].IsComplete = index < currentStepIndex;
        }

        CurrentPage = new TemplateWizardPageViewModel(
            this,
            stepKinds[currentStepIndex]);
        OnPropertyChanged(nameof(CurrentStepTitle));
        OnPropertyChanged(nameof(CurrentStepDescription));
        NotifyNavigationState();
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsPreviewStep));
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLanguageChanged(TemplateLanguageOption? value)
    {
        AvailableBuildSystems.Clear();
        if (value is not null)
        {
            foreach (var buildSystem in value.BuildSystems
                         .Select(id =>
                             options.BuildSystems.FirstOrDefault(
                                 option => option.Id == id) ??
                             new TemplateBuildSystemOption(
                                 id,
                                 HumanizeId(id),
                                 "Build system declared by the selected catalog template.",
                                 [])))
            {
                var choice = new TemplateBuildSystemChoiceViewModel(
                    buildSystem);
                AttachBuildSystem(choice);
                AvailableBuildSystems.Add(choice);
            }
        }

        if (AvailableBuildSystems.Count > 0)
        {
            AvailableBuildSystems[0].IsSelected = true;
        }
        NotifyNavigationState();
    }

    partial void OnSelectedCatalogTemplateChanged(
        TemplateCatalogAuthoringChoiceViewModel? value)
    {
        if (value is not null)
        {
            ApplyCatalogTemplate(value.Template);
        }

        NotifyNavigationState();
    }

    partial void OnDestinationPathChanged(string value)
    {
        ValidateDestination();
    }

    partial void OnInspectionChanged(ExistingTemplateInspection? value)
    {
        ValidateDestination();
    }

    private void InspectExisting()
    {
        Inspection = authoringService.Inspect(ExistingFolderPath);
        InspectionIssues.Clear();
        foreach (var issue in Inspection.Issues)
        {
            InspectionIssues.Add(new TemplateInspectionIssueViewModel(issue));
        }
        OnPropertyChanged(nameof(HasIssues));

        if (Inspection.Metadata is not null)
        {
            ApplyMetadata(Inspection.Metadata);
        }
        else
        {
            ApplyFolderDefaults(Inspection.RootPath);
        }

        if (!Inspection.IsAlreadyRegistrySource)
        {
            var parent = Directory.GetParent(Inspection.RootPath);
            if (parent is not null)
            {
                DestinationPath = Path.Combine(
                    parent.FullName,
                    $"{Path.GetFileName(Inspection.RootPath)}-klonker-template");
            }
        }

        StatusMessage = Inspection.Summary;
        NotifyNavigationState();
    }

    private void ValidateDestination()
    {
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            DestinationIsValid = false;
            DestinationValidationVisible = false;
            DestinationValidationMessage = string.Empty;
            NotifyNavigationState();
            return;
        }

        var validation = authoringService.ValidateDestination(
            DestinationPath,
            IsExistingFolder
                ? Inspection?.RootPath
                : IsCatalogTemplate
                    ? SelectedCatalogTemplate?.Template.Package.ContentPath
                    : null);
        DestinationIsValid = validation.IsSuccess;
        DestinationValidationVisible = true;
        DestinationValidationMessage = validation.IsSuccess
            ? "Destination is valid. Klonker will create or replace the empty folder transactionally."
            : validation.Issues.FirstOrDefault()?.Message ??
              "The destination is not valid.";
        NotifyNavigationState();
    }

    private void ApplyMetadata(ExistingTemplateMetadata metadata)
    {
        NamespaceId = metadata.NamespaceId;
        PackageId = metadata.PackageId;
        PackageName = metadata.Name;
        Description = metadata.Description;
        Version = metadata.Version;
        SelectedLicense = Licenses.FirstOrDefault(option =>
            option.SourceLicense.Equals(
                metadata.SourceLicense,
                StringComparison.OrdinalIgnoreCase)) ?? SelectedLicense;
        SelectedLanguage = Languages.FirstOrDefault(option =>
            option.Id.Equals(
                metadata.Language,
                StringComparison.OrdinalIgnoreCase)) ?? SelectedLanguage;
        SelectBuildSystems(metadata.BuildSystems);
        foreach (var platform in Platforms)
        {
            platform.IsSelected = metadata.Platforms.Contains(
                platform.Id,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private void ApplyFolderDefaults(string path)
    {
        var folderName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        PackageId = SanitizeId(folderName);
        PackageName = string.Join(
            ' ',
            folderName
                .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        Description = $"A reusable template based on {PackageName}.";
    }

    private bool BuildPreview()
    {
        GenerationResult = null;
        var request = CreateRequest();
        var result = authoringService.CreatePlan(request);
        InspectionIssues.Clear();
        foreach (var issue in result.Issues)
        {
            InspectionIssues.Add(new TemplateInspectionIssueViewModel(issue));
        }
        OnPropertyChanged(nameof(HasIssues));

        if (!result.IsSuccess)
        {
            StatusMessage = "Fix the highlighted metadata or destination issues before previewing.";
            return false;
        }

        Preview = new GenerationPreviewViewModel(result.Value!);
        StatusMessage =
            $"Preview ready: {Preview.Files.Count} files across " +
            $"{request.Platforms.Length * request.BuildSystems.Length} variant(s).";
        return true;
    }

    private TemplateAuthoringRequest CreateRequest()
    {
        var selectedLanguage = SelectedLanguage!;
        var selectedBuildSystems = AvailableBuildSystems
            .Where(buildSystem => buildSystem.IsSelected)
            .Select(buildSystem => buildSystem.Option)
            .OrderBy(buildSystem => buildSystem.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var seeds = IsNewTemplate
            ? selectedLanguage.SeedFiles
                .Select(seed => new TemplateAuthoringSeedFile(
                    seed.Path,
                    seed.Content,
                    VariantSpecific: false))
                .Concat(selectedBuildSystems.SelectMany(buildSystem =>
                    buildSystem.SeedFiles.Select(seed =>
                        new TemplateAuthoringSeedFile(
                            seed.Path,
                            seed.Content,
                            VariantSpecific: true,
                            buildSystem.Id))))
                .ToImmutableArray()
            : [];
        var existingContent = IsExistingFolder && Inspection is not null
            ? Inspection.ContentSourcePath
            : IsCatalogTemplate
                ? SelectedCatalogTemplate?.Template.Package.ContentPath
                : null;
        var sourceManifest = IsCatalogTemplate
            ? SelectedCatalogTemplate?.Template.Package.Manifest
            : null;
        return new TemplateAuthoringRequest(
            DestinationPath,
            existingContent,
            NamespaceId.Trim(),
            PackageId.Trim(),
            PackageName.Trim(),
            Description.Trim(),
            Version.Trim(),
            selectedLanguage.Id,
            selectedBuildSystems
                .Select(buildSystem => buildSystem.Id)
                .ToImmutableArray(),
            Platforms
                .Where(platform => platform.IsSelected)
                .Select(platform => platform.Id)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            SelectedLicense!.SourceLicense,
            SelectedLicense.Summary,
            CreateReadme,
            seeds,
            sourceManifest?.Parameters ?? [],
            sourceManifest?.Prerequisites ?? [],
            sourceManifest?.Tags ?? []);
    }

    private static string SanitizeId(string value)
    {
        var characters = value
            .ToLowerInvariant()
            .Select(character =>
                char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var result = string.Join(
            '-',
            new string(characters)
                .Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (result.Length == 0 || !char.IsLetter(result[0]))
        {
            result = $"template-{result}";
        }

        return result;
    }

    private static (string Title, string Description) DescribeStep(
        TemplateWizardStepKind kind) =>
        kind switch
        {
            TemplateWizardStepKind.Welcome =>
                ("Start", "Create from a starter, catalog template, or existing files."),
            TemplateWizardStepKind.CatalogTemplate =>
                ("Catalog source", "Choose a loaded template to copy and customize."),
            TemplateWizardStepKind.ExistingFolder =>
                ("Source folder", "Choose code or a template tree; nothing is modified."),
            TemplateWizardStepKind.Inspection =>
                ("Inspect", "Fix actionable schema and structure findings, then refresh."),
            TemplateWizardStepKind.Destination =>
                ("Destination", "Choose the new authoring-package folder."),
            TemplateWizardStepKind.Basics =>
                ("Basics", "Choose licensing and generated documentation."),
            TemplateWizardStepKind.Technology =>
                ("Technology", "Select language, build systems, and target platforms."),
            TemplateWizardStepKind.Metadata =>
                ("Package", "Define stable IDs and publication metadata."),
            _ => ("Preview", "Review every planned file before generating."),
        };

    private void EnforcePlatformSelection(
        TemplatePlatformChoiceViewModel changed)
    {
        if (updatingExclusiveChoices || !changed.IsSelected)
        {
            return;
        }

        updatingExclusiveChoices = true;
        try
        {
            foreach (var platform in Platforms.Where(platform =>
                         platform != changed &&
                         (changed.Id == "any" || platform.Id == "any")))
            {
                platform.IsSelected = false;
            }
        }
        finally
        {
            updatingExclusiveChoices = false;
        }
    }

    private void EnforceBuildSystemSelection(
        TemplateBuildSystemChoiceViewModel changed)
    {
        if (updatingExclusiveChoices || !changed.IsSelected)
        {
            return;
        }

        updatingExclusiveChoices = true;
        try
        {
            foreach (var buildSystem in AvailableBuildSystems.Where(
                         buildSystem =>
                             buildSystem != changed &&
                             (changed.Id == "none" ||
                              buildSystem.Id == "none")))
            {
                buildSystem.IsSelected = false;
            }
        }
        finally
        {
            updatingExclusiveChoices = false;
        }
    }

    private void SelectBuildSystems(IEnumerable<string> buildSystemIds)
    {
        var ids = buildSystemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        updatingExclusiveChoices = true;
        try
        {
            foreach (var buildSystem in AvailableBuildSystems)
            {
                buildSystem.IsSelected = ids.Contains(buildSystem.Id);
            }
        }
        finally
        {
            updatingExclusiveChoices = false;
        }

        NotifyNavigationState();
    }

    private void ApplyCatalogTemplate(RegistryTemplatePackage template)
    {
        var manifest = template.Package.Manifest;
        var language = Languages.FirstOrDefault(option =>
            option.Id.Equals(
                manifest.Language,
                StringComparison.OrdinalIgnoreCase));
        if (language is null)
        {
            language = new TemplateLanguageOption(
                manifest.Language,
                HumanizeId(manifest.Language),
                "Language declared by the selected catalog template.",
                [manifest.BuildSystem],
                []);
            Languages.Add(language);
        }

        if (!Platforms.Any(platform => platform.Id.Equals(
                manifest.TargetOs,
                StringComparison.OrdinalIgnoreCase)))
        {
            var platform = new TemplatePlatformChoiceViewModel(
                new TemplatePlatformOption(
                    manifest.TargetOs,
                    HumanizeId(manifest.TargetOs),
                    "Target declared by the selected catalog template."));
            AttachPlatform(platform);
            Platforms.Add(platform);
        }

        var familyParts = manifest.FamilyId.Split('.', 2);
        NamespaceId = familyParts[0];
        PackageId = SanitizeId(
            (familyParts.Length == 2 ? familyParts[1] : manifest.FamilyId) +
            "-custom");
        PackageName = $"{manifest.Name} Custom";
        Description =
            $"A customized template based on {template.QualifiedId}.";
        Version = manifest.Version;
        SelectedLicense = Licenses.FirstOrDefault(option =>
            option.SourceLicense.Equals(
                manifest.SourceLicense,
                StringComparison.OrdinalIgnoreCase)) ?? SelectedLicense;
        SelectedLanguage = language;
        if (!AvailableBuildSystems.Any(buildSystem =>
                buildSystem.Id.Equals(
                    manifest.BuildSystem,
                    StringComparison.OrdinalIgnoreCase)))
        {
            var buildSystem = new TemplateBuildSystemChoiceViewModel(
                new TemplateBuildSystemOption(
                    manifest.BuildSystem,
                    HumanizeId(manifest.BuildSystem),
                    "Build system declared by the selected catalog template.",
                    []));
            AttachBuildSystem(buildSystem);
            AvailableBuildSystems.Add(buildSystem);
        }

        SelectBuildSystems([manifest.BuildSystem]);
        foreach (var platform in Platforms)
        {
            platform.IsSelected = platform.Id.Equals(
                manifest.TargetOs,
                StringComparison.OrdinalIgnoreCase);
        }

        StatusMessage =
            "Metadata, parameters, prerequisites, tags, and content were copied. Choose an empty destination; the installed catalog package remains untouched.";
    }

    private void AttachPlatform(
        TemplatePlatformChoiceViewModel platform)
    {
        platform.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName ==
                nameof(TemplatePlatformChoiceViewModel.IsSelected))
            {
                EnforcePlatformSelection(platform);
                NotifyNavigationState();
            }
        };
    }

    private void AttachBuildSystem(
        TemplateBuildSystemChoiceViewModel buildSystem)
    {
        buildSystem.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName ==
                nameof(TemplateBuildSystemChoiceViewModel.IsSelected))
            {
                EnforceBuildSystemSelection(buildSystem);
                NotifyNavigationState();
            }
        };
    }

    private static string HumanizeId(string value) =>
        string.Join(
            ' ',
            value.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(word =>
                    char.ToUpperInvariant(word[0]) + word[1..]));
}
