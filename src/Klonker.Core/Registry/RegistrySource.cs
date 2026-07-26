namespace Klonker.Core.Registry;

public sealed record RegistrySource(
    string Name,
    RegistrySourceKind Kind,
    string Location,
    bool Enabled = true);
