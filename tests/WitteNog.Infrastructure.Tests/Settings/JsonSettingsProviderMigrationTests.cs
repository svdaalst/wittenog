using System.IO.Abstractions.TestingHelpers;
using WitteNog.Infrastructure.Settings;

namespace WitteNog.Infrastructure.Tests.Settings;

public class JsonSettingsProviderMigrationTests
{
    private const string VaultPath = "/vault";
    private static string OldPath => $"{VaultPath}/vault-settings.json";
    private static string NewPath => $"{VaultPath}/.metadata/vault-settings.json";

    [Fact]
    public void OldSettingsFile_IsMigratedToMetadataSubfolder_OnFirstAccess()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(VaultPath);
        fs.File.WriteAllText(OldPath,
            """{"ArchivedLinks":["OldLink"]}""");

        var sut = new JsonSettingsProvider(fs);
        Assert.True(sut.IsArchived(VaultPath, "OldLink"));

        Assert.False(fs.File.Exists(OldPath));
        Assert.True(fs.File.Exists(NewPath));
    }

    [Fact]
    public void Migration_IsIdempotent_WhenBothFilesExist()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(VaultPath);
        fs.Directory.CreateDirectory($"{VaultPath}/.metadata");
        fs.File.WriteAllText(OldPath,
            """{"ArchivedLinks":["OldLink"]}""");
        fs.File.WriteAllText(NewPath,
            """{"ArchivedLinks":["NewLink"]}""");

        var sut = new JsonSettingsProvider(fs);
        // New file takes precedence; old file is not overwritten
        Assert.True(sut.IsArchived(VaultPath, "NewLink"));
        Assert.False(sut.IsArchived(VaultPath, "OldLink"));
        Assert.True(fs.File.Exists(OldPath));
        Assert.True(fs.File.Exists(NewPath));
    }

    [Fact]
    public void InvalidateCache_CausesReloadFromDisk_OnNextAccess()
    {
        var fs = new MockFileSystem();
        var sut = new JsonSettingsProvider(fs);

        sut.SetArchivedStatus(VaultPath, "Link", true);
        Assert.True(sut.IsArchived(VaultPath, "Link")); // cache hit

        // Simulate external change: rewrite file with different content
        fs.File.WriteAllText(NewPath,
            """{"ArchivedLinks":[]}""");

        // Without invalidation the cache is still used
        Assert.True(sut.IsArchived(VaultPath, "Link"));

        // After invalidation the new file content is loaded
        sut.InvalidateCache(VaultPath);
        Assert.False(sut.IsArchived(VaultPath, "Link"));
    }

    [Fact]
    public void SettingsFile_IsWrittenToMetadataSubfolder()
    {
        var fs = new MockFileSystem();
        var sut = new JsonSettingsProvider(fs);

        sut.SetArchivedStatus(VaultPath, "Link", true);

        Assert.True(fs.File.Exists(NewPath));
        Assert.False(fs.File.Exists(OldPath));
    }
}
