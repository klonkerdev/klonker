using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Klonker.Desktop.Views;

public partial class MainWindow : Window
{
    private SettingsWindow? settingsWindow;
    private TemplateWizardWindow? templateWizardWindow;
    private RegistryWizardWindow? registryWizardWindow;
    private AboutWindow? aboutWindow;

    public Func<SettingsWindow>? SettingsWindowFactory { get; set; }

    public Func<TemplateWizardWindow>? TemplateWizardWindowFactory { get; set; }

    public Func<RegistryWizardWindow>? RegistryWizardWindowFactory { get; set; }

    public Func<AboutWindow>? AboutWindowFactory { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.Property == WindowStateProperty)
            {
                UpdateMaximizeGlyph();
            }
        };
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        BeginMoveDrag(eventArgs);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Minimized;
    }

    private async void OnSettingsClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (settingsWindow is not null)
        {
            settingsWindow.Activate();
            return;
        }

        settingsWindow = SettingsWindowFactory?.Invoke();
        if (settingsWindow is null)
        {
            return;
        }

        try
        {
            await settingsWindow.ShowDialog(this);
        }
        finally
        {
            settingsWindow = null;
        }
    }

    private async void OnTemplateWizardClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (templateWizardWindow is not null)
        {
            templateWizardWindow.Activate();
            return;
        }

        templateWizardWindow = TemplateWizardWindowFactory?.Invoke();
        if (templateWizardWindow is null)
        {
            return;
        }

        try
        {
            await templateWizardWindow.ShowDialog(this);
        }
        finally
        {
            templateWizardWindow = null;
        }
    }

    private async void OnRegistryWizardClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (registryWizardWindow is not null)
        {
            registryWizardWindow.Activate();
            return;
        }

        registryWizardWindow = RegistryWizardWindowFactory?.Invoke();
        if (registryWizardWindow is null)
        {
            return;
        }

        try
        {
            await registryWizardWindow.ShowDialog(this);
        }
        finally
        {
            registryWizardWindow = null;
        }
    }

    private async void OnAboutClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (aboutWindow is not null)
        {
            aboutWindow.Activate();
            return;
        }

        aboutWindow = AboutWindowFactory?.Invoke();
        if (aboutWindow is null)
        {
            return;
        }

        try
        {
            await aboutWindow.ShowDialog(this);
        }
        finally
        {
            aboutWindow = null;
        }
    }

    private void OnMaximizeClicked(object? sender, RoutedEventArgs eventArgs)
    {
        ToggleMaximize();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeIcon.IsVisible = !isMaximized;
        RestoreIcon.IsVisible = isMaximized;
    }
}
