using Klonker.Core.Templates;

namespace Klonker.Core.Registry;

public sealed record RegistryTemplatePackage(
    string RegistryId,
    string RegistryDisplayName,
    RegistryTemplateEntry Entry,
    TemplatePackage Package)
{
    public string QualifiedId => $"{RegistryId}:{Entry.TemplateId}@{Entry.Version}";
}
