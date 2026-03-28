namespace WitteNog.App.Services;

/// <summary>
/// Whisper-ondersteunde talen beschikbaar voor selectie in de app.
/// Volgorde = weergavevolgorde in de "taal toevoegen" dropdown.
/// </summary>
public static class KnownLanguages
{
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("nl", "Nederlands"),
        ("en", "Engels"),
        ("de", "Duits"),
        ("fr", "Frans"),
        ("es", "Spaans"),
        ("it", "Italiaans"),
        ("pt", "Portugees"),
        ("pl", "Pools"),
        ("ru", "Russisch"),
        ("tr", "Turks"),
        ("ja", "Japans"),
        ("zh", "Chinees"),
        ("ar", "Arabisch"),
        ("ko", "Koreaans"),
        ("auto", "Auto-detecteer"),
    ];

    /// <summary>Returns the display name for the given ISO code, or the code itself as fallback.</summary>
    public static string GetName(string code) =>
        All.FirstOrDefault(l => l.Code == code).Name ?? code;
}
