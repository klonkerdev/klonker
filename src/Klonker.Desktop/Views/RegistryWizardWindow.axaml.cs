using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Klonker.Desktop.Views;

public partial class RegistryWizardWindow : Window
{
    public RegistryWizardWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(
        object? sender,
        RoutedEventArgs eventArgs) =>
        Close();
}
