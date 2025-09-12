// MainWindow.xaml.cs - 主視窗的程式邏輯
// 這個檔案控制整個主介面的行為

// 引用必要的程式庫
using System;                               // 基本系統功能
using System.Linq;                          // LINQ 查詢功能
using System.Windows;                       // WPF 視窗功能
using System.Windows.Controls;              // WPF 控制項
using System.Windows.Input;                 // 滑鼠鍵盤事件
using System.Windows.Forms.Integration;     // 讓 WPF 可以使用 WinForms
using System.Windows.Forms;                 // Windows Forms（用來取得 HWND）

namespace SentryX
{
    /// <summary>
    /// 主視窗類別 - 控制整個程式的主要介面
    /// </summary>
    public partial class MainWindow : Window
    {
        // === Geohot 風格：把所有變數放在最上面，一目瞭然 ===

        /// <summary>
        /// 設備管理視窗 - 用來管理攝影機設備
        /// ? 表示這個變數可能是 null（沒有值）
        /// </summary>
        private DeviceManagerWindow? _deviceManager = null;

        /// <summary>
        /// 目前選中的設備ID - 記住用戶選了哪個攝影機
        /// </summary>
        private string? _selectedDeviceId = null;

        /// <summary>
        /// 視頻播放器 - 用來播放攝影機的影像
        /// </summary>
        private SimpleVideoPlayer? _videoPlayer = null;

        /// <summary>
        /// Windows Forms Panel - 真正顯示影像的區域
        /// 因為大華SDK需要Windows原生視窗句柄(HWND)，所以要用這個
        /// </summary>
        private System.Windows.Forms.Panel? _videoPanel = null;

        // === 記住當前選擇的解碼模式 ===
        private DecodeMode _currentDecodeMode = DecodeMode.Auto;

        // === 建構子 - 程式啟動時第一個執行的方法 ===

        /// <summary>
        /// 主視窗建構子 - 當程式啟動時自動執行
        /// </summary>
        public MainWindow()
        {
            // 第1步：初始化XAML中定義的所有控制項（按鈕、文字框等）
            InitializeComponent();

            // 第2步：設定介面的初始狀態
            SetupUI();

            // 第3步：準備視頻顯示區域
            SetupVideoArea();

            // 第4步：訂閱事件（當某些事情發生時，我們要收到通知）
            SubscribeEvents();
        }

        // === Geohot 風格：把初始化邏輯分解成小方法 ===

        /// <summary>
        /// 設定使用者介面的初始狀態
        /// </summary>
        private void SetupUI()
        {
            // 設定視窗標題，包含當前日期
            this.Title = $"SentryX CCTV 系統 ({DateTime.Now:yyyy-MM-dd})";

            // 顯示啟動訊息給用戶
            ShowMessage("✅ 系統啟動完成，SDK 已就緒");
            ShowMessage("💡 點擊「設備管理」開始添加攝影機");

            // 更新設備列表和系統狀態顯示
            RefreshDeviceList();
            UpdateStatusBar();
        }

