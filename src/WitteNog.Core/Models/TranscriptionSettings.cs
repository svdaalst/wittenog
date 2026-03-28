namespace WitteNog.Core.Models;

public record TranscriptionSettings
{
    /// <summary>ISO 639-1 language codes in user-preferred order. First entry = default in picker.</summary>
    public List<string> Languages { get; init; } = ["nl"];

    /// <summary>
    /// String name of the Whisper GgmlType enum: "Tiny" | "Base" | "Small" | "Medium" | "Large".
    /// Defaults to "Base" (~142 MB).
    /// </summary>
    public string Model { get; init; } = "Base";
}
