using System.Windows;
using System.Windows.Controls;
using MultiRDPManager.FreeRDP.Models;

namespace MultiRDPManager.FreeRDP.Views.Dialogs
{
    /// <summary>
    /// CSV导入对话框 — 支持直接粘贴CSV文本（IP, 用户名, 密码），端口默认3389
    /// </summary>
    public partial class ImportCsvDialog : Window
    {
        /// <summary>
        /// 解析后的服务器列表
        /// </summary>
        public List<ServerConnectionInfo>? ImportedServers { get; private set; }

        private List<ServerConnectionInfo>? _parsedServers;
        private bool _isParsing;

        public ImportCsvDialog()
        {
            InitializeComponent();
        }

        private void OnCsvTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isParsing) return;

            var text = CsvTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                _parsedServers = null;
                ImportButton.IsEnabled = false;
                ImportButton.Content = "导入";
                PreviewInfoText.Text = "预览：等待输入...";
                ParseStatusText.Text = "粘贴CSV数据后自动解析...";
                return;
            }

            // 解析文本
            try
            {
                _isParsing = true;
                _parsedServers = ParseCsvText(text);
            }
            catch (Exception ex)
            {
                _parsedServers = null;
                ImportButton.IsEnabled = false;
                ImportButton.Content = "导入";
                PreviewInfoText.Text = "预览：解析失败";
                ParseStatusText.Text = "解析错误: " + ex.Message;
                return;
            }
            finally
            {
                _isParsing = false;
            }

            if (_parsedServers == null || _parsedServers.Count == 0)
            {
                ImportButton.IsEnabled = false;
                ImportButton.Content = "导入";
                PreviewInfoText.Text = "预览：未能解析到有效数据";
                ParseStatusText.Text = "请检查CSV格式是否正确 (IP, 用户名, 密码)";
                return;
            }

            // 更新UI
            ImportButton.IsEnabled = true;
            ImportButton.Content = $"导入 ({_parsedServers.Count} 台服务器)";
            PreviewInfoText.Text = $"预览：已解析 {_parsedServers.Count} 台服务器（显示前3台）";

            // 显示前3条预览
            string previewLines = string.Join("\n",
                _parsedServers.Take(3).Select(s =>
                {
                    var name = string.IsNullOrEmpty(s.Name) ? s.IpAddress : s.Name;
                    return $"  - {name}  {s.IpAddress}:{s.Port}  {s.Username}";
                }));
            string moreInfo = _parsedServers.Count > 3
                ? $"\n  ... 还有 {_parsedServers.Count - 3} 台服务器"
                : "";
            ParseStatusText.Text = previewLines + moreInfo;
        }

        /// <summary>
        /// 解析CSV文本为服务器信息列表
        /// 格式支持：名称,IP,用户名,密码 或 IP,用户名,密码
        /// </summary>
        private static List<ServerConnectionInfo> ParseCsvText(string text)
        {
            var servers = new List<ServerConnectionInfo>();
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                // 尝试用逗号或Tab分隔
                string[] parts;
                if (trimmedLine.Contains('\t'))
                    parts = trimmedLine.Split('\t', StringSplitOptions.TrimEntries);
                else
                    parts = trimmedLine.Split(',', StringSplitOptions.TrimEntries);

                if (parts.Length < 3)
                    continue;

                string name;
                string ip;
                string username;
                string password;

                if (parts.Length >= 4)
                {
                    // 格式：名称, IP, 用户名, 密码
                    name = parts[0];
                    ip = parts[1];
                    username = parts[2];
                    password = parts[3];
                }
                else
                {
                    // 格式：IP, 用户名, 密码
                    name = parts[0];
                    ip = parts[0];
                    username = parts[1];
                    password = parts[2];
                }

                // 解析端口（IP中可能包含端口）
                // IPv6: [::1]:3389 → 从右找最后一个']:'后面的端口
                // IPv4: 192.168.1.1:3389 → 最后一个冒号后面的端口
                int port = 3389;
                if (ip.EndsWith(']'))
                {
                    // IPv6 无端口：[::1] 或 [2001:db8::1]
                }
                else if (ip.Contains(':'))
                {
                    int colonIndex;
                    if (ip.StartsWith('['))
                    {
                        // IPv6 带端口: [::1]:3389 → 找"]:"
                        int bracketEnd = ip.IndexOf("]:", StringComparison.Ordinal);
                        if (bracketEnd > 0 && bracketEnd + 2 < ip.Length)
                        {
                            colonIndex = bracketEnd + 1;
                            if (int.TryParse(ip[(colonIndex + 1)..], out int parsedPort)
                                && parsedPort >= 1 && parsedPort <= 65535)
                            {
                                port = parsedPort;
                                ip = ip[..(colonIndex - 1)].TrimStart('[').TrimEnd(']');
                            }
                        }
                    }
                    else
                    {
                        // IPv4 带端口: 192.168.1.1:3389
                        colonIndex = ip.LastIndexOf(':');
                        if (colonIndex > ip.LastIndexOf('.') && colonIndex + 1 < ip.Length
                            && int.TryParse(ip[(colonIndex + 1)..], out int parsedPort)
                            && parsedPort >= 1 && parsedPort <= 65535)
                        {
                            port = parsedPort;
                            ip = ip[..colonIndex];
                        }
                    }
                }

                servers.Add(new ServerConnectionInfo
                {
                    Name = name,
                    IpAddress = ip,
                    Port = port,
                    Username = username,
                    Password = password
                });
            }

            return servers;
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            CsvTextBox.Clear();
            _parsedServers = null;
            ImportButton.IsEnabled = false;
            ImportButton.Content = "导入";
            PreviewInfoText.Text = "预览：等待输入...";
            ParseStatusText.Text = "粘贴CSV数据后自动解析...";
        }

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (_parsedServers == null || _parsedServers.Count == 0)
            {
                System.Windows.MessageBox.Show("没有可导入的服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ImportedServers = _parsedServers;
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
