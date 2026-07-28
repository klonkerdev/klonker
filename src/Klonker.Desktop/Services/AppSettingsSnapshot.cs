namespace Klonker.Desktop.Services;

public sealed record AppSettingsSnapshot(
    string StoragePath,
    AppAppearance Appearance,
    bool DiagnosticLoggingEnabled,
    DiagnosticLogLevel DiagnosticLogLevel,
    bool PrerequisiteProbesEnabled,
    int RegistryDownloadTimeoutSeconds);
