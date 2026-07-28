using Klonker.Core.Modules;

namespace Klonker.Core.Registry;

public sealed record RegistryModulePackage(
    string RegistryId,
    string RegistryDisplayName,
    RegistryModuleEntry Entry,
    ModulePackage Package)
{
    public string QualifiedId => $"{RegistryId}:{Entry.ModuleId}@{Entry.Version}";
}
