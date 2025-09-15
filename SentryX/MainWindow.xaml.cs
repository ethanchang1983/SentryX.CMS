using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SentryX
{
    public partial class MainWindow : Window
    {
        private DeviceManagerWindow? _deviceManager = null;
        
        // 將所有管理器改為私有字段，避免在 XAML 解析期間被存取
        private UIInitializationManager? _uiManager;
        private SplitScreenManager? _splitScreenManager;
        private VideoPlaybackManager? _playbackManager;
        private DeviceListManager? _deviceListManager;
        private PerformanceMonitorManager? _performanceManager;

        // 公開屬性，但添加 null 檢查
        public UIInitializationManager UIManager => _uiManager ?? throw new InvalidOperationException("UIManager 尚未初始化");
        public SplitScreenManager SplitScreenManager => _splitScreenManager ?? throw new InvalidOperationException("SplitScreenManager 尚未初始化");
        public VideoPlaybackManager PlaybackManager => _playbackManager ?? throw new InvalidOperationException("PlaybackManager 尚未初始化");
        public DeviceListManager DeviceListManager => _deviceListManager ?? throw new InvalidOperationException("DeviceListManager 尚未初始化");
        public PerformanceMonitorManager PerformanceManager => _performanceManager ?? throw new InvalidOperationException("PerformanceManager 尚未初始化");

        public MainWindow()
        {
            try
            {
                // 首先初始化 XAML 組件
                InitializeComponent();
                
                // 然後初始化管理器
                InitializeManagers();

                // 確保所有管理器都已初始化後才繼續
                if (_uiManager == null)
                {
                    throw new InvalidOperationException("UIManager 初始化失敗");
                }

                if (!_uiManager.InitializeUI())
                {
                    return;
                }

                SetupVideoArea();
                _uiManager.SubscribeEvents();
                _performanceManager?.StartMonitoring();

                _deviceListManager?.RefreshDeviceList();
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"主視窗初始化失敗：{ex.Message}", "嚴重錯誤",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine($"MainWindow 初始化異常：{ex}");
            }
        }

        private void InitializeManagers()
        {
            _uiManager = new UIInitializationManager(this);
            _splitScreenManager = new SplitScreenManager(this);
            _playbackManager = new VideoPlaybackManager(this, _splitScreenManager);
            _deviceListManager = new DeviceListManager(this);
            _performanceManager = new PerformanceMonitorManager(this, _splitScreenManager);
        }

        private void SetupVideoArea()
        {
            if (_uiManager?.IsUIInitialized != true)
            {
                Console.WriteLine("警告：UI 尚未初始化，跳過視頻區域設定");
                return;
            }

            try
            {
                if (VideoDisplayGrid == null)
                {
                    ShowMessage("❌ 視頻顯示區域初始化失敗：VideoDisplayGrid 控制項未找到");
                    return;
                }

                _splitScreenManager?.CreateSplitScreenLayout(1);
                ShowMessage("📺 視頻顯示區域準備完成（1分割模式）");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 視頻區域初始化失敗: {ex.Message}");
                Console.WriteLine($"SetupVideoArea 異常：{ex}");
            }
        }

        #region Event Handlers

        private void SplitScreenComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            try
            {
                if (SplitScreenComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    if (int.TryParse(selectedItem.Tag?.ToString(), out int splitCount))
                    {
                        _splitScreenManager?.CreateSplitScreenLayout(splitCount);
                        ShowMessage($"🔄 已切換到 {splitCount} 分割畫面模式");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 切換分割畫面時發生錯誤: {ex.Message}");
            }
        }

        private void StreamTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            try
            {
                if (StreamTypeComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string tag = selectedItem.Tag?.ToString() ?? "Main";
                    _playbackManager?.HandleStreamTypeChanged(tag);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StreamTypeComboBox_SelectionChanged 發生錯誤: {ex.Message}");
            }
        }

        private void DecodeTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            try
            {
                if (DecodeTypeComboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string tag = selectedItem.Tag?.ToString() ?? "Software";
                    _playbackManager?.HandleDecodeTypeChanged(tag);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DecodeTypeComboBox_SelectionChanged 發生錯誤: {ex.Message}");
            }
        }

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

        private void QuickAddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceManagerButton_Click(sender, e);
            ShowMessage("💡 請在設備管理視窗中填寫攝影機資訊");
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _deviceListManager?.RefreshDeviceList();
            UpdateStatusBar();
            ShowMessage("🔄 設備列表已手動刷新");
        }

        private void StartVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_deviceListManager == null || _playbackManager == null) return;

            var deviceId = _deviceListManager.SelectedDeviceId;
            if (string.IsNullOrEmpty(deviceId))
            {
                ShowMessage("❌ 未選擇設備，無法啟動視頻播放");
                if (StartVideoButton != null) StartVideoButton.IsEnabled = false;
                if (StopVideoButton != null) StopVideoButton.IsEnabled = false;
                return;
            }

            bool success;

            if (_deviceListManager.IsDeviceSelected)
            {
                // 選中的是設備本身，使用多通道播放
                var device = DahuaSDK.GetDevice(deviceId);
                if (device != null && device.ChannelCount > 1)
                {
                    ShowMessage($"🎬 開始 DVR/NVR 多通道播放模式...");
                    int successCount = _playbackManager.StartMultiChannelPlayback(deviceId);
                    success = successCount > 0;
                }
                else
                {
                    // 單通道設備，使用普通播放
                    int channel = 0;
                    success = _playbackManager.StartVideoPlayback(deviceId, channel);
                }
            }
            else
            {
                // 選中的是具體通道，使用單通道播放
                int channel = _deviceListManager.ExtractChannelFromSelection();
                success = _playbackManager.StartVideoPlayback(deviceId, channel);
            }

            if (StartVideoButton != null) StartVideoButton.IsEnabled = true;
            if (StopVideoButton != null) StopVideoButton.IsEnabled = success;
        }

        private void StopVideoButton_Click(object sender, RoutedEventArgs e)
        {
            int stoppedCount = _playbackManager?.StopAllPlayback() ?? 0;

            if (StartVideoButton != null) StartVideoButton.IsEnabled = true;
            if (StopVideoButton != null) StopVideoButton.IsEnabled = false;
        }

        private void StopAllVideoButton_Click(object sender, RoutedEventArgs e)
        {
            StopVideoButton_Click(sender, e);
        }

        private void DeviceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            if (DeviceListBox.SelectedItem is string selectedText)
            {
                _deviceListManager?.HandleDeviceSelection(selectedText);

                bool canPlay = !string.IsNullOrEmpty(_deviceListManager?.SelectedDeviceId) &&
                              _splitScreenManager?.SelectedPlayer != null;

                if (StartVideoButton != null) StartVideoButton.IsEnabled = canPlay;
            }
            else
            {
                if (StartVideoButton != null) StartVideoButton.IsEnabled = false;
            }
        }

        private void DeviceListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            // 修復錯誤：將錯誤的賦值語法改為正確的比較語法
            if (StartVideoButton?.IsEnabled == true && _splitScreenManager?.SelectedPlayer != null)
            {
                StartVideoButton_Click(sender, new RoutedEventArgs());
            }
            else if (_splitScreenManager?.SelectedPlayer == null)
            {
                ShowMessage("請先點擊選中一個分割區域");
            }
        }

        #endregion

        #region Public Methods for Managers

        public void OnDeviceChanged(DeviceInfo device)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            try
            {
                Dispatcher.Invoke(() =>
                {
                    _deviceListManager?.RefreshDeviceList();
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

        public void OnSDKMessage(string message)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            try
            {
                Dispatcher.Invoke(() => ShowMessage($"SDK: {message}"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnSDKMessage 發生錯誤: {ex.Message}");
            }
        }

        public void ShowMessage(string message)
        {
            try
            {
                string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

                if (_uiManager?.IsUIInitialized == true && StatusTextBlock != null)
                {
                    StatusTextBlock.Text += timestampedMessage + "\n";
                }

                Console.WriteLine(timestampedMessage);

                if (_uiManager?.IsUIInitialized == true && StatusScrollViewer != null)
                {
                    StatusScrollViewer.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ShowMessage 發生錯誤: {ex.Message}");
                Console.WriteLine($"原本要顯示的訊息: {message}");
            }
        }

        public void UpdateStatusBar()
        {
            if (_uiManager?.IsUIInitialized != true) return;

            try
            {
                var totalDevices = DahuaSDK.TotalDeviceCount;
                var onlineDevices = DahuaSDK.OnlineDeviceCount;

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

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _performanceManager?.StopMonitoring();
                _splitScreenManager?.StopAllVideoPlayers();
                SimpleVideoPlayer.GlobalCleanup();
                _deviceManager?.Close();
                _uiManager?.UnsubscribeEvents();
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