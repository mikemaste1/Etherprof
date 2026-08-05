using System.Windows;
using System.Windows.Controls;
using Etherprof.Contracts.Models;
using Etherprof.App.ViewModels;

namespace Etherprof.App.Views;

public partial class AdapterSelectorDialog : Window
{
    private readonly MainViewModel _vm;
    private readonly List<AdapterRow> _rows = new();
    private bool _showingAll = false;

    private sealed class AdapterRow
    {
        public string AdapterId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public CheckBox VisibleCheck { get; init; } = null!;
        public RadioButton SelectRadio { get; init; } = null!;
    }

    public AdapterSelectorDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Loaded += async (_, _) => await LoadAdaptersAsync();
    }

    private async Task LoadAdaptersAsync()
    {
        var adapters = await ((Etherprof.Contracts.Interfaces.INetworkAdapterProvider)
            typeof(MainViewModel).GetField("_adapterProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_vm)!).GetAdaptersAsync();

        var settings = (AppSettings)typeof(MainViewModel)
            .GetField("_settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_vm)!;

        adapterList.Items.Clear();
        _rows.Clear();

        bool hasAnyChecked = settings.AdapterPreferences.Any(p => p.IsVisible);

        // Filter adapters: if not showing all and there are checked preferences, show only checked (plus active)
        var displayAdapters = adapters.Where(a =>
        {
            if (_showingAll || !hasAnyChecked) return true;
            var pref = settings.AdapterPreferences.FirstOrDefault(p => p.AdapterId == a.Id);
            return (pref?.IsVisible == true) || (settings.SelectedAdapterId == a.Id);
        }).ToList();

        // Update button text
        showAllBtn.Content = _showingAll ? "Filtered View" : "Show All / Reset";

        foreach (var adapter in displayAdapters)
        {
            var pref = settings.AdapterPreferences.FirstOrDefault(p => p.AdapterId == adapter.Id);
            bool isVisible = pref?.IsVisible ?? false;
            bool isSelected = settings.SelectedAdapterId == adapter.Id;

            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new CheckBox 
            { 
                IsChecked = isVisible, 
                VerticalAlignment = VerticalAlignment.Center, 
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Check to keep in quick list"
            };
            check.SetValue(Grid.ColumnProperty, 0);

            var label = new TextBlock
            {
                Text = $"{adapter.Name} ({adapter.Description})",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = $"{adapter.Name}\n{adapter.Description}\nMAC: {adapter.MacAddress}"
            };
            label.SetValue(Grid.ColumnProperty, 1);

            var radio = new RadioButton
            {
                Content = "Use",
                IsChecked = isSelected,
                GroupName = "AdapterSelect",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            radio.SetValue(Grid.ColumnProperty, 2);

            grid.Children.Add(check);
            grid.Children.Add(label);
            grid.Children.Add(radio);

            var row = new AdapterRow
            {
                AdapterId = adapter.Id,
                DisplayName = adapter.Name,
                VisibleCheck = check,
                SelectRadio = radio
            };

            radio.Checked += async (_, _) =>
            {
                await _vm.SelectAdapterAsync(adapter.Id);
            };

            check.Checked += async (_, _) => await UpdatePreferencesAsync(settings);
            check.Unchecked += async (_, _) => await UpdatePreferencesAsync(settings);

            _rows.Add(row);
            adapterList.Items.Add(grid);
        }
    }

    private async Task UpdatePreferencesAsync(AppSettings settings)
    {
        foreach (var row in _rows)
        {
            var pref = settings.AdapterPreferences.FirstOrDefault(p => p.AdapterId == row.AdapterId);
            if (pref is null)
            {
                pref = new AdapterPreference { AdapterId = row.AdapterId };
                settings.AdapterPreferences.Add(pref);
            }
            pref.IsVisible = row.VisibleCheck.IsChecked == true;
        }

        var settingsRepo = (Etherprof.Contracts.Interfaces.ISettingsRepository)
            typeof(MainViewModel).GetField("_settingsRepo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_vm)!;
        await settingsRepo.SaveAsync(settings);
    }

    private async void OnShowAll(object sender, RoutedEventArgs e)
    {
        _showingAll = !_showingAll;
        await LoadAdaptersAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
