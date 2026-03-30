namespace WitteNog.App.Services;

public class FilePickerService
{
    public async Task<string?> PickAudioFileAsync(CancellationToken ct = default)
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".wav");

        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;
        if (window is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync().AsTask(ct);
        return file?.Path;
#else
        var result = await Microsoft.Maui.Storage.FilePicker.Default.PickAsync(
            new PickOptions { PickerTitle = "Selecteer WAV bestand" });
        return result?.FullPath;
#endif
    }
}
