using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record ResolvedRegistryCatalog(
    ImmutableArray<RegistryTemplatePackage> Templates,
    ImmutableArray<RegistryTemplateVersionSelection> TemplateVersions = default,
    ImmutableArray<RegistryModulePackage> Modules = default,
    ImmutableArray<RegistryModuleVersionSelection> ModuleVersions = default);
