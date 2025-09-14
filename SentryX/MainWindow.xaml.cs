// MainWindow.xaml.cs - 主視窗的程式邏輯
// 這個檔案控制整個主介面的行為

// 引用必要的程式庫
using System;                               // 基本系統功能
using System.Collections.Generic;           // 集合類型
using System.Diagnostics;                   // 性能監控
using System.Linq;                          // LINQ 查詢功能
using System.Windows;                       // WPF 視窗功能
using System.Windows.Controls;              // WPF 控制項
using System.Windows.Input;                 // 滑鼠鍵盤事件
using System.Windows.Forms.Integration;     // 讓 WPF 可以使用 WinForms
using System.Windows.Forms;                 // Windows Forms（用來取得 HWND）
using System.Threading;                     // 執行緒功能

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
        /// 視頻播放器列表 - 支援多分割畫面
        /// </summary>
        private List<MultiViewPlayer> _videoPlayers = new List<MultiViewPlayer>();

        /// <summary>
        /// 當前分割畫面數量
        /// </summary>
        private int _currentSplitCount = 1;

        /// <summary>
        /// 記住當前選擇的解碼模式 - 預設改為軟體解碼
        /// </summary>
        private DecodeMode _currentDecodeMode = DecodeMode.Software;

        /// <summary>
        /// 當前選擇的碼流類型 - 預設為主碼流
        /// </summary>
        private VideoStreamType _currentStreamType = VideoStreamType.Main;

        /// <summary>
        /// 視頻資訊更新計時器
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? _videoInfoTimer;

        /// <summary>
        /// 性能監控計時器
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? _performanceTimer;

        /// <summary>
        /// 性能計數器
        /// </summary>
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _memoryCounter;

        /// <summary>
        /// 當前選中的分割區域播放器
        /// </summary>
        private MultiViewPlayer? _selectedPlayer = null;

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

            // 第5步：設定視頻資訊更新計時器
            SetupVideoInfoTimer();

            // 第6步：設定性能監控
            SetupPerformanceMonitoring();
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
            ShowMessage("🔧 預設解碼模式已設為 CPU 軟體解碼（相容性最佳）");
            ShowMessage("📡 預設碼流類型已設為主碼流（高畫質）");
            ShowMessage("🖱️ 點擊分割區域選中，雙擊設備通道加入選中區域");

            // 更新設備列表和系統狀態顯示
            RefreshDeviceList();
            UpdateStatusBar();
        }

        /// <summary>
        /// 設定視頻顯示區域 - 支援多分割畫面
        /// </summary>
        private void SetupVideoArea()
        {
            try
            {
                // 初始化為1分割畫面
                CreateSplitScreenLayout(1);
                ShowMessage("📺 視頻顯示區域準備完成（1分割模式）");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 視頻區域初始化失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定視頻資訊更新計時器
        /// </summary>
        private void SetupVideoInfoTimer()
        {
            _videoInfoTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // 每秒更新一次
            };
            _videoInfoTimer.Tick += UpdateVideoInfo;
            _videoInfoTimer.Start();
        }

        /// <summary>
        /// 設定性能監控
        /// </summary>
        private void SetupPerformanceMonitoring()
        {
            try
            {
                // 初始化性能計數器
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");

                // 設定性能監控計時器
                _performanceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2) // 每2秒更新一次
                };
                _performanceTimer.Tick += UpdatePerformanceInfo;
                _performanceTimer.Start();

                ShowMessage("🎯 性能監控已啟動");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 性能監控初始化失敗: {ex.Message}");
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

        // === 新增：多分割畫面相關方法 ===

        /// <summary>
        /// 分割畫面選擇改變事件
        /// </summary>
        private void SplitScreenComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (SplitScreenComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    if (int.TryParse(selectedItem.Tag?.ToString(), out int splitCount))
                    {
                        _currentSplitCount = splitCount;
                        CreateSplitScreenLayout(splitCount);
                        ShowMessage($"🔄 已切換到 {splitCount} 分割畫面模式");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 切換分割畫面時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 碼流類型選擇改變事件
        /// </summary>
        private void StreamTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (StreamTypeComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string tag = selectedItem.Tag?.ToString() ?? "Main";

                    switch (tag)
                    {
                        case "Main":
                            _currentStreamType = VideoStreamType.Main;
                            ShowMessage("已切換到主碼流模式 (高解析度，高碼率)");
                            break;

                        case "Sub":
                            _currentStreamType = VideoStreamType.Sub;
                            ShowMessage("已切換到輔碼流模式 (低解析度，低碼率，適合多路預覽)");
                            break;
                    }

                    // 如果有播放器在運行，提示用戶
                    bool hasPlayingVideo = _videoPlayers.Any(p => p.IsPlaying);
                    if (hasPlayingVideo)
                    {
                        ShowMessage("提示：碼流類型變更將在下次播放時生效");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StreamTypeComboBox_SelectionChanged 發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 解碼模式選擇改變事件 - 預設為軟體解碼
        /// </summary>
        private void DecodeTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DecodeTypeComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string tag = selectedItem.Tag?.ToString() ?? "Software";

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
                            _currentDecodeMode = DecodeMode.Auto;
                            ShowMessage("已切換到自動選擇模式 (先試硬體，再試軟體)");
                            break;
                    }

                    // 如果有播放器在運行，提示用戶
                    bool hasPlayingVideo = _videoPlayers.Any(p => p.IsPlaying);
                    if (hasPlayingVideo)
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
        /// 建立分割畫面佈局 - 增加選擇功能
        /// </summary>  
        private void CreateSplitScreenLayout(int splitCount)
        {
            try
            {
                // 停止並清理所有現有播放器
                StopAllVideoPlayers();

                // 清除現有佈局
                VideoDisplayGrid.Children.Clear();
                VideoDisplayGrid.RowDefinitions.Clear();
                VideoDisplayGrid.ColumnDefinitions.Clear();

                // 計算網格佈局
                int gridSize = (int)Math.Ceiling(Math.Sqrt(splitCount));
                
                // 建立行和列定義
                for (int i = 0; i < gridSize; i++)
                {
                    VideoDisplayGrid.RowDefinitions.Add(new RowDefinition());
                    VideoDisplayGrid.ColumnDefinitions.Add(new ColumnDefinition());
                }

                // 建立視頻播放器
                _videoPlayers.Clear();
                _selectedPlayer = null; // 重置選中的播放器
                int panelIndex = 0;

                for (int row = 0; row < gridSize && panelIndex < splitCount; row++)
                {
                    for (int col = 0; col < gridSize && panelIndex < splitCount; col++)
                    {
                        var player = new MultiViewPlayer(panelIndex);
                        
                        // 訂閱選中事件
                        player.Selected += OnPlayerSelected;
                        
                        _videoPlayers.Add(player);

                        // 設定網格位置
                        Grid.SetRow(player.HostControl, row);
                        Grid.SetColumn(player.HostControl, col);

                        // 加入到顯示網格
                        VideoDisplayGrid.Children.Add(player.HostControl);

                        panelIndex++;
                    }
                }

                // 預設選中第一個分割區域
                if (_videoPlayers.Count > 0)
                {
                    SelectPlayer(_videoPlayers[0]);
                }

                ShowMessage($"📐 建立了 {splitCount} 個視頻顯示區域");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 建立分割畫面佈局失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放器被選中事件處理
        /// </summary>
        private void OnPlayerSelected(MultiViewPlayer selectedPlayer)
        {
            try
            {
                SelectPlayer(selectedPlayer);
                ShowMessage($"🎯 已選中分割區域 {selectedPlayer.Index + 1}");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 選擇分割區域時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 選中指定的播放器
        /// </summary>
        private void SelectPlayer(MultiViewPlayer player)
        {
            // 取消之前選中的播放器
            if (_selectedPlayer != null)
            {
                _selectedPlayer.IsSelected = false;
            }

            // 設定新選中的播放器
            _selectedPlayer = player;
            _selectedPlayer.IsSelected = true;
        }

        /// <summary>
        /// 停止所有視頻播放器
        /// </summary>
        private void StopAllVideoPlayers()
        {
            foreach (var player in _videoPlayers)
            {
                // 移除事件訂閱
                player.Selected -= OnPlayerSelected;
                player.Dispose();
            }
            _videoPlayers.Clear();
            _selectedPlayer = null;
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
        /// 開始播放方法 - 修改為支援選中區域播放
        /// </summary>
        private void StartVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedDeviceId))
            {
                ShowMessage("請先在左側選擇一個攝影機設備或通道");
                return;
            }

            if (_selectedPlayer == null)
            {
                ShowMessage("請先點擊選中一個分割區域");
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
                // 使用選中的播放器
                var targetPlayer = _selectedPlayer;

                // 從選中的項目中提取通道號
                int channel = ExtractChannelFromSelection();

                string decodeModeText = GetDecodeModeText();
                string streamTypeText = _currentStreamType == VideoStreamType.Main ? "主碼流" : "輔碼流";
                ShowMessage($"準備使用{decodeModeText}在分割區域 {targetPlayer.Index + 1} 播放 {device.Name} 通道{channel + 1} 的{streamTypeText}視頻...");

                // 開始播放
                if (targetPlayer.StartPlay(device.LoginHandle, channel, _currentDecodeMode, _currentStreamType, device.Name))
                {
                    StartVideoButton.IsEnabled = true;
                    StopVideoButton.IsEnabled = true;
                    ShowMessage($"開始播放 {device.Name} 通道{channel + 1} 的即時視頻 ({decodeModeText}, {streamTypeText}) - 分割區域 {targetPlayer.Index + 1}");
                    
                    // 自動選中下一個可用的分割區域
                    SelectNextAvailablePlayer();
                }
                else
                {
                    ShowMessage("視頻播放啟動失敗，請檢查設備連接或嘗試其他解碼模式");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"視頻播放發生錯誤：{ex.Message}");
            }
        }

        /// <summary>
        /// 自動選中下一個可用的分割區域
        /// </summary>
        private void SelectNextAvailablePlayer()
        {
            try
            {
                var nextPlayer = _videoPlayers.FirstOrDefault(p => !p.IsPlaying);
                if (nextPlayer != null)
                {
                    SelectPlayer(nextPlayer);
                    ShowMessage($"🎯 自動選中下一個可用區域：分割區域 {nextPlayer.Index + 1}");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 自動選擇下一個區域時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止播放方法 - 修改為支援多分割畫面
        /// </summary>
        private void StopVideoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int stoppedCount = 0;
                foreach (var player in _videoPlayers)
                {
                    if (player.IsPlaying)
                    {
                        player.StopPlay();
                        stoppedCount++;
                    }
                }

                if (stoppedCount > 0)
                {
                    StartVideoButton.IsEnabled = true;
                    StopVideoButton.IsEnabled = false;
                    ShowMessage($"已停止 {stoppedCount} 個視頻播放");
                }
                else
                {
                    ShowMessage("沒有正在播放的視頻");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"停止播放時發生錯誤：{ex.Message}");
            }
        }

        /// <summary>
        /// 停止所有播放按鈕事件
        /// </summary>
        private void StopAllVideoButton_Click(object sender, RoutedEventArgs e)
        {
            StopVideoButton_Click(sender, e);
        }

        // === 新增：視頻資訊更新方法 ===

        /// <summary>
        /// 更新視頻資訊顯示 - 修正 null 警告
        /// </summary>
        private void UpdateVideoInfo(object? sender, EventArgs e)
        {
            try
            {
                var playingPlayers = _videoPlayers.Where(p => p.IsPlaying).ToList();
                
                if (playingPlayers.Count > 0)
                {
                    // 顯示第一個播放中的視頻資訊
                    var firstPlayer = playingPlayers.First();
                    if (firstPlayer.VideoInfo != null)
                    {
                        var info = firstPlayer.VideoInfo;
                        ResolutionTextBlock.Text = $"{info.Width}x{info.Height}";
                        FpsTextBlock.Text = $"{info.Fps:F1}";
                        BitrateTextBlock.Text = $"{info.Bitrate:F1} kbps";
                    }

                    // 更新性能統計
                    PlayingCountTextBlock.Text = playingPlayers.Count.ToString();
                    double totalBitrate = playingPlayers.Where(p => p.VideoInfo != null)
                                                      .Sum(p => p.VideoInfo!.Bitrate);
                    TotalBitrateTextBlock.Text = $"{totalBitrate:F1} kbps";
                }
                else
                {
                    ResolutionTextBlock.Text = "--";
                    FpsTextBlock.Text = "--";
                    BitrateTextBlock.Text = "--";
                    PlayingCountTextBlock.Text = "0";
                    TotalBitrateTextBlock.Text = "0 kbps";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateVideoInfo 發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新性能資訊顯示 - 修正 null 警告
        /// </summary>
        private void UpdatePerformanceInfo(object? sender, EventArgs e)
        {
            try
            {
                if (_cpuCounter != null && _memoryCounter != null)
                {
                    // 取得 CPU 和可用記憶體的最新數據
                    float cpuUsage = _cpuCounter.NextValue();
                    float availableMemory = _memoryCounter.NextValue();

                    // 更新介面上的顯示
                    CpuUsageTextBlock.Text = $"{cpuUsage:F1}%";
                    MemoryUsageTextBlock.Text = $"{availableMemory:F1} MB";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdatePerformanceInfo 發生錯誤: {ex.Message}");
            }
        }

        // === 列表選擇事件 - 當用戶在設備列表中選擇項目時 ===

        /// <summary>
        /// 修改設備選擇邏輯 - 適配多分割畫面
        /// </summary>
        private void DeviceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
                    StartVideoButton.IsEnabled = selectedDevice.IsOnline && (_selectedPlayer != null);
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
        /// 設備列表雙擊事件 - 用戶雙擊設備時直接開始播放到選中區域
        /// </summary>
        private void DeviceListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 如果開始播放按鈕可用，就模擬點擊它
            if (StartVideoButton.IsEnabled && _selectedPlayer != null)
            {
                StartVideoButton_Click(sender, new RoutedEventArgs());
            }
            else if (_selectedPlayer == null)
            {
                ShowMessage("請先點擊選中一個分割區域");
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
                _videoInfoTimer?.Stop();
                _performanceTimer?.Stop();
                StopAllVideoPlayers();
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
