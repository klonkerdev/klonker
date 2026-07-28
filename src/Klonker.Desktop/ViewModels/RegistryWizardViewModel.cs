using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Registry;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class RegistryWizardViewModel : ViewModelBase
{
    private readonly RegistryConfigurationStore configurationStore;
    private readonly IRegistryWorkspacePicker picker;
    private RegistrySigningKeyMaterial? keyMaterial;
    private GenerationPlan? workspacePlan;

    public RegistryWizardViewModel(
        RegistryConfigurationStore configurationStore,
        IRegistryWorkspacePicker picker)
    {
        this.configurationStore = configurationStore;
        this.picker = picker;
    }

    public bool IsWelcome => Step == RegistryWizardStep.Welcome;

    public bool IsConfigure => Step == RegistryWizardStep.Configure;

    public bool IsReview => Step == RegistryWizardStep.Review;

    public bool IsComplete => Step == RegistryWizardStep.Complete;

    public bool IsDevelopment => Mode == RegistryWizardMode.Development;

    public bool IsProduction => Mode == RegistryWizardMode.Production;

    public bool IsExisting => Mode == RegistryWizardMode.Existing;

    public bool IsNewWorkspace => IsDevelopment || IsProduction;

    public bool ShowsSigning => IsProduction || IsExisting;

    public bool HasIssues => Issues.Count > 0;

    public bool HasPreview => Preview is not null;

    public bool HasBuildResult => BuildResult is not null;

    public string PrimaryActionText => IsExisting
        ? "Build registry"
        : InitialTemplatePath.Length > 0
            ? "Create, build & test"
            : "Create workspace";

    public string ReviewSummary => IsExisting
        ? $"Klonker will validate source packages, rebuild {OutputPath} transactionally, and " +
          (RegisterLocally
              ? "register the local index for catalog testing."
              : "leave app registry settings unchanged.")
        : $"Klonker will create {(IsProduction ? "a production" : "a development")} " +
          $"registry workspace at {WorkspacePath}. " +
          (string.IsNullOrWhiteSpace(InitialTemplatePath)
              ? "Add source packages with the Template wizard, then reopen this registry to build it."
              : "The selected source package will be imported, built, validated, and made ready for testing.");

    public string BuildSummary => BuildResult is null
        ? string.Empty
        : $"Built {BuildResult.PackageCount} package variant(s) and " +
          $"{BuildResult.ModuleCount} module(s) at {BuildResult.IndexPath}" +
          (BuildResult.IsSigned ? " with a detached publisher signature." : ".");

    public bool CanGoBack =>
        !IsBusy && Step is RegistryWizardStep.Configure or RegistryWizardStep.Review;

    public bool CanGoNext =>
        !IsBusy &&
        Step == RegistryWizardStep.Configure &&
        !string.IsNullOrWhiteSpace(WorkspacePath) &&
        !string.IsNullOrWhiteSpace(OutputPath) &&
        (IsExisting ||
         (!string.IsNullOrWhiteSpace(RegistryId) &&
          !string.IsNullOrWhiteSpace(DisplayName))) &&
        (!IsProduction ||
         (!string.IsNullOrWhiteSpace(PublisherId) &&
          !string.IsNullOrWhiteSpace(SigningKeyId) &&
          !string.IsNullOrWhiteSpace(PrivateKeyPath)));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome))]
    [NotifyPropertyChangedFor(nameof(IsConfigure))]
    [NotifyPropertyChangedFor(nameof(IsReview))]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial RegistryWizardStep Step { get; set; } =
        RegistryWizardStep.Welcome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDevelopment))]
    [NotifyPropertyChangedFor(nameof(IsProduction))]
    [NotifyPropertyChangedFor(nameof(IsExisting))]
    [NotifyPropertyChangedFor(nameof(IsNewWorkspace))]
    [NotifyPropertyChangedFor(nameof(ShowsSigning))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(ReviewSummary))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial RegistryWizardMode? Mode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(ReviewSummary))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string WorkspacePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(ReviewSummary))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string OutputPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string RegistryId { get; set; } = "local-development";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string DisplayName { get; set; } =
        "Local development templates";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string PublisherId { get; set; } = "my-publisher";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string SigningKeyId { get; set; } = "primary-2026";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string PrivateKeyPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(ReviewSummary))]
    public partial string InitialTemplatePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewSummary))]
    public partial bool RegisterLocally { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial GenerationPreviewViewModel? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBuildResult))]
    [NotifyPropertyChangedFor(nameof(BuildSummary))]
    public partial RegistryBuildOutput? BuildResult { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Choose a development, production, or existing registry workflow.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial bool IsBusy { get; set; }

    public System.Collections.ObjectModel.ObservableCollection<
        TemplateInspectionIssueViewModel> Issues
    { get; } = [];

    [RelayCommand]
    private void ChooseDevelopment()
    {
        Mode = RegistryWizardMode.Development;
        RegistryId = "local-development";
        DisplayName = "Local development templates";
        RegisterLocally = true;
        Step = RegistryWizardStep.Configure;
        StatusMessage =
            "Choose a workspace. You can optionally import a source package and test it immediately.";
        NotifyCommands();
    }

    [RelayCommand]
    private void ChooseProduction()
    {
        Mode = RegistryWizardMode.Production;
        RegistryId = "my-registry";
        DisplayName = "My template registry";
        RegisterLocally = false;
        Step = RegistryWizardStep.Configure;
        StatusMessage =
            "Choose an empty workspace and an external private-key destination.";
        NotifyCommands();
    }

    [RelayCommand]
    private void ChooseExisting()
    {
        Mode = RegistryWizardMode.Existing;
        RegisterLocally = true;
        Step = RegistryWizardStep.Configure;
        StatusMessage =
            "Choose a registry source folder containing registry.toml and templates.";
        NotifyCommands();
    }

    [RelayCommand]
    private async Task BrowseWorkspaceAsync()
    {
        var selected = await picker.PickFolderAsync(
            IsExisting
                ? "Choose existing registry source"
                : "Choose new or empty registry workspace");
        if (selected is null)
        {
            return;
        }

        WorkspacePath = selected;
        OutputPath = Path.Combine(selected, "dist");
    }

    [RelayCommand]
    private async Task BrowseInitialTemplateAsync()
    {
        var selected = await picker.PickFolderAsync(
            "Choose registry source package");
        if (selected is not null)
        {
            InitialTemplatePath = selected;
        }
    }

    [RelayCommand]
    private async Task BrowsePrivateKeyAsync()
    {
        var selected = IsProduction
            ? await picker.PickPrivateKeyDestinationAsync()
            : await picker.PickExistingPrivateKeyAsync();
        if (selected is not null)
        {
            PrivateKeyPath = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        Step = Step == RegistryWizardStep.Review
            ? RegistryWizardStep.Configure
            : RegistryWizardStep.Welcome;
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        ClearIssues();
        if (!PrepareReview())
        {
            return;
        }

        Step = RegistryWizardStep.Review;
        StatusMessage = "Review the operation, then confirm it explicitly.";
        NotifyCommands();
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (Step != RegistryWizardStep.Review || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearIssues();
        try
        {
            if (IsNewWorkspace)
            {
                if (IsProduction)
                {
                    var keyWrite =
                        await RegistrySigningKeyService.WritePrivateKeyAsync(
                            PrivateKeyPath,
                            keyMaterial!.PrivateKeyPem);
                    AddIssues(keyWrite.Issues);
                    if (!keyWrite.IsSuccess)
                    {
                        StatusMessage =
                            "The private key was not written. Nothing in the registry workspace changed.";
                        return;
                    }
                }

                var generated = await GenerationExecutor.ExecuteAsync(
                    workspacePlan!,
                    WorkspacePath);
                AddIssues(generated.Issues);
                if (!generated.Succeeded)
                {
                    StatusMessage = generated.Message;
                    return;
                }
            }

            var shouldBuild =
                IsExisting || !string.IsNullOrWhiteSpace(InitialTemplatePath);
            if (shouldBuild)
            {
                var signingKeyPath =
                    IsProduction ||
                    (IsExisting &&
                     !string.IsNullOrWhiteSpace(PrivateKeyPath))
                        ? PrivateKeyPath
                        : null;
                var built = await RegistryDevelopmentBuilder.BuildAsync(
                    new RegistryBuildRequest(
                        WorkspacePath,
                        OutputPath,
                        signingKeyPath));
                AddIssues(built.Issues);
                if (!built.IsSuccess)
                {
                    StatusMessage =
                        "The registry source was not installed to the output folder. Fix the reported findings and retry.";
                    return;
                }

                BuildResult = built.Value;
                if (RegisterLocally)
                {
                    if (!RegisterLocalIndex(built.Value!.IndexPath))
                    {
                        return;
                    }
                }
            }

            Step = RegistryWizardStep.Complete;
            StatusMessage = BuildResult is null
                ? "Registry workspace created. Add authoring packages, then use the existing-registry workflow to validate and build it."
                : RegisterLocally
                    ? "Registry built and registered. Refresh the catalog to load the new variants."
                    : "Registry built successfully.";
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    partial void OnWorkspacePathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            (string.IsNullOrWhiteSpace(OutputPath) ||
             OutputPath.EndsWith(
                 $"{Path.DirectorySeparatorChar}dist",
                 StringComparison.OrdinalIgnoreCase)))
        {
            OutputPath = Path.Combine(value, "dist");
        }
    }

    private bool PrepareReview()
    {
        Preview = null;
        BuildResult = null;
        workspacePlan = null;
        keyMaterial = null;

        if (IsExisting)
        {
            if (!Directory.Exists(WorkspacePath) ||
                !File.Exists(Path.Combine(WorkspacePath, "registry.toml")))
            {
                AddIssue(new ValidationIssue(
                    ValidationSeverity.Error,
                    "registry.wizard_source_invalid",
                    "Choose a registry source folder containing registry.toml.",
                    Path: WorkspacePath));
                return false;
            }

            return ValidatePrivateKeyLocation();
        }

        if (IsProduction)
        {
            if (!ValidatePrivateKeyLocation())
            {
                return false;
            }

            keyMaterial = RegistrySigningKeyService.Create();
        }

        var planned = RegistryWorkspacePlanner.CreatePlan(
            new RegistryWorkspaceRequest(
                WorkspacePath,
                RegistryId.Trim(),
                DisplayName.Trim(),
                IsProduction,
                IsProduction ? PublisherId.Trim() : null,
                IsProduction ? SigningKeyId.Trim() : null,
                keyMaterial?.PublicKeySpki,
                string.IsNullOrWhiteSpace(InitialTemplatePath)
                    ? null
                    : InitialTemplatePath));
        AddIssues(planned.Issues);
        if (!planned.IsSuccess)
        {
            return false;
        }

        workspacePlan = planned.Value;
        Preview = new GenerationPreviewViewModel(planned.Value!);
        return true;
    }

    private bool ValidatePrivateKeyLocation()
    {
        if (string.IsNullOrWhiteSpace(PrivateKeyPath))
        {
            return !IsProduction;
        }

        try
        {
            var workspace = Path.GetFullPath(WorkspacePath);
            var privateKey = Path.GetFullPath(PrivateKeyPath);
            var relative = Path.GetRelativePath(workspace, privateKey);
            if (relative != ".." &&
                !relative.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                AddIssue(new ValidationIssue(
                    ValidationSeverity.Error,
                    "registry.wizard_private_key_inside_workspace",
                    "Keep the private signing key outside the registry workspace so it cannot be committed or published.",
                    Path: privateKey));
                return false;
            }

            if (IsProduction &&
                (File.Exists(privateKey) || Directory.Exists(privateKey)))
            {
                AddIssue(new ValidationIssue(
                    ValidationSeverity.Error,
                    "registry.wizard_private_key_exists",
                    "Choose a new private-key path. Klonker never overwrites key material.",
                    Path: privateKey));
                return false;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            AddIssue(new ValidationIssue(
                ValidationSeverity.Error,
                "registry.wizard_private_key_path_invalid",
                $"The private-key path is invalid: {exception.Message}",
                Path: PrivateKeyPath));
            return false;
        }

        return true;
    }

    private bool RegisterLocalIndex(string indexPath)
    {
        var loaded = configurationStore.Load();
        AddIssues(loaded.Issues);
        if (!loaded.IsSuccess)
        {
            StatusMessage =
                "The registry was built, but app registry settings could not be loaded.";
            return false;
        }

        var sources = loaded.Value!.Sources
            .Where(source => !(
                source.Kind == RegistrySourceKind.Local &&
                string.Equals(
                    Path.GetFullPath(source.Location),
                    Path.GetFullPath(indexPath),
                    StringComparison.OrdinalIgnoreCase)))
            .Append(new RegistrySource(
                IsExisting || string.IsNullOrWhiteSpace(DisplayName)
                    ? Path.GetFileName(WorkspacePath)
                    : DisplayName,
                RegistrySourceKind.Local,
                indexPath,
                Enabled: true))
            .ToArray();
        var saved = configurationStore.Save(loaded.Value.Offline, sources);
        AddIssues(saved.Issues);
        if (!saved.IsSuccess)
        {
            StatusMessage =
                "The registry was built, but its local source could not be registered.";
            return false;
        }

        return true;
    }

    private void ClearIssues()
    {
        Issues.Clear();
        OnPropertyChanged(nameof(HasIssues));
    }

    private void AddIssues(IEnumerable<ValidationIssue> issues)
    {
        foreach (var issue in issues)
        {
            AddIssue(issue);
        }
    }

    private void AddIssue(ValidationIssue issue)
    {
        Issues.Add(new TemplateInspectionIssueViewModel(issue));
        OnPropertyChanged(nameof(HasIssues));
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
    }
}
