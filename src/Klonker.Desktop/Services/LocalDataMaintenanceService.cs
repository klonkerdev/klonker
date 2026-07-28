using Klonker.Core.Diagnostics;

namespace Klonker.Desktop.Services;

public sealed class LocalDataMaintenanceService
{
    private readonly RegistryConfigurationStore registryStore;
    private readonly AppSettingsStore settingsStore;
    private readonly IFavoriteStore favoriteStore;
    private readonly AppDiagnosticLog diagnosticLog;

    public LocalDataMaintenanceService(
        RegistryConfigurationStore registryStore,
        AppSettingsStore settingsStore,
        IFavoriteStore favoriteStore,
        AppDiagnosticLog diagnosticLog)
    {
        this.registryStore = registryStore;
        this.settingsStore = settingsStore;
        this.favoriteStore = favoriteStore;
        this.diagnosticLog = diagnosticLog;
    }

    public OperationResult<MaintenanceResult> ClearCache() =>
        ClearDirectory(
            registryStore.CacheRoot,
            "cache",
            "registry cache");

    public OperationResult<MaintenanceResult> ClearFavorites()
    {
        var reset = favoriteStore.Reset();
        return reset.IsSuccess
            ? Success("Local favorite preferences were cleared.")
            : new OperationResult<MaintenanceResult>(null, reset.Issues);
    }

    public OperationResult<MaintenanceResult> ResetApplicationSettings()
    {
        var reset = settingsStore.Reset();
        return reset.IsSuccess
            ? Success("Application settings were reset to defaults.")
            : new OperationResult<MaintenanceResult>(null, reset.Issues);
    }

    public OperationResult<MaintenanceResult> ResetRegistryConfiguration()
    {
        var reset = registryStore.Reset();
        return reset.IsSuccess
            ? Success("Registry sources were reset to first-run defaults.")
            : new OperationResult<MaintenanceResult>(null, reset.Issues);
    }

    public OperationResult<MaintenanceResult> ClearDiagnosticLog() =>
        diagnosticLog.Clear()
            ? Success("The diagnostic log was cleared.")
            : Failure(
                "maintenance.log_clear_failed",
                "The diagnostic log could not be cleared.");

    private OperationResult<MaintenanceResult> ClearDirectory(
        string path,
        string expectedName,
        string description)
    {
        var fullRoot = Path.GetFullPath(registryStore.ApplicationDataRoot);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (!string.Equals(relative, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                "maintenance.path_invalid",
                $"Refusing to clear unexpected {description} path '{fullPath}'.");
        }

        try
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            Directory.CreateDirectory(fullPath);
            return Success($"The {description} was cleared.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "maintenance.clear_failed",
                $"The {description} could not be cleared: {exception.Message}");
        }
    }

    private static OperationResult<MaintenanceResult> Success(string message) =>
        new(new MaintenanceResult(message), []);

    private static OperationResult<MaintenanceResult> Failure(
        string code,
        string message) =>
        new(
            null,
            [new ValidationIssue(ValidationSeverity.Error, code, message)]);
}
