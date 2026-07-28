using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Modules;
using Klonker.Core.Templates;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    public const string AllLanguages = "All languages";
    public const string AllPlatforms = "All platforms";
    public const string AllBuildSystems = "All build systems";
    public const string AllTags = "All tags";

    private readonly ITemplateCatalog? catalog;
    private readonly IProjectGenerationService? generationService;
    private readonly IDestinationPicker? destinationPicker;
    private readonly IFavoriteStore? favoriteStore;
    private readonly AppSettingsStore? appSettingsStore;
    private readonly IPrerequisiteProbeService? prerequisiteProbeService;
    private readonly AppDiagnosticLog? diagnosticLog;
    private readonly IWslGenerationService? wslGenerationService;
    private readonly CatalogTabStore? catalogTabStore;
    private readonly TemplateTagPalette tagPalette = new();
    private bool prerequisiteProbesEnabled;
    private CancellationTokenSource? catalogLoadCancellation;
    private CancellationTokenSource? generationCancellation;
    private ModuleGenerationPlan? modulePlan;

    public MainViewModel()
    {
        Languages.Add(AllLanguages);
        Platforms.Add(AllPlatforms);
        BuildSystems.Add(AllBuildSystems);
        AvailableTags.Add(AllTags);
        SelectedLanguage = AllLanguages;
        SelectedPlatform = AllPlatforms;
        SelectedBuildSystem = AllBuildSystems;
        SelectedTag = AllTags;
        SelectedCatalogTabKind = CatalogTabKinds[0];
    }

    public MainViewModel(
        ITemplateCatalog catalog,
        IProjectGenerationService? generationService = null,
        IDestinationPicker? destinationPicker = null,
        IFavoriteStore? favoriteStore = null,
        AppSettingsStore? appSettingsStore = null,
        IPrerequisiteProbeService? prerequisiteProbeService = null,
        AppDiagnosticLog? diagnosticLog = null,
        TemplateTagPalette? tagPalette = null,
        IWslGenerationService? wslGenerationService = null,
        CatalogTabStore? catalogTabStore = null)
        : this()
    {
        this.catalog = catalog;
        this.generationService = generationService;
        this.destinationPicker = destinationPicker;
        this.favoriteStore = favoriteStore;
        this.appSettingsStore = appSettingsStore;
        this.prerequisiteProbeService = prerequisiteProbeService;
        this.diagnosticLog = diagnosticLog;
        this.tagPalette = tagPalette ?? new TemplateTagPalette();
        this.wslGenerationService = wslGenerationService;
        this.catalogTabStore = catalogTabStore;
    }

    public ObservableCollection<TemplateListItemViewModel> Templates { get; } = [];

    public ObservableCollection<PackageListItemViewModel> Packages { get; } = [];

    public ObservableCollection<PackageListItemViewModel> FilteredPackages { get; } = [];

    public ObservableCollection<TemplateListItemViewModel> FilteredVariants { get; } = [];

    public ObservableCollection<ModuleListItemViewModel> Modules { get; } = [];

    public ObservableCollection<ModuleListItemViewModel> FilteredModules { get; } = [];

    public ObservableCollection<ParameterEditorViewModel> ModuleParameters { get; } = [];

    public ObservableCollection<WslDistribution> RunningWslDistributions { get; } = [];

    public ObservableCollection<CatalogTabViewModel> CustomCatalogTabs { get; } = [];

    public ObservableCollection<CatalogTabCandidateViewModel> CatalogTabCandidates { get; } = [];

    public IReadOnlyList<CatalogTabKindOption> CatalogTabKinds { get; } =
    [
        new(
            CatalogTabKind.FavoriteTemplates,
            "Favorite templates",
            "A live view of locally favorited template variants."),
        new(
            CatalogTabKind.FavoriteModules,
            "Favorite modules",
            "A live view of locally favorited modules."),
        new(
            CatalogTabKind.SelectedTemplates,
            "Selected templates",
            "Choose the template variants included in this tab."),
        new(
            CatalogTabKind.SelectedModules,
            "Selected modules",
            "Choose the modules included in this tab."),
    ];

    public IReadOnlyList<GenerationHostKind> GenerationHosts { get; } =
        [GenerationHostKind.Windows, GenerationHostKind.Wsl];

    public ObservableCollection<ParameterEditorViewModel> Parameters { get; } = [];

    public ObservableCollection<PrerequisiteViewModel> Prerequisites { get; } = [];

    public ObservableCollection<string> Languages { get; } = [];

    public ObservableCollection<string> Platforms { get; } = [];

    public ObservableCollection<string> BuildSystems { get; } = [];

    public ObservableCollection<string> AvailableTags { get; } = [];

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasPreview => Preview is not null;

    public bool HasSelectedPackage => SelectedPackage is not null;

    public bool HasSelectedTemplate => SelectedTemplate is not null;

    public bool HasPrerequisites => Prerequisites.Count > 0;

    public bool CanProbePrerequisites =>
        HasPrerequisites &&
        prerequisiteProbesEnabled &&
        prerequisiteProbeService is not null &&
        !IsProbingPrerequisites;

    public string PrerequisiteProbeConsentText => prerequisiteProbesEnabled
        ? "Checks inspect PATH and known folders only after you click; no tools are installed or executed."
        : "Active checks are disabled. Enable prerequisite probes in Settings to consent.";

    public bool HasCatalogNotice => !string.IsNullOrWhiteSpace(CatalogNotice);

    public bool HasGenerationResult => GenerationResult is not null;

    public bool IsConfigurationView =>
        !IsCatalogView && !IsModuleConfigurationView;

    public bool IsTemplateCatalog => !IsModuleCatalog;

    public bool IsTemplatePackageSelection =>
        IsTemplateCatalog && IsPackageSelection;

    public bool IsTemplateVariantSelection =>
        IsTemplateCatalog && IsVariantSelection;

    public bool IsWindowsGeneration => GenerationHost == GenerationHostKind.Windows;

    public bool IsWslGeneration => GenerationHost == GenerationHostKind.Wsl;

    public bool HasWslDistributions => RunningWslDistributions.Count > 0;

    public bool HasSelectedModule => SelectedModule is not null;

    public bool HasModulePreview => ModulePreview is not null;

    public bool HasModulePostGenerationInstructions =>
            !string.IsNullOrWhiteSpace(ModulePostGenerationInstructions);

    public bool IsCatalogTabSelectionRequired =>
        SelectedCatalogTabKind?.Kind is
            CatalogTabKind.SelectedTemplates or
            CatalogTabKind.SelectedModules;

    public bool HasActiveCustomCatalogTab =>
        ActiveCustomCatalogTab is not null;

    public string ActiveDestinationDisplay =>
        IsWslGeneration && SelectedWslDistribution is not null
            ? $"{SelectedWslDistribution.Name}:{WslDestinationPath}"
            : DestinationPath;

    public bool IsVariantSelection => !IsPackageSelection;

    public string CatalogStageTitle =>
        IsModuleCatalog
            ? "MODULES"
            : IsPackageSelection ? "PACKAGES" : "VARIANTS";

    public string CatalogStageDescription =>
        IsModuleCatalog
            ? "Choose reusable files to add safely to an existing source tree."
            : IsPackageSelection
            ? "Choose a project family, then confirm to inspect its variants."
            : SelectedPackage?.HasBuildSystems == true
                ? $"Choose the platform and build system for {SelectedPackage.Name}."
                : $"Choose the {SelectedPackage?.Name ?? "package"} starter that matches what you want to create.";

    public string CatalogItemCountText =>
        IsModuleCatalog
            ? $"{FilteredModules.Count} available"
            : IsPackageSelection
            ? $"{FilteredPackages.Count} available"
            : $"{FilteredVariants.Count} available";

    public Exception? DiagnosticException { get; private set; }

    public Task CatalogLoadTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPackage))]
    public partial PackageListItemViewModel? SelectedPackage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTemplate))]
    public partial TemplateListItemViewModel? SelectedTemplate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedModule))]
    public partial ModuleListItemViewModel? SelectedModule { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModulePreview))]
    public partial GenerationPreviewViewModel? ModulePreview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial GenerationPreviewViewModel? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationView))]
    public partial bool IsCatalogView { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateCatalog))]
    public partial bool IsModuleCatalog { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationView))]
    public partial bool IsModuleConfigurationView { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVariantSelection))]
    [NotifyPropertyChangedFor(nameof(CatalogStageTitle))]
    [NotifyPropertyChangedFor(nameof(CatalogStageDescription))]
    [NotifyPropertyChangedFor(nameof(CatalogItemCountText))]
    public partial bool IsPackageSelection { get; set; } = true;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = AllLanguages;

    [ObservableProperty]
    public partial string SelectedPlatform { get; set; } = AllPlatforms;

    [ObservableProperty]
    public partial string SelectedBuildSystem { get; set; } = AllBuildSystems;

    [ObservableProperty]
    public partial string SelectedTag { get; set; } = AllTags;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Loading template registries…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCatalogNotice))]
    public partial string? CatalogNotice { get; set; }

    [ObservableProperty]
    public partial string? CatalogConfigurationPath { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsGenerating { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProbePrerequisites))]
    public partial bool IsProbingPrerequisites { get; set; }

    [ObservableProperty]
    public partial string DestinationPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowsGeneration))]
    [NotifyPropertyChangedFor(nameof(IsWslGeneration))]
    [NotifyPropertyChangedFor(nameof(ActiveDestinationDisplay))]
    public partial GenerationHostKind GenerationHost { get; set; } =
        GenerationHostKind.Windows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDestinationDisplay))]
    public partial WslDistribution? SelectedWslDistribution { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDestinationDisplay))]
    public partial string WslDestinationPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WslStatusMessage { get; set; } =
        "Refresh to list distributions that are already running.";

    [ObservableProperty]
    public partial bool IsRefreshingWsl { get; set; }

    [ObservableProperty]
    public partial bool IsCatalogTabEditorVisible { get; set; }

    [ObservableProperty]
    public partial string NewCatalogTabName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCatalogTabSelectionRequired))]
    public partial CatalogTabKindOption? SelectedCatalogTabKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveCustomCatalogTab))]
    public partial CatalogTabViewModel? ActiveCustomCatalogTab { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModulePostGenerationInstructions))]
    public partial string? ModulePostGenerationInstructions { get; set; }

    [ObservableProperty]
    public partial string? ModuleLicenseSummary { get; set; }

    [ObservableProperty]
    public partial bool IsGenerationConfirmationVisible { get; set; }

    [ObservableProperty]
    public partial string GenerationConfirmationText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGenerationResult))]
    public partial GenerationResultViewModel? GenerationResult { get; set; }

    public void Load()
    {
        catalogLoadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        catalogLoadCancellation = cancellation;
        CatalogLoadTask = LoadCoreAsync(cancellation);
    }

    public void ToggleFavorite(TemplateListItemViewModel template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var nextValue = !template.IsFavorite;
        if (favoriteStore is null)
        {
            template.IsFavorite = nextValue;
            return;
        }

        var saved = favoriteStore.SetFavorite(
            template.FavoriteIdentity,
            nextValue);
        if (!saved.IsSuccess)
        {
            ErrorMessage = string.Join(
                Environment.NewLine,
                saved.Issues.Select(issue => issue.Message));
            StatusMessage = "Favorite could not be saved";
            return;
        }

        template.IsFavorite = nextValue;
        ApplyFilters();
        StatusMessage = nextValue
            ? $"Favorited {template.VariantDisplayName}"
            : $"Removed {template.VariantDisplayName} from favorites";
    }

    public void ToggleFavorite(ModuleListItemViewModel module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var nextValue = !module.IsFavorite;
        if (favoriteStore is null)
        {
            module.IsFavorite = nextValue;
            return;
        }

        var saved = favoriteStore.SetFavorite(
            module.FavoriteIdentity,
            nextValue);
        if (!saved.IsSuccess)
        {
            SetIssues(saved.Issues, "Favorite could not be saved.");
            return;
        }

        module.IsFavorite = nextValue;
        ApplyFilters();
        StatusMessage = nextValue
            ? $"Favorited {module.Name}"
            : $"Removed {module.Name} from favorites";
    }

    [RelayCommand]
    private void ShowTemplatesCatalog()
    {
        ActiveCustomCatalogTab = null;
        IsModuleCatalog = false;
        IsPackageSelection = true;
        ApplyFilters();
        StatusMessage = "Choose a package";
    }

    [RelayCommand]
    private void ShowModulesCatalog()
    {
        ActiveCustomCatalogTab = null;
        IsModuleCatalog = true;
        ApplyFilters();
        StatusMessage = Modules.Count == 0
            ? "No modules are available in the configured registries"
            : "Choose a reusable module";
    }

    [RelayCommand]
    private void BeginAddCatalogTab()
    {
        IsCatalogTabEditorVisible = true;
        NewCatalogTabName = string.Empty;
        SelectedCatalogTabKind = CatalogTabKinds[0];
        PopulateCatalogTabCandidates();
        StatusMessage = "Create a local personal catalog tab";
    }

    [RelayCommand]
    private void CancelAddCatalogTab()
    {
        IsCatalogTabEditorVisible = false;
        CatalogTabCandidates.Clear();
        StatusMessage = IsModuleCatalog
            ? "Choose a reusable module"
            : "Choose a package";
    }

    [RelayCommand]
    private void CreateCatalogTab()
    {
        if (catalogTabStore is null)
        {
            ErrorMessage = "Personal catalog tab storage is unavailable.";
            return;
        }

        var name = NewCatalogTabName.Trim();
        if (name.Length is 0 or > 40 ||
            SelectedCatalogTabKind is null)
        {
            ErrorMessage =
                "Enter a tab name between 1 and 40 characters and choose its content.";
            return;
        }

        var selectedIdentities = CatalogTabCandidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Identity)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (IsCatalogTabSelectionRequired &&
            selectedIdentities.IsDefaultOrEmpty)
        {
            ErrorMessage = "Select at least one catalog item for this tab.";
            return;
        }

        var definition = new CatalogTabDefinition(
            Guid.NewGuid().ToString("N"),
            name,
            SelectedCatalogTabKind.Kind,
            selectedIdentities);
        var definitions = CustomCatalogTabs
            .Select(tab => tab.Definition)
            .Append(definition);
        var saved = catalogTabStore.Save(definitions);
        if (!saved.IsSuccess)
        {
            SetIssues(saved.Issues, "The personal catalog tab could not be saved.");
            return;
        }

        ReloadCatalogTabs(saved.Value!);
        var created = CustomCatalogTabs.First(tab => tab.Id == definition.Id);
        SelectCustomCatalogTab(created);
        IsCatalogTabEditorVisible = false;
        CatalogTabCandidates.Clear();
        ErrorMessage = null;
        StatusMessage = $"Created local tab '{name}'";
    }

    [RelayCommand]
    private void RemoveActiveCatalogTab()
    {
        if (ActiveCustomCatalogTab is null || catalogTabStore is null)
        {
            return;
        }

        var removedName = ActiveCustomCatalogTab.Name;
        var saved = catalogTabStore.Save(
            CustomCatalogTabs
                .Where(tab => tab != ActiveCustomCatalogTab)
                .Select(tab => tab.Definition));
        if (!saved.IsSuccess)
        {
            SetIssues(saved.Issues, "The personal catalog tab could not be removed.");
            return;
        }

        ReloadCatalogTabs(saved.Value!);
        ActiveCustomCatalogTab = null;
        ShowTemplatesCatalog();
        StatusMessage = $"Removed local tab '{removedName}'";
    }

    public void SelectCustomCatalogTab(CatalogTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!CustomCatalogTabs.Contains(tab))
        {
            return;
        }

        ActiveCustomCatalogTab = tab;
        IsModuleCatalog = tab.IsModuleTab;
        IsPackageSelection = true;
        IsCatalogTabEditorVisible = false;
        ApplyFilters();
        StatusMessage = $"Showing personal tab '{tab.Name}'";
    }

    [RelayCommand(CanExecute = nameof(CanOpenModuleConfiguration))]
    private void OpenModuleConfiguration(ModuleListItemViewModel? module)
    {
        if (module is null || !Modules.Contains(module))
        {
            return;
        }

        SelectedModule = module;
        IsModuleConfigurationView = true;
        IsCatalogView = false;
        StatusMessage = $"Configure {module.Name}";
    }

    [RelayCommand]
    private void BackToModuleCatalog()
    {
        IsModuleConfigurationView = false;
        IsCatalogView = true;
        IsModuleCatalog = true;
        ErrorMessage = null;
        IsGenerationConfirmationVisible = false;
        StatusMessage = "Choose a reusable module";
    }

    [RelayCommand]
    private async Task RefreshWslAsync()
    {
        if (wslGenerationService is null || IsRefreshingWsl)
        {
            WslStatusMessage = "WSL support is unavailable in this session.";
            return;
        }

        IsRefreshingWsl = true;
        WslStatusMessage = "Finding running WSL distributions…";
        try
        {
            var result = await wslGenerationService.DiscoverRunningAsync();
            RunningWslDistributions.Clear();
            if (!result.IsSuccess)
            {
                WslStatusMessage = string.Join(
                    Environment.NewLine,
                    result.Issues.Select(issue => issue.Message));
                return;
            }

            foreach (var distribution in result.Value!.Distributions)
            {
                RunningWslDistributions.Add(distribution);
            }

            SelectedWslDistribution =
                RunningWslDistributions.FirstOrDefault(distribution =>
                    string.Equals(
                        distribution.Name,
                        SelectedWslDistribution?.Name,
                        StringComparison.OrdinalIgnoreCase)) ??
                RunningWslDistributions.FirstOrDefault();
            if (SelectedWslDistribution is not null &&
                string.IsNullOrWhiteSpace(WslDestinationPath))
            {
                WslDestinationPath =
                    $"{SelectedWslDistribution.HomePath}/projects";
            }

            OnPropertyChanged(nameof(HasWslDistributions));
            WslStatusMessage = RunningWslDistributions.Count == 0
                ? "No running distributions found. Start one and refresh."
                : $"{RunningWslDistributions.Count} running distribution" +
                  (RunningWslDistributions.Count == 1 ? " found." : "s found.");
        }
        finally
        {
            IsRefreshingWsl = false;
        }
    }

    [RelayCommand]
    private void UseWslHome()
    {
        if (SelectedWslDistribution is not null)
        {
            WslDestinationPath = SelectedWslDistribution.HomePath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanProbePrerequisites))]
    private async Task ProbePrerequisitesAsync()
    {
        if (prerequisiteProbeService is null)
        {
            return;
        }

        IsProbingPrerequisites = true;
        StatusMessage = "Checking prerequisites with consented read-only probes…";
        try
        {
            foreach (var prerequisite in Prerequisites)
            {
                prerequisite.ProbeResult =
                    await prerequisiteProbeService.ProbeAsync(prerequisite.Id);
            }

            StatusMessage = "Prerequisite checks complete";
        }
        finally
        {
            IsProbingPrerequisites = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmPackage))]
    private void ConfirmPackage()
    {
        if (SelectedPackage is null)
        {
            return;
        }

        IsPackageSelection = false;
        ErrorMessage = null;
        ApplyFilters();
        StatusMessage =
            $"Choose one of {SelectedPackage.Variants.Count} variants for {SelectedPackage.Name}";
    }

    [RelayCommand]
    private void BackToPackages()
    {
        IsPackageSelection = true;
        SelectedTemplate = null;
        ErrorMessage = null;
        ApplyFilters();
        StatusMessage = "Choose a package";
    }

    [RelayCommand(CanExecute = nameof(CanOpenConfiguration))]
    private void OpenConfiguration(TemplateListItemViewModel? template)
    {
        if (template is null ||
            SelectedPackage is null ||
            !SelectedPackage.Variants.Contains(template))
        {
            return;
        }

        SelectedTemplate = template;
        IsCatalogView = false;
        ErrorMessage = null;
        StatusMessage = $"Configure {template.VariantDisplayName}";
    }

    [RelayCommand]
    private void BackToCatalog()
    {
        IsCatalogView = true;
        IsPackageSelection = SelectedPackage is null;
        ErrorMessage = null;
        IsGenerationConfirmationVisible = false;
        ApplyFilters();
        StatusMessage = IsPackageSelection
            ? "Choose a package"
            : $"Choose a variant for {SelectedPackage!.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        Preview = null;
        GenerationResult = null;
        IsGenerationConfirmationVisible = false;
        StatusMessage = "Rendering preview…";

        try
        {
            var result = await TemplatePlanner.CreatePlanAsync(
                SelectedTemplate.Package,
                GetParameterValues());
            if (!result.IsSuccess)
            {
                SetIssues(result.Issues, "The template configuration is invalid.");
                return;
            }

            Preview = new GenerationPreviewViewModel(result.Value!);
            StatusMessage = $"Preview ready · {result.Value!.Files.Length} files";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticException = exception;
            ErrorMessage = "Klonker could not read the selected template's content.";
            StatusMessage = "Preview failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewModule))]
    private async Task PreviewModuleAsync()
    {
        if (SelectedModule is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        ModulePreview = null;
        modulePlan = null;
        GenerationResult = null;
        IsGenerationConfirmationVisible = false;
        StatusMessage = "Rendering module preview…";
        try
        {
            var result = await ModulePlanner.CreatePlanAsync(
                SelectedModule.Package,
                GetModuleParameterValues());
            if (!result.IsSuccess)
            {
                SetIssues(result.Issues, "The module configuration is invalid.");
                return;
            }

            modulePlan = result.Value!;
            ModulePreview = new GenerationPreviewViewModel(result.Value!.FilePlan);
            ModulePostGenerationInstructions =
                result.Value.PostGenerationInstructions;
            ModuleLicenseSummary = result.Value.LicenseReport.Summary;
            StatusMessage =
                $"Module preview ready · {result.Value.FilePlan.Files.Length} files";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticException = exception;
            ErrorMessage = "Klonker could not read the selected module's content.";
            StatusMessage = "Module preview failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseDestination))]
    private async Task BrowseDestinationAsync()
    {
        if (destinationPicker is null)
        {
            ErrorMessage = "A native destination picker is unavailable in this session.";
            return;
        }

        try
        {
            var selected = await destinationPicker.PickAsync();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                DestinationPath = selected;
                StatusMessage = "Destination selected";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticException = exception;
            ErrorMessage = "The destination picker could not be opened.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRequestGeneration))]
    private void RequestGeneration()
    {
        if (Preview is null)
        {
            return;
        }

        ErrorMessage = null;
        var selectedDestination = ResolveActiveDestination();
        if (!selectedDestination.IsSuccess)
        {
            SetIssues(selectedDestination.Issues, "The destination is not ready.");
            return;
        }

        var validation = GenerationDestinationValidator.Validate(
            selectedDestination.Value!.WindowsPath);
        if (!validation.IsSuccess)
        {
            SetIssues(validation.Issues, "The destination is not ready.");
            return;
        }

        if (IsWindowsGeneration)
        {
            DestinationPath = validation.Value!;
        }

        GenerationConfirmationText =
            $"Generate {Preview.Plan.Files.Length} files into '{selectedDestination.Value.DisplayPath}'? " +
            "Klonker will only use a new or empty directory and will not manage the project afterward.";
        IsGenerationConfirmationVisible = true;
        GenerationResult = null;
        StatusMessage = "Confirm project generation";
    }

    [RelayCommand(CanExecute = nameof(CanRequestModuleGeneration))]
    private void RequestModuleGeneration()
    {
        if (modulePlan is null || ModulePreview is null)
        {
            return;
        }

        ErrorMessage = null;
        var selectedDestination = ResolveActiveDestination();
        if (!selectedDestination.IsSuccess)
        {
            SetIssues(selectedDestination.Issues, "The module destination is not ready.");
            return;
        }

        var preflight = ModuleGenerationExecutor.Preflight(
            modulePlan,
            selectedDestination.Value!.WindowsPath);
        if (!preflight.IsDefaultOrEmpty)
        {
            SetIssues(
                preflight,
                "Resolve the listed destination conflicts, then retry.");
            return;
        }

        GenerationConfirmationText =
            $"Add {modulePlan.FilePlan.Files.Length} module files to " +
            $"'{selectedDestination.Value.DisplayPath}'? Existing files are never overwritten; " +
            "the complete file tree was preflighted.";
        IsGenerationConfirmationVisible = true;
        GenerationResult = null;
        StatusMessage = "Confirm module generation";
    }

    [RelayCommand(CanExecute = nameof(CanConfirmGeneration))]
    private async Task ConfirmGenerationAsync()
    {
        if (Preview is null || generationService is null)
        {
            return;
        }

        IsGenerationConfirmationVisible = false;
        IsGenerating = true;
        IsBusy = true;
        ErrorMessage = null;
        GenerationResult = null;
        generationCancellation?.Dispose();
        generationCancellation = new CancellationTokenSource();
        StatusMessage = "Generating project…";

        try
        {
            var result = IsWslGeneration
                ? await wslGenerationService!.GenerateProjectAsync(
                    Preview.Plan,
                    SelectedWslDistribution!.Name,
                    WslDestinationPath,
                    generationCancellation.Token)
                : await generationService.GenerateAsync(
                    Preview.Plan,
                    DestinationPath,
                    generationCancellation.Token);
            DiagnosticException = result.DiagnosticException;
            GenerationResult = new GenerationResultViewModel(
                result,
                ActiveDestinationDisplay);
            StatusMessage = result.Message;
            if (!result.Succeeded)
            {
                ErrorMessage = result.Message;
                diagnosticLog?.Write(
                    DiagnosticLogLevel.Error,
                    "generation.failed",
                    result.Message,
                    result.DiagnosticException);
            }
        }
        catch (OperationCanceledException)
        {
            GenerationResult = new GenerationResultViewModel(
                new Klonker.Core.Generation.GenerationResult(
                    GenerationStatus.Cancelled,
                    "Generation was cancelled before the project was installed.",
                    []),
                ActiveDestinationDisplay);
            StatusMessage = GenerationResult.Message;
        }
        finally
        {
            generationCancellation?.Dispose();
            generationCancellation = null;
            IsGenerating = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmModuleGeneration))]
    private async Task ConfirmModuleGenerationAsync()
    {
        if (modulePlan is null)
        {
            return;
        }

        IsGenerationConfirmationVisible = false;
        IsGenerating = true;
        IsBusy = true;
        ErrorMessage = null;
        GenerationResult = null;
        generationCancellation?.Dispose();
        generationCancellation = new CancellationTokenSource();
        StatusMessage = "Generating module…";
        try
        {
            GenerationResult result;
            if (IsWslGeneration)
            {
                result = await wslGenerationService!.GenerateModuleAsync(
                    modulePlan,
                    SelectedWslDistribution!.Name,
                    WslDestinationPath,
                    generationCancellation.Token);
            }
            else
            {
                result = await ModuleGenerationExecutor.ExecuteAsync(
                    modulePlan,
                    DestinationPath,
                    generationCancellation.Token);
            }

            DiagnosticException = result.DiagnosticException;
            GenerationResult = new GenerationResultViewModel(
                result,
                ActiveDestinationDisplay);
            StatusMessage = result.Message;
            if (!result.Succeeded)
            {
                ErrorMessage = result.Message;
                diagnosticLog?.Write(
                    DiagnosticLogLevel.Error,
                    "module.generation_failed",
                    result.Message,
                    result.DiagnosticException);
            }
        }
        catch (OperationCanceledException)
        {
            var cancelled = new GenerationResult(
                GenerationStatus.Cancelled,
                "Module generation was cancelled.",
                []);
            GenerationResult = new GenerationResultViewModel(
                cancelled,
                ActiveDestinationDisplay);
            StatusMessage = cancelled.Message;
        }
        finally
        {
            generationCancellation?.Dispose();
            generationCancellation = null;
            IsGenerating = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelGeneration))]
    private void CancelGeneration()
    {
        generationCancellation?.Cancel();
    }

    [RelayCommand]
    private void CancelGenerationConfirmation()
    {
        IsGenerationConfirmationVisible = false;
        StatusMessage = Preview is null
            ? "Build a preview before generation"
            : $"Preview ready · {Preview.Plan.Files.Length} files";
    }

    private bool CanConfirmPackage() => SelectedPackage is not null && !IsBusy;

    private bool CanOpenConfiguration(TemplateListItemViewModel? template) =>
        template is not null &&
        SelectedPackage is not null &&
        SelectedPackage.Variants.Contains(template) &&
        !IsBusy;

    private bool CanOpenModuleConfiguration(ModuleListItemViewModel? module) =>
        module is not null && Modules.Contains(module) && !IsBusy;

    private bool CanPreview() => SelectedTemplate is not null && !IsBusy;

    private bool CanPreviewModule() => SelectedModule is not null && !IsBusy;

    private bool CanBrowseDestination() =>
        !IsBusy && IsWindowsGeneration;

    private bool CanRequestGeneration() =>
        Preview is not null &&
        generationService is not null &&
        !IsBusy &&
        GenerationResult?.Succeeded != true &&
        HasActiveDestination;

    private bool CanRequestModuleGeneration() =>
        modulePlan is not null &&
        ModulePreview is not null &&
        !IsBusy &&
        GenerationResult?.Succeeded != true &&
        HasActiveDestination;

    private bool CanConfirmGeneration() =>
        IsGenerationConfirmationVisible &&
        Preview is not null &&
        generationService is not null &&
        !IsBusy &&
        (!IsWslGeneration ||
         wslGenerationService is not null &&
         SelectedWslDistribution is not null);

    private bool CanConfirmModuleGeneration() =>
        IsGenerationConfirmationVisible &&
        modulePlan is not null &&
        !IsBusy &&
        (!IsWslGeneration ||
         wslGenerationService is not null &&
         SelectedWslDistribution is not null);

    private bool CanCancelGeneration() => IsGenerating;

    private bool HasActiveDestination =>
        IsWindowsGeneration
            ? !string.IsNullOrWhiteSpace(DestinationPath)
            : wslGenerationService is not null &&
              SelectedWslDistribution is not null &&
              !string.IsNullOrWhiteSpace(WslDestinationPath);

    private OperationResult<GenerationDestinationSelection>
        ResolveActiveDestination()
    {
        if (IsWindowsGeneration)
        {
            if (string.IsNullOrWhiteSpace(DestinationPath))
            {
                return new OperationResult<GenerationDestinationSelection>(
                    null,
                    [
                        new ValidationIssue(
                            ValidationSeverity.Error,
                            "destination.required",
                            "Choose a Windows destination."),
                    ]);
            }

            try
            {
                var fullPath = Path.GetFullPath(DestinationPath);
                return new OperationResult<GenerationDestinationSelection>(
                    new GenerationDestinationSelection(fullPath, fullPath),
                    []);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
            {
                return new OperationResult<GenerationDestinationSelection>(
                    null,
                    [
                        new ValidationIssue(
                            ValidationSeverity.Error,
                            "destination.invalid",
                            $"The Windows destination is invalid: {exception.Message}"),
                    ]);
            }
        }

        if (wslGenerationService is null ||
            SelectedWslDistribution is null)
        {
            return new OperationResult<GenerationDestinationSelection>(
                null,
                [
                    new ValidationIssue(
                        ValidationSeverity.Error,
                        "wsl.distribution_required",
                        "Refresh and select a running WSL distribution."),
                ]);
        }

        var resolved = wslGenerationService.ResolveDestination(
            SelectedWslDistribution.Name,
            WslDestinationPath);
        return resolved.IsSuccess
            ? new OperationResult<GenerationDestinationSelection>(
                new GenerationDestinationSelection(
                    resolved.Value!.WindowsUncPath,
                    $"{resolved.Value.DistributionName}:{resolved.Value.LinuxPath}"),
                resolved.Issues)
            : new OperationResult<GenerationDestinationSelection>(
                null,
                resolved.Issues);
    }

    private async Task LoadCoreAsync(CancellationTokenSource cancellation)
    {
        if (catalog is null)
        {
            StatusMessage = "Design-time preview";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        CatalogNotice = null;
        DiagnosticException = null;
        DisposePackages();
        Templates.Clear();
        Packages.Clear();
        FilteredPackages.Clear();
        FilteredVariants.Clear();
        Modules.Clear();
        FilteredModules.Clear();
        SelectedPackage = null;
        SelectedTemplate = null;
        IsPackageSelection = true;

        try
        {
            var result = await catalog.LoadAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
            {
                SetIssues(result.Issues, "The template catalog could not be loaded.");
                return;
            }

            CatalogConfigurationPath = result.Value!.ConfigurationPath;
            var appSettings = appSettingsStore?.Load();
            prerequisiteProbesEnabled =
                appSettings?.IsSuccess == true &&
                appSettings.Value!.PrerequisiteProbesEnabled;
            OnPropertyChanged(nameof(CanProbePrerequisites));
            OnPropertyChanged(nameof(PrerequisiteProbeConsentText));
            ProbePrerequisitesCommand.NotifyCanExecuteChanged();
            var favoriteIdentities = Array.Empty<string>();
            if (favoriteStore is not null)
            {
                var favorites = favoriteStore.Load();
                if (favorites.IsSuccess)
                {
                    favoriteIdentities = favorites.Value!.TemplateIdentities.ToArray();
                }
                else
                {
                    CatalogNotice = string.Join(
                        Environment.NewLine,
                        favorites.Issues.Select(issue => issue.Message));
                }
            }

            foreach (var template in result.Value.Templates)
            {
                var favoriteIdentity =
                    $"{template.RegistryId}:{template.Entry.TemplateId}";
                Templates.Add(new TemplateListItemViewModel(
                    template,
                    favoriteIdentities.Contains(
                        favoriteIdentity,
                        StringComparer.Ordinal),
                    tagPalette));
            }

            if (!result.Value.Modules.IsDefault)
            {
                foreach (var module in result.Value.Modules)
                {
                    var favoriteIdentity =
                        $"module:{module.RegistryId}:{module.Entry.ModuleId}";
                    Modules.Add(new ModuleListItemViewModel(
                        module,
                        favoriteIdentities.Contains(
                            favoriteIdentity,
                            StringComparer.Ordinal),
                        tagPalette));
                }
            }

            foreach (var packageGroup in Templates
                         .GroupBy(
                             template => (template.RegistryId, template.Family))
                         .OrderBy(
                             group => group.Key.RegistryId,
                             StringComparer.Ordinal)
                         .ThenBy(
                             group => group.Key.Family,
                             StringComparer.Ordinal))
            {
                Packages.Add(new PackageListItemViewModel(
                    packageGroup,
                    tagPalette));
            }

            if (catalogTabStore is not null)
            {
                var tabSnapshot = catalogTabStore.Load();
                if (tabSnapshot.IsSuccess)
                {
                    ReloadCatalogTabs(tabSnapshot.Value!);
                }
                else
                {
                    CatalogNotice = string.Join(
                        Environment.NewLine,
                        tabSnapshot.Issues.Select(issue => issue.Message));
                }
            }

            var notices = result.Issues
                .Where(issue => issue.Severity != ValidationSeverity.Error)
                .Select(issue => issue.Message)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var catalogNotices = new List<string>();
            if (!string.IsNullOrWhiteSpace(CatalogNotice))
            {
                catalogNotices.Add(CatalogNotice);
            }

            catalogNotices.AddRange(notices);
            CatalogNotice = catalogNotices.Count == 0
                ? null
                : string.Join(
                    Environment.NewLine,
                    catalogNotices.Distinct(StringComparer.Ordinal));

            if (Packages.Count == 0 && Modules.Count == 0)
            {
                ErrorMessage =
                    "The configured registries do not contain any usable packages or modules.";
                StatusMessage = "No catalog items available";
                return;
            }

            PopulateFilters();
            ApplyFilters();
            var registryCount = Packages
                .Select(package => package.RegistryId)
                .Concat(Modules.Select(module => module.RegistryId))
                .Distinct(StringComparer.Ordinal)
                .Count();
            StatusMessage =
                $"{Packages.Count} package{(Packages.Count == 1 ? string.Empty : "s")} · " +
                $"{Templates.Count} variant{(Templates.Count == 1 ? string.Empty : "s")} " +
                $"· {Modules.Count} module{(Modules.Count == 1 ? string.Empty : "s")} " +
                $"from {registryCount} registr{(registryCount == 1 ? "y" : "ies")}" +
                (result.Value.Offline ? " · offline" : string.Empty);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                HttpRequestException)
        {
            DiagnosticException = exception;
            ErrorMessage = "Klonker could not load the configured template registries.";
            StatusMessage = "Template loading failed";
            diagnosticLog?.Write(
                DiagnosticLogLevel.Error,
                "catalog.load_failed",
                ErrorMessage,
                exception);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Template loading cancelled";
        }
        finally
        {
            if (ReferenceEquals(catalogLoadCancellation, cancellation))
            {
                catalogLoadCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
        }
    }

    partial void OnSelectedPackageChanged(PackageListItemViewModel? value)
    {
        ConfirmPackageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CatalogStageDescription));
        if (!IsPackageSelection &&
            (value is null ||
             SelectedTemplate is null ||
             !value.Variants.Contains(SelectedTemplate)))
        {
            SelectedTemplate = FilteredVariants.FirstOrDefault();
        }
    }

    partial void OnSelectedTemplateChanged(TemplateListItemViewModel? value)
    {
        foreach (var parameter in Parameters)
        {
            parameter.ValueChanged -= OnParameterValueChanged;
        }

        Parameters.Clear();
        Prerequisites.Clear();
        Preview = null;
        GenerationResult = null;
        IsGenerationConfirmationVisible = false;
        ErrorMessage = null;

        if (value is not null)
        {
            foreach (var definition in value.Package.Manifest.Parameters)
            {
                var parameter = new ParameterEditorViewModel(definition);
                parameter.ValueChanged += OnParameterValueChanged;
                Parameters.Add(parameter);
            }

            if (!value.Package.Manifest.Prerequisites.IsDefault)
            {
                foreach (var prerequisite in value.Package.Manifest.Prerequisites)
                {
                    Prerequisites.Add(new PrerequisiteViewModel(prerequisite));
                }
            }
        }

        OnPropertyChanged(nameof(HasPrerequisites));
        OnPropertyChanged(nameof(CanProbePrerequisites));
        OnPropertyChanged(nameof(PrerequisiteProbeConsentText));
        ProbePrerequisitesCommand.NotifyCanExecuteChanged();
        OpenConfigurationCommand.NotifyCanExecuteChanged();
        NotifyCommandStates();
    }

    partial void OnSelectedModuleChanged(ModuleListItemViewModel? value)
    {
        foreach (var parameter in ModuleParameters)
        {
            parameter.ValueChanged -= OnModuleParameterValueChanged;
        }

        ModuleParameters.Clear();
        ModulePreview = null;
        modulePlan = null;
        ModulePostGenerationInstructions = null;
        ModuleLicenseSummary = null;
        GenerationResult = null;
        IsGenerationConfirmationVisible = false;
        ErrorMessage = null;

        if (value is not null)
        {
            foreach (var slot in value.Package.Manifest.Slots)
            {
                var editor = new ParameterEditorViewModel(
                    new TemplateParameterDefinition(
                        slot.Id,
                        TemplateParameterType.Text,
                        slot.Label,
                        slot.Description,
                        slot.Required,
                        slot.DefaultPath,
                        null,
                        ImmutableArray<string>.Empty));
                editor.ValueChanged += OnModuleParameterValueChanged;
                ModuleParameters.Add(editor);
            }

            foreach (var definition in value.Package.Manifest.Parameters)
            {
                var editor = new ParameterEditorViewModel(definition);
                editor.ValueChanged += OnModuleParameterValueChanged;
                ModuleParameters.Add(editor);
            }
        }

        OpenModuleConfigurationCommand.NotifyCanExecuteChanged();
        PreviewModuleCommand.NotifyCanExecuteChanged();
        RequestModuleGenerationCommand.NotifyCanExecuteChanged();
        ConfirmModuleGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnSelectedLanguageChanged(string value) => ApplyFilters();

    partial void OnSelectedPlatformChanged(string value) => ApplyFilters();

    partial void OnSelectedBuildSystemChanged(string value) => ApplyFilters();

    partial void OnSelectedTagChanged(string value) => ApplyFilters();

    partial void OnIsModuleCatalogChanged(bool value)
    {
        ApplyFilters();
        OnPropertyChanged(nameof(IsTemplateCatalog));
        OnPropertyChanged(nameof(CatalogStageTitle));
        OnPropertyChanged(nameof(CatalogStageDescription));
        OnPropertyChanged(nameof(CatalogItemCountText));
        OnPropertyChanged(nameof(IsTemplatePackageSelection));
        OnPropertyChanged(nameof(IsTemplateVariantSelection));
    }

    partial void OnIsPackageSelectionChanged(bool value)
    {
        ApplyFilters();
        OnPropertyChanged(nameof(CatalogStageTitle));
        OnPropertyChanged(nameof(CatalogStageDescription));
        OnPropertyChanged(nameof(CatalogItemCountText));
        OnPropertyChanged(nameof(IsTemplatePackageSelection));
        OnPropertyChanged(nameof(IsTemplateVariantSelection));
    }

    partial void OnIsBusyChanged(bool value)
    {
        ConfirmPackageCommand.NotifyCanExecuteChanged();
        OpenConfigurationCommand.NotifyCanExecuteChanged();
        NotifyCommandStates();
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        CancelGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsProbingPrerequisitesChanged(bool value)
    {
        ProbePrerequisitesCommand.NotifyCanExecuteChanged();
    }

    partial void OnDestinationPathChanged(string value)
    {
        IsGenerationConfirmationVisible = false;
        GenerationResult = null;
        RequestGenerationCommand.NotifyCanExecuteChanged();
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
        RequestModuleGenerationCommand.NotifyCanExecuteChanged();
        ConfirmModuleGenerationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ActiveDestinationDisplay));
    }

    partial void OnWslDestinationPathChanged(string value)
    {
        IsGenerationConfirmationVisible = false;
        GenerationResult = null;
        RequestGenerationCommand.NotifyCanExecuteChanged();
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
        RequestModuleGenerationCommand.NotifyCanExecuteChanged();
        ConfirmModuleGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnGenerationHostChanged(GenerationHostKind value)
    {
        IsGenerationConfirmationVisible = false;
        GenerationResult = null;
        BrowseDestinationCommand.NotifyCanExecuteChanged();
        NotifyCommandStates();
        RequestModuleGenerationCommand.NotifyCanExecuteChanged();
        ConfirmModuleGenerationCommand.NotifyCanExecuteChanged();
        if (value == GenerationHostKind.Wsl &&
            RunningWslDistributions.Count == 0)
        {
            _ = RefreshWslCommand.ExecuteAsync(null);
        }
    }

    partial void OnSelectedWslDistributionChanged(WslDistribution? value)
    {
        IsGenerationConfirmationVisible = false;
        GenerationResult = null;
        if (value is not null &&
            string.IsNullOrWhiteSpace(WslDestinationPath))
        {
            WslDestinationPath = value.HomePath;
        }

        NotifyCommandStates();
        RequestModuleGenerationCommand.NotifyCanExecuteChanged();
        ConfirmModuleGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCatalogTabKindChanged(CatalogTabKindOption? value)
    {
        PopulateCatalogTabCandidates();
        OnPropertyChanged(nameof(IsCatalogTabSelectionRequired));
    }

    partial void OnIsGenerationConfirmationVisibleChanged(bool value)
    {
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
        ConfirmModuleGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreviewChanged(GenerationPreviewViewModel? value)
    {
        RequestGenerationCommand.NotifyCanExecuteChanged();
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnGenerationResultChanged(GenerationResultViewModel? value)
    {
        RequestGenerationCommand.NotifyCanExecuteChanged();
        RequestModuleGenerationCommand.NotifyCanExecuteChanged();
    }

    private void PopulateFilters()
    {
        Languages.Clear();
        Languages.Add(AllLanguages);
        foreach (var language in Templates
                     .Select(template => template.Language)
                     .Concat(Modules.Select(module => module.Language))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            Languages.Add(language);
        }

        Platforms.Clear();
        Platforms.Add(AllPlatforms);
        foreach (var platform in Templates
                     .Select(template => template.Platform)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            Platforms.Add(platform);
        }

        BuildSystems.Clear();
        BuildSystems.Add(AllBuildSystems);
        foreach (var buildSystem in Templates
                     .Select(template => template.BuildSystem)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            BuildSystems.Add(buildSystem);
        }

        AvailableTags.Clear();
        AvailableTags.Add(AllTags);
        foreach (var tag in Templates
                     .SelectMany(template => template.Tags)
                     .Concat(Modules.SelectMany(module => module.Tags))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            AvailableTags.Add(tag);
        }

        SelectedLanguage = AllLanguages;
        SelectedPlatform = AllPlatforms;
        SelectedBuildSystem = AllBuildSystems;
        SelectedTag = AllTags;
    }

    private void ApplyFilters()
    {
        if (IsModuleCatalog)
        {
            ApplyModuleFilters();
            OnPropertyChanged(nameof(CatalogItemCountText));
            return;
        }

        if (IsPackageSelection)
        {
            ApplyPackageFilters();
        }
        else
        {
            ApplyVariantFilters();
        }

        OnPropertyChanged(nameof(CatalogItemCountText));
    }

    private void ApplyModuleFilters()
    {
        var search = SearchText.Trim();
        var matches = Modules.Where(module =>
            IsModuleAllowedByActiveTab(module) &&
            module.Matches(
                search,
                SelectedLanguage,
                SelectedTag));

        FilteredModules.Clear();
        foreach (var module in matches)
        {
            FilteredModules.Add(module);
        }

        if (SelectedModule is null ||
            !FilteredModules.Contains(SelectedModule))
        {
            SelectedModule = FilteredModules.FirstOrDefault();
        }
    }

    private void ApplyPackageFilters()
    {
        var search = SearchText.Trim();
        var matches = Packages.Where(package =>
            package.Variants.Any(variant =>
                IsTemplateAllowedByActiveTab(variant) &&
                (SelectedLanguage == AllLanguages ||
                 variant.Language == SelectedLanguage) &&
                (SelectedPlatform == AllPlatforms ||
                 variant.Platform == SelectedPlatform) &&
                (SelectedBuildSystem == AllBuildSystems ||
                 variant.BuildSystem == SelectedBuildSystem) &&
                (SelectedTag == AllTags ||
                 variant.Tags.Contains(
                     SelectedTag,
                     StringComparer.OrdinalIgnoreCase))) &&
            package.MatchesSearch(search));

        FilteredPackages.Clear();
        foreach (var package in matches)
        {
            FilteredPackages.Add(package);
        }

        if (SelectedPackage is null || !FilteredPackages.Contains(SelectedPackage))
        {
            SelectedPackage = FilteredPackages.FirstOrDefault();
        }
    }

    private void ApplyVariantFilters()
    {
        var search = SearchText.Trim();
        var candidates = SelectedPackage?.Variants ??
            Array.Empty<TemplateListItemViewModel>();
        var matches = candidates.Where(template =>
            IsTemplateAllowedByActiveTab(template) &&
            (SelectedLanguage == AllLanguages ||
             template.Language == SelectedLanguage) &&
            (SelectedPlatform == AllPlatforms ||
             template.Platform == SelectedPlatform) &&
            (SelectedBuildSystem == AllBuildSystems ||
             template.BuildSystem == SelectedBuildSystem) &&
            (SelectedTag == AllTags ||
             template.Tags.Contains(SelectedTag, StringComparer.OrdinalIgnoreCase)) &&
            (search.Length == 0 ||
             template.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             template.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             template.Family.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             template.Variant.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             template.Platform.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             template.BuildSystem.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             template.RegistryName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             template.Tags.Any(tag =>
                 tag.Contains(search, StringComparison.OrdinalIgnoreCase))));

        FilteredVariants.Clear();
        foreach (var template in matches)
        {
            FilteredVariants.Add(template);
        }

        if (SelectedTemplate is null || !FilteredVariants.Contains(SelectedTemplate))
        {
            SelectedTemplate = FilteredVariants.FirstOrDefault();
        }
    }

    private bool IsTemplateAllowedByActiveTab(
        TemplateListItemViewModel template)
    {
        var tab = ActiveCustomCatalogTab;
        if (tab is null)
        {
            return true;
        }

        return tab.Kind switch
        {
            CatalogTabKind.FavoriteTemplates => template.IsFavorite,
            CatalogTabKind.SelectedTemplates =>
                tab.ItemIdentities.Contains(
                    template.FavoriteIdentity,
                    StringComparer.Ordinal),
            _ => false,
        };
    }

    private bool IsModuleAllowedByActiveTab(ModuleListItemViewModel module)
    {
        var tab = ActiveCustomCatalogTab;
        if (tab is null)
        {
            return true;
        }

        return tab.Kind switch
        {
            CatalogTabKind.FavoriteModules => module.IsFavorite,
            CatalogTabKind.SelectedModules =>
                tab.ItemIdentities.Contains(
                    module.FavoriteIdentity,
                    StringComparer.Ordinal),
            _ => false,
        };
    }

    private void PopulateCatalogTabCandidates()
    {
        CatalogTabCandidates.Clear();
        if (SelectedCatalogTabKind?.Kind ==
            CatalogTabKind.SelectedTemplates)
        {
            foreach (var template in Templates
                         .OrderBy(template => template.Name, StringComparer.Ordinal)
                         .ThenBy(template => template.Variant, StringComparer.Ordinal))
            {
                CatalogTabCandidates.Add(new CatalogTabCandidateViewModel(
                    template.FavoriteIdentity,
                    $"{template.Name} — {template.VariantDisplayName}",
                    $"{template.RegistryName} · {template.Version}"));
            }
        }
        else if (SelectedCatalogTabKind?.Kind ==
                 CatalogTabKind.SelectedModules)
        {
            foreach (var module in Modules
                         .OrderBy(module => module.Name, StringComparer.Ordinal))
            {
                CatalogTabCandidates.Add(new CatalogTabCandidateViewModel(
                    module.FavoriteIdentity,
                    module.Name,
                    $"{module.RegistryName} · {module.Version}"));
            }
        }
    }

    private void ReloadCatalogTabs(CatalogTabSnapshot snapshot)
    {
        var activeId = ActiveCustomCatalogTab?.Id;
        CustomCatalogTabs.Clear();
        foreach (var definition in snapshot.Tabs)
        {
            CustomCatalogTabs.Add(new CatalogTabViewModel(definition));
        }

        ActiveCustomCatalogTab = activeId is null
            ? null
            : CustomCatalogTabs.FirstOrDefault(tab => tab.Id == activeId);
    }

    private Dictionary<string, object?> GetParameterValues() =>
        Parameters.ToDictionary(
            parameter => parameter.Id,
            parameter => parameter.GetValue(),
            StringComparer.Ordinal);

    private Dictionary<string, object?> GetModuleParameterValues() =>
        ModuleParameters.ToDictionary(
            parameter => parameter.Id,
            parameter => parameter.GetValue(),
            StringComparer.Ordinal);

    private void OnParameterValueChanged(object? sender, EventArgs eventArgs)
    {
        Preview = null;
        GenerationResult = null;
        IsGenerationConfirmationVisible = false;
        ErrorMessage = null;
        StatusMessage = "Configuration changed · preview to refresh";
    }

    private void OnModuleParameterValueChanged(object? sender, EventArgs eventArgs)
    {
        ModulePreview = null;
        modulePlan = null;
        ModulePostGenerationInstructions = null;
        ModuleLicenseSummary = null;
        GenerationResult = null;
        IsGenerationConfirmationVisible = false;
        ErrorMessage = null;
        StatusMessage = "Module configuration changed · preview to refresh";
        RequestModuleGenerationCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCommandStates()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        BrowseDestinationCommand.NotifyCanExecuteChanged();
        RequestGenerationCommand.NotifyCanExecuteChanged();
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
        CancelGenerationCommand.NotifyCanExecuteChanged();
        PreviewModuleCommand.NotifyCanExecuteChanged();
        RequestModuleGenerationCommand.NotifyCanExecuteChanged();
        ConfirmModuleGenerationCommand.NotifyCanExecuteChanged();
        OpenModuleConfigurationCommand.NotifyCanExecuteChanged();
    }

    private void SetIssues(
        IEnumerable<ValidationIssue> issues,
        string fallbackMessage)
    {
        var errors = issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        ErrorMessage = errors.Length == 0
            ? fallbackMessage
            : string.Join(Environment.NewLine, errors);
        StatusMessage = fallbackMessage;
        diagnosticLog?.Write(
            DiagnosticLogLevel.Error,
            "validation.failed",
            ErrorMessage);
    }

    private void DisposePackages()
    {
        foreach (var package in Packages)
        {
            package.Dispose();
        }
    }

    public void Dispose()
    {
        catalogLoadCancellation?.Cancel();
        catalogLoadCancellation?.Dispose();
        generationCancellation?.Cancel();
        generationCancellation?.Dispose();
        DisposePackages();
        Templates.Clear();
        Packages.Clear();
        FilteredPackages.Clear();
        FilteredVariants.Clear();
        Modules.Clear();
        FilteredModules.Clear();
        GC.SuppressFinalize(this);
    }

    private sealed record GenerationDestinationSelection(
        string WindowsPath,
        string DisplayPath);
}
