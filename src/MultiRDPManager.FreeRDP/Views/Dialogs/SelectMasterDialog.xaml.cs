using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MultiRDPManager.FreeRDP.Models;

namespace MultiRDPManager.FreeRDP.Views.Dialogs
{
    /// <summary>
    /// 群控主控选择对话框
    /// 显示已勾选的服务器列表，用户单选一个作为主控窗口
    /// </summary>
    public partial class SelectMasterDialog : Window
    {
        /// <summary>
        /// 用户选择的会话（确认后有效）
        /// </summary>
        public RdpSession? SelectedSession { get; private set; }

        /// <summary>
        /// 可选的会话列表（包装为ObservableCollection支持绑定）
        /// </summary>
        public ObservableCollection<RdpSession> SessionList { get; } = new();

        public SelectMasterDialog()
        {
            InitializeComponent();
            DataContext = SessionList;
        }

        /// <summary>
        /// 使用已勾选的服务器列表初始化对话框
        /// </summary>
        /// <param name="selectedSessions">已勾选的会话列表</param>
        public SelectMasterDialog(System.Collections.Generic.List<RdpSession> selectedSessions) : this()
        {
            foreach (var session in selectedSessions)
            {
                SessionList.Add(session);
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (ServerListBox.SelectedItem is RdpSession session)
            {
                SelectedSession = session;
                DialogResult = true;
                Close();
            }
            else
            {
                System.Windows.MessageBox.Show(this,
                    "请选择一台服务器作为主控窗口",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
