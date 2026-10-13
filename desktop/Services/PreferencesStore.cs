using System.Text;
using System.Text.Json;
using Cadenza.Desktop.Models;

namespace Cadenza.Desktop.Services;

/// <summary>Reads and writes <see cref="Preferences"/> under %LOCALAPPDATA%\Cadenza.</summary>
public static class PreferencesStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadenza", "preferences.json");

    /// <summary>Loads saved preferences, falling back to defaults if the file is missing or unreadable.</summary>
    public static Preferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new Preferences()
                : new Preferences();
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return new Preferences();
        }
    }

    /// <summary>Saves preferences atomically by writing a temporary file and replacing the original.</summary>
    public static void Save(Preferences preferences)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences), Encoding.UTF8);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
