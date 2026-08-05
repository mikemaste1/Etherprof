using System.Globalization;
using System.Windows.Data;
using Etherprof.App.ViewModels;

namespace Etherprof.App.Converters;

public sealed class ProfileStateToIndicatorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ProfileState state ? state switch
        {
            ProfileState.Active => "● ",
            ProfileState.Applying => "",
            ProfileState.Failed => "✕ ",
            _ => ""
        } : "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
