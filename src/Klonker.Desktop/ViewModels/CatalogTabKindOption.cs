using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed record CatalogTabKindOption(
    CatalogTabKind Kind,
    string Label,
    string Description)
{
    public override string ToString() => Label;
}
