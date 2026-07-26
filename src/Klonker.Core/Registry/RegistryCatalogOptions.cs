namespace Klonker.Core.Registry;

public sealed record RegistryCatalogOptions(
    string CacheRoot,
    bool Offline = false);
