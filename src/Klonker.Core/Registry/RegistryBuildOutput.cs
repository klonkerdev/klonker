namespace Klonker.Core.Registry;

public sealed record RegistryBuildOutput(
    string IndexPath,
    int PackageCount,
    bool IsSigned,
    int ModuleCount = 0);
