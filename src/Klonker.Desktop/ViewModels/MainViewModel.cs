using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
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
    private CancellationTokenSource? catalogLoadCancellation;
    private CancellationTokenSource? generationCancellation;

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
    }

    public MainViewModel(
        ITemplateCatalog catalog,
        IProjectGenerationService? generationService = null,
        IDestinationPicker? destinationPicker = null)
        : this()
    {
        this.catalog = catalog;
        this.generationService = generationService;
        this.destinationPicker = destinationPicker;
    }

    public ObservableCollection<TemplateListItemViewModel> Templates { get; } = [];

    public ObservableCollection<PackageListItemViewModel> Packages { get; } = [];

    public ObservableCollection<PackageListItemViewModel> FilteredPackages { get; } = [];

    public ObservableCollection<TemplateListItemViewModel> FilteredVariants { get; } = [];

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

    public bool HasCatalogNotice => !string.IsNullOrWhiteSpace(CatalogNotice);

    public bool HasGenerationResult => GenerationResult is not null;

    public bool IsConfigurationView => !IsCatalogView;

    public bool IsVariantSelection => !IsPackageSelection;

    public string CatalogStageTitle =>
        IsPackageSelection ? "PACKAGES" : "VARIANTS";

    public string CatalogStageDescription =>
        IsPackageSelection
            ? "Choose a project family, then confirm to inspect its variants."
            : $"Choose the platform and build system for {SelectedPackage?.Name ?? "this package"}.";

    public string CatalogItemCountText =>
        IsPackageSelection
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
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial GenerationPreviewViewModel? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationView))]
    public partial bool IsCatalogView { get; set; } = true;

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
    public partial string DestinationPath { get; set; } = string.Empty;

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
        var validation = GenerationDestinationValidator.Validate(DestinationPath);
        if (!validation.IsSuccess)
        {
            SetIssues(validation.Issues, "The destination is not ready.");
            return;
        }

        DestinationPath = validation.Value!;
        GenerationConfirmationText =
            $"Generate {Preview.Plan.Files.Length} files into '{DestinationPath}'? " +
            "Klonker will only use a new or empty directory and will not manage the project afterward.";
        IsGenerationConfirmationVisible = true;
        GenerationResult = null;
        StatusMessage = "Confirm project generation";
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
            var result = await generationService.GenerateAsync(
                Preview.Plan,
                DestinationPath,
                generationCancellation.Token);
            DiagnosticException = result.DiagnosticException;
            GenerationResult = new GenerationResultViewModel(
                result,
                DestinationPath);
            StatusMessage = result.Message;
            if (!result.Succeeded)
            {
                ErrorMessage = result.Message;
            }
        }
        catch (OperationCanceledException)
        {
            GenerationResult = new GenerationResultViewModel(
                new Klonker.Core.Generation.GenerationResult(
                    GenerationStatus.Cancelled,
                    "Generation was cancelled before the project was installed.",
                    []),
                DestinationPath);
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

    private bool CanPreview() => SelectedTemplate is not null && !IsBusy;

    private bool CanBrowseDestination() => !IsBusy;

    private bool CanRequestGeneration() =>
        Preview is not null &&
        generationService is not null &&
        !IsBusy &&
        GenerationResult?.Succeeded != true &&
        !string.IsNullOrWhiteSpace(DestinationPath);

    private bool CanConfirmGeneration() =>
        IsGenerationConfirmationVisible &&
        Preview is not null &&
        generationService is not null &&
        !IsBusy;

    private bool CanCancelGeneration() => IsGenerating;

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
            foreach (var template in result.Value.Templates)
            {
                Templates.Add(new TemplateListItemViewModel(template));
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
                Packages.Add(new PackageListItemViewModel(packageGroup));
            }

            var notices = result.Issues
                .Where(issue => issue.Severity != ValidationSeverity.Error)
                .Select(issue => issue.Message)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            CatalogNotice = notices.Length == 0
                ? null
                : string.Join(Environment.NewLine, notices);

            if (Packages.Count == 0)
            {
                ErrorMessage = "The configured registries do not contain any usable packages.";
                StatusMessage = "No packages available";
                return;
            }

            PopulateFilters();
            ApplyFilters();
            var registryCount = Packages
                .Select(package => package.RegistryId)
                .Distinct(StringComparer.Ordinal)
                .Count();
            StatusMessage =
                $"{Packages.Count} package{(Packages.Count == 1 ? string.Empty : "s")} · " +
                $"{Templates.Count} variant{(Templates.Count == 1 ? string.Empty : "s")} " +
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
        OpenConfigurationCommand.NotifyCanExecuteChanged();
        NotifyCommandStates();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnSelectedLanguageChanged(string value) => ApplyFilters();

    partial void OnSelectedPlatformChanged(string value) => ApplyFilters();

    partial void OnSelectedBuildSystemChanged(string value) => ApplyFilters();

    partial void OnSelectedTagChanged(string value) => ApplyFilters();

    partial void OnIsPackageSelectionChanged(bool value)
    {
        ApplyFilters();
        OnPropertyChanged(nameof(CatalogStageTitle));
        OnPropertyChanged(nameof(CatalogStageDescription));
        OnPropertyChanged(nameof(CatalogItemCountText));
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

    partial void OnDestinationPathChanged(string value)
    {
        IsGenerationConfirmationVisible = false;
        GenerationResult = null;
        RequestGenerationCommand.NotifyCanExecuteChanged();
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsGenerationConfirmationVisibleChanged(bool value)
    {
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreviewChanged(GenerationPreviewViewModel? value)
    {
        RequestGenerationCommand.NotifyCanExecuteChanged();
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnGenerationResultChanged(GenerationResultViewModel? value)
    {
        RequestGenerationCommand.NotifyCanExecuteChanged();
    }

    private void PopulateFilters()
    {
        Languages.Clear();
        Languages.Add(AllLanguages);
        foreach (var language in Templates
                     .Select(template => template.Language)
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

    private void ApplyPackageFilters()
    {
        var search = SearchText.Trim();
        var matches = Packages.Where(package =>
            package.MatchesVariantFilters(
                SelectedLanguage,
                SelectedPlatform,
                SelectedBuildSystem,
                SelectedTag) &&
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

    private Dictionary<string, object?> GetParameterValues() =>
        Parameters.ToDictionary(
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

    private void NotifyCommandStates()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        BrowseDestinationCommand.NotifyCanExecuteChanged();
        RequestGenerationCommand.NotifyCanExecuteChanged();
        ConfirmGenerationCommand.NotifyCanExecuteChanged();
        CancelGenerationCommand.NotifyCanExecuteChanged();
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
        GC.SuppressFinalize(this);
    }
}
