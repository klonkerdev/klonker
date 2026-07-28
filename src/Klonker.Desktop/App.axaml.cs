using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Klonker.Desktop.Services;
using Klonker.Desktop.ViewModels;
using Klonker.Desktop.Views;

namespace Klonker.Desktop;

public partial class App : Application, IDisposable
{
    private HttpClient? registryHttpClient;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var registryConfigurationStore =
                RegistryConfigurationStore.CreateDefault();
            var appSettingsStore = new AppSettingsStore(
                registryConfigurationStore.ApplicationDataRoot);
            var favoriteStore = new FavoriteStore(
                registryConfigurationStore.ApplicationDataRoot);
            var catalogTabStore = new CatalogTabStore(
                registryConfigurationStore.ApplicationDataRoot);
            var diagnosticLog = new AppDiagnosticLog(appSettingsStore);
            var tagPalette = new TemplateTagPalette();
            var appearanceService = new AppearanceService(tagPalette);
            var appSettings = appSettingsStore.Load();
            var timeoutSeconds = appSettings.IsSuccess
                ? appSettings.Value!.RegistryDownloadTimeoutSeconds
                : AppSettingsStore.DefaultRegistryDownloadTimeoutSeconds;
            if (appSettings.IsSuccess)
            {
                appearanceService.Apply(appSettings.Value!.Appearance);
            }

            registryHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            };
            var catalog = new ConfiguredTemplateCatalog(
                registryConfigurationStore,
                new Klonker.Core.Registry.RegistryCatalogService(registryHttpClient),
                appSettingsStore);
            var window = new MainWindow();
            var viewModel = new MainViewModel(
                catalog,
                new CoreProjectGenerationService(),
                new AvaloniaDestinationPicker(window),
                favoriteStore,
                appSettingsStore,
                new WindowsPrerequisiteProbeService(),
                diagnosticLog,
                tagPalette,
                new WslGenerationService(),
                catalogTabStore);
            var maintenanceService = new LocalDataMaintenanceService(
                registryConfigurationStore,
                appSettingsStore,
                favoriteStore,
                diagnosticLog);
            var authoringOptions =
                TemplateAuthoringOptionsLoader.LoadDefault();
            window.TemplateWizardWindowFactory = () =>
            {
                var wizardWindow = new TemplateWizardWindow();
                wizardWindow.DataContext = new TemplateWizardViewModel(
                    authoringOptions,
                    new CoreTemplateAuthoringService(),
                    new AvaloniaTemplateAuthoringFolderPicker(wizardWindow),
                    viewModel.Templates.Select(template => template.Template));
                return wizardWindow;
            };
            window.RegistryWizardWindowFactory = () =>
            {
                var registryWindow = new RegistryWizardWindow();
                registryWindow.DataContext = new RegistryWizardViewModel(
                    registryConfigurationStore,
                    new AvaloniaRegistryWorkspacePicker(registryWindow));
                return registryWindow;
            };
            window.AboutWindowFactory = () => new AboutWindow
            {
                DataContext = new AboutViewModel(),
            };
            window.SettingsWindowFactory = () => new SettingsWindow(
                new SettingsViewModel(
                    appSettingsStore,
                    registryConfigurationStore,
                    maintenanceService,
                    appearanceService,
                    diagnosticLog,
                    viewModel.Load));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                viewModel.Dispose();
                Dispose();
            };
            diagnosticLog.Write(
                DiagnosticLogLevel.Information,
                "application.start",
                "Klonker desktop initialized.");
            viewModel.Load();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose()
    {
        registryHttpClient?.Dispose();
        registryHttpClient = null;
        GC.SuppressFinalize(this);
    }
}
