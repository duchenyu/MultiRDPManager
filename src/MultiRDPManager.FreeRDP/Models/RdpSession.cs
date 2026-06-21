using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoyalApps.Community.FreeRdp.WinForms;

namespace MultiRDPManager.FreeRDP.Models
{
    /// <summary>
    /// 表示一个RDP连接会话，包含FreeRdpControl实例和WindowsFormsHost实例
    /// 实现INotifyPropertyChanged以支持WPF数据绑定
    /// </summary>
    public class RdpSession : INotifyPropertyChanged
    {
        private ConnectionStatus _status = ConnectionStatus.Disconnected;
        private bool _isMaster;
        private int _remoteWidth;
        private int _remoteHeight;
        private string _serverId = string.Empty;
        private string _serverName = string.Empty;
        private string _ipAddress = string.Empty;
        private int _port = 3389;

        /// <summary>
        /// 对应的服务器ID
        /// </summary>
        public string ServerId
        {
            get => _serverId;
            set => SetProperty(ref _serverId, value);
        }

        /// <summary>
        /// 服务器名称
        /// </summary>
        public string ServerName
        {
            get => _serverName;
            set => SetProperty(ref _serverName, value);
        }

        /// <summary>
        /// 该会话的IP地址
        /// </summary>
        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        /// <summary>
        /// 该会话的端口
        /// </summary>
        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        /// <summary>
        /// 是否为群控主控
        /// </summary>
        public bool IsMaster
        {
            get => _isMaster;
            set
            {
                if (SetProperty(ref _isMaster, value))
                {
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 当前连接状态
        /// </summary>
        public ConnectionStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// FreeRDP控件实例（WinForms）
        /// 由独立的WindowsFormsHost承载，保持wfreerdp持续渲染
        /// </summary>
        public FreeRdpControl? FreeRdpControl { get; set; }

        /// <summary>
        /// 承载FreeRdpControl的WindowsFormsHost（独立会话用）
        /// 所有会话的Host存放在隐藏容器SessionHostsContainer中
        /// 选中时移动到MainPreviewContent显示
        /// </summary>
        public WindowsFormsHost? Host { get; set; }

        private ImageSource? _thumbnailSource;

        /// <summary>
        /// wfreerdp.exe 的渲染子窗口（PrintWindow截图目标）
        /// 通过 FindWindowEx(FreeRdpControl.Handle, ...) 找到 _renderTarget（一层深度）
        /// </summary>
        public IntPtr WfreerdpHwnd { get; set; } = IntPtr.Zero;

        /// <summary>
        /// wfreerdp.exe 的真实子窗口句柄（PostMessage输入转发目标）
        /// 通过 FindWindowEx(_renderTarget.Handle, ...) 找到（两层深度）
        /// 仅用于群控输入转发，可能为空（wfreerdp尚未创建子窗口）
        /// </summary>
        public IntPtr RdpInputHwnd { get; set; } = IntPtr.Zero;

        /// <summary>
        /// 是否正在重连中（窗口缩放触发的重连，不执行CleanupSession）
        /// </summary>
        public bool IsReconnecting { get; set; }

        /// <summary>
        /// 缩略图预览（由定时器每500ms更新，绑定到缩略面板的Image控件）
        /// 仅存内存Bitmap，每次覆盖，不落盘
        /// </summary>
        public ImageSource? ThumbnailSource
        {
            get => _thumbnailSource;
            set => SetProperty(ref _thumbnailSource, value);
        }

        /// <summary>
        /// 远程桌面宽度
        /// </summary>
        public int RemoteWidth
        {
            get => _remoteWidth;
            set => SetProperty(ref _remoteWidth, value);
        }

        /// <summary>
        /// 远程桌面高度
        /// </summary>
        public int RemoteHeight
        {
            get => _remoteHeight;
            set => SetProperty(ref _remoteHeight, value);
        }

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event EventHandler? StatusChanged;

        private bool _isGroupControlSelected;

        /// <summary>
        /// 是否已勾选参与群控（缩略面板中的CheckBox）
        /// </summary>
        public bool IsGroupControlSelected
        {
            get => _isGroupControlSelected;
            set => SetProperty(ref _isGroupControlSelected, value);
        }

        /// <summary>
        /// 属性变更通知
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 释放会话资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (FreeRdpControl != null)
                {
                    try
                    {
                        FreeRdpControl.Disconnect();
                    }
                    catch
                    {
                        // 忽略断开时的异常
                    }

                    FreeRdpControl.Dispose();
                    FreeRdpControl = null;
                }

                if (Host != null)
                {
                    Host.Child = null;
                    Host.Dispose();
                    Host = null;
                }
            }
            catch
            {
                // 忽略释放时的异常
            }
        }

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
