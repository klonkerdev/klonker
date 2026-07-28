using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Klonker.Core.Diagnostics;
using Klonker.Core.Registry;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettingsStore settingsStore;
    private readonly RegistryConfigurationStore registryStore;
    private readonly LocalDataMaintenanceService maintenanceService;
    private readonly AppearanceService appearanceService;
    private readonly Action? catalogChanged;

    public SettingsViewModel(
        AppSettingsStore settingsStore,
        RegistryConfigurationStore registryStore,
        LocalDataMaintenanceService maintenanceService,
        AppearanceService appearanceService,
        AppDiagnosticLog diagnosticLog,
        Action? catalogChanged = null)
    {
        this.settingsStore = settingsStore;
        this.registryStore = registryStore;
        this.maintenanceService = maintenanceService;
        this.appearanceService = appearanceService;
        this.catalogChanged = catalogChanged;
        DiagnosticLogPath = diagnosticLog.LogPath;
        ApplicationDataPath = settingsStore.ApplicationDataRoot;
        CachePath = registryStore.CacheRoot;
        Load();
    }

    public ObservableCollection<RegistrySourceEditorViewModel> RegistrySources { get; } = [];

    public IReadOnlyList<AppAppearance> AppearanceOptions { get; } =
        Enum.GetValues<AppAppearance>();

    public IReadOnlyList<DiagnosticLogLevel> DiagnosticLogLevelOptions { get; } =
        Enum.GetValues<DiagnosticLogLevel>();

    public IReadOnlyList<RegistryVersionPreference>
        RegistryVersionPreferenceOptions
    { get; } =
        Enum.GetValues<RegistryVersionPreference>();

    public IReadOnlyList<RegistryDuplicateSourcePolicy>
        RegistryDuplicateSourcePolicyOptions
    { get; } =
        Enum.GetValues<RegistryDuplicateSourcePolicy>();

    public string ApplicationDataPath { get; }

    public string CachePath { get; }

    public string DiagnosticLogPath { get; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    public partial AppAppearance Appearance { get; set; } = AppAppearance.System;

    [ObservableProperty]
    public partial bool DiagnosticLoggingEnabled { get; set; }

    [ObservableProperty]
    public partial DiagnosticLogLevel DiagnosticLogLevel { get; set; } =
        DiagnosticLogLevel.Information;

    [ObservableProperty]
    public partial bool PrerequisiteProbesEnabled { get; set; }

    [ObservableProperty]
    public partial int RegistryDownloadTimeoutSeconds { get; set; } =
        AppSettingsStore.DefaultRegistryDownloadTimeoutSeconds;

    [ObservableProperty]
    public partial RegistryVersionPreference RegistryVersionPreference { get; set; } =
        RegistryVersionPreference.LatestStable;

    [ObservableProperty]
    public partial RegistryDuplicateSourcePolicy RegistryDuplicateSourcePolicy { get; set; } =
        RegistryDuplicateSourcePolicy.PreferFirstConfiguredSource;

    [ObservableProperty]
    public partial string RegistryVersionPinsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Offline { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Settings loaded";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [RelayCommand]
    private void AddRegistry()
    {
        RegistrySources.Add(new RegistrySourceEditorViewModel());
        StatusMessage = "New registry source added; save to apply it";
    }

    public void RemoveRegistry(RegistrySourceEditorViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RegistrySources.Remove(source);
        StatusMessage = "Registry source removed; save to apply it";
    }

    public static void AddTrustedKey(RegistrySourceEditorViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.TrustedKeys.Add(new TrustedPublisherKeyViewModel());
    }

    public static void RemoveTrustedKey(
        RegistrySourceEditorViewModel source,
        TrustedPublisherKeyViewModel key)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        source.TrustedKeys.Remove(key);
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        var pins = ParseVersionPins(RegistryVersionPinsText);
        if (!pins.IsSuccess)
        {
            SetFailure(pins.Issues, "Registry version pins are invalid.");
            return;
        }

        var settings = settingsStore.Save(new AppSettingsSnapshot(
            settingsStore.StoragePath,
            Appearance,
            DiagnosticLoggingEnabled,
            DiagnosticLogLevel,
            PrerequisiteProbesEnabled,
            RegistryDownloadTimeoutSeconds,
            RegistryVersionPreference,
            pins.Value!.ToImmutableDictionary(StringComparer.Ordinal),
            RegistryDuplicateSourcePolicy));
        if (!settings.IsSuccess)
        {
            SetFailure(settings.Issues, "Application settings could not be saved.");
            return;
        }

        var registries = registryStore.Save(
            Offline,
            RegistrySources.Select(source => source.ToModel()));
        if (!registries.IsSuccess)
        {
            SetFailure(registries.Issues, "Registry sources could not be saved.");
            return;
        }

        appearanceService.Apply(settings.Value!.Appearance);
        StatusMessage = "Settings saved";
        catalogChanged?.Invoke();
    }

    [RelayCommand]
    private void ClearCache() =>
        ApplyMaintenance(
            maintenanceService.ClearCache(),
            reload: false,
            refreshCatalog: true);

    [RelayCommand]
    private void ClearFavorites() =>
        ApplyMaintenance(
            maintenanceService.ClearFavorites(),
            reload: false,
            refreshCatalog: true);

    [RelayCommand]
    private void ClearDiagnosticLog() =>
        ApplyMaintenance(
            maintenanceService.ClearDiagnosticLog(),
            reload: false,
            refreshCatalog: false);

    [RelayCommand]
    private void ResetApplicationSettings() =>
        ApplyMaintenance(
            maintenanceService.ResetApplicationSettings(),
            reload: true,
            refreshCatalog: true);

    [RelayCommand]
    private void ResetRegistryConfiguration() =>
        ApplyMaintenance(
            maintenanceService.ResetRegistryConfiguration(),
            reload: true,
            refreshCatalog: true);

    private void Load()
    {
        ErrorMessage = null;
        var settings = settingsStore.Load();
        var registries = registryStore.Load();
        if (!settings.IsSuccess || !registries.IsSuccess)
        {
            SetFailure(
                settings.Issues.Concat(registries.Issues),
                "Settings could not be loaded.");
            return;
        }

        Appearance = settings.Value!.Appearance;
        DiagnosticLoggingEnabled = settings.Value.DiagnosticLoggingEnabled;
        DiagnosticLogLevel = settings.Value.DiagnosticLogLevel;
        PrerequisiteProbesEnabled = settings.Value.PrerequisiteProbesEnabled;
        RegistryDownloadTimeoutSeconds =
            settings.Value.RegistryDownloadTimeoutSeconds;
        RegistryVersionPreference =
            settings.Value.RegistryVersionPreference;
        RegistryDuplicateSourcePolicy =
            settings.Value.RegistryDuplicateSourcePolicy;
        RegistryVersionPinsText = string.Join(
            Environment.NewLine,
            (settings.Value.RegistryVersionPins ??
             ImmutableDictionary<string, string>.Empty)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        Offline = registries.Value!.Offline;
        RegistrySources.Clear();
        foreach (var source in registries.Value.Sources)
        {
            RegistrySources.Add(new RegistrySourceEditorViewModel(source));
        }

        StatusMessage = "Settings loaded";
    }

    private static OperationResult<Dictionary<string, string>>
        ParseVersionPins(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var issues = new List<ValidationIssue>();
        foreach (var rawLine in text.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 ||
                separator == line.Length - 1)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "settings.version_pin_invalid",
                    $"Version pin '{line}' must use registry:item=version."));
                continue;
            }

            var identity = line[..separator].Trim();
            var version = line[(separator + 1)..].Trim();
            if (!identity.Contains(':', StringComparison.Ordinal) ||
                !result.TryAdd(identity, version))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "settings.version_pin_invalid",
                    $"Version pin '{line}' has an invalid or duplicate qualified ID."));
            }
        }

        return issues.Count == 0
            ? new OperationResult<Dictionary<string, string>>(result, [])
            : new OperationResult<Dictionary<string, string>>(null, issues);
    }

    private void ApplyMaintenance(
        OperationResult<MaintenanceResult> result,
        bool reload,
        bool refreshCatalog)
    {
        ErrorMessage = null;
        if (!result.IsSuccess)
        {
            SetFailure(result.Issues, "The local data operation failed.");
            return;
        }

        if (reload)
        {
            Load();
            var settings = settingsStore.Load();
            if (settings.IsSuccess)
            {
                appearanceService.Apply(settings.Value!.Appearance);
            }
        }

        StatusMessage = result.Value!.Message;
        if (refreshCatalog)
        {
            catalogChanged?.Invoke();
        }
    }

    private void SetFailure(
        IEnumerable<ValidationIssue> issues,
        string fallbackMessage)
    {
        var messages = issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        ErrorMessage = messages.Length == 0
            ? fallbackMessage
            : string.Join(Environment.NewLine, messages);
        StatusMessage = fallbackMessage;
    }
}
