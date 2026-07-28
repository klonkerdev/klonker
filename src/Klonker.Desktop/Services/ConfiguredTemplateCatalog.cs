using Klonker.Core.Diagnostics;
using Klonker.Core.Registry;

namespace Klonker.Desktop.Services;

public sealed class ConfiguredTemplateCatalog : ITemplateCatalog
{
    private readonly RegistryConfigurationStore configurationStore;
    private readonly RegistryCatalogService catalogService;
    private readonly AppSettingsStore? appSettingsStore;

    public ConfiguredTemplateCatalog(
        RegistryConfigurationStore configurationStore,
        RegistryCatalogService catalogService,
        AppSettingsStore? appSettingsStore = null)
    {
        this.configurationStore = configurationStore;
        this.catalogService = catalogService;
        this.appSettingsStore = appSettingsStore;
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

        var appSettings = appSettingsStore?.Load();
        var catalogOptions = appSettings?.IsSuccess == true
            ? new RegistryCatalogOptions(
                configuration.Value!.CacheRoot,
                configuration.Value.Offline,
                appSettings.Value!.RegistryVersionPreference,
                appSettings.Value.RegistryVersionPins,
                appSettings.Value.RegistryDuplicateSourcePolicy)
            : new RegistryCatalogOptions(
                configuration.Value!.CacheRoot,
                configuration.Value.Offline);
        var catalog = await catalogService.LoadAsync(
            configuration.Value!.Sources,
            catalogOptions,
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
                configuration.Value.Offline,
                catalog.Value.Modules,
                catalog.Value.TemplateVersions,
                catalog.Value.ModuleVersions),
            configuration.Issues
                .Concat(appSettings?.Issues ?? [])
                .Concat(catalog.Issues));
    }
}
