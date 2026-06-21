using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MultiRDPManager.FreeRDP.Models;
using WpfColor = System.Windows.Media.Color;

namespace MultiRDPManager.FreeRDP.Converters
{
    /// <summary>
    /// 连接状态→颜色转换器
    /// Connected → #00b42a (绿色)
    /// Disconnected → #86909c (灰色)
    /// Error → #e84749 (红色)
    /// </summary>
    [ValueConversion(typeof(ConnectionStatus), typeof(SolidColorBrush))]
    public class ConnectionStatusToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush GreenBrush = FreezeBrush(new(WpfColor.FromRgb(0x00, 0xB4, 0x2A)));
        private static readonly SolidColorBrush GrayBrush = FreezeBrush(new(WpfColor.FromRgb(0x86, 0x90, 0x9C)));
        private static readonly SolidColorBrush RedBrush = FreezeBrush(new(WpfColor.FromRgb(0xE8, 0x47, 0x49)));
        private static readonly SolidColorBrush YellowBrush = FreezeBrush(new(WpfColor.FromRgb(0xFF, 0xA9, 0x40)));

        private static SolidColorBrush FreezeBrush(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ConnectionStatus status)
            {
                return status switch
                {
                    ConnectionStatus.Connected => GreenBrush,
                    ConnectionStatus.Error => RedBrush,
                    ConnectionStatus.Connecting => YellowBrush,
                    ConnectionStatus.Reconnecting => YellowBrush,
                    _ => GrayBrush
                };
            }
            return GrayBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
