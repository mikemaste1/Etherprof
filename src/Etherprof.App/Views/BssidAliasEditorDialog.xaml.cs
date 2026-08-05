namespace Etherprof.App.Views;

using System.Windows;
using Etherprof.App.ViewModels;

public partial class BssidAliasEditorDialog : Window
{
    public BssidAliasEditorDialog(BssidAliasEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = result =>
        {
            DialogResult = result;
            Close();
        };

        Loaded += (_, _) =>
        {
            aliasTextBox.Focus();
            aliasTextBox.SelectAll();
        };
    }
}
