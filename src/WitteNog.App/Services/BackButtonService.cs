namespace WitteNog.App.Services;

/// <summary>
/// Broker for Android back-button events. Registered as a singleton on Android only.
/// Components subscribe to BackPressed and return true if they handled the event
/// (preventing default Android back behavior such as exiting the app).
/// </summary>
public class BackButtonService
{
    public event Func<bool>? BackPressed;

    /// <summary>
    /// Invokes all subscribers in order. Returns true if any subscriber handled the event.
    /// </summary>
    public bool RaiseBackPressed()
    {
        if (BackPressed is null) return false;
        foreach (var handler in BackPressed.GetInvocationList().Cast<Func<bool>>())
        {
            if (handler()) return true;
        }
        return false;
    }
}
