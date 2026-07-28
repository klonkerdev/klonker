using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed class CatalogTabViewModel : ViewModelBase
{
    public CatalogTabViewModel(CatalogTabDefinition definition)
    {
        Definition = definition;
    }

    public CatalogTabDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Name => Definition.Name;

    public CatalogTabKind Kind => Definition.Kind;

    public bool IsModuleTab =>
        Kind is CatalogTabKind.FavoriteModules or
            CatalogTabKind.SelectedModules;

    public bool IsFavoriteTab =>
        Kind is CatalogTabKind.FavoriteTemplates or
            CatalogTabKind.FavoriteModules;

    public IReadOnlyList<string> ItemIdentities =>
        Definition.ItemIdentities;
}
