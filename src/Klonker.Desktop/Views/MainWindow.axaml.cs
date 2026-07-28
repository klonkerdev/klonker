using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Klonker.Desktop.Views;

public partial class MainWindow : Window
{
    private SettingsWindow? settingsWindow;

    public Func<SettingsWindow>? SettingsWindowFactory { get; set; }

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
