namespace Klonker.Core.Generation;

public sealed record TemplateIdentity(
    string RegistryId,
    string Id,
    string FamilyId,
    string VariantId,
    string Version)
{
    public string QualifiedId => $"{RegistryId}:{Id}@{Version}";
}
