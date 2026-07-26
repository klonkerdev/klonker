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
            registryHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20),
            };
            var configurationStore = RegistryConfigurationStore.CreateDefault();
            var catalog = new ConfiguredTemplateCatalog(
                configurationStore,
                new Klonker.Core.Registry.RegistryCatalogService(registryHttpClient));
            var window = new MainWindow();
            var viewModel = new MainViewModel(
                catalog,
                new CoreProjectGenerationService(),
                new AvaloniaDestinationPicker(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                viewModel.Dispose();
                Dispose();
            };
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
