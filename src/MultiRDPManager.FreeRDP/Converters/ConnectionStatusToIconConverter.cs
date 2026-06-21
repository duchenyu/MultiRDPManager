using System.Globalization;
using System.Windows.Data;
using MultiRDPManager.FreeRDP.Models;

namespace MultiRDPManager.FreeRDP.Converters
{
    /// <summary>
    /// 连接状态→图标文本转换器
    /// Connected → ● (绿)
    /// Disconnected → ○ (灰)
    /// Error → ✕ (红)
    /// </summary>
    [ValueConversion(typeof(ConnectionStatus), typeof(string))]
    public class ConnectionStatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ConnectionStatus status)
            {
                return status switch
                {
                    ConnectionStatus.Connected => "\u25CF",      // ●
                    ConnectionStatus.Connecting => "\u25D0",     // ◐
                    ConnectionStatus.Reconnecting => "\u25D1",   // ◑
                    ConnectionStatus.Disconnecting => "\u25D2",  // ◒
                    ConnectionStatus.Error => "\u2715",          // ✕
                    _ => "\u25CB"                                 // ○
                };
            }
            return "\u25CB"; // ○
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
