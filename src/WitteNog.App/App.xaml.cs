namespace WitteNog.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Microsoft.Maui.Controls.Window CreateWindow(
        IActivationState? activationState)
    {
        return new Microsoft.Maui.Controls.Window(new MainPage())
        {
            Title = $"Witte nog? v{Microsoft.Maui.ApplicationModel.AppInfo.VersionString}"
        };
    }
}
