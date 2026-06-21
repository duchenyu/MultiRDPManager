using System.Windows;
using MultiRDPManager.FreeRDP.Models;

namespace MultiRDPManager.FreeRDP.Views.Dialogs
{
    /// <summary>
    /// 添加服务器对话框逻辑
    /// </summary>
    public partial class AddServerDialog : Window
    {
        /// <summary>
        /// 创建好的服务器信息
        /// </summary>
        public ServerConnectionInfo? ServerInfo { get; private set; }

        public AddServerDialog()
        {
            InitializeComponent();
            IpTextBox.Focus();
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            // 验证
            if (string.IsNullOrWhiteSpace(IpTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please enter an IP address", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                IpTextBox.Focus();
                return;
            }

            if (!int.TryParse(PortTextBox.Text, out int port) || port < 1 || port > 65535)
            {
                System.Windows.MessageBox.Show("Please enter a valid port number (1-65535)", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                PortTextBox.Focus();
                return;
            }

            // 创建服务器信息
            ServerInfo = new ServerConnectionInfo
            {
                Name = string.IsNullOrWhiteSpace(NameTextBox.Text) ? IpTextBox.Text.Trim() : NameTextBox.Text.Trim(),
                IpAddress = IpTextBox.Text.Trim(),
                Port = port,
                Username = UsernameTextBox.Text.Trim(),
                Password = PasswordBox.Password
            };

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
