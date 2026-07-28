namespace Klonker.Core.Registry;

public sealed record RegistryBuildRequest(
    string SourceRoot,
    string OutputRoot,
    string? SigningKeyPath = null);
