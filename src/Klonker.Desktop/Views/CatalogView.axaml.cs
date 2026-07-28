using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Klonker.Desktop.ViewModels;

namespace Klonker.Desktop.Views;

public partial class CatalogView : UserControl
{
    public CatalogView()
    {
        InitializeComponent();
    }

    private void OnPackagePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (ShouldIgnorePointer(eventArgs) || eventArgs.ClickCount < 2)
        {
            return;
        }

        if (sender is not ListBox { SelectedItem: PackageListItemViewModel package } ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedPackage = package;
        if (viewModel.ConfirmPackageCommand.CanExecute(null))
        {
            viewModel.ConfirmPackageCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void OnVariantPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (ShouldIgnorePointer(eventArgs) || eventArgs.ClickCount < 2)
        {
            return;
        }

        if (sender is not ListBox { SelectedItem: TemplateListItemViewModel template } ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.OpenConfigurationCommand.CanExecute(template))
        {
            viewModel.OpenConfigurationCommand.Execute(template);
            eventArgs.Handled = true;
        }
    }

    private void OnConfirmPackageClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: PackageListItemViewModel package } &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedPackage = package;
            viewModel.ConfirmPackageCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void OnConfigureVariantClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: TemplateListItemViewModel template } &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.OpenConfigurationCommand.Execute(template);
            eventArgs.Handled = true;
        }
    }

    private void OnFavoriteVariantClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: TemplateListItemViewModel template } &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.ToggleFavorite(template);
            eventArgs.Handled = true;
        }
    }

    private static bool ShouldIgnorePointer(PointerPressedEventArgs eventArgs) =>
        eventArgs.Source is Visual source &&
        (source.FindAncestorOfType<Button>() is not null ||
         source.FindAncestorOfType<ToggleButton>() is not null);
}
