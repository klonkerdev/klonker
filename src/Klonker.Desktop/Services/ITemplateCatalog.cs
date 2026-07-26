using Klonker.Core.Diagnostics;

namespace Klonker.Desktop.Services;

public interface ITemplateCatalog
{
    OperationResult<TemplateCatalogSnapshot> Load();
}
