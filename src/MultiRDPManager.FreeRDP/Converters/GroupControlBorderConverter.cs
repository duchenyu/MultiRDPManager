using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace MultiRDPManager.FreeRDP.Converters
{
    [ValueConversion(typeof(bool), typeof(SolidColorBrush))]
    public class GroupControlBorderConverter : IValueConverter
    {
        private static readonly SolidColorBrush ActiveBorderBrush = FreezeBrush(new(WpfColor.FromRgb(0xFF, 0x4D, 0x4F)));
        private static readonly SolidColorBrush InactiveBrush = FreezeBrush(new(Colors.Transparent));

        private static SolidColorBrush FreezeBrush(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
                return ActiveBorderBrush;
            return InactiveBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color == ActiveBorderBrush.Color;
            return false;
        }
    }
}
