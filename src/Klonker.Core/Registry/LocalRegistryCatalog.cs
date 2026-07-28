using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record LocalRegistryCatalog(
    int SchemaVersion,
    string RegistryId,
    string DisplayName,
    string RootPath,
    ImmutableArray<RegistryTemplateEntry> Templates,
    ImmutableArray<RegistryModuleEntry> Modules = default);
