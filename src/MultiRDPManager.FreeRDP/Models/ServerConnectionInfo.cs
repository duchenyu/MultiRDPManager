using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MultiRDPManager.FreeRDP.Models
{
    /// <summary>
    /// 服务器连接信息数据模型（简化版，不需要DPAPI加密密码）
    /// </summary>
    public class ServerConnectionInfo : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _name = string.Empty;
        private string _ipAddress = string.Empty;
        private int _port = 3389;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 唯一标识
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 服务器名称
        /// </summary>
        public string Name
        {
            get => string.IsNullOrWhiteSpace(_name) ? _ipAddress : _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (SetProperty(ref _ipAddress, value))
                    OnPropertyChanged(nameof(Name));
            }
        }

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        /// <summary>
        /// 密码（明文存储，仅内存中使用）
        /// </summary>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        /// <summary>
        /// 连接状态（运行时属性）
        /// </summary>
        public ConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        /// <summary>
        /// 完整显示地址（IP:Port）
        /// </summary>
        public string AddressDisplay => $"{IpAddress}:{Port}";

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
