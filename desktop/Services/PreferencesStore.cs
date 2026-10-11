using System.Text.Json;
using System.Text;
using Cadenza.Desktop.Models;

namespace Cadenza.Desktop.Services;

internal static class PreferencesStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadenza", "preferences.json");

    public static Preferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return new();
        }
    }

    public static void Save(Preferences preferences)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences), Encoding.UTF8);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
