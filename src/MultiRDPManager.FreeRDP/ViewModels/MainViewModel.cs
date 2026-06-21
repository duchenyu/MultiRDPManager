using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using MultiRDPManager.FreeRDP.Models;

namespace MultiRDPManager.FreeRDP.ViewModels
{
    /// <summary>
    /// 主界面ViewModel，管理服务器列表、连接状态、群控模式及统计信息
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private ServerConnectionInfo? _selectedServer;
        private bool _isGroupControlActive;
        private int _onlineCount;
        private int _totalCount;
        private string _statusBarText = "Ready";
        private bool _canConnect = true;
        private string _searchText = string.Empty;

        // ─── 事件：通知UI执行实际操作 ───
        public event EventHandler<ServerConnectionInfo>? ConnectServerRequested;
        public event EventHandler<ServerConnectionInfo>? DisconnectServerRequested;
        public event EventHandler? ConnectAllRequested;
        public event EventHandler? DisconnectAllRequested;
        public event EventHandler? OpenAddServerDialogRequested;
        public event EventHandler? OpenImportCsvDialogRequested;
        public event EventHandler<ServerConnectionInfo>? DeleteServerRequested;
        public event EventHandler? ToggleGroupControlRequested;

        // ─── 属性 ───

        /// <summary>
        /// 服务器列表
        /// </summary>
        public ObservableCollection<ServerConnectionInfo> Servers { get; } = new();

        /// <summary>
        /// 已连接会话列表
        /// </summary>
        public ObservableCollection<RdpSession> ActiveSessions { get; } = new();

        /// <summary>
        /// 当前选中的服务器
        /// </summary>
        public ServerConnectionInfo? SelectedServer
        {
            get => _selectedServer;
            set
            {
                if (SetProperty(ref _selectedServer, value))
                {
                    OnPropertyChanged(nameof(HasSelectedServer));
                    OnPropertyChanged(nameof(SelectedServerDisplay));
                }
            }
        }

        /// <summary>
        /// 是否有选中的服务器
        /// </summary>
        public bool HasSelectedServer => SelectedServer != null;

        /// <summary>
        /// 选中服务器显示文本
        /// </summary>
        public string SelectedServerDisplay => SelectedServer != null
            ? $"{SelectedServer.Name} ({SelectedServer.IpAddress}:{SelectedServer.Port})"
            : "(None selected)";

        /// <summary>
        /// 是否启用群控模式
        /// </summary>
        public bool IsGroupControlActive
        {
            get => _isGroupControlActive;
            set
            {
                if (SetProperty(ref _isGroupControlActive, value))
                {
                    OnPropertyChanged(nameof(GroupControlStatusText));
                    UpdateStatusBarText();
                }
            }
        }

        /// <summary>
        /// 在线服务器数量
        /// </summary>
        public int OnlineCount
        {
            get => _onlineCount;
            set => SetProperty(ref _onlineCount, value);
        }

        /// <summary>
        /// 服务器总数
        /// </summary>
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        /// <summary>
        /// 状态栏文本
        /// </summary>
        public string StatusBarText
        {
            get => _statusBarText;
            set => SetProperty(ref _statusBarText, value);
        }