        /// <summary>
        /// 設定視頻顯示區域
        /// 這是關鍵部分：建立一個 Windows Forms Panel 來顯示視頻
        /// </summary>
        private void SetupVideoArea()
        {
            try
            {
                // 建立一個黑色的 Panel 作為視頻顯示容器
                _videoPanel = new System.Windows.Forms.Panel
                {
                    BackColor = System.Drawing.Color.Black,    // 背景設為黑色
                    Dock = DockStyle.Fill                      // 填滿整個容器
                };

                // 將 Panel 放到 XAML 中的 VideoHost 裡面
                // VideoHost 是 WindowsFormsHost，它可以承載 Windows Forms 控制項
                VideoHost.Child = _videoPanel;

                ShowMessage("📺 視頻顯示區域準備完成");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 視頻區域初始化失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 訂閱事件 - 告訴系統當某些事情發生時要通知我們
        /// </summary>
        private void SubscribeEvents()
        {
            // 當設備狀態改變時（上線/下線），執行 OnDeviceChanged 方法
            DahuaSDK.DeviceStatusChanged += OnDeviceChanged;

            // 當 SDK 有訊息要告訴我們時，執行 OnSDKMessage 方法
            DahuaSDK.StatusMessage += OnSDKMessage;
        }

        // === 按鈕點擊事件 - 當用戶點擊按鈕時執行的方法 ===

        /// <summary>
        /// 設備管理按鈕被點擊 - 開啟設備管理視窗
        /// </summary>
        private void DeviceManagerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_deviceManager != null && _deviceManager.IsVisible)
                {
                    _deviceManager.Activate();
                    _deviceManager.WindowState = WindowState.Normal;
                    ShowMessage("設備管理視窗已激活");
                    return;
                }

                _deviceManager = new DeviceManagerWindow();
                _deviceManager.Owner = this;

                _deviceManager.Closed += (s, args) =>
                {
                    _deviceManager = null;
                    ShowMessage("設備管理視窗已關閉");
                };

                _deviceManager.Show();
                ShowMessage("設備管理視窗已開啟");
            }
            catch (Exception ex)
            {
                ShowMessage($"開啟設備管理視窗失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 快速添加設備按鈕被點擊 - 直接開啟設備管理並提示用戶
        /// </summary>
        private void QuickAddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            // 先開啟設備管理視窗（如果還沒開啟的話）
            DeviceManagerButton_Click(sender, e);

            // 給用戶一個提示
            ShowMessage("💡 請在設備管理視窗中填寫攝影機資訊");
        }

        /// <summary>
        /// 刷新按鈕被點擊 - 手動更新設備列表和系統狀態
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshDeviceList();
            UpdateStatusBar();
            ShowMessage("🔄 設備列表已手動刷新");
        }

        /// <summary>
        /// 解碼模式選擇改變事件
        /// </summary>
        private void DecodeTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DecodeTypeComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string tag = selectedItem.Tag?.ToString() ?? "Auto";

                    switch (tag)
                    {
                        case "Software":
                            _currentDecodeMode = DecodeMode.Software;
                            ShowMessage("已切換到軟體解碼模式 (使用CPU，相容性最佳)");
                            break;

                        case "Hardware":
                            _currentDecodeMode = DecodeMode.Hardware;
                            ShowMessage("已切換到硬體解碼模式 (使用GPU，性能最佳)");
                            break;

                        case "Auto":
                        default:
                            _currentDecodeMode = DecodeMode.Auto;
                            ShowMessage("已切換到自動選擇模式 (先試硬體，再試軟體)");
                            break;
                    }

                    if (_videoPlayer != null && _videoPlayer.IsPlaying)
                    {
                        ShowMessage("提示：解碼模式變更將在下次播放時生效");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DecodeTypeComboBox_SelectionChanged 發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 開始播放方法 - 使用用戶選擇的解碼模式並清除舊畫面
        /// </summary>
        private void StartVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedDeviceId))
            {
                ShowMessage("請先在左側選擇一個攝影機設備或通道");
                return;
            }

            var device = DahuaSDK.GetDevice(_selectedDeviceId);
            if (device == null || !device.IsOnline)
            {
                ShowMessage("選中的設備不在線，請先連接設備");
                return;
            }

