using System.Windows;

namespace MultiRDPManager.FreeRDP.Views.Dialogs
{
    /// <summary>
    /// 连接分辨率设置对话框
    /// 在建立RDP连接前让用户输入宽度和高度
    /// </summary>
    public partial class ConnectionResolutionDialog : Window
    {
        /// <summary>
        /// 获取或设置分辨率宽度值
        /// </summary>
        public int WidthValue { get; set; } = 2560;

        /// <summary>
        /// 获取或设置分辨率高度值
        /// </summary>
        public int HeightValue { get; set; } = 1440;

        public ConnectionResolutionDialog()
        {
            InitializeComponent();
            WidthTextBox.Focus();
            WidthTextBox.SelectAll();
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            // 验证宽度
            if (!int.TryParse(WidthTextBox.Text.Trim(), out int width) || width < 640 || width > 7680)
            {
                System.Windows.MessageBox.Show(
                    "Please enter a valid width value (640-7680)",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                WidthTextBox.Focus();
                WidthTextBox.SelectAll();
                return;
            }

            // 验证高度
            if (!int.TryParse(HeightTextBox.Text.Trim(), out int height) || height < 480 || height > 4320)
            {
                System.Windows.MessageBox.Show(
                    "Please enter a valid height value (480-4320)",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                HeightTextBox.Focus();
                HeightTextBox.SelectAll();
                return;
            }

            WidthValue = width;
            HeightValue = height;

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
