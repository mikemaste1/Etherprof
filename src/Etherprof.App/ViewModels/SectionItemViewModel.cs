namespace Etherprof.App.ViewModels;

using System;
using System.Windows.Input;
using System.Windows.Media;

public sealed class SectionItemViewModel : INotifyPropertyChangedHelper
{
    public string Id { get; }
    public string Name { get; }
    public string Icon { get; }
    public string ToolTip { get; }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
                _onVisibilityChanged?.Invoke(value);
            }
        }
    }

    private bool _isStarred;
    public bool IsStarred
    {
        get => _isStarred;
        set
        {
            if (_isStarred != value)
            {
                _isStarred = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StarIcon));
                OnPropertyChanged(nameof(StarBrush));
                _onStarredChanged?.Invoke(value);
            }
        }
    }

    public string StarIcon => IsStarred ? "★" : "☆";
    public Brush StarBrush => IsStarred
        ? new SolidColorBrush(Color.FromRgb(245, 158, 11))   // Gold #F59E0B
        : new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Muted slate #94A3B8

    public ICommand ToggleStarCommand { get; }

    private readonly Action<bool>? _onVisibilityChanged;
    private readonly Action<bool>? _onStarredChanged;

    public SectionItemViewModel(
        string id,
        string name,
        string icon,
        string toolTip,
        bool isVisible,
        bool isStarred,
        Action<bool>? onVisibilityChanged = null,
        Action<bool>? onStarredChanged = null)
    {
        Id = id;
        Name = name;
        Icon = icon;
        ToolTip = toolTip;
        _isVisible = isVisible;
        _isStarred = isStarred;
        _onVisibilityChanged = onVisibilityChanged;
        _onStarredChanged = onStarredChanged;

        ToggleStarCommand = new RelayCommand(() => IsStarred = !IsStarred);
    }

    public void SetVisibleSilently(bool value)
    {
        if (_isVisible != value)
        {
            _isVisible = value;
            OnPropertyChanged(nameof(IsVisible));
        }
    }

    public void SetStarredSilently(bool value)
    {
        if (_isStarred != value)
        {
            _isStarred = value;
            OnPropertyChanged(nameof(IsStarred));
            OnPropertyChanged(nameof(StarIcon));
            OnPropertyChanged(nameof(StarBrush));
        }
    }
}
