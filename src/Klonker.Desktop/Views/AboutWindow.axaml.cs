using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Klonker.Desktop.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(
        object? sender,
        RoutedEventArgs eventArgs) =>
        Close();

    private async void OnLinkClicked(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string target } ||
            !Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        await Launcher.LaunchUriAsync(uri);
        eventArgs.Handled = true;
    }
}
