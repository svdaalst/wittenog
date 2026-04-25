namespace WitteNog.App;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
#if WINDOWS
		blazorWebView.BlazorWebViewInitialized += (_, e) =>
		{
			e.WebView.CoreWebView2.PermissionRequested += (_, args) =>
			{
				if (args.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.ClipboardRead)
					args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
			};
		};
#endif
	}
}
