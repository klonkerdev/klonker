using Klonker.Core.Diagnostics;

namespace Klonker.Desktop.Services;

public interface ITemplateCatalog
{
    Task<OperationResult<TemplateCatalogSnapshot>> LoadAsync(
        CancellationToken cancellationToken = default);
}
