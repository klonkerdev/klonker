using Klonker.Core.Diagnostics;

namespace Klonker.Desktop.Services;

public interface IFavoriteStore
{
    OperationResult<FavoriteSnapshot> Load();

    OperationResult<FavoriteSnapshot> SetFavorite(
        string templateIdentity,
        bool isFavorite);

    OperationResult<FavoriteSnapshot> Reset();
}
