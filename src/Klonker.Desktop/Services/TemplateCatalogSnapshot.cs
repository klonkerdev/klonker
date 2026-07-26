using System.Collections.Immutable;
using Klonker.Core.Templates;

namespace Klonker.Desktop.Services;

public sealed record TemplateCatalogSnapshot(ImmutableArray<TemplatePackage> Packages);
