using System.Windows;
using System.Windows.Controls;
using Etherprof.Contracts.Models;
using Etherprof.Core;

namespace Etherprof.App.Views;

public partial class TestSetEditorDialog : Window
{
    private readonly TestSet? _existing;
    private readonly List<TargetRow> _rows = new();
    public TestSet? Result { get; private set; }

    private sealed class TargetRow
    {
        public TextBox NameBox { get; init; } = null!;
        public TextBox HostBox { get; init; } = null!;
        public Button CopyBtn { get; init; } = null!;
        public Button PasteBtn { get; init; } = null!;
        public Button RemoveBtn { get; init; } = null!;
        public Grid Container { get; init; } = null!;
    }

    public TestSetEditorDialog(TestSet? existing)
    {
        InitializeComponent();
        _existing = existing;

        if (existing is not null)
        {
            Title = "Edit Test Set";
            nameBox.Text = existing.Name;
            foreach (var t in existing.Targets)
                AddTargetRow(t.Name, t.Host);
        }
        else
        {
            Title = "New Test Set";
            AddTargetRow("", "");
        }
    }

    private TargetRow AddTargetRow(string name, string host)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBox = new TextBox { Text = name, Margin = new Thickness(0, 0, 4, 0) };
        nameBox.SetValue(Grid.ColumnProperty, 0);

        var hostBox = new TextBox { Text = host, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 0, 4, 0) };
        hostBox.SetValue(Grid.ColumnProperty, 1);

        var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        actionsPanel.SetValue(Grid.ColumnProperty, 2);

        var copyBtn = new Button { Content = "⧉", Width = 24, Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 2, 0), ToolTip = "Copy target & edit last byte" };
        var pasteBtn = new Button { Content = "📋", Width = 24, Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 2, 0), ToolTip = "Paste IP from clipboard" };
        var removeBtn = new Button { Content = "✕", Width = 24, Height = 24, FontSize = 10, ToolTip = "Remove target" };

        actionsPanel.Children.Add(copyBtn);
        actionsPanel.Children.Add(pasteBtn);
        actionsPanel.Children.Add(removeBtn);

        grid.Children.Add(nameBox);
        grid.Children.Add(hostBox);
        grid.Children.Add(actionsPanel);

        var row = new TargetRow
        {
            NameBox = nameBox,
            HostBox = hostBox,
            CopyBtn = copyBtn,
            PasteBtn = pasteBtn,
            RemoveBtn = removeBtn,
            Container = grid
        };

        copyBtn.Click += (_, _) =>
        {
            var newRow = AddTargetRow(row.NameBox.Text, row.HostBox.Text);
            FocusLastByte(newRow.HostBox);
        };

        pasteBtn.Click += (_, _) =>
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    row.HostBox.Text = text;
                    FocusLastByte(row.HostBox);
                }
            }
        };

        removeBtn.Click += (_, _) => RemoveTargetRow(row);
        _rows.Add(row);

        targetsList.Items.Add(grid);
        return row;
    }

    private void FocusLastByte(TextBox hostBox)
    {
        hostBox.Focus();
        string text = hostBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        int lastDot = text.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < text.Length - 1)
        {
            // Select the younger byte (after the last dot)
            hostBox.Select(lastDot + 1, text.Length - (lastDot + 1));
        }
        else
        {
            hostBox.SelectAll();
        }
    }

    private void RemoveTargetRow(TargetRow row)
    {
        if (_rows.Count <= 1) return; // keep at least one
        _rows.Remove(row);
        targetsList.Items.Remove(row.Container);
    }

    private void OnAddTarget(object sender, RoutedEventArgs e)
    {
        var row = AddTargetRow("", "");
        row.HostBox.Focus();
    }

    private void OnPasteTarget(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText()) return;
        string text = Clipboard.GetText().Trim();
        if (string.IsNullOrEmpty(text)) return;

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        TargetRow? lastAdded = null;
        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                lastAdded = AddTargetRow("", line);
            }
        }
        if (lastAdded != null)
        {
            FocusLastByte(lastAdded.HostBox);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        errorText.Text = "";

        var targets = new List<TestTarget>();
        foreach (var row in _rows)
        {
            var tName = row.NameBox.Text.Trim();
            var tHost = row.HostBox.Text.Trim();

            if (string.IsNullOrEmpty(tName) && string.IsNullOrEmpty(tHost))
                continue; // skip empty rows

            if (string.IsNullOrEmpty(tHost))
            {
                errorText.Text = $"Target '{tName}' has no host";
                return;
            }

            if (!IPv4Validator.IsValidHostOrAddress(tHost))
            {
                errorText.Text = $"Invalid host: '{tHost}'";
                return;
            }

            targets.Add(new TestTarget
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrEmpty(tName) ? tHost : tName,
                Host = tHost
            });
        }

        if (targets.Count == 0)
        {
            errorText.Text = "At least one target is required";
            return;
        }

        var name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            // Allow save without label: default to first host or summary
            name = targets.Count == 1 ? targets[0].Host : $"{targets[0].Host} +{targets.Count - 1}";
        }

        Result = new TestSet
        {
            Id = _existing?.Id ?? Guid.NewGuid(),
            Name = name,
            Targets = targets,
            SortOrder = _existing?.SortOrder ?? 0
        };

        DialogResult = true;
    }
}
