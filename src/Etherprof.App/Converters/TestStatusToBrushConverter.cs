using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Etherprof.Contracts.Models;

namespace Etherprof.App.Converters;

public sealed class TestStatusToBrushConverter : IValueConverter
{
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush FailureBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush TestingBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x7F, 0x17));
    private static readonly Brush UntestedBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is TestStatus status ? status switch
        {
            TestStatus.Success => SuccessBrush,
            TestStatus.Failure => FailureBrush,
            TestStatus.Testing => TestingBrush,
            _ => UntestedBrush
        } : UntestedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
