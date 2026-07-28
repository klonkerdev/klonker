namespace Klonker.Core.Registry;

public sealed record RegistryTemplateEntry(
    string FamilyId,
    string VariantId,
    string TemplateId,
    string Name,
    string Description,
    string Version,
    string TargetOs,
    string BuildSystem,
    string PackagePath,
    string LicenseSummary,
    string PackageSha256,
    long PackageSizeBytes,
    string Language = "unknown") : IRegistryPackageEntry
{
    public string ArtifactId => TemplateId;
}
