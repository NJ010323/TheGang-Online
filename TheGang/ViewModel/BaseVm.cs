using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ViewModel;

/// <summary>
/// Class defining generic ViewModel
/// </summary>
public abstract class BaseVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}