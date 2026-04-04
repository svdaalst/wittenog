using Android.App;
using Android.Content.PM;
using Android.OS;
using WitteNog.App.Services;

namespace WitteNog.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnResume()
    {
        base.OnResume();
        // Compensate for unreliable FileSystemWatcher on Android by triggering
        // a full refresh whenever the app returns to the foreground.
        IPlatformApplication.Current?.Services
            .GetService<VaultWatcherService>()
            ?.TriggerManualRefresh();
    }

    public override void OnBackPressed()
    {
        var svc = IPlatformApplication.Current?.Services.GetService<BackButtonService>();
        if (svc?.RaiseBackPressed() == true) return;
#pragma warning disable CS0612 // OnBackPressed is obsolete on API 33+ but still functional in MAUI 9
        base.OnBackPressed();
#pragma warning restore CS0612
    }
}
