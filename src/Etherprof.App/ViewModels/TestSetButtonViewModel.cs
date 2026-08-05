using System.ComponentModel;
using System.Runtime.CompilerServices;
using Etherprof.Contracts.Models;

namespace Etherprof.App.ViewModels;

public sealed class TestSetButtonViewModel : INotifyPropertyChanged
{
    public TestSet TestSet { get; }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); } }
    }

    public string DisplayText => IsActive ? $"■ {TestSet.Name}" : TestSet.Name;

    public TestSetButtonViewModel(TestSet testSet)
    {
        TestSet = testSet;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
