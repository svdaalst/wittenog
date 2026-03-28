using System.IO.Abstractions.TestingHelpers;
using WitteNog.Core.Models;
using WitteNog.Infrastructure.Settings;

namespace WitteNog.Infrastructure.Tests.Settings;

public class JsonSettingsProviderTests
{
    private readonly MockFileSystem _fs = new();
    private const string VaultPath = "/vault";

    // ── ILinkMetadataService (existing) ────────────────────────────────────────

    [Fact]
    public void IsArchived_ReturnsFalse_WhenNoSettingsFile()
    {
        var sut = new JsonSettingsProvider(_fs);
        Assert.False(sut.IsArchived(VaultPath, "Projecten/Test"));
    }

    [Fact]
    public void SetArchivedStatus_Persists_AndIsReadBack()
    {
        var sut = new JsonSettingsProvider(_fs);
        sut.SetArchivedStatus(VaultPath, "Projecten/Test", true);

        // Nieuwe instantie leest van dezelfde (in-memory) disk
        var sut2 = new JsonSettingsProvider(_fs);
        Assert.True(sut2.IsArchived(VaultPath, "Projecten/Test"));
    }

    [Fact]
    public void SetArchivedStatus_Unarchive_RemovesFromFile()
    {
        var sut = new JsonSettingsProvider(_fs);
        sut.SetArchivedStatus(VaultPath, "Link", true);
        sut.SetArchivedStatus(VaultPath, "Link", false);
        Assert.False(sut.IsArchived(VaultPath, "Link"));
    }

    [Fact]
    public void MetadataChanged_IsFired_OnSetArchivedStatus()
    {
        var sut = new JsonSettingsProvider(_fs);
        bool fired = false;
        sut.MetadataChanged += () => fired = true;
        sut.SetArchivedStatus(VaultPath, "Link", true);
        Assert.True(fired);
    }

    [Fact]
    public void GetArchivedLinks_ReturnsAllArchived()
    {
        var sut = new JsonSettingsProvider(_fs);
        sut.SetArchivedStatus(VaultPath, "A", true);
        sut.SetArchivedStatus(VaultPath, "B", true);
        var result = sut.GetArchivedLinks(VaultPath);
        Assert.Equal(2, result.Count);
        Assert.Contains("A", result);
        Assert.Contains("B", result);
    }

    [Fact]
    public void IsArchived_IsCaseInsensitive()
    {
        var sut = new JsonSettingsProvider(_fs);
        sut.SetArchivedStatus(VaultPath, "Projecten/Test", true);
        Assert.True(sut.IsArchived(VaultPath, "projecten/test"));
    }

    // ── IVaultSettings (new) ───────────────────────────────────────────────────

    [Fact]
    public void GetTranscriptionSettings_ReturnsDefaults_WhenNoSettingsFile()
    {
        var sut = new JsonSettingsProvider(_fs);
        var settings = sut.GetTranscriptionSettings(VaultPath);
        Assert.Equal(["nl"], settings.Languages);
        Assert.Equal("Base", settings.Model);
    }

    [Fact]
    public void SaveTranscriptionSettings_Persists_AndIsReadBack()
    {
        var sut = new JsonSettingsProvider(_fs);
        var saved = new TranscriptionSettings { Languages = ["en", "nl"], Model = "Small" };
        sut.SaveTranscriptionSettings(VaultPath, saved);

        var sut2 = new JsonSettingsProvider(_fs);
        var loaded = sut2.GetTranscriptionSettings(VaultPath);
        Assert.Equal(["en", "nl"], loaded.Languages);
        Assert.Equal("Small", loaded.Model);
    }

    [Fact]
    public void SaveTranscriptionSettings_PreservesArchivedLinks()
    {
        var sut = new JsonSettingsProvider(_fs);
        sut.SetArchivedStatus(VaultPath, "Projecten/Test", true);

        sut.SaveTranscriptionSettings(VaultPath,
            new TranscriptionSettings { Languages = ["de"], Model = "Tiny" });

        var sut2 = new JsonSettingsProvider(_fs);
        Assert.True(sut2.IsArchived(VaultPath, "Projecten/Test"));
        Assert.Equal(["de"], sut2.GetTranscriptionSettings(VaultPath).Languages);
    }

    [Fact]
    public void SetArchivedStatus_PreservesTranscriptionSettings()
    {
        var sut = new JsonSettingsProvider(_fs);
        sut.SaveTranscriptionSettings(VaultPath,
            new TranscriptionSettings { Languages = ["fr"], Model = "Medium" });

        sut.SetArchivedStatus(VaultPath, "Link", true);

        Assert.Equal("Medium", sut.GetTranscriptionSettings(VaultPath).Model);
    }
}
