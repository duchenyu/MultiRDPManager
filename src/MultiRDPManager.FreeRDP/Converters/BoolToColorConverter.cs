using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace MultiRDPManager.FreeRDP.Converters
{
    [ValueConversion(typeof(bool), typeof(SolidColorBrush))]
    public class BoolToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush GreenBrush = FreezeBrush(new(WpfColor.FromRgb(0x00, 0xB4, 0x2A)));
        private static readonly SolidColorBrush GrayBrush = FreezeBrush(new(WpfColor.FromRgb(0x86, 0x90, 0x9C)));

        private static SolidColorBrush FreezeBrush(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? GreenBrush : GrayBrush;
            return GrayBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush && brush.Color == GreenBrush.Color)
                return true;
            return false;
        }
    }
}
