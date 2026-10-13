using Cadenza.Desktop.Views;
using Microsoft.UI.Xaml;

namespace Cadenza.Desktop;

/// <summary>WinUI application entry point; the SDK generates <c>Main</c>.</summary>
public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
