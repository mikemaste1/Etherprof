using System.Globalization;
using System.Windows.Data;
using Etherprof.Contracts.Models;

namespace Etherprof.App.Converters;

public sealed class TestStatusToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is TestStatus status ? status switch
        {
            TestStatus.Success => "●",
            TestStatus.Failure => "✕",
            TestStatus.Testing => "◌",
            TestStatus.Untested => "○",
            _ => "?"
        } : "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
