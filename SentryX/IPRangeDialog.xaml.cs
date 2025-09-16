using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace SentryX
{
    /// <summary>
    /// IP 範圍設定對話框 - 從大華 DEMO 的 PointSetDialog 轉換而來
    /// </summary>
    public partial class IPRangeDialog : Window
    {
        /// <summary>
        /// IP 地址數量
        /// </summary>
        public int IPCount { get; set; }

        /// <summary>
        /// 生成的 IP 地址清單
        /// </summary>
        public List<string> IPList { get; private set; } = new List<string>();

        public IPRangeDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 確定按鈕點擊事件
        /// </summary>
        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(StartIPTextBox.Text) || string.IsNullOrWhiteSpace(EndIPTextBox.Text))
            {
                MessageBox.Show("請輸入起始和結束 IP 地址", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string startIPText = StartIPTextBox.Text.Trim();
                string endIPText = EndIPTextBox.Text.Trim();

                // 檢查 IP 格式
                if (!startIPText.Contains(".") || !endIPText.Contains("."))
                {
                    MessageBox.Show("請輸入有效的 IP 地址", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string[] splitStart = startIPText.Split('.');
                string[] splitEnd = endIPText.Split('.');

                if (splitStart.Length != 4 || splitEnd.Length != 4)
                {
                    MessageBox.Show("請輸入有效的 IP 地址", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 解析 IP 地址
                byte[] startIP = IPAddress.Parse(startIPText).GetAddressBytes();
                byte[] endIP = IPAddress.Parse(endIPText).GetAddressBytes();

                // 驗證 IP 範圍（保持與原始 DEMO 相同的邏輯）
                if (startIP[0] == endIP[0] && startIP[1] == endIP[1])
                {
                    IPList.Clear();

                    if (startIP[2] != endIP[2])
                    {
                        // 跨越兩個 C 類網段
                        if ((startIP[2] + 1) != endIP[2])
                        {
                            MessageBox.Show("IP 數量超過 256 個限制", "範圍錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        if ((startIP[3] - 1) < endIP[3])
                        {
                            MessageBox.Show("IP 數量超過 256 個限制", "範圍錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        IPCount = 256 + startIP[3] - 1 - endIP[3];

                        // 添加第一個網段的 IP
                        for (int i = startIP[3]; i <= 255; i++)
                        {
                            byte[] byIP = new byte[4];
                            byIP[0] = startIP[0];
                            byIP[1] = startIP[1];
                            byIP[2] = startIP[2];
                            byIP[3] = (byte)i;
                            IPAddress ip = new IPAddress(byIP);
                            IPList.Add(ip.ToString());
                        }

                        // 添加第二個網段的 IP
                        for (int i = 0; i <= endIP[3]; i++)
                        {
                            byte[] byIP = new byte[4];
                            byIP[0] = startIP[0];
                            byIP[1] = startIP[1];
                            byIP[2] = endIP[2];
                            byIP[3] = (byte)i;
                            IPAddress ip = new IPAddress(byIP);
                            IPList.Add(ip.ToString());
                        }
                    }
                    else
                    {
                        // 同一個 C 類網段
                        IPCount = endIP[3] - startIP[3] + 1;

                        if (IPCount > 256)
                        {
                            MessageBox.Show("IP 數量超過 256 個限制", "範圍錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        for (int i = startIP[3]; i <= endIP[3]; i++)
                        {
                            byte[] byIP = new byte[4];
                            byIP[0] = startIP[0];
                            byIP[1] = startIP[1];
                            byIP[2] = startIP[2];
                            byIP[3] = (byte)i;
                            IPAddress ip = new IPAddress(byIP);
                            IPList.Add(ip.ToString());
                        }
                    }
                }
                else
                {
                    MessageBox.Show("IP 數量超過 256 個限制", "範圍錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 驗證成功
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"請輸入有效的 IP 地址\n錯誤：{ex.Message}", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 取消按鈕點擊事件
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}