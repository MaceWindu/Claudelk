// Persisted per-user config (last paired device id). Stored under
// %APPDATA%\Claudelk\config.json so a fresh install or git pull doesn't
// disturb what the user has paired.

using System.Text.Json;

namespace Claudelk.Core.Configuration;

/// <summary>
/// Per-user settings persisted as JSON at <see cref="DefaultPath"/>.
/// Currently remembers which BLE device was last paired so commands can
/// reconnect without rescanning.
/// </summary>
public sealed class UserConfig
{
    private static readonly JsonSerializerOptions WriteIndentedOptions = new() { WriteIndented = true };

    /// <summary>BLE device id (MAC address on Windows) of the most-recently-paired strip.</summary>
    public string? LastDeviceId { get; set; }

    /// <summary>Advertised name of the most-recently-paired strip, for display only.</summary>
    public string? LastDeviceName { get; set; }

    /// <summary>Default path the config is loaded from and saved to: <c>%APPDATA%\Claudelk\config.json</c>.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Claudelk",
        "config.json");

    /// <summary>
    /// Loads the config from <paramref name="path"/> (default <see cref="DefaultPath"/>).
    /// Returns a fresh empty instance if the file is missing or malformed.
    /// </summary>
    public static UserConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new UserConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserConfig>(json) ?? new UserConfig();
        }
        catch
        {
            // Silent recovery: a malformed or unreadable config is replaced on the next Save.
            return new UserConfig();
        }
    }

    /// <summary>
    /// Persists this config to <paramref name="path"/> (default <see cref="DefaultPath"/>).
    /// Creates the parent directory if missing.
    /// </summary>
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, WriteIndentedOptions);
        File.WriteAllText(path, json);
    }
}
