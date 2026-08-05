namespace Etherprof.App.ViewModels;

using System.Windows.Input;

public sealed class BssidAliasEditorViewModel : INotifyPropertyChangedHelper
{
    private string _alias = "";

    public string Bssid { get; }

    public string Alias
    {
        get => _alias;
        set { _alias = value; OnPropertyChanged(); }
    }

    public Action<bool>? CloseAction { get; set; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public BssidAliasEditorViewModel(string bssid, string? currentAlias)
    {
        Bssid = bssid;
        Alias = currentAlias ?? "";

        SaveCommand = new RelayCommand(_ => CloseAction?.Invoke(true));
        CancelCommand = new RelayCommand(_ => CloseAction?.Invoke(false));
    }
}
