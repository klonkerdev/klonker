using System.Collections.Immutable;

namespace Klonker.Desktop.Services;

public sealed record CatalogTabDefinition(
    string Id,
    string Name,
    CatalogTabKind Kind,
    ImmutableArray<string> ItemIdentities);
