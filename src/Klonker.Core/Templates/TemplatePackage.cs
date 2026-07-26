using System.Collections.Immutable;

namespace Klonker.Core.Templates;

public sealed record TemplatePackage(
    string RootPath,
    string ContentPath,
    TemplateManifest Manifest,
    ImmutableArray<TemplateSourceFile> SourceFiles);
