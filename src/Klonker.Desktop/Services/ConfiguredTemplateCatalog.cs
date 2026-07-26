using Klonker.Core.Diagnostics;
using Klonker.Core.Registry;

namespace Klonker.Desktop.Services;

public sealed class ConfiguredTemplateCatalog : ITemplateCatalog
{
    private readonly RegistryConfigurationStore configurationStore;
    private readonly RegistryCatalogService catalogService;

    public ConfiguredTemplateCatalog(
        RegistryConfigurationStore configurationStore,
        RegistryCatalogService catalogService)
    {
        this.configurationStore = configurationStore;
        this.catalogService = catalogService;
    }

    public async Task<OperationResult<TemplateCatalogSnapshot>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = configurationStore.Load();
        if (!configuration.IsSuccess)
        {
            return new OperationResult<TemplateCatalogSnapshot>(
                null,
                configuration.Issues);
        }

        var catalog = await catalogService.LoadAsync(
            configuration.Value!.Sources,
            new RegistryCatalogOptions(
                configuration.Value.CacheRoot,
                configuration.Value.Offline),
            cancellationToken).ConfigureAwait(false);
        if (!catalog.IsSuccess)
        {
            return new OperationResult<TemplateCatalogSnapshot>(
                null,
                configuration.Issues.Concat(catalog.Issues));
        }

        return new OperationResult<TemplateCatalogSnapshot>(
            new TemplateCatalogSnapshot(
                catalog.Value!.Templates,
                configuration.Value.ConfigurationPath,
                configuration.Value.CacheRoot,
                configuration.Value.Offline),
            configuration.Issues.Concat(catalog.Issues));
    }
}
