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
        // Android SAF may return a content:// URI; try to resolve it to a real file path.
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = TryResolveContentUri(path);
            if (resolved is not null)
                return resolved;

            // Resolution failed — use app-specific external storage (visible in Settings > Apps).
            return Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath + "/vault";
        }

        return path;
#else
        var result = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync(cancellationToken);
        return result.IsSuccessful ? result.Folder.Path : null;
#endif
    }

#if ANDROID
    /// <summary>
    /// Attempts to resolve an Android SAF content:// URI to a real filesystem path.
    /// Works for the primary volume (internal storage). Returns null for SD cards or
    /// any URI that cannot be mapped to a path accessible via System.IO.
    /// </summary>
    private static string? TryResolveContentUri(string contentUri)
    {
        try
        {
            var uri = Android.Net.Uri.Parse(contentUri)!;
            var docId = Android.Provider.DocumentsContract.GetTreeDocumentId(uri);
            if (docId is null) return null;

            // Primary volume pattern: "primary:Documents/Foo" → /storage/emulated/0/Documents/Foo
            var parts = docId.Split(':', 2);
            if (parts.Length == 2 && parts[0].Equals("primary", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(
                    Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath,
                    parts[1].Replace('/', Path.DirectorySeparatorChar));
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
#endif
}
