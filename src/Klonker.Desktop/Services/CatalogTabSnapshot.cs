using System.Collections.Immutable;

namespace Klonker.Desktop.Services;

public sealed record CatalogTabSnapshot(
    ImmutableArray<CatalogTabDefinition> Tabs,
    string Path);
