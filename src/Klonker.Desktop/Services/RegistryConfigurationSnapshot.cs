using System.Collections.Immutable;
using Klonker.Core.Registry;

namespace Klonker.Desktop.Services;

public sealed record RegistryConfigurationSnapshot(
    string ConfigurationPath,
    string CacheRoot,
    bool Offline,
    ImmutableArray<RegistrySource> Sources);
