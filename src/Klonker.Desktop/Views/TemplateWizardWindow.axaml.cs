using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Klonker.Desktop.Views;

public partial class TemplateWizardWindow : Window
{
    public TemplateWizardWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
