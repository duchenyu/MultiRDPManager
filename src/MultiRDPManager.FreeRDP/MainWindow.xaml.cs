using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using MultiRDPManager.FreeRDP.Models;
using MultiRDPManager.FreeRDP.Native;
using MultiRDPManager.FreeRDP.Services;
using MultiRDPManager.FreeRDP.ViewModels;
using MultiRDPManager.FreeRDP.Views.Dialogs;
using RoyalApps.Community.FreeRdp.WinForms;
using WindowsFormsHost = System.Windows.Forms.Integration.WindowsFormsHost;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MultiRDPManager.FreeRDP
{
    /// <summary>
    /// 主窗口 — 使用FreeRDP的RDP多会话管理器
    /// 三栏布局：左侧服务器列表 | 中间主预览区 | 右侧缩略面板
    /// 每个RDP连接对应一个独立的FreeRdpControl实例，通过WindowsFormsHost嵌入WPF
    /// 主预览区通过ScrollViewer支持大分辨率RDP画面拖动查看
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;

        /// <summary>
        /// 所有活跃会话的字典（ServerId → RdpSession）
        /// </summary>
        private readonly Dictionary<string, RdpSession> _sessions = new();

        /// <summary>
        /// 当前在主预览区显示的会话
        /// </summary>
        private RdpSession? _activePreviewSession;

        private bool _isFullScreen;
        private WindowStyle _previousWindowStyle;
        private ResizeMode _previousResizeMode;
        private WindowState _previousWindowState;

        /// <summary>
        /// 当前RDP分辨率（自适应窗口大小）
        /// </summary>
        private int _resolutionWidth = 1920;
        private int _resolutionHeight = 1080;

        /// <summary>
        /// 缩略面板的筛选视图（基于 ActiveSessions 的 ICollectionView）
        /// 按 SearchText 过滤，SelectAll 只作用于可见项
        /// </summary>
        private ICollectionView? _sessionView;

        /// <summary>
        /// SizeChanged 节流定时器 — 拖窗口时等用户停下来 200ms 才执行重连
        /// </summary>
        private readonly DispatcherTimer _resizeDebounceTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(200),
            IsEnabled = false
        };

        /// <summary>
        /// 缩略图捕获定时器（1000ms间隔，UI线程安全）
        /// 每1000ms截取所有已连接会话的wfreerdp窗口内容并更新缩略图
        /// </summary>
        private readonly DispatcherTimer _thumbnailTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(1000),
            IsEnabled = false
        };

        /// <summary>
        /// 隐藏的 off-screen 窗口，承载所有非预览状态的 WindowsFormsHost
        /// 避免 HWND 穿透遮挡主窗口 UI
        /// </summary>
        private readonly Window _offScreenWindow;

        /// <summary>
        /// off-screen 窗口内的容器
        /// </summary>
        private readonly Canvas _offScreenContainer;

        /// <summary>
        /// 群控输入钩子（WH_MOUSE_LL / WH_KEYBOARD_LL）
        /// 拦截主控窗口的输入并转发到所有从机 wfreerdp 窗口
        /// </summary>
        private readonly GroupControlHook _groupControlHook = new();

        /// <summary>
        /// HwndSource 引用，用于窗口关闭时移除钩子
        /// </summary>
        private HwndSource? _hwndSource;

        /// <summary>
        /// 缩略图缓存池 — 复用 Bitmap 对象以减少 GDI 分配
        /// </summary>
        private readonly ConditionalWeakTable<RdpSession, ThumbnailCache> _thumbnailCache = new();

        /// <summary>
        /// 缩略图缓存条目
        /// </summary>
        private class ThumbnailCache
        {
            public Bitmap? Bitmap;
            public Bitmap? ThumbBitmap;
            public Graphics? ThumbGraphics;
            public int LastWidth;
            public int LastHeight;
        }

        /// <summary>
        /// _sessions 字典的读写锁（跨线程安全）
        /// </summary>
        private readonly ReaderWriterLockSlim _sessionsLock = new();

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // 将所有 WPF 渲染切换到软件模式（GDI），与 FreeRDP(GDI) 同层渲染
            // 避免 DirectX+GDI 交替绘制导致的画面撕裂
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            // 创建 off-screen 窗口，用于存放非预览状态的 WindowsFormsHost
            // 窗口放在虚拟屏幕右边缘之外，HWND坐标有效，PostMessage可以正确定位
            _offScreenContainer = new Canvas();

            // 修复：使用 SM_XVIRTUALSCREEN + SM_CXVIRTUALSCREEN 而非仅 SM_CXVIRTUALSCREEN
            // 多显示器配置中主显示器不一定在坐标原点 (0,0)
            int virtualLeft = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
            int virtualWidth = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
            int offX = virtualLeft + virtualWidth;

            _offScreenWindow = new Window
            {
                Width = 1920,     // 跟host分辨率一致
                Height = 1080,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Left = offX,      // 紧贴虚拟桌面右边缘（用户看不到）
                Top = 0,
                Content = _offScreenContainer
            };
            _offScreenWindow.Show();

            // 初始化缩略图捕获定时器
            _thumbnailTimer.Tick += OnCaptureThumbnails;

            // 初始化 SizeChanged 节流定时器
            _resizeDebounceTimer.Tick += OnResizeDebounceElapsed;

            Loaded += OnLoaded;
            // 注意：不再在 Closed 事件中订阅 HandleWindowClosed，
            // OnClosed 重写（L1050）已处理清理逻辑，避免双调用
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            // 服务器列表变化时自动保存 — 在 Add 循环之前注册，避免重复写入
            _viewModel.Servers.CollectionChanged += (_, _) => _viewModel.SaveServers();

            // 加载已保存的服务器列表（Register 在先，循环 Add 在后 → 只触发一次全量 Save）
            var savedServers = _viewModel.LoadServers();
            foreach (var server in savedServers)
            {
                _viewModel.Servers.Add(server);
            }

            // 设置群控钩子的主窗口句柄 & 钩住自定义消息
            // （在 Loaded 中执行，确保窗口完全初始化）
            _groupControlHook.MainWindowHwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_groupControlHook.MainWindowHwnd);
            if (_hwndSource != null)
                _hwndSource.AddHook(WndProc);

            // 订阅ViewModel事件
            _viewModel.ConnectServerRequested += OnConnectServerRequested;
            _viewModel.DisconnectServerRequested += OnDisconnectServerRequested;
            _viewModel.ConnectAllRequested += OnConnectAllRequested;
            _viewModel.DisconnectAllRequested += OnDisconnectAllRequested;
            _viewModel.OpenAddServerDialogRequested += OnOpenAddServerDialogRequested;
            _viewModel.OpenImportCsvDialogRequested += OnOpenImportCsvDialogRequested;
            _viewModel.DeleteServerRequested += OnDeleteServerRequested;
            _viewModel.ToggleGroupControlRequested += OnToggleGroupControlRequested;

            // 监听主预览区尺寸变化，用于自适应分辨率
            MainPreviewContent.SizeChanged += OnMainPreviewContentSizeChanged;

            // 设置缩略面板筛选视图（按名称/IP过滤已连接会话）
            _sessionView = CollectionViewSource.GetDefaultView(_viewModel.ActiveSessions);
            _sessionView.Filter = o =>
            {
                if (o is not RdpSession session) return false;
                if (string.IsNullOrWhiteSpace(_viewModel.SearchText)) return true;
                return session.ServerName.Contains(_viewModel.SearchText, StringComparison.OrdinalIgnoreCase)
                    || session.IpAddress.Contains(_viewModel.SearchText, StringComparison.OrdinalIgnoreCase);
            };
            ThumbnailPanelItems.ItemsSource = _sessionView;

            // 监听 ViewModel 属性变化（存储委托引用以便后续取消订阅）
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        /// <summary>
        /// ViewModel 属性变化处理（分离为命名方法以便取消订阅）
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 群控模式切换
            if (e.PropertyName == nameof(MainViewModel.IsGroupControlActive))
                OnGroupControlToggled();
            // 搜索文本变化 → 刷新筛选
            if (e.PropertyName == nameof(MainViewModel.SearchText))
                _sessionView?.Refresh();
        }

        #region ─── 连接管理 ───

        /// <summary>
        /// 连接指定服务器
        /// </summary>
        private void OnConnectServerRequested(object? sender, ServerConnectionInfo server)
        {
            ConnectToServer(server);
        }

        /// <summary>
        /// 连接到单个服务器（使用全局固定分辨率）
        /// 连接后FreeRdpControl保存在会话中，显示时将会话的WindowsFormsHost从隐藏容器移到主预览区
        /// </summary>
        private void ConnectToServer(ServerConnectionInfo server)
        {
            // 检查是否已经连接
            bool exists;
            _sessionsLock.EnterReadLock();
            try { exists = _sessions.ContainsKey(server.Id); }
            finally { _sessionsLock.ExitReadLock(); }

            if (exists)
            {
                ShowInfo($"Server {server.Name} is already connected");
                return;
            }

            try
            {
                server.ConnectionStatus = ConnectionStatus.Connecting;
                UpdateStatusBar($"Connecting to {server.Name} ({server.IpAddress}:{server.Port})...");

                // 创建FreeRdpControl（之后由独立的WindowsFormsHost承载）
                var rdpControl = new FreeRdpControl
                {
                    Dock = System.Windows.Forms.DockStyle.Fill
                };

                // 配置连接 — 使用主预览区当前尺寸作为RDP分辨率（自适应）
                // 如果 MainPreviewContent 尚未布局完成，使用默认值
                int rdpWidth = _resolutionWidth;
                int rdpHeight = _resolutionHeight;
                if (MainPreviewContent.ActualWidth > 100 && MainPreviewContent.ActualHeight > 100)
                {
                    rdpWidth = (int)MainPreviewContent.ActualWidth;
                    rdpHeight = (int)MainPreviewContent.ActualHeight;
                    _resolutionWidth = rdpWidth;
                    _resolutionHeight = rdpHeight;
                }
                rdpControl.Configuration.DesktopWidth = rdpWidth;
                rdpControl.Configuration.DesktopHeight = rdpHeight;
                rdpControl.Configuration.SmartReconnect = false;

                rdpControl.Configuration.Server = server.IpAddress;
                rdpControl.Configuration.Port = server.Port;
                rdpControl.Configuration.Username = server.Username;
                rdpControl.Configuration.Password = server.Password;
                rdpControl.Configuration.Certificate.Ignore = true;

                // 创建独立的WindowsFormsHost（大尺寸，保持wfreerdp高分辨率渲染）
                // 所有Host存放在隐藏容器_offScreenContainer中
                // 缩略图截图时捕获高清画面 → 缩放到缩略图尺寸
                // 主预览区时清除固定尺寸让Host自适应填充
                var sessionHost = new WindowsFormsHost
                {
                    Child = rdpControl,
                    Width = 1920,
                    Height = 1080
                };
                _offScreenContainer.Children.Add(sessionHost);

                // 创建RdpSession对象
                var session = new RdpSession
                {
                    ServerId = server.Id,
                    ServerName = server.Name,
                    IpAddress = server.IpAddress,
                    Port = server.Port,
                    Status = ConnectionStatus.Connecting,
                    FreeRdpControl = rdpControl,
                    Host = sessionHost
                };

                // 添加事件处理
                rdpControl.Connected += (s, args) => OnSessionConnected(session);
                rdpControl.Disconnected += (s, args) => OnSessionDisconnected(session);
                rdpControl.CertificateError += (s, args) => args.Continue();

                // 注册会话
                _sessionsLock.EnterWriteLock();
                try { _sessions[server.Id] = session; }
                finally { _sessionsLock.ExitWriteLock(); }
                _viewModel?.ActiveSessions.Add(session);

                // 占位符在第一个会话连接后隐藏
                PlaceholderText.Visibility = Visibility.Collapsed;

                // 发起连接
                rdpControl.Connect();

                _viewModel?.RefreshStatistics();
            }
            catch (Exception ex)
            {
                ShowError($"Connection failed: {ex.Message}");
                HandleSessionError(server.Id);
            }
        }

        /// <summary>
        /// 连接成功回调 — 自动在主预览区显示新连接的会话
        /// </summary>
        private void OnSessionConnected(RdpSession session)
        {
            Dispatcher.BeginInvoke(() =>
            {
                session.Status = ConnectionStatus.Connected;

                // 更新服务器状态
                var server = _viewModel?.Servers.FirstOrDefault(s => s.Id == session.ServerId);
                if (server != null)
                {
                    server.ConnectionStatus = ConnectionStatus.Connected;
                }

                // 立即尝试查找 wfreerdp 子窗口句柄（重试机制，最多等2秒）
                TryFindWfreerdpHwnd(session);

                // 给 FreeRdpControl 加 WS_EX_COMPOSITED 双缓冲
                // 消除 wfreerdp 局部增量更新导致的残留画面撕裂
                if (session.FreeRdpControl?.Handle is IntPtr ctrlHwnd and not 0)
                {
                    uint exStyle = Win32.GetWindowLong(ctrlHwnd, Win32.GWL_EXSTYLE);
                    Win32.SetWindowLong(ctrlHwnd, Win32.GWL_EXSTYLE,
                        exStyle | Win32.WS_EX_COMPOSITED);
                    Win32.SetWindowPos(ctrlHwnd, IntPtr.Zero, 0, 0, 0, 0,
                        0x0001 | 0x0004 | 0x0020); // SWP_NOSIZE|SWP_NOMOVE|SWP_FRAMECHANGED
                }

                // 仅首次连接或没有活跃预览时自动显示
                // SmartReconnect 触发的重连不会篡改当前预览
                if (_activePreviewSession == null)
                {
                    ShowInMainPreview(session);
                }

                _viewModel?.RefreshStatistics();
                UpdateStatusBar($"Connected to {session.ServerName}");

                // 有连接成功且窗口可见时启动缩略图定时器
                if (!_thumbnailTimer.IsEnabled && _sessions.Count > 0
                    && IsVisible && WindowState != WindowState.Minimized)
                {
                    _thumbnailTimer.Start();
                }
            });
        }

        /// <summary>
        /// 断开连接回调
        /// </summary>
        private void OnSessionDisconnected(RdpSession session)
        {
            Dispatcher.BeginInvoke(() =>
            {
                // 窗口缩放触发的重连，不执行 CleanupSession
                if (session.IsReconnecting)
                {
                    session.Status = ConnectionStatus.Reconnecting;
                    return;
                }

                CleanupSession(session.ServerId, "Remote connection disconnected");
            });
        }

        /// <summary>
        /// 主预览区尺寸变化 → 节流后仅重连主控以适配新分辨率
        /// 200ms 节流避免拖窗口时每个像素都触发断连重连
        /// 只重连主控（从机不需要分辨率适配）
        /// </summary>
        private void OnMainPreviewContentSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            int newWidth = (int)e.NewSize.Width;
            int newHeight = (int)e.NewSize.Height;

            if (newWidth < 100 || newHeight < 100) return;
            if (newWidth == _resolutionWidth && newHeight == _resolutionHeight) return;

            _resolutionWidth = newWidth;
            _resolutionHeight = newHeight;

            // 重置节流定时器：每次 SizeChanged 都推迟 200ms
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        }

        /// <summary>
        /// 节流定时器触发 — 仅重连当前主控会话
        /// </summary>
        private void OnResizeDebounceElapsed(object? sender, EventArgs e)
        {
            _resizeDebounceTimer.Stop();

            if (_activePreviewSession == null) return;

            System.Diagnostics.Debug.WriteLine($"Resize complete: {_resolutionWidth}×{_resolutionHeight}");
            UpdateStatusBar($"Adjusting resolution to {_resolutionWidth}×{_resolutionHeight}...");

            // 窗口缩放时不再重连 RDP，由 FreeRDP 的 SmartSizing 缩放适配
            //ReconnectWithNewResolution(_activePreviewSession);

            UpdateStatusBar($"Resolution adjusted to {_resolutionWidth}×{_resolutionHeight}");
        }

        /// <summary>
        /// 断开指定服务器
        /// </summary>
        private void OnDisconnectServerRequested(object? sender, ServerConnectionInfo server)
        {
            DisconnectServer(server.Id);
        }

        private void DisconnectServer(string serverId)
        {
            RdpSession? session;
            _sessionsLock.EnterReadLock();
            try { _sessions.TryGetValue(serverId, out session); }
            finally { _sessionsLock.ExitReadLock(); }

            if (session == null) return;

            UpdateStatusBar($"Disconnecting {session.ServerName}...");

            try
            {
                if (session.FreeRdpControl != null)
                {
                    session.FreeRdpControl.Disconnect();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Disconnect error: {ex.Message}");
            }
            finally
            {
                CleanupSession(serverId, "Disconnected");
            }
        }

        /// <summary>
        /// 全部连接 — 使用当前全局分辨率
        /// </summary>
        private void OnConnectAllRequested(object? sender, EventArgs e)
        {
            if (_viewModel == null) return;

            foreach (var server in _viewModel.Servers)
            {
                bool exists;
                _sessionsLock.EnterReadLock();
                try { exists = _sessions.ContainsKey(server.Id); }
                finally { _sessionsLock.ExitReadLock(); }

                if (!exists)
                {
                    ConnectToServer(server);
                }
            }
        }

        /// <summary>
        /// 全部断开
        /// </summary>
        private void OnDisconnectAllRequested(object? sender, EventArgs e)
        {
            // 复制键列表，避免修改时迭代
            List<string> serverIds;
            _sessionsLock.EnterReadLock();
            try { serverIds = _sessions.Keys.ToList(); }
            finally { _sessionsLock.ExitReadLock(); }

            foreach (var serverId in serverIds)
            {
                DisconnectServer(serverId);
            }
        }

        /// <summary>
        /// 清理会话资源
        /// </summary>
        private void CleanupSession(string serverId, string statusMessage)
        {
            RdpSession? session;
            _sessionsLock.EnterReadLock();
            try { _sessions.TryGetValue(serverId, out session); }
            finally { _sessionsLock.ExitReadLock(); }

            if (session == null) return;

            session.Status = ConnectionStatus.Disconnected;

            // 更新服务器状态
            var server = _viewModel?.Servers.FirstOrDefault(s => s.Id == serverId);
            if (server != null)
            {
                server.ConnectionStatus = ConnectionStatus.Disconnected;
            }

            // 如果清理的是当前预览的会话，清空主预览区
            if (_activePreviewSession?.ServerId == serverId)
            {
                MainPreviewContent.Children.Clear();
                PlaceholderText.Visibility = Visibility.Visible;
                MainPreviewTitle.Text = "RDP Preview — Select a session";
                _activePreviewSession = null;
            }

            // 从隐藏容器移除Host（如果还在容器中）
            if (session.Host != null && _offScreenContainer.Children.Contains(session.Host))
            {
                _offScreenContainer.Children.Remove(session.Host);
            }

            // 从ActiveSessions移除（ItemsControl自动更新）
            _viewModel?.ActiveSessions.Remove(session);

            // 释放缩略图缓存
            if (_thumbnailCache.TryGetValue(session, out var cache))
            {
                cache.Bitmap?.Dispose();
                cache.ThumbBitmap?.Dispose();
                cache.ThumbGraphics?.Dispose();
                _thumbnailCache.Remove(session);
            }

            // 释放资源
            session.Dispose();

            // 从字典移除
            _sessionsLock.EnterWriteLock();
            try { _sessions.Remove(serverId); }
            finally { _sessionsLock.ExitWriteLock(); }

            // 如果没有活跃会话，显示占位符
            int sessionCount;
            _sessionsLock.EnterReadLock();
            try { sessionCount = _sessions.Count; }
            finally { _sessionsLock.ExitReadLock(); }

            if (sessionCount == 0)
            {
                PlaceholderText.Visibility = Visibility.Visible;
            }

            _viewModel?.RefreshStatistics();
            UpdateStatusBar(statusMessage);
        }

        /// <summary>
        /// 处理连接错误
        /// </summary>
        private void HandleSessionError(string serverId)
        {
            var server = _viewModel?.Servers.FirstOrDefault(s => s.Id == serverId);
            if (server != null)
            {
                server.ConnectionStatus = ConnectionStatus.Error;
            }

            RdpSession? session;
            _sessionsLock.EnterReadLock();
            try { _sessions.TryGetValue(serverId, out session); }
            finally { _sessionsLock.ExitReadLock(); }

            if (session != null)
            {
                session.Status = ConnectionStatus.Error;
                CleanupSession(serverId, "Connection failed");
            }
        }

        /// <summary>
        /// 删除服务器
        /// </summary>
        private void OnDeleteServerRequested(object? sender, ServerConnectionInfo server)
        {
            // 如果已连接，先断开
            bool exists;
            _sessionsLock.EnterReadLock();
            try { exists = _sessions.ContainsKey(server.Id); }
            finally { _sessionsLock.ExitReadLock(); }

            if (exists)
            {
                DisconnectServer(server.Id);
            }

            _viewModel?.Servers.Remove(server);
            _viewModel?.RefreshStatistics();
            UpdateStatusBar($"Deleted server {server.Name}");
        }

        #endregion

        #region ─── 主预览与缩略面板管理 ───

        /// <summary>
        /// 将指定会话的RDP画面显示到主预览区
        /// 通过将会话的WindowsFormsHost从隐藏容器移到MainPreviewContent实现
        /// </summary>
        private void ShowInMainPreview(RdpSession session)
        {
            if (session == null || session.Host == null) return;

            // 如果已经是当前预览的会话，不做操作
            if (_activePreviewSession != null &&
                _activePreviewSession.ServerId == session.ServerId)
            {
                return;
            }

            // 从隐藏容器移除旧的（如果之前显示了其他会话）
            if (_activePreviewSession != null && _activePreviewSession.Host != null)
            {
                // 把旧会话的Host移回隐藏容器，恢复小尺寸
                if (MainPreviewContent.Children.Contains(_activePreviewSession.Host))
                {
                    MainPreviewContent.Children.Remove(_activePreviewSession.Host);
                    _activePreviewSession.Host.Width = 1920;
                    _activePreviewSession.Host.Height = 1080;
                    _offScreenContainer.Children.Add(_activePreviewSession.Host);
                }
            }

            // 从隐藏容器取出新会话的Host，放入主预览区
            // 清除固定尺寸，让Host自适应填充MainPreviewContent
            _offScreenContainer.Children.Remove(session.Host);
            session.Host.Width = double.NaN;  // NaN = auto/stretch
            session.Host.Height = double.NaN;
            session.Host.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            session.Host.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            MainPreviewContent.Children.Clear();
            MainPreviewContent.Children.Add(session.Host);
            PlaceholderText.Visibility = Visibility.Collapsed;

            // 更新标题（远程分辨率 = 容器大小，由SmartReconnect自动适配）
            MainPreviewTitle.Text = $"RDP — {session.ServerName} ({session.IpAddress})";

            _activePreviewSession = session;

            // 群控开启时更新主控窗口目标
            if (_viewModel != null && _viewModel.IsGroupControlActive)
            {
                UpdateGroupControlHookTargets();
            }
        }

        /// <summary>
        /// 点击右侧缩略面板卡片 — 切换主预览区显示该会话的RDP画面
        /// </summary>
        private void OnThumbnailCardClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is RdpSession session)
            {
                ShowInMainPreview(session);
                SelectSession(session.ServerId);

                // 双击设置为主控（群控模式下）
                if (e.ClickCount == 2 && _viewModel != null && _viewModel.IsGroupControlActive)
                {
                    SetAsMaster(session.ServerId);
                }
            }
        }

        /// <summary>
        /// 设置会话为主控（群控模式下）
        /// </summary>
        private void SetAsMaster(string serverId)
        {
            if (_viewModel == null) return;

            foreach (var session in _viewModel.ActiveSessions)
            {
                session.IsMaster = (session.ServerId == serverId);
            }

            var masterSession = _viewModel.FindSession(serverId);
            if (masterSession != null)
            {
                UpdateStatusBar($"Master switched to: {masterSession.ServerName}");
            }

            // 群控开启时更新钩子目标窗口
            if (_viewModel.IsGroupControlActive)
            {
                UpdateGroupControlHookTargets();
            }
        }

        /// <summary>
        /// "全选"勾选 — 勾选所有当前可见（筛选后）的会话
        /// </summary>
        private void OnSelectAllChecked(object sender, RoutedEventArgs e)
        {
            if (_sessionView == null) return;
            foreach (RdpSession session in _sessionView)
                session.IsGroupControlSelected = true;
        }

        /// <summary>
        /// "全选"取消勾选 — 取消所有会话的勾选
        /// </summary>
        private void OnSelectAllUnchecked(object sender, RoutedEventArgs e)
        {
            if (_sessionView == null) return;
            foreach (RdpSession session in _sessionView)
                session.IsGroupControlSelected = false;
        }

        /// <summary>
        /// 选中会话（同步服务器列表选中状态）
        /// </summary>
        private void SelectSession(string serverId)
        {
            if (_viewModel == null) return;

            var server = _viewModel.Servers.FirstOrDefault(s => s.Id == serverId);
            if (server != null)
            {
                _viewModel.SelectedServer = server;
                ServerListView.SelectedItem = server;
            }
        }

        #endregion

        #region ─── 群控钩子（WH_MOUSE_LL / WH_KEYBOARD_LL） ───

        /// <summary>
        /// 群控开关请求 — 由 ViewModel 触发（点击群控按钮/菜单）
        /// 因为此时群控尚未开启（IsGroupControlActive=false），需要先做：
        /// 1. 检查是否有已勾选的服务器
        /// 2. 弹出对话框选择主控
        /// 3. 确认后设置主控并开启群控
        /// </summary>
        private void OnToggleGroupControlRequested(object? sender, EventArgs e)
        {
            if (_viewModel == null) return;

            // 获取已勾选且已连接的会话
            var selectedSessions = _viewModel.ActiveSessions
                .Where(s => s.IsGroupControlSelected && s.Status == ConnectionStatus.Connected)
                .ToList();

            if (selectedSessions.Count == 0)
            {
                System.Windows.MessageBox.Show(this,
                    "Please select servers to join group control in the thumbnail panel first",
                    "Group Control",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (selectedSessions.Count < 2)
            {
                System.Windows.MessageBox.Show(this,
                    "Group control requires at least 2 connected servers",
                    "Group Control",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // 弹出主控选择对话框
            var dialog = new Views.Dialogs.SelectMasterDialog(selectedSessions)
            {
                Owner = this,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                var masterSession = dialog.SelectedSession;
                if (masterSession == null) return;

                // 设置主控标记
                foreach (var session in _viewModel.ActiveSessions)
                {
                    session.IsMaster = (session.ServerId == masterSession.ServerId);
                }

                // 切换到主控会话为主预览
                var masterViewModelSession = _viewModel.FindConnectedSession(masterSession.ServerId);
                if (masterViewModelSession != null)
                {
                    ShowInMainPreview(masterViewModelSession);
                }

                // 正式开启群控
                _viewModel.IsGroupControlActive = true;
                // OnGroupControlToggled 会通过 PropertyChanged 自动触发
            }
        }
        private void OnGroupControlToggled()
        {
            if (_viewModel == null) return;

            if (_viewModel.IsGroupControlActive)
            {
                // ── 诊断: 转储所有会话的窗口层级 ──
                DumpWindowHierarchy();

                // 启动钩子前确保所有会话的 RdpInputHwnd 和 WfreerdpHwnd 已填充
                foreach (var session in _viewModel.ActiveSessions)
                {
                    if (session.WfreerdpHwnd == IntPtr.Zero && session.FreeRdpControl != null)
                        session.WfreerdpHwnd = FindWfreerdpWindow(session.FreeRdpControl);
                    if (session.RdpInputHwnd == IntPtr.Zero && session.FreeRdpControl != null)
                        session.RdpInputHwnd = FindWfreerdpInputWindow(session.FreeRdpControl);
                }

                // 启动钩子前先更新目标窗口列表
                UpdateGroupControlHookTargets();
                _groupControlHook.Start();

                // 发送 WM_SETFOCUS 到所有从机，激活 wfreerdp 输入通道
                // wfreerdp 失焦后会设置 SuspendInput=TRUE 静默丢弃输入
                _groupControlHook.WarmUpFocus();

                UpdateStatusBar("Group control started");
            }
            else
            {
                // 停止钩子
                _groupControlHook.Stop();
                UpdateStatusBar("Group control stopped");
            }
        }

        /// <summary>
        /// 诊断：转储所有会话的完整窗口层级（用于排查群控HWND定位问题）
        /// 输出格式示例：
        ///   [HIERARCHY] session=hk1 control=0x001507F0
        ///     level1: _renderTarget=0x001607E0
        ///       level2: child[0] class=FREERDP hwnd=0x001707D0  ← 输入目标
        /// </summary>
        private void DumpWindowHierarchy()
        {
            System.Text.StringBuilder sb = new();
            sb.AppendLine("=== 窗口层级诊断 ===");
            if (_viewModel == null) return;

            foreach (var session in _viewModel.ActiveSessions)
            {
                if (session.FreeRdpControl == null) continue;
                IntPtr controlHwnd = session.FreeRdpControl.Handle;
                sb.AppendLine($"  session={session.ServerName} control=0x{controlHwnd.ToInt64():X8}");

                // Level 1: _renderTarget
                IntPtr renderTargetHwnd = Win32.FindWindowEx(controlHwnd, IntPtr.Zero, null, null);
                if (renderTargetHwnd != IntPtr.Zero)
                {
                    var clsName1 = new System.Text.StringBuilder(256);
                    Win32.GetClassName(renderTargetHwnd, clsName1, 256);
                    sb.AppendLine($"    L1: _renderTarget=0x{renderTargetHwnd.ToInt64():X8} class={clsName1}");

                    // Level 2: enumerate children of _renderTarget
                    int childIndex = 0;
                    Win32.EnumChildWindows(renderTargetHwnd, (childHwnd, _) =>
                    {
                        var clsName2 = new System.Text.StringBuilder(256);
                        Win32.GetClassName(childHwnd, clsName2, 256);
                        string marker = clsName2.ToString().Equals("FREERDP", StringComparison.OrdinalIgnoreCase)
                            ? " ← 输入目标" : "";
                        sb.AppendLine($"      L2: child[{childIndex}]=0x{childHwnd.ToInt64():X8} class={clsName2}{marker}");
                        childIndex++;
                        return true;
                    }, IntPtr.Zero);

                    if (childIndex == 0)
                        sb.AppendLine($"      L2: (no children)");
                }
                else
                {
                    sb.AppendLine($"    L1: (no _renderTarget found)");
                }
            }
            sb.AppendLine("=== 诊断结束 ===");
            System.Diagnostics.Debug.WriteLine(sb.ToString());
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MultiRDPManager_DIAG.log"),
                sb.ToString());
        }

        /// <summary>
        /// 更新群控钩子的目标窗口（主控 + 从机列表）
        /// 主控 = 始终用当前预览的 FreeRdpControl.Handle（覆盖完整可视区域，确保鼠标边界检测正确）
        /// 从机 = 所有已连接且非主控的会话（用 RdpInputHwnd，SendMessage 直达 wfreerdp 子窗口）
        /// </summary>
        private void UpdateGroupControlHookTargets()
        {
            if (_viewModel == null) return;

            // ── 诊断日志 ──
            var diag = new System.Text.StringBuilder();
            diag.AppendLine($"[GROUPCTRL] UpdateGroupControlHookTargets");

            // 设置主控窗口 — 始终用当前可见预览会话的 FreeRdpControl.Handle
            // 因为鼠标点击永远发生在可视区域，不能用 off-screen 中的会话做边界检测
            if (_activePreviewSession != null && _activePreviewSession.FreeRdpControl != null)
            {
                _groupControlHook.MasterHwnd = _activePreviewSession.FreeRdpControl.Handle;
                diag.AppendLine($"  master=0x{_groupControlHook.MasterHwnd.ToInt64():X8} session={_activePreviewSession.ServerName}");
            }
            else
            {
                _groupControlHook.MasterHwnd = IntPtr.Zero;
                diag.AppendLine($"  master=IntPtr.Zero (no active preview)");
            }

            // 设置从机窗口列表 — 每次强制重找 RdpInputHwnd（wfreerdp 重连会重建子窗口）
            var slaveHwnds = new List<IntPtr>();
            foreach (var session in _viewModel.ActiveSessions)
            {
                // 预览会话即主控，不加入从机列表
                if (_activePreviewSession != null && session.ServerId == _activePreviewSession.ServerId)
                    continue;
                if (session.Status != ConnectionStatus.Connected) continue;

                // 强制刷新 RdpInputHwnd，不信任缓存（重连后 HWND 会变）
                IntPtr inputHwnd = IntPtr.Zero;
                if (session.FreeRdpControl != null)
                {
                    inputHwnd = FindWfreerdpInputWindow(session.FreeRdpControl);
                    session.RdpInputHwnd = inputHwnd;
                }

                if (inputHwnd != IntPtr.Zero)
                {
                    slaveHwnds.Add(inputHwnd);
                    diag.AppendLine($"  slave=0x{inputHwnd.ToInt64():X8} session={session.ServerName} size=N/A (cached later)");
                }
                else
                {
                    diag.AppendLine($"  slave=IntPtr.Zero session={session.ServerName} (RdpInputHwnd not found!)");
                }
            }

            // 使用预缓存方式：钩子回调中不再调用 GetClientRect
            _groupControlHook.UpdateSlaves(slaveHwnds);

            diag.AppendLine($"  total slaves={slaveHwnds.Count}");
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MultiRDPManager_DIAG.log"),
                diag.ToString());
        }

        #endregion

        #region ─── 对话框 ───

        private void OnOpenAddServerDialogRequested(object? sender, EventArgs e)
        {
            var dialog = new AddServerDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ServerInfo != null && _viewModel != null)
            {
                _viewModel.Servers.Add(dialog.ServerInfo);
                _viewModel.RefreshStatistics();
                UpdateStatusBar($"Added server: {dialog.ServerInfo.Name}");
            }
        }

        private void OnOpenImportCsvDialogRequested(object? sender, EventArgs e)
        {
            OpenImportCsvDialog();
        }

        /// <summary>
        /// 导入按钮 Click 备用处理（Command 绑定在部分场景下可能不触发，兜底用）
        /// </summary>
        private void OnImportCsvClick(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenImportCsvDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Import failed: {ex.GetType().Name}: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenImportCsvDialog()
        {
            var dialog = new ImportCsvDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ImportedServers != null && _viewModel != null)
            {
                foreach (var server in dialog.ImportedServers)
                {
                    _viewModel.Servers.Add(server);
                }
                _viewModel.RefreshStatistics();
                UpdateStatusBar($"Imported {dialog.ImportedServers.Count} servers");
            }
        }

        #endregion

        #region ─── 工具栏事件 ───

        private void OnCopyIpClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.SelectedServer != null)
            {
                System.Windows.Clipboard.SetText(_viewModel.SelectedServer.IpAddress);
                UpdateStatusBar($"Copied IP: {_viewModel.SelectedServer.IpAddress}");
            }
            else
            {
                ShowInfo("Please select a server first");
            }
        }

        private void OnToggleFullScreen(object sender, RoutedEventArgs e)
        {
            _isFullScreen = !_isFullScreen;

            if (_isFullScreen)
            {
                _previousWindowStyle = WindowStyle;
                _previousResizeMode = ResizeMode;
                _previousWindowState = WindowState;

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = _previousWindowStyle;
                ResizeMode = _previousResizeMode;
                WindowState = _previousWindowState;
            }

            UpdateStatusBar(_isFullScreen ? "Fullscreen mode (press Esc to exit)" : "Exited fullscreen mode");
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "MultiRDPManager v2.0\nBased on RoyalApps.Community.FreeRdp.WinForms\n\nMulti-server Windows Remote Desktop Manager",
                "About MultiRDPManager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        #endregion

        #region ─── UI辅助方法 ───

        private void UpdateStatusBar(string text)
        {
            if (_viewModel != null)
            {
                _viewModel.StatusBarText = text;
            }
        }

        private void ShowError(string message)
        {
            System.Windows.MessageBox.Show(
                message,
                "MultiRDPManager - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void ShowInfo(string message)
        {
            System.Windows.MessageBox.Show(
                message,
                "MultiRDPManager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        protected override void OnClosed(EventArgs e)
        {
            HandleWindowClosed();
            base.OnClosed(e);
        }

        /// <summary>
        /// 窗口关闭时清理所有资源
        /// 注意：仅在 OnClosed 中被调用一次（不再通过 Closed 事件重复订阅），避免双调用
        /// </summary>
        private void HandleWindowClosed()
        {
            // 停止缩略图定时器
            _thumbnailTimer.Stop();

            // 关闭 off-screen 窗口，释放 Host HWND
            try { _offScreenWindow.Close(); } catch { }

            // 断开所有连接并释放资源
            List<RdpSession> sessionsToClean;
            _sessionsLock.EnterReadLock();
            try { sessionsToClean = _sessions.Values.ToList(); }
            finally { _sessionsLock.ExitReadLock(); }

            foreach (var session in sessionsToClean)
            {
                try
                {
                    // 释放缩略图缓存
                    if (_thumbnailCache.TryGetValue(session, out var cache))
                    {
                        cache.Bitmap?.Dispose();
                        cache.ThumbBitmap?.Dispose();
                        cache.ThumbGraphics?.Dispose();
                    }
                    session.Dispose();
                }
                catch
                {
                    // 忽略清理时的异常
                }
            }

            _sessionsLock.EnterWriteLock();
            try { _sessions.Clear(); }
            finally { _sessionsLock.ExitWriteLock(); }

            // 停止并释放群控钩子
            _groupControlHook.Stop();
            _groupControlHook.Dispose();

            // 显式移除 HwndSource Hook
            if (_hwndSource != null)
            {
                try { _hwndSource.RemoveHook(WndProc); } catch { }
                _hwndSource = null;
            }

            if (_viewModel != null)
            {
                _viewModel.ConnectServerRequested -= OnConnectServerRequested;
                _viewModel.DisconnectServerRequested -= OnDisconnectServerRequested;
                _viewModel.ConnectAllRequested -= OnConnectAllRequested;
                _viewModel.DisconnectAllRequested -= OnDisconnectAllRequested;
                _viewModel.OpenAddServerDialogRequested -= OnOpenAddServerDialogRequested;
                _viewModel.OpenImportCsvDialogRequested -= OnOpenImportCsvDialogRequested;
                _viewModel.DeleteServerRequested -= OnDeleteServerRequested;
                _viewModel.ToggleGroupControlRequested -= OnToggleGroupControlRequested;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            // 清理缩略图缓存
            _thumbnailCache.Clear();
        }

        #endregion

        #region ─── WndProc（HwndSource钩子） ───

        /// <summary>
        /// WPF窗口的HwndSource钩子 — 在UI线程上处理自定义消息
        /// WM_APP_FORWARD_MOUSE/KEYBOARD 由 GroupControlHook 的钩子回调从全局钩子线程 PostMessage 发出
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == GroupControlHook.WM_APP_FORWARD_MOUSE)
            {
                // wParam = 消息类型 (WM_LBUTTONDOWN, WM_RBUTTONDOWN 等)
                // lParam = 坐标: 低16位=X偏移, 高16位=Y偏移 (相对于MasterHwnd客户区)
                uint eventMsg = (uint)wParam;
                int x = (int)(lParam.ToInt64() & 0xFFFF);
                int y = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                // 计算百分比坐标（用 MasterHwnd 的尺寸）
                Win32.GetClientRect(_groupControlHook.MasterHwnd, out var masterRect);
                if (masterRect.Width > 0 && masterRect.Height > 0)
                {
                    double pctX = Math.Clamp((double)x / masterRect.Width, 0, 1);
                    double pctY = Math.Clamp((double)y / masterRect.Height, 0, 1);

                    // 在 UI 线程上 PostMessage 到从机 + 释放鼠标捕获
                    // 从 UI 线程调用可以正确调用 ReleaseCapture
                    _groupControlHook.ForwardToSlaves(pctX, pctY, eventMsg);
                }

                handled = true;
            }
            else if (msg == GroupControlHook.WM_APP_FORWARD_KEYBOARD)
            {
                // wParam = 消息类型 (WM_KEYDOWN/WM_KEYUP/WM_SYSKEYDOWN/WM_SYSKEYUP)
                // lParam → 低16位 vkCode, 中16位 scanCode, 高8位 flags
                uint kbdMsg = (uint)wParam;
                uint data = (uint)lParam.ToInt64();
                uint vkCode = data & 0xFFFF;
                uint scanCode = (data >> 16) & 0xFFFF;
                uint kbdFlags = (data >> 24) & 0xFF;

                // 后台线程执行 SendMessageTimeout，避免阻塞 UI 线程
                _groupControlHook.ForwardKeyboardToSlaves(kbdMsg, vkCode, scanCode, kbdFlags);

                handled = true;
            }
            return IntPtr.Zero;
        }

        #endregion

        #region ─── 缩略图捕获 ───

        /// <summary>
        /// 每1000ms截取所有已连接会话的wfreerdp窗口内容并更新缩略图
        /// PrintWindow 仍跑在 UI 线程（后台线程会导致群控异常）
        /// </summary>
        private void OnCaptureThumbnails(object? sender, EventArgs e)
        {
            // 窗口最小化或不可见时跳过捕获
            if (WindowState == WindowState.Minimized || !IsVisible)
            {
                // 暂停定时器直到窗口恢复可见
                if (_thumbnailTimer.IsEnabled)
                    _thumbnailTimer.Stop();
                return;
            }

            // 线程安全遍历 _sessions
            List<RdpSession> sessionsSnapshot;
            _sessionsLock.EnterReadLock();
            try { sessionsSnapshot = _sessions.Values.ToList(); }
            finally { _sessionsLock.ExitReadLock(); }

            foreach (var session in sessionsSnapshot)
            {
                if (session.Status != ConnectionStatus.Connected) continue;
                if (session.FreeRdpControl == null) continue;

                CaptureSessionThumbnail(session);
            }
        }

        /// <summary>
        /// 截取单个会话的缩略图
        /// 通过 PrintWin32 API 捕获 wfreerdp 子窗口内容 → 缩放 → 转为 WPF ImageSource
        /// 增加 Bitmap 缓存复用，减少 GDI 对象分配
        /// </summary>
        private void CaptureSessionThumbnail(RdpSession session)
        {
            try
            {
                if (session.FreeRdpControl != null)
                {
                    if (session.WfreerdpHwnd == IntPtr.Zero)
                        session.WfreerdpHwnd = FindWfreerdpWindow(session.FreeRdpControl);
                    session.RdpInputHwnd = FindWfreerdpInputWindow(session.FreeRdpControl);
                }

                if (session.WfreerdpHwnd == IntPtr.Zero) return;

                if (!Win32.GetClientRect(session.WfreerdpHwnd, out var clientRect)) return;
                if (clientRect.Width <= 0 || clientRect.Height <= 0) return;

                // 获取或创建缓存条目
                var cache = _thumbnailCache.GetOrCreateValue(session);
                const int thumbWidth = 240;
                const int thumbHeight = 135;

                // 如果源尺寸变化，释放旧 Bitmap
                if (cache.Bitmap != null && (cache.LastWidth != clientRect.Width || cache.LastHeight != clientRect.Height))
                {
                    cache.Bitmap.Dispose();
                    cache.Bitmap = null;
                }

                // 复用或创建源 Bitmap
                if (cache.Bitmap == null)
                {
                    cache.Bitmap = new Bitmap(clientRect.Width, clientRect.Height);
                    cache.LastWidth = clientRect.Width;
                    cache.LastHeight = clientRect.Height;
                }

                using (var g = Graphics.FromImage(cache.Bitmap))
                {
                    var hdc = g.GetHdc();
                    try
                    {
                        Win32.PrintWindow(session.WfreerdpHwnd, hdc, 0);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                // 复用或创建缩略图 Bitmap 和 Graphics
                if (cache.ThumbBitmap == null || cache.ThumbBitmap.Width != thumbWidth || cache.ThumbBitmap.Height != thumbHeight)
                {
                    cache.ThumbBitmap?.Dispose();
                    cache.ThumbGraphics?.Dispose();
                    cache.ThumbBitmap = new Bitmap(thumbWidth, thumbHeight);
                    cache.ThumbGraphics = Graphics.FromImage(cache.ThumbBitmap);
                    cache.ThumbGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                }

                cache.ThumbGraphics!.DrawImage(cache.Bitmap, 0, 0, thumbWidth, thumbHeight);

                var imageSource = BitmapToImageSource(cache.ThumbBitmap);
                session.ThumbnailSource = imageSource;
            }
            catch { }
        }

        /// <summary>
        /// 查找wfreerdp.exe的渲染子窗口句柄（一层深度 = _renderTarget）
        /// _renderTarget 是 FreeRdpControl 内部的 UserControl，wfreerdp 在其上渲染
        /// PrintWindow 截取此窗口可获得完整 RDP 画面
        /// </summary>
        private static IntPtr FindWfreerdpWindow(FreeRdpControl rdpControl)
        {
            IntPtr parentHwnd = rdpControl.Handle;
            if (parentHwnd == IntPtr.Zero) return IntPtr.Zero;

            // FreeRdpControl 的第一个子窗口 = _renderTarget（内部UserControl）
            return Win32.FindWindowEx(parentHwnd, IntPtr.Zero, null, null);
        }

        /// <summary>
        /// 查找wfreerdp.exe的真实输入子窗口（通过类名 "FREERDP" 精确匹配）
        /// 第一层：FreeRdpControl.Handle → _renderTarget（内部UserControl）
        /// 第二层：枚举 _renderTarget 的子窗口，找类名为 "FREERDP" 的窗口
        /// 这是 wfreerdp 的真实渲染窗口，其窗口过程 (wf_event_proc) 处理 WM_LBUTTONDOWN 等鼠标消息
        /// </summary>
        private static IntPtr FindWfreerdpInputWindow(FreeRdpControl rdpControl)
        {
            IntPtr parentHwnd = rdpControl.Handle;
            if (parentHwnd == IntPtr.Zero) return IntPtr.Zero;

            // 第一层：FreeRdpControl 的子窗口 = _renderTarget（内部UserControl）
            IntPtr renderTargetHwnd = Win32.FindWindowEx(parentHwnd, IntPtr.Zero, null, null);
            if (renderTargetHwnd == IntPtr.Zero) return IntPtr.Zero;

            // 第二层：枚举 _renderTarget 的子窗口，找类名为 "FREERDP" 的窗口
            IntPtr foundHwnd = IntPtr.Zero;
            Win32.EnumChildWindows(renderTargetHwnd, (hwnd, _) =>
            {
                var sb = new System.Text.StringBuilder(256);
                Win32.GetClassName(hwnd, sb, 256);
                if (sb.ToString().Equals("FREERDP", StringComparison.OrdinalIgnoreCase))
                {
                    foundHwnd = hwnd;
                    return false; // 停止枚举
                }
                return true; // 继续枚举
            }, IntPtr.Zero);

            return foundHwnd;
        }

        /// <summary>
        /// 尝试查找 wfreerdp 的各层窗口句柄（带重试，最多等2秒）
        /// WfreerdpHwnd（一层，_renderTarget）立即可用
        /// RdpInputHwnd（两层，wfreerdp子窗口）可能需要等待wfreerdp创建
        /// </summary>
        private void TryFindWfreerdpHwnd(RdpSession session)
        {
            if (session.FreeRdpControl == null) return;

            // WfreerdpHwnd（一层，_renderTarget）— 立即可用
            session.WfreerdpHwnd = FindWfreerdpWindow(session.FreeRdpControl);

            // RdpInputHwnd（两层，wfreerdp子窗口）— 可能较慢，后台重试
            var capturedSession = session; // 捕获局部引用
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        // 检查会话是否已被清理（FreeRdpControl 可能已被 Dispose）
                        if (capturedSession.FreeRdpControl == null) return;

                        var hwnd = FindWfreerdpInputWindow(capturedSession.FreeRdpControl);
                        if (hwnd != IntPtr.Zero)
                        {
                            await Dispatcher.BeginInvoke(() =>
                            {
                                if (capturedSession.FreeRdpControl != null) // 二次确认
                                    capturedSession.RdpInputHwnd = hwnd;
                            });
                            return;
                        }
                        await System.Threading.Tasks.Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    DiagLog($"TryFindWfreerdpHwnd error: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 写入诊断日志（%TEMP%\MultiRDPManager_DIAG.log，与 GroupControlHook 共用同一文件）
        /// </summary>
        private static void DiagLog(string message)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "MultiRDPManager_DIAG.log");
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>
        /// System.Drawing.Bitmap → WPF ImageSource
        /// </summary>
        private static ImageSource BitmapToImageSource(Bitmap bitmap)
        {
            using var memory = new System.IO.MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            memory.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            return bitmapImage;
        }

        #endregion
    }
}
