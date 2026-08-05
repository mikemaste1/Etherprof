using System.Windows;
using Etherprof.App.ViewModels;
using Etherprof.Contracts.Models;

namespace Etherprof.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // Wire dialog open actions
            vm.OpenProfileEditor = profile => ShowProfileEditor(vm, profile);
            vm.OpenTestSetEditor = testSet => ShowTestSetEditor(vm, testSet);
            vm.OpenCaptureProfile = () => ShowCaptureProfile(vm);
            vm.OpenAdapterSelector = () => ShowAdapterSelector(vm);
            vm.OpenAliasEditorDialog = (bssid, alias) => ShowAliasEditor(vm, bssid, alias);
        }
    }

    private void ShowProfileEditor(MainViewModel vm, NetworkProfile? existingProfile)
    {
        var dialog = new ProfileEditorDialog(existingProfile)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _ = vm.AddOrUpdateProfileAsync(dialog.Result, existingProfile is null);
        }
    }

    private void ShowTestSetEditor(MainViewModel vm, TestSet? existingTestSet)
    {
        var dialog = new TestSetEditorDialog(existingTestSet)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _ = vm.AddOrUpdateTestSetAsync(dialog.Result, existingTestSet is null);
        }
    }

    private async void ShowCaptureProfile(MainViewModel vm)
    {
        var captured = await vm.CaptureCurrentProfileAsync();
        if (captured is null) return;

        var dialog = new CaptureProfileDialog(captured)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _ = vm.AddOrUpdateProfileAsync(dialog.Result, true);
        }
    }

    private async void ShowAdapterSelector(MainViewModel vm)
    {
        var dialog = new AdapterSelectorDialog(vm)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private void ShowAliasEditor(MainViewModel vm, string bssid, string? currentAlias)
    {
        var editorVm = new BssidAliasEditorViewModel(bssid, currentAlias);
        var dialog = new BssidAliasEditorDialog(editorVm)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _ = vm.WifiPanel.SetAliasAsync(bssid, editorVm.Alias);
        }
    }
}
