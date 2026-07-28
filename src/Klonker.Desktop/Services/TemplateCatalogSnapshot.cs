using System.Collections.Immutable;
using Klonker.Core.Registry;

namespace Klonker.Desktop.Services;

public sealed record TemplateCatalogSnapshot(
    ImmutableArray<RegistryTemplatePackage> Templates,
    string ConfigurationPath,
    string CacheRoot,
    bool Offline,
    ImmutableArray<RegistryModulePackage> Modules = default,
    ImmutableArray<RegistryTemplateVersionSelection> TemplateVersions = default,
    ImmutableArray<RegistryModuleVersionSelection> ModuleVersions = default);
