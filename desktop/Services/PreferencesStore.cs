using System.Text;
using System.Text.Json;
using Cadenza.Desktop.Interfaces;
using Cadenza.Desktop.Models;

namespace Cadenza.Desktop.Services;

/// <summary>Reads and writes <see cref="Preferences"/> under %LOCALAPPDATA%\Cadenza.</summary>
public sealed class PreferencesStore : IPreferencesStore
{
    private readonly string _filePath;

    /// <param name="filePath">The preferences JSON file.</param>
    public PreferencesStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>Loads saved preferences, falling back to defaults if the file is missing or unreadable.</summary>
    public Preferences Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_filePath)) ?? new Preferences()
                : new Preferences();
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return new Preferences();
        }
    }

    /// <summary>Saves preferences atomically by writing a temporary file and replacing the original.</summary>
    public void Save(Preferences preferences)
    {
        var path = _filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences), Encoding.UTF8);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
