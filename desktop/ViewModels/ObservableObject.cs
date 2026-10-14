using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cadenza.Desktop.ViewModels;

/// <summary>Minimal <see cref="INotifyPropertyChanged"/> base for view models (no MVVM toolkit, to keep the app light).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets a backing field and raises <see cref="PropertyChanged"/> when the value actually changes.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
