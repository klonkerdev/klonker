using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record RegistryCatalogOptions(
    string CacheRoot,
    bool Offline = false,
    RegistryVersionPreference VersionPreference =
        RegistryVersionPreference.LatestStable,
    ImmutableDictionary<string, string>? VersionPins = null,
    RegistryDuplicateSourcePolicy DuplicateSourcePolicy =
        RegistryDuplicateSourcePolicy.PreferFirstConfiguredSource);
