using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Klonker.Desktop.ViewModels;

namespace Klonker.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void OnRemoveRegistryClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is SettingsViewModel viewModel &&
            sender is Button { Tag: RegistrySourceEditorViewModel source })
        {
            viewModel.RemoveRegistry(source);
            eventArgs.Handled = true;
        }
    }

    private static void OnAddTrustedKeyClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: RegistrySourceEditorViewModel source })
        {
            SettingsViewModel.AddTrustedKey(source);
            eventArgs.Handled = true;
        }
    }

    private static void OnRemoveTrustedKeyClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: TrustedPublisherKeyViewModel key } button)
        {
            return;
        }

        var source = button
            .GetVisualAncestors()
            .OfType<ItemsControl>()
            .Select(control => control.DataContext)
            .OfType<RegistrySourceEditorViewModel>()
            .FirstOrDefault();
        if (source is not null)
        {
            SettingsViewModel.RemoveTrustedKey(source, key);
            eventArgs.Handled = true;
        }
    }
}
