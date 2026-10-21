using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ViewModel;

/// <summary>
/// Base des vues qui notifient les changements de propriétés à WPF.
/// </summary>
public abstract class BaseVm : INotifyPropertyChanged
{
    /// <summary>Événement émis lorsqu'une propriété affichable change.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Notifie la propriété appelante, ou celle fournie explicitement.</summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