        /// <summary>
        /// 缩略面板搜索关键词（用于按名称/IP筛选会话）
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
            }
        }

        /// <summary>
        /// 群控状态文本
        /// </summary>
        public string GroupControlStatusText => _isGroupControlActive ? "Group control active" : "Group control disabled";

        /// <summary>
        /// 是否能连接（防止并发操作）
        /// </summary>
        public bool CanConnect
        {
            get => _canConnect;
            set => SetProperty(ref _canConnect, value);
        }

        // ─── 命令 ───

        public ICommand AddServerCommand { get; }
        public ICommand ImportCsvCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ConnectAllCommand { get; }
        public ICommand DisconnectAllCommand { get; }
        public ICommand DeleteServerCommand { get; }
        public ICommand ToggleGroupControlCommand { get; }

        public MainViewModel()
        {
            AddServerCommand = new RelayCommand(OnAddServer);
            ImportCsvCommand = new RelayCommand(OnImportCsv);
            ConnectCommand = new RelayCommand(OnConnect, _ => HasSelectedServer);
            DisconnectCommand = new RelayCommand(OnDisconnect, _ => HasSelectedServer);
            ConnectAllCommand = new RelayCommand(OnConnectAll, _ => Servers.Count > 0);
            DisconnectAllCommand = new RelayCommand(OnDisconnectAll);
            DeleteServerCommand = new RelayCommand(OnDeleteServer, _ => HasSelectedServer);
            ToggleGroupControlCommand = new RelayCommand(OnToggleGroupControl);

            Servers.CollectionChanged += (_, _) =>
            {
                TotalCount = Servers.Count;
                CommandManager.InvalidateRequerySuggested();
            };
        }

        // ─── 命令处理 ───

        private void OnAddServer(object? parameter)
        {
            OpenAddServerDialogRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnImportCsv(object? parameter)
        {
            OpenImportCsvDialogRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnConnect(object? parameter)
        {
            if (SelectedServer != null)
            {
                ConnectServerRequested?.Invoke(this, SelectedServer);
            }
        }

        private void OnDisconnect(object? parameter)
        {
            if (SelectedServer != null)
            {
                DisconnectServerRequested?.Invoke(this, SelectedServer);
            }
        }

        private void OnConnectAll(object? parameter)
        {
            ConnectAllRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnDisconnectAll(object? parameter)
        {
            DisconnectAllRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnDeleteServer(object? parameter)
        {
            if (SelectedServer != null)
            {
                DeleteServerRequested?.Invoke(this, SelectedServer);
            }
        }

        private void OnToggleGroupControl(object? parameter)
        {
            if (IsGroupControlActive)
            {
                // 关闭群控：直接关
                IsGroupControlActive = false;
            }
            else
            {
                // 开启群控：通知 UI 处理勾选检查 + 主控选择对话框
                ToggleGroupControlRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        // ─── 公开方法 ───

        /// <summary>
        /// 刷新在线统计
        /// </summary>
        public void RefreshStatistics()
        {
            OnlineCount = ActiveSessions.Count(s =>
                s.Status == ConnectionStatus.Connected ||
                s.Status == ConnectionStatus.Connecting);
            TotalCount = Servers.Count;
            UpdateStatusBarText();
        }

        /// <summary>
        /// 根据服务器ID查找会话
        /// </summary>
        public RdpSession? FindSession(string serverId)
        {
            return ActiveSessions.FirstOrDefault(s => s.ServerId == serverId);
        }

        /// <summary>
        /// 根据服务器ID查找会话（已连接状态）
        /// </summary>
        public RdpSession? FindConnectedSession(string serverId)
        {
            return ActiveSessions.FirstOrDefault(s =>
                s.ServerId == serverId && s.Status == ConnectionStatus.Connected);
        }

        /// <summary>
        /// 获取所有已连接的会话（用于群控广播）
        /// </summary>
        public List<RdpSession> GetConnectedSessions()
        {
            return ActiveSessions.Where(s => s.Status == ConnectionStatus.Connected).ToList();
        }

        private void UpdateStatusBarText()
        {
            if (_isGroupControlActive)
            {
                StatusBarText = $"Group control active | Online: {OnlineCount}/{TotalCount}";
            }
            else
            {
                StatusBarText = $"Ready | Online: {OnlineCount}/{TotalCount}";
            }
        }

        // ─── 服务器持久化 ───

        private static readonly string _serversDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiRDPManager");
        private static readonly string _serversFile = Path.Combine(_serversDir, "servers.json");

        /// <summary>
        /// 保存服务器列表到 %APPDATA%/MultiRDPManager/servers.json
        /// </summary>
        public void SaveServers()
        {
            try
            {
                Directory.CreateDirectory(_serversDir);
                var data = Servers.Select(s => new ServerData
                {
                    Name = s.Name,
                    IpAddress = s.IpAddress,
                    Port = s.Port,
                    Username = s.Username,
                    Password = s.Password
                }).ToList();
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_serversFile, json);
            }
            catch { }
        }

        /// <summary>
        /// 加载服务器列表，返回已恢复的 ServerConnectionInfo 列表
        /// </summary>
        public List<ServerConnectionInfo> LoadServers()
        {
            try
            {
                if (!File.Exists(_serversFile)) return new();
                string json = File.ReadAllText(_serversFile);
                var data = JsonSerializer.Deserialize<List<ServerData>>(json);
                if (data == null) return new();
                return data.Select(d => new ServerConnectionInfo
                {
                    Name = d.Name,
                    IpAddress = d.IpAddress,
                    Port = d.Port,
                    Username = d.Username,
                    Password = d.Password,
                    ConnectionStatus = Models.ConnectionStatus.Disconnected
                }).ToList();
            }
            catch { return new(); }
        }

        /// <summary>
        /// 序列化辅助类（不含连接状态等运行时数据）
        /// </summary>
        private class ServerData
        {
            public string Name { get; set; } = "";
            public string IpAddress { get; set; } = "";
            public int Port { get; set; } = 3389;
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }
    }
}
