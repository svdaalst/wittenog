using System.IO.Compression;
using WitteNog.Infrastructure.Services;

namespace WitteNog.Infrastructure.Tests.Services;

public class GitHubUpdateServiceTests
{
    // Builds a .zip in-memory containing the given (entryName, content) pairs and writes
    // it to a temp file. Caller is responsible for deleting both the zip and the destDir.
    private static string CreateZipWithEntries(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"witte-test-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var w = new StreamWriter(entry.Open());
            w.Write(content);
        }
        return path;
    }

    private static string MakeFreshDir(string suffix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"witte-extract-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ExtractZipSafely_ExtractsNormalEntries()
    {
        var zipPath = CreateZipWithEntries(("foo.txt", "hello"), ("sub/bar.txt", "world"));
        var dest = MakeFreshDir("ok");
        try
        {
            GitHubUpdateService.ExtractZipSafely(zipPath, dest);

            Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "foo.txt")));
            Assert.Equal("world", File.ReadAllText(Path.Combine(dest, "sub", "bar.txt")));
        }
        finally
        {
            File.Delete(zipPath);
            Directory.Delete(dest, recursive: true);
        }
    }

    [Fact]
    public void ExtractZipSafely_RejectsParentTraversal()
    {
        // ZipArchive.CreateEntry preserves the literal entry name, including ".." segments.
        var zipPath = CreateZipWithEntries(("../escaped.txt", "pwned"));
        var dest = MakeFreshDir("traversal");
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() =>
                GitHubUpdateService.ExtractZipSafely(zipPath, dest));
            Assert.Contains("Zip-slip", ex.Message);

            // Make sure no file was written outside the destination.
            var parent = Directory.GetParent(dest)!.FullName;
            Assert.False(File.Exists(Path.Combine(parent, "escaped.txt")));
        }
        finally
        {
            File.Delete(zipPath);
            Directory.Delete(dest, recursive: true);
        }
    }

    [Fact]
    public void ExtractZipSafely_RejectsNestedTraversal()
    {
        var zipPath = CreateZipWithEntries(("nested/../../escaped.txt", "pwned"));
        var dest = MakeFreshDir("nested-traversal");
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                GitHubUpdateService.ExtractZipSafely(zipPath, dest));
        }
        finally
        {
            File.Delete(zipPath);
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        }
    }
}
