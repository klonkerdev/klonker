using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record RegistryModuleEntry(
    string ModuleId,
    string Name,
    string Description,
    string Version,
    string Language,
    string PackagePath,
    string LicenseSummary,
    string PackageSha256,
    long PackageSizeBytes,
    ImmutableArray<string> Tags) : IRegistryPackageEntry
{
    public string ArtifactId => ModuleId;
}
