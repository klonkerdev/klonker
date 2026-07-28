using System.Collections.Immutable;
using Klonker.Core.Templates;

namespace Klonker.Core.Modules;

public sealed record ModulePackage(
    string RootPath,
    string ContentPath,
    ModuleManifest Manifest,
    ImmutableArray<TemplateSourceFile> SourceFiles,
    string RegistryId = "local.unregistered");
