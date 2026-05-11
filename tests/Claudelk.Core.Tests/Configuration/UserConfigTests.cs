// Exercises UserConfig's JSON round-trip and its tolerance for missing /
// malformed files. Uses a per-test temp directory so we never touch the real
// %APPDATA%\Claudelk\config.json the developer is using locally.

using System.Globalization;
using Claudelk.Core.Configuration;

namespace Claudelk.Core.Tests.Configuration;

[TestFixture]
public sealed class UserConfigTests
{
    private string _tempDir = null!;
    private string _configPath = null!;

    [SetUp]
    public void CreateTempDirectory()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "Claudelk.Tests." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    [TearDown]
    public void DeleteTempDirectory()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Test ran on a filesystem that's still holding the file; not our concern.
        }
    }

    [Test]
    public void Load_MissingFile_ReturnsEmptyInstance()
    {
        var config = UserConfig.Load(_configPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(config, Is.Not.Null);
            Assert.That(config.LastDeviceId, Is.Null);
            Assert.That(config.LastDeviceName, Is.Null);
        }
    }

    [Test]
    public void Load_MalformedJson_ReturnsEmptyInstance()
    {
        File.WriteAllText(_configPath, "this is not json {");

        var config = UserConfig.Load(_configPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(config, Is.Not.Null);
            Assert.That(config.LastDeviceId, Is.Null);
            Assert.That(config.LastDeviceName, Is.Null);
        }
    }

    [Test]
    public void Load_EmptyJsonObject_ReturnsEmptyInstance()
    {
        File.WriteAllText(_configPath, "{}");

        var config = UserConfig.Load(_configPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(config.LastDeviceId, Is.Null);
            Assert.That(config.LastDeviceName, Is.Null);
        }
    }

    [Test]
    public void Save_ThenLoad_RoundTripsAllProperties()
    {
        var saved = new UserConfig
        {
            LastDeviceId = "be:ff:f0:01:04:a8",
            LastDeviceName = "ELK-BLEDOM",
        };
        saved.Save(_configPath);

        var loaded = UserConfig.Load(_configPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.LastDeviceId, Is.EqualTo("be:ff:f0:01:04:a8"));
            Assert.That(loaded.LastDeviceName, Is.EqualTo("ELK-BLEDOM"));
        }
    }

    [Test]
    public void Save_CreatesParentDirectoryWhenMissing()
    {
        // Point at a nested directory that doesn't exist yet.
        var nested = Path.Combine(_tempDir, "a", "b", "c", "config.json");
        var config = new UserConfig { LastDeviceId = "id" };

        config.Save(nested);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(nested), Is.True);
            Assert.That(UserConfig.Load(nested).LastDeviceId, Is.EqualTo("id"));
        }
    }

    [Test]
    public void Save_WritesIndentedJson()
    {
        var config = new UserConfig
        {
            LastDeviceId = "id-1",
            LastDeviceName = "name-1",
        };

        config.Save(_configPath);

        var text = File.ReadAllText(_configPath);
        // Indented output spans multiple lines — a single-line blob would mean
        // we lost the WriteIndented setting.
        Assert.That(text, Does.Contain("\n"));
    }

    [Test]
    public void DefaultPath_LivesUnderApplicationData()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserConfig.DefaultPath, Does.StartWith(appData));
            Assert.That(UserConfig.DefaultPath, Does.EndWith(Path.Combine("Claudelk", "config.json")));
        }
    }
}
