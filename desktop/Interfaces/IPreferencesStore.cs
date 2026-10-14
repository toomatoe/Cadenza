using Cadenza.Desktop.Models;

namespace Cadenza.Desktop.Interfaces;

/// <summary>Loads and saves the user's <see cref="Preferences"/>.</summary>
public interface IPreferencesStore
{
    /// <summary>Saved preferences, or defaults if there are none or the file can't be read.</summary>
    Preferences Load();

    /// <summary>Saves preferences atomically.</summary>
    void Save(Preferences preferences);
}
