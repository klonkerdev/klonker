namespace Klonker.Core.Registry;

public interface IRegistryPackageEntry
{
    string ArtifactId { get; }

    string Version { get; }

    string PackagePath { get; }

    string PackageSha256 { get; }

    long PackageSizeBytes { get; }
}
