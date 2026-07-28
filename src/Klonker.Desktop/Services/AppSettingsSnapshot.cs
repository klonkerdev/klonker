using System.Collections.Immutable;
using Klonker.Core.Registry;

namespace Klonker.Desktop.Services;

public sealed record AppSettingsSnapshot(
    string StoragePath,
    AppAppearance Appearance,
    bool DiagnosticLoggingEnabled,
    DiagnosticLogLevel DiagnosticLogLevel,
    bool PrerequisiteProbesEnabled,
    int RegistryDownloadTimeoutSeconds,
    RegistryVersionPreference RegistryVersionPreference =
        RegistryVersionPreference.LatestStable,
    ImmutableDictionary<string, string>? RegistryVersionPins = null,
    RegistryDuplicateSourcePolicy RegistryDuplicateSourcePolicy =
        RegistryDuplicateSourcePolicy.PreferFirstConfiguredSource);
