namespace WitteNog.App.Services;

public class FolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");

        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;
        if (window is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken);
        return folder?.Path;
#elif ANDROID
        var result = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync(cancellationToken);
        if (!result.IsSuccessful) return null;

        var path = result.Folder.Path;
        // Android SAF returns a content:// URI for external storage which System.IO cannot use.
        // Fall back to app-private storage so all existing file I/O works unchanged.
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(FileSystem.AppDataDirectory, "vault");

        return path;
#else
        var result = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync(cancellationToken);
        return result.IsSuccessful ? result.Folder.Path : null;
#endif
    }
}