            try
            {
                // 第1步：停止並清理舊的播放器（解決畫面殘留問題）
                if (_videoPlayer != null)
                {
                    _videoPlayer.StopPlay();
                    _videoPlayer.Dispose();
                    _videoPlayer = null;

                    // 清除視頻顯示區域的內容
                    ClearVideoDisplay();
                    ShowMessage("已清除舊的視頻畫面");
                }

                // 第2步：從選中的項目中提取通道號
                int channel = ExtractChannelFromSelection();

                string decodeModeText = GetDecodeModeText();
                ShowMessage($"準備使用{decodeModeText}播放 {device.Name} 通道{channel + 1} 的視頻...");

                // 第3步：使用用戶選擇的解碼模式建立新播放器
                _videoPlayer = new SimpleVideoPlayer(_currentDecodeMode);

                // 第4步：取得視頻顯示窗口句柄
                IntPtr windowHandle = GetVideoWindowHandle();

                if (windowHandle == IntPtr.Zero)
                {
                    ShowMessage("無法取得視頻顯示區域的窗口句柄");
                    CleanupVideoPlayer();
                    return;
                }

                // 第5步：開始播放
                if (_videoPlayer.StartPlay(device.LoginHandle, channel, windowHandle))
                {
                    StartVideoButton.IsEnabled = false;
                    StopVideoButton.IsEnabled = true;
                    ShowMessage($"開始播放 {device.Name} 通道{channel + 1} 的即時視頻 ({decodeModeText})");
                }
                else
                {
                    ShowMessage("視頻播放啟動失敗，請檢查設備連接或嘗試其他解碼模式");
                    CleanupVideoPlayer();
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"視頻播放發生錯誤：{ex.Message}");
                CleanupVideoPlayer();
            }
        }

        /// <summary>
        /// 修改停止播放方法 - 停止播放視頻
        /// </summary>
        private void StopVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPlayer != null)
            {
                // 停止播放
                _videoPlayer.StopPlay();
                _videoPlayer.Dispose();
                _videoPlayer = null;

                // 清除視頻顯示區域
                ClearVideoDisplay();

                // 更新按鈕狀態
                StartVideoButton.IsEnabled = true;
                StopVideoButton.IsEnabled = false;

                ShowMessage("視頻播放已停止，畫面已清除");
            }
        }

        // === 新增的輔助方法 ===

        /// <summary>
        /// 清除視頻顯示區域 - 解決畫面殘留問題
        /// </summary>
        private void ClearVideoDisplay()
        {
            try
            {
                if (_videoPanel != null)
                {
                    // 方法1：重新建立 Panel（最有效的清除方式）
                    _videoPanel.Dispose();

                    // 建立新的黑色 Panel
                    _videoPanel = new System.Windows.Forms.Panel
                    {
                        BackColor = System.Drawing.Color.Black,
                        Dock = DockStyle.Fill
                    };

                    // 重新設定到 VideoHost
                    VideoHost.Child = _videoPanel;

                    ShowMessage("視頻顯示區域已重設");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"清除視頻顯示時發生錯誤：{ex.Message}");
            }
        }

        /// <summary>
        /// 取得解碼模式的文字描述
        /// </summary>
        private string GetDecodeModeText()
        {
            return _currentDecodeMode switch
            {
                DecodeMode.Software => "軟體解碼",
                DecodeMode.Hardware => "硬體解碼",
                DecodeMode.Auto => "自動解碼",
                _ => "未知模式"
            };
        }

        // === 列表選擇事件 - 當用戶在設備列表中選擇項目時 ===

        /// <summary>
        /// 修改設備選擇邏輯 - 選擇新設備時停止舊播放
        /// </summary>
        private void DeviceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 如果正在播放，先停止（避免畫面混亂）
            if (_videoPlayer != null && _videoPlayer.IsPlaying)
            {
                StopVideoButton_Click(sender, new RoutedEventArgs());
            }

            // 現有的選擇邏輯保持不變
            if (DeviceListBox.SelectedItem is string selectedText)
            {
                DeviceInfo? selectedDevice = null;
                int selectedChannel = 0;

                if (selectedText.Contains("通道"))
                {
                    selectedChannel = ExtractChannelFromSelection();

                    int selectedIndex = DeviceListBox.SelectedIndex;
                    for (int i = selectedIndex - 1; i >= 0; i--)
                    {
                        if (DeviceListBox.Items[i] is string itemText && itemText.Contains("📹"))
                        {
                            var devices = DahuaSDK.GetAllDevices();
                            selectedDevice = devices.FirstOrDefault(d =>
                                itemText.Contains(d.Name) && itemText.Contains(d.IpAddress));
                            break;
                        }
                    }
                }
                else if (selectedText.Contains("📹"))
                {
                    var devices = DahuaSDK.GetAllDevices();
                    selectedDevice = devices.FirstOrDefault(d =>
                        selectedText.Contains(d.Name) && selectedText.Contains(d.IpAddress));
                    selectedChannel = 0;
                }

                if (selectedDevice != null)
                {
                    _selectedDeviceId = selectedDevice.Id;
                    ShowMessage($"已選中: {selectedDevice.Name} 通道{selectedChannel + 1}");
                    StartVideoButton.IsEnabled = selectedDevice.IsOnline && !StopVideoButton.IsEnabled;
                }
                else
                {
                    _selectedDeviceId = null;
                    StartVideoButton.IsEnabled = false;
                }
            }
            else
            {
                _selectedDeviceId = null;
                StartVideoButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// 設備列表雙擊事件 - 用戶雙擊設備時直接開始播放
        /// </summary>
        private void DeviceListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 如果開始播放按鈕可用，就模擬點擊它
            if (StartVideoButton.IsEnabled)
            {
                StartVideoButton_Click(sender, new RoutedEventArgs());
            }
        }

        // === 事件回調方法 - 當某些事件發生時自動執行 ===

        /// <summary>
        /// 設備狀態改變回調 - 設備上線或下線時執行
        /// </summary>
        private void OnDeviceChanged(DeviceInfo device)
        {
            try
            {
                // 確保在主執行緒中執行，並檢查 UI 是否已載入
                Dispatcher.Invoke(() =>
                {
                    RefreshDeviceList();
                    UpdateStatusBar();
                    string status = device.IsOnline ? "上線" : "下線";
                    ShowMessage($"設備狀態變更: {device.Name} {status}");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnDeviceChanged 發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// SDK 訊息回調 - 當 SDK 有重要訊息時執行
        /// </summary>
        private void OnSDKMessage(string message)
    {
        try
        {
            Dispatcher.Invoke(() => ShowMessage($"SDK: {message}"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OnSDKMessage 發生錯誤: {ex.Message}");
        }
    }

        // === Geohot 風格的輔助方法 - 小而專一的功能 ===

        /// <summary>
        /// 取得視頻窗口句柄 - 這是播放視頻的關鍵
        /// 大華 SDK 需要一個真正的 Windows 窗口句柄 (HWND) 來顯示視頻
        /// </summary>
        private IntPtr GetVideoWindowHandle()
        {
            try
            {
                if (_videoPanel == null)
                {
                    ShowMessage("錯誤：視頻面板未初始化");
                    return IntPtr.Zero;
                }

                // 確保 Panel 的窗口句柄已經被建立
                // 在 Windows 中，控制項只有在真正需要顯示時才會建立句柄
                if (!_videoPanel.IsHandleCreated)
                {
                    // 強制建立句柄
                    var handle = _videoPanel.Handle;
                }

                ShowMessage($"📺 取得視頻窗口句柄: {_videoPanel.Handle}");
                return _videoPanel.Handle;
            }
            catch (Exception ex)
            {
                ShowMessage($"取得窗口句柄時發生異常: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 清理視頻播放器 - 當出錯時清理資源
        /// </summary>
        private void CleanupVideoPlayer()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.Dispose();
                _videoPlayer = null;
            }
        }

        /// <summary>
        /// 刷新設備列表顯示 - 支援多通道顯示
        /// </summary>
        private void RefreshDeviceList()
        {
            try
            {
                // 檢查 DeviceListBox 是否存在
                if (DeviceListBox == null) return;

                DeviceListBox.Items.Clear();
                var devices = DahuaSDK.GetAllDevices();

                if (devices.Count == 0)
                {
                    DeviceListBox.Items.Add("尚未添加任何攝影機設備");
                    DeviceListBox.Items.Add("點擊「設備管理」開始添加");
                }
                else
                {
                    var onlineDevices = devices.Where(d => d.IsOnline).ToList();
                    var offlineDevices = devices.Where(d => !d.IsOnline).ToList();

                    if (onlineDevices.Count > 0)
                    {
                        DeviceListBox.Items.Add($"在線設備 ({onlineDevices.Count})");
                        foreach (var device in onlineDevices)
                        {
                            DeviceListBox.Items.Add($"📹 {device.Name} ({device.IpAddress})");

                            if (device.ChannelCount > 0)
                            {
                                for (int channel = 0; channel < device.ChannelCount; channel++)
                                {
                                    DeviceListBox.Items.Add($"    └─ 通道 {channel + 1} (CH{channel})");
                                }
                            }
                            else
                            {
                                DeviceListBox.Items.Add($"    └─ 通道 1 (CH0)");
                            }
                            DeviceListBox.Items.Add("");
                        }
                    }

                    if (offlineDevices.Count > 0)
                    {
                        DeviceListBox.Items.Add($"離線設備 ({offlineDevices.Count})");
                        foreach (var device in offlineDevices)
                        {
                            DeviceListBox.Items.Add($"📹 {device.Name} ({device.IpAddress}) - 離線");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RefreshDeviceList 發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 從選中的文字中提取通道號
        /// </summary>
        private int ExtractChannelFromSelection()
        {
            if (DeviceListBox.SelectedItem is string selectedText)
            {
                // 檢查是否選中了通道項目
                if (selectedText.Contains("通道") && selectedText.Contains("CH"))
                {
                    // 提取 CH 後面的數字
                    var chIndex = selectedText.IndexOf("CH");
                    if (chIndex >= 0)
                    {
                        var chText = selectedText.Substring(chIndex + 2);
                        var endIndex = chText.IndexOf(')');
                        if (endIndex > 0)
                        {
                            chText = chText.Substring(0, endIndex);
                            if (int.TryParse(chText, out int channel))
                            {
                                return channel;
                            }
                        }
                    }
                }
            }

            // 預設返回通道0
            return 0;
        }

        /// <summary>
        /// 更新狀態欄顯示 - 更新系統狀態和設備統計
        /// </summary>
        private void UpdateStatusBar()
        {
            try
            {
                var totalDevices = DahuaSDK.TotalDeviceCount;
                var onlineDevices = DahuaSDK.OnlineDeviceCount;

                // 檢查 UI 元素是否存在
                if (SystemStatusTextBlock != null)
                {
                    if (totalDevices == 0)
                    {
                        SystemStatusTextBlock.Text = "等待設備";
                        SystemStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
                    }
                    else if (onlineDevices == 0)
                    {
                        SystemStatusTextBlock.Text = "所有設備離線";
                        SystemStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    }
                    else
                    {
                        SystemStatusTextBlock.Text = "正常運行";
                        SystemStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    }
                }

                if (DeviceStatsTextBlock != null)
                {
                    DeviceStatsTextBlock.Text = $"總計: {totalDevices}, 在線: {onlineDevices}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateStatusBar 發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 顯示訊息 - 修正版本，加入 null 檢查
        /// </summary>
        private void ShowMessage(string message)
        {
            try
            {
                string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

                // 加入 null 檢查，確保 UI 元素已經載入
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text += timestampedMessage + "\n";
                }

                // Console 輸出不依賴 UI，所以一定會執行
                Console.WriteLine(timestampedMessage);

                // 自動捲動也要檢查是否存在
                if (StatusScrollViewer != null)
                {
                    StatusScrollViewer.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                // 如果連顯示訊息都出錯，至少要在 Console 輸出
                Console.WriteLine($"ShowMessage 發生錯誤: {ex.Message}");
                Console.WriteLine($"原本要顯示的訊息: {message}");
            }
        }

        /// <summary>
        /// 視窗關閉事件 - 程式結束前清理所有資源
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _videoPlayer?.StopPlay();
                _videoPlayer?.Dispose();
                SimpleVideoPlayer.GlobalCleanup();
                _deviceManager?.Close();

                DahuaSDK.DeviceStatusChanged -= OnDeviceChanged;
                DahuaSDK.StatusMessage -= OnSDKMessage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"視窗關閉時發生錯誤: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
    }
}
