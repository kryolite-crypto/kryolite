using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing.Internal.PathSegments;

namespace Kryolite.Wallet;

public abstract class NotifyPropertyChanged : INotifyPropertyChanged, INotifyDataErrorInfo
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void RaisePropertyChanged<T>(ref T current, T next, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(current, next))
        {
            current = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    protected void RaisePropertyChanged<T>(ref T current, T next, Func<string?> validate, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(current, next))
        {
            current = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            _errors[propertyName!] = validate();
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }
    }

    public bool HasErrors => _errors.Any(x => !string.IsNullOrEmpty(x.Value));

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is null)
        {
            return Array.Empty<string>();
        }

        if (!_errors.TryGetValue(propertyName, out var error))
        {
            return Array.Empty<string>();
        }

        if (string.IsNullOrEmpty(error))
        {
            return Array.Empty<string>();
        }

        return new [] { error };
    }

    private Dictionary<string, string?> _errors = new();
}
