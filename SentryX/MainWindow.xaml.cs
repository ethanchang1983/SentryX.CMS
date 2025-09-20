using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SentryX
{
    public partial class MainWindow : Window
    {
        private DeviceManagerWindow? _deviceManager = null;

        // 將所有管理器改为私有字段，避免在 XAML 解析期间被存取
        private UIInitializationManager? _uiManager;
        private SplitScreenManager? _splitScreenManager;
        private VideoPlaybackManager? _playbackManager;
        private PlaybackManager? _playbackControlManager;
        private DeviceListManager? _deviceListManager;
        private PerformanceMonitorManager? _performanceManager;
        private PlaybackControlDialog? _playbackControlDialog;
        
        // 🔥 新增：語音對講管理器
        private VoiceIntercomManager? _voiceIntercomManager;

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
            _playbackControlManager = new PlaybackManager(this, _splitScreenManager);
            _deviceListManager = new DeviceListManager(this);
            _performanceManager = new PerformanceMonitorManager(this, _splitScreenManager);
            
            // 🔥 新增：初始化語音對講管理器
            _voiceIntercomManager = new VoiceIntercomManager(this);
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

                // 初始化按鈕狀態
                UpdateButtonStates();
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
                UpdateButtonStates();
                return;
            }

            // 檢查選中的播放器是否在回放模式
            var selectedPlayer = _splitScreenManager?.SelectedPlayer;
            if (selectedPlayer != null && _playbackControlManager?.IsInPlaybackMode(selectedPlayer.Index) == true)
            {
                ShowMessage("⚠️ 選中的分割區域正在回放模式，請選擇其他區域或先切換回實況模式");

                // 修正第 229 行：確保 _splitScreenManager 不為 null
                if (_splitScreenManager != null)
                {
                    var availablePlayer = _splitScreenManager.VideoPlayers
                        ?.FirstOrDefault(p => !p.IsPlaying && !(_playbackControlManager?.IsInPlaybackMode(p.Index) ?? false));

                    if (availablePlayer != null)
                    {
                        _splitScreenManager.SelectPlayer(availablePlayer);
                        ShowMessage($"🔄 已自動選擇可用的分割區域：{availablePlayer.Index + 1}");
                    }
                    else
                    {
                        ShowMessage("❌ 沒有可用的分割區域，請停止某些播放或切換回實況模式");
                        return;
                    }
                }
                else
                {
                    ShowMessage("❌ 分割畫面管理器未初始化");
                    return;
                }
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

            // 統一更新按鈕狀態
            UpdateButtonStates();
        }

        private void StopVideoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                if (selectedPlayer == null)
                {
                    ShowMessage("❌ 請先選擇一個分割區域");
                    return;
                }

                bool hasStoppedSomething = false;

                // 檢查選中播放器是否在回放模式，如果是則停止回放
                if (_playbackControlManager?.IsInPlaybackMode(selectedPlayer.Index) == true)
                {
                    if (_playbackControlManager.StopPlayback(selectedPlayer.Index))
                    {
                        ShowMessage($"🔄 已停止分割區域 {selectedPlayer.Index + 1} 的回放");
                        hasStoppedSomething = true;
                    }
                }

                // 檢查選中播放器是否在實況播放，如果是則停止實況
                if (selectedPlayer.IsPlaying)
                {
                    if (_splitScreenManager != null && _splitScreenManager.StopSelectedPlayer())
                    {
                        ShowMessage($"🔄 已停止分割區域 {selectedPlayer.Index + 1} 的實況播放");
                        hasStoppedSomething = true;
                    }
                }

                if (hasStoppedSomething)
                {
                    // 清除選中分割區域的顯示內容
                    try
                    {
                        selectedPlayer.SetPlaybackMode(false);
                        selectedPlayer.RefreshDisplay();
                        ShowMessage($"🧹 已清除分割區域 {selectedPlayer.Index + 1} 的顯示內容");
                    }
                    catch (Exception ex)
                    {
                        ShowMessage($"清除分割區域 {selectedPlayer.Index + 1} 顯示時發生錯誤：{ex.Message}");
                    }

                    ShowMessage($"✅ 分割區域 {selectedPlayer.Index + 1} 播放已完全停止並清除顯示");
                }
                else
                {
                    ShowMessage($"⚠️ 分割區域 {selectedPlayer.Index + 1} 沒有正在播放的內容");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"停止選中區域播放時發生錯誤：{ex.Message}");
            }
            finally
            {
                // 統一更新按鈕狀態
                UpdateButtonStates();
            }
        }

        private void StopAllVideoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowMessage("🛑 開始停止所有分割區域的播放...");

                // 先停止所有回放會話
                _playbackControlManager?.StopAllPlaybacks();
                ShowMessage("🔄 已停止所有回放會話");

                // 停止所有實況播放
                int stoppedCount = _playbackManager?.StopAllPlayback() ?? 0;

                // 強制清除所有分割區域的顯示內容
                if (_splitScreenManager != null)
                {
                    foreach (var player in _splitScreenManager.VideoPlayers)
                    {
                        try
                        {
                            // 設定為非回放模式
                            player.SetPlaybackMode(false);

                            // 強制重新整理顯示（清除殘留畫面）
                            player.RefreshDisplay();
                        }
                        catch (Exception ex)
                        {
                            ShowMessage($"清除分割區域 {player.Index + 1} 顯示時發生錯誤：{ex.Message}");
                        }
                    }

                    ShowMessage("🧹 已強制清除所有分割區域的顯示內容");
                }

                if (stoppedCount > 0)
                {
                    ShowMessage($"✅ 已停止 {stoppedCount} 個實況播放");
                }

                ShowMessage("🛑 所有播放已完全停止並清除顯示");
            }
            catch (Exception ex)
            {
                ShowMessage($"停止所有播放時發生錯誤：{ex.Message}");
            }
            finally
            {
                // 統一更新按鈕狀態
                UpdateButtonStates();
            }
        }

        private void DeviceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            if (DeviceListBox.SelectedItem is string selectedText)
            {
                _deviceListManager?.HandleDeviceSelection(selectedText);
            }

            // 統一更新按鈑狀態
            UpdateButtonStates();
        }

        private void DeviceListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_uiManager?.IsUIInitialized != true) return;

            // 檢查是否能夠開始播放
            if (StartVideoButton?.IsEnabled == true && _splitScreenManager?.SelectedPlayer != null)
            {
                StartVideoButton_Click(sender, new RoutedEventArgs());
            }
            else if (_splitScreenManager?.SelectedPlayer == null)
            {
                ShowMessage("請先點擊選中一個分割區域");
            }
            else
            {
                ShowMessage("⚠️ 當前無法開始播放，請檢查設備選擇和分割區域狀態");
            }
        }

        /// <summary>
        /// 回放控制按鈕點擊事件
        /// </summary>
        private void PlaybackControlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_playbackControlManager == null)
                {
                    ShowMessage("❌ 回放管理器尚未初始化");
                    return;
                }

                // 檢查是否已經有回放控制視窗開啟
                if (_playbackControlDialog != null && _playbackControlDialog.IsVisible)
                {
                    _playbackControlDialog.Activate();
                    _playbackControlDialog.WindowState = WindowState.Normal;
                    ShowMessage("回放控制視窗已激活");
                    return;
                }

                // 建立新的回放控制視窗
                _playbackControlDialog = new PlaybackControlDialog(this, _playbackControlManager);
                _playbackControlDialog.Owner = this;
                _playbackControlDialog.Closed += (s, args) =>
                {
                    _playbackControlDialog = null;
                    ShowMessage("回放控制視窗已關閉");
                };

                _playbackControlDialog.Show();
                ShowMessage("📹 回放控制視窗已開啟");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 開啟回放控制視窗失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 快速切換到回放模式（過去1小時）- 修正版本
        /// </summary>
        private void QuickPlaybackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_playbackControlManager == null)
                {
                    ShowMessage("❌ 回放管理器尚未初始化");
                    return;
                }

                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                if (selectedPlayer == null)
                {
                    ShowMessage("❌ 請先選擇一個分割區域");
                    return;
                }

                if (!selectedPlayer.IsPlaying)
                {
                    ShowMessage("❌ 選中的區域沒有正在播放的視頻");
                    return;
                }

                // 修正：檢查是否已經在回放模式
                if (_playbackControlManager.IsInPlaybackMode(selectedPlayer.Index))
                {
                    ShowMessage("⚠️ 選中的區域已經在回放模式，請先切換回實況模式");
                    return;
                }

                // 快速設定為過去1小時的回放
                var endTime = DateTime.Now;
                var startTime = endTime.AddHours(-1);

                ShowMessage($"🕐 快速切換到回放模式（過去1小時）：{startTime:HH:mm:ss} - {endTime:HH:mm:ss}");

                // 修正：使用按索引的方法
                if (_playbackControlManager.SwitchToPlaybackByIndex(selectedPlayer.Index, startTime, endTime))
                {
                    ShowMessage("✅ 快速回放模式啟動成功");

                    // 修正：通知回放控制對話框更新狀態（如果開啟的話）
                    NotifyPlaybackDialogUpdate();
                }
                else
                {
                    ShowMessage("❌ 快速回放模式啟動失敗");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 快速回放切換失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 切換回實況模式 - 修正版本
        /// </summary>
        private void BackToLiveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_playbackControlManager == null)
                {
                    ShowMessage("❌ 回放管理器尚未初始化");
                    return;
                }

                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                if (selectedPlayer == null)
                {
                    ShowMessage("❌ 請先選擇一個分割區域");
                    return;
                }

                if (!_playbackControlManager.IsInPlaybackMode(selectedPlayer.Index))
                {
                    ShowMessage("⚠️ 選中的區域不在回放模式");
                    return;
                }

                ShowMessage("🔄 正在切換回實況模式...");

                // 修正：使用按索引的方法
                if (_playbackControlManager.SwitchToLiveByIndex(selectedPlayer.Index))
                {
                    ShowMessage("✅ 已切換回實況模式");

                    // 修正：通知回放控制對話框更新狀態（如果開啟的話）
                    NotifyPlaybackDialogUpdate();
                }
                else
                {
                    ShowMessage("❌ 切換實況模式失敗");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 切換實況模式失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// PTZ控制按鈕點擊事件
        /// </summary>
        private void PTZControlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 檢查是否有選中的播放器
                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                if (selectedPlayer == null)
                {
                    ShowMessage("❌ 請先選擇一個分割區域");
                    return;
                }

                // 檢查播放器是否有正在播放的內容
                if (!selectedPlayer.IsPlaying)
                {
                    ShowMessage("❌ 選中的分割區域沒有正在播放的視頻");
                    return;
                }

                // 檢查是否有現有的PTZ控制視窗
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is PTZControlWindow existingPTZWindow)
                    {
                        existingPTZWindow.Activate();
                        existingPTZWindow.WindowState = WindowState.Normal;
                        ShowMessage("PTZ控制視窗已激活");
                        return;
                    }
                }

                // 創建新的PTZ控制視窗
                var ptzWindow = new PTZControlWindow(this);
                ptzWindow.Owner = this;
                ptzWindow.Show();
                ShowMessage("🎮 PTZ控制視窗已開啟");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 開啟PTZ控制視窗失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔥 IVS 畫線規則顯示切換按鈕點擊事件
        /// </summary>
        private void IVSToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                if (selectedPlayer == null)
                {
                    ShowMessage("❌ 請先選擇一個分割區域");
                    return;
                }

                if (!selectedPlayer.IsPlaying)
                {
                    ShowMessage("❌ 選中的分割區域沒有正在播放的視頻");
                    return;
                }

                var videoPlayer = selectedPlayer.GetVideoPlayer();
                if (videoPlayer == null)
                {
                    ShowMessage("❌ 無法取得視頻播放器實例");
                    return;
                }

                // 🔥 檢查是否支援 IVS
                if (!videoPlayer.IsIVSSupported())
                {
                    ShowMessage($"⚠️ 當前解碼模式 ({videoPlayer.DecodeMode}) 不支援 IVS 規則顯示");
                    ShowMessage("💡 請切換到軟體解碼模式以使用 IVS 功能");
                    return;
                }

                // 切換 IVS 狀態
                bool newState = videoPlayer.ToggleIVSRender();
                
                // 更新按鈕顯示
                UpdateIVSButtonDisplay(newState);

                // 顯示狀態變更訊息
                string statusMessage = newState ? "已啟用" : "已停用";
                ShowMessage($"🎯 分割區域 {selectedPlayer.Index + 1} 的 IVS 畫線規則顯示{statusMessage}");

                Console.WriteLine($"IVS 切換: 播放器 {selectedPlayer.Index + 1}, 新狀態: {newState}");
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 切換 IVS 顯示時發生錯誤: {ex.Message}");
                Console.WriteLine($"IVSToggleButton_Click 異常: {ex}");
            }
        }

        /// <summary>
        /// 🔥 語音接收按鈕點擊事件 - 修正版本
        /// </summary>
        private void VoiceReceiveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_voiceIntercomManager == null)
                {
                    ShowMessage("❌ 語音對講管理器尚未初始化");
                    return;
                }

                // 如果目前正在接收音頻，則停止
                if (_voiceIntercomManager.IsListening)
                {
                    _voiceIntercomManager.StopListening();
                    UpdateVoiceReceiveButtonDisplay(false);
                    UpdateVoiceTalkButtonState(); // 同時更新對講按鈕狀態
                    ShowMessage("🔇 語音接收已停止");
                    return;
                }

                // 檢查是否有選中的播放器
                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                if (selectedPlayer == null)
                {
                    ShowMessage("❌ 請先選擇一個分割區域");
                    return;
                }

                // 檢查播放器是否有正在播放的內容
                if (!selectedPlayer.IsPlaying)
                {
                    ShowMessage("❌ 選中的分割區域沒有正在播放的視頻");
                    return;
                }

                // 獲取選中播放器對應的設備資訊
                var playbackState = selectedPlayer.GetCurrentPlaybackState();
                if (playbackState == null || string.IsNullOrEmpty(playbackState.DeviceId))
                {
                    ShowMessage("❌ 無法取得分割區域對應的設備資訊");
                    return;
                }

                ShowMessage($"🎙️ 正在啟動分割區域 {selectedPlayer.Index + 1} 的語音接收...");

                // 🔥 修正：查詢設備支援的編碼格式，並選擇最佳編碼
                var supportedEncodings = _voiceIntercomManager.QuerySupportedEncodings(playbackState.DeviceId);
                if (supportedEncodings.Count > 0) // 🔥 修正：改為 > 0
                {
                    ShowMessage($"📊 設備支援 {supportedEncodings.Count} 種音訊編碼格式");
                    
                    // 🔥 修正：優先選擇 PCM，符合 Demo 的做法
                    var preferredEncoding = supportedEncodings.FirstOrDefault(e => e.EncodeType == NetSDKCS.EM_TALK_CODING_TYPE.PCM) ??
                                          supportedEncodings.FirstOrDefault(e => e.EncodeType == NetSDKCS.EM_TALK_CODING_TYPE.G711a) ??
                                          supportedEncodings.FirstOrDefault(e => e.EncodeType == NetSDKCS.EM_TALK_CODING_TYPE.AAC) ??
                                          supportedEncodings.First();
                    
                    _voiceIntercomManager.SetAudioEncoding(preferredEncoding.EncodeType);
                    ShowMessage($"🎯 選擇編碼：{preferredEncoding.DisplayName}");
                }

                // 開始接收音頻
                if (_voiceIntercomManager.StartListening(playbackState.DeviceId))
                {
                    UpdateVoiceReceiveButtonDisplay(true);
                    UpdateVoiceTalkButtonState(); // 同時更新對講按鈕狀態
                    ShowMessage($"✅ 分割區域 {selectedPlayer.Index + 1} 語音接收已啟動");
                    ShowMessage($"💡 使用編碼：{_voiceIntercomManager.CurrentEncodeConfig.DisplayName}");
                }
                else
                {
                    UpdateVoiceReceiveButtonDisplay(false);
                    ShowMessage($"❌ 分割區域 {selectedPlayer.Index + 1} 語音接收啟動失敗");
                    ShowMessage("🔧 請檢查設備是否支援語音對講功能");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 語音接收按鈕操作時發生錯誤: {ex.Message}");
                Console.WriteLine($"VoiceReceiveButton_Click 異常: {ex}");
                // 🔥 發生錯誤時確保按鈕狀態正確
                UpdateVoiceReceiveButtonDisplay(false);
                UpdateVoiceTalkButtonState();
            }
        }

        /// <summary>
        /// 🔥 語音對講按鈕點擊事件 - 修正版本
        /// </summary>
        private void VoiceTalkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_voiceIntercomManager == null)
                {
                    ShowMessage("❌ 語音對講管理器尚未初始化");
                    return;
                }

                // 如果目前正在對講，則停止
                if (_voiceIntercomManager.IsSpeaking)
                {
                    _voiceIntercomManager.StopSpeaking();
                    UpdateVoiceTalkButtonDisplay(false);
                    ShowMessage("🔇 語音對講已停止");
                    return;
                }

                // 檢查是否有選中的播放器
                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                if (selectedPlayer == null)
                {
                    ShowMessage("❌ 請先選擇一個分割區域");
                    return;
                }

                // 檢查播放器是否有正在播放的內容
                if (!selectedPlayer.IsPlaying)
                {
                    ShowMessage("❌ 選中的分割區域沒有正在播放的視頻");
                    return;
                }

                // 🔥 修正：語音對講作為獨立功能，如果沒有語音接收則自動啟動
                if (!_voiceIntercomManager.IsListening)
                {
                    // 獲取選中播放器對應的設備資訊
                    var playbackState = selectedPlayer.GetCurrentPlaybackState();
                    if (playbackState == null || string.IsNullOrEmpty(playbackState.DeviceId))
                    {
                        ShowMessage("❌ 無法取得分割區域對應的設備資訊");
                        return;
                    }

                    ShowMessage($"🎙️ 自動啟動語音接收以支援對講功能...");

                    // 查詢設備支援的編碼格式
                    var supportedEncodings = _voiceIntercomManager.QuerySupportedEncodings(playbackState.DeviceId);
                    if (supportedEncodings.Count > 0) // 🔥 修正：改為 > 0
                    {
                        ShowMessage($"📊 設備支援 {supportedEncodings.Count} 種音訊編碼格式");
                        
                        // 🔥 修正：優先選擇 PCM，符合 Demo 的做法
                        var preferredEncoding = supportedEncodings.FirstOrDefault(e => e.EncodeType == NetSDKCS.EM_TALK_CODING_TYPE.PCM) ??
                                              supportedEncodings.FirstOrDefault(e => e.EncodeType == NetSDKCS.EM_TALK_CODING_TYPE.G711a) ??
                                              supportedEncodings.FirstOrDefault(e => e.EncodeType == NetSDKCS.EM_TALK_CODING_TYPE.AAC) ??
                                              supportedEncodings.First();
                        
                        _voiceIntercomManager.SetAudioEncoding(preferredEncoding.EncodeType);
                        ShowMessage($"🎯 選擇編碼：{preferredEncoding.DisplayName}");
                    }

                    // 🔥 開始語音接收，增加詳細的錯誤檢查
                    if (!_voiceIntercomManager.StartListening(playbackState.DeviceId))
                    {
                        ShowMessage($"❌ 無法啟動語音接收，對講功能無法使用");
                        ShowMessage("🔧 可能原因：");
                        ShowMessage("  - 設備不支援語音對講");
                        ShowMessage("  - 設備已被其他應用使用");
                        ShowMessage("  - 網路連線問題");
                        return;
                    }

                    UpdateVoiceReceiveButtonDisplay(true);
                    ShowMessage($"✅ 語音接收已自動啟動");
                }

                ShowMessage($"🎤 正在啟動分割區域 {selectedPlayer.Index + 1} 的語音對講...");

                // 🔥 開始對講，增加詳細的錯誤檢查
                if (_voiceIntercomManager.StartSpeaking())
                {
                    UpdateVoiceTalkButtonDisplay(true);
                    ShowMessage($"✅ 分割區域 {selectedPlayer.Index + 1} 語音對講已啟動");
                    ShowMessage($"💡 使用編碼：{_voiceIntercomManager.CurrentEncodeConfig.DisplayName}");
                    ShowMessage($"🎤 請對著麥克風說話，音頻將發送到設備");
                    ShowMessage($"📞 對講句柄：{_voiceIntercomManager.CurrentDeviceHandle}");
                }
                else
                {
                    UpdateVoiceTalkButtonDisplay(false);
                    ShowMessage($"❌ 分割區域 {selectedPlayer.Index + 1} 語音對講啟動失敗");
                    ShowMessage("🔧 可能原因：");
                    ShowMessage("  - 麥克風設備未連接");
                    ShowMessage("  - 麥克風權限不足");
                    ShowMessage("  - 麥克風被其他應用占用");
                    ShowMessage("  - 設備不支援此編碼格式");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"❌ 語音對講按鈕操作時發生錯誤: {ex.Message}");
                Console.WriteLine($"VoiceTalkButton_Click 異常: {ex}");
                // 🔥 發生錯誤時確保按鈕狀態正確
                UpdateVoiceTalkButtonDisplay(false);
            }
        }

        #endregion

        #region Button Display Methods

        /// <summary>
        /// 🔥 更新 IVS 按鈕的顯示狀態
        /// </summary>
        private void UpdateIVSButtonDisplay(bool ivsEnabled)
        {
            try
            {
                if (IVSToggleButton != null)
                {
                    if (ivsEnabled)
                    {
                        IVSToggleButton.Content = "🎯 IVS開啟";
                        IVSToggleButton.Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(33, 150, 243)); // 藍色
                        IVSToggleButton.ToolTip = "點擊關閉 IVS 智能分析畫線規則顯示";
                    }
                    else
                    {
                        IVSToggleButton.Content = "🎯 IVS關閉";
                        IVSToggleButton.Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(158, 158, 158)); // 灰色
                        IVSToggleButton.ToolTip = "點擊開啟 IVS 智能分析畫線規則顯示";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新 IVS 按鈕顯示時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔥 更新語音接收按鈕的顯示狀態
        /// </summary>
        private void UpdateVoiceReceiveButtonDisplay(bool isListening)
        {
            try
            {
                if (VoiceReceiveButton != null)
                {
                    if (isListening)
                    {
                        VoiceReceiveButton.Content = "🔊 音訊開啟";
                        VoiceReceiveButton.Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(76, 175, 80)); // 綠色
                        VoiceReceiveButton.ToolTip = "點擊停止接收設備音訊";
                    }
                    else
                    {
                        VoiceReceiveButton.Content = "🔇 音訊關閉";
                        VoiceReceiveButton.Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(158, 158, 158)); // 灰色
                        VoiceReceiveButton.ToolTip = "點擊開始接收選中分割區域的設備音訊";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新語音接收按鈕顯示時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔥 更新語音對講按鈕的顯示狀態
        /// </summary>
        private void UpdateVoiceTalkButtonDisplay(bool isTalking)
        {
            try
            {
                if (VoiceTalkButton != null)
                {
                    if (isTalking)
                    {
                        VoiceTalkButton.Content = "🎤 對講開啟";
                        VoiceTalkButton.Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(33, 150, 243)); // 藍色
                        VoiceTalkButton.ToolTip = "點擊停止語音對講";
                    }
                    else
                    {
                        VoiceTalkButton.Content = "🎤 對講關閉";
                        VoiceTalkButton.Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(255, 87, 34)); // 橘紅色
                        VoiceTalkButton.ToolTip = "點擊開始與選中分割區域的設備進行語音對講";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新語音對講按鈕顯示時發生錯誤: {ex.Message}");
            }
        }

        #endregion

        #region Button State Methods

        /// <summary>
        /// 🔥 更新 IVS 按鈕狀態（在 UpdateButtonStates 方法中調用）
        /// </summary>
        private void UpdateIVSButtonState()
        {
            try
            {
                if (IVSToggleButton == null) return;

                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                bool hasPlayingVideo = selectedPlayer?.IsPlaying == true;

                if (!hasPlayingVideo)
                {
                    // 沒有播放時，停用按鈕
                    IVSToggleButton.IsEnabled = false;
                    IVSToggleButton.Content = "🎯 IVS關閉";
                    IVSToggleButton.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(200, 200, 200)); // 淺灰色
                    IVSToggleButton.ToolTip = "需要先播放視頻才能使用 IVS 功能";
                    return;
                }

                var videoPlayer = selectedPlayer?.GetVideoPlayer();
                if (videoPlayer != null)
                {
                    bool supportsIVS = videoPlayer.IsIVSSupported();
                    
                    if (supportsIVS)
                    {
                        // 軟體解碼模式：支援 IVS
                        IVSToggleButton.IsEnabled = true;
                        bool currentIVSState = videoPlayer.IsIVSRenderEnabled;
                        UpdateIVSButtonDisplay(currentIVSState);
                    }
                    else
                    {
                        // 硬體解碼模式：不支援 IVS
                        IVSToggleButton.IsEnabled = false;
                        IVSToggleButton.Content = "🎯 IVS不支援";
                        IVSToggleButton.Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(158, 158, 158)); // 灰色
                        IVSToggleButton.ToolTip = $"當前解碼模式 ({videoPlayer.DecodeMode}) 不支援 IVS 功能\n請切換到軟體解碼模式以使用 IVS";
                    }
                }
                else
                {
                    // 無法取得播放器實例
                    IVSToggleButton.IsEnabled = false;
                    IVSToggleButton.Content = "🎯 IVS關閉";
                    IVSToggleButton.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(200, 200, 200));
                    IVSToggleButton.ToolTip = "無法取得播放器狀態";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新 IVS 按鈕狀態時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔥 更新語音接收按鈕狀態 - 修正 null 參考問題
        /// </summary>
        private void UpdateVoiceReceiveButtonState()
        {
            try
            {
                if (VoiceReceiveButton == null || _voiceIntercomManager == null) return;

                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                bool hasPlayingVideo = selectedPlayer?.IsPlaying == true;

                if (!hasPlayingVideo)
                {
                    // 沒有播放時，停用按鈕
                    VoiceReceiveButton.IsEnabled = false;
                    VoiceReceiveButton.Content = "🔇 音訊關閉";
                    VoiceReceiveButton.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(200, 200, 200)); // 淺灰色
                    VoiceReceiveButton.ToolTip = "需要先播放視頻才能使用語音接收功能";
                    return;
                }

                // 有播放視頻時，啟用按鈕
                VoiceReceiveButton.IsEnabled = true;
                
                // 根據當前狀態更新顯示
                bool isListening = _voiceIntercomManager.IsListening;
                UpdateVoiceReceiveButtonDisplay(isListening);

                // 🔥 修正：增加 null 檢查，避免 CS8602 警告
                if (selectedPlayer != null)
                {
                    var playbackState = selectedPlayer.GetCurrentPlaybackState();
                    if (playbackState != null && !string.IsNullOrEmpty(playbackState.DeviceId))
                    {
                        var device = DahuaSDK.GetDevice(playbackState.DeviceId);
                        if (device != null)
                        {
                            VoiceReceiveButton.ToolTip = isListening ? 
                                $"正在接收 {device.Name} 的音訊 - 點擊停止" : 
                                $"點擊開始接收 {device.Name} 的音訊";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新語音接收按鈕狀態時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔥 更新語音對講按鈕狀態 - 修正 null 參考問題
        /// </summary>
        private void UpdateVoiceTalkButtonState()
        {
            try
            {
                if (VoiceTalkButton == null || _voiceIntercomManager == null) return;

                var selectedPlayer = _splitScreenManager?.SelectedPlayer;
                bool hasPlayingVideo = selectedPlayer?.IsPlaying == true;

                if (!hasPlayingVideo)
                {
                    // 沒有播放時，停用按鈕
                    VoiceTalkButton.IsEnabled = false;
                    VoiceTalkButton.Content = "🎤 對講關閉";
                    VoiceTalkButton.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(200, 200, 200)); // 淺灰色
                    VoiceTalkButton.ToolTip = "需要先播放視頻才能使用語音對講功能";
                    return;
                }

                // 🔥 修正：有播放視頻時就啟用對講按鈕，不依賴語音接收
                VoiceTalkButton.IsEnabled = true;

                // 根據當前狀態更新顯示
                bool isTalking = _voiceIntercomManager.IsSpeaking;
                UpdateVoiceTalkButtonDisplay(isTalking);

                // 🔥 修正：增加 null 檢查，避免 CS8602 警告
                if (selectedPlayer != null)
                {
                    var playbackState = selectedPlayer.GetCurrentPlaybackState();
                    if (playbackState != null && !string.IsNullOrEmpty(playbackState.DeviceId))
                    {
                        var device = DahuaSDK.GetDevice(playbackState.DeviceId);
                        if (device != null)
                        {
                            if (isTalking)
                            {
                                VoiceTalkButton.ToolTip = $"正在對講 {device.Name} - 點擊停止";
                            }
                            else if (_voiceIntercomManager.IsListening)
                            {
                                VoiceTalkButton.ToolTip = $"點擊開始對講 {device.Name}";
                            }
                            else
                            {
                                VoiceTalkButton.ToolTip = $"點擊開始對講 {device.Name}（將自動啟動語音接收）";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新語音對講按鈕狀態時發生錯誤: {ex.Message}");
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 新增：通知回放控制對話框更新狀態
        /// </summary>
        private void NotifyPlaybackDialogUpdate()
        {
            try
            {
                if (_playbackControlDialog != null && _playbackControlDialog.IsVisible)
                {
                    // 使用 Dispatcher 確保在正確的線程上更新 UI
                    _playbackControlDialog.Dispatcher.Invoke(() =>
                    {
                        // 呼叫對話框的更新方法（需要在 PlaybackControlDialog 中新增此方法）
                        _playbackControlDialog.RefreshCurrentStatus();
                    });
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"通知回放對話框更新時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔥 統一管理所有按鈕狀態的方法
        /// </summary>
        public void UpdateButtonStates()
        {
            try
            {
                // 檢查基本條件
                bool hasSelectedDevice = !string.IsNullOrEmpty(_deviceListManager?.SelectedDeviceId);
                bool hasSelectedPlayer = _splitScreenManager?.SelectedPlayer != null;
                bool hasAnyPlaying = _splitScreenManager?.HasAnyPlayerPlaying() ?? false;

                // 完全安全的 null 檢查
                bool hasAnyPlayback = false;
                if (_playbackControlManager != null && _splitScreenManager?.VideoPlayers != null)
                {
                    try
                    {
                        hasAnyPlayback = _splitScreenManager.VideoPlayers.Any(p => _playbackControlManager.IsInPlaybackMode(p.Index));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"檢查回放狀態時發生錯誤: {ex.Message}");
                        hasAnyPlayback = false;
                    }
                }

                // 播放按鈕：需要選中設備和播放器
                if (StartVideoButton != null)
                {
                    StartVideoButton.IsEnabled = hasSelectedDevice && hasSelectedPlayer;
                }

                // 停止按鈕：需要有選中的播放器，且該播放器有內容在播放或回放
                if (StopVideoButton != null)
                {
                    bool selectedPlayerHasContent = false;
                    if (hasSelectedPlayer && _splitScreenManager?.SelectedPlayer != null)
                    {
                        var selectedPlayer = _splitScreenManager.SelectedPlayer;
                        selectedPlayerHasContent = selectedPlayer.IsPlaying ||
                            (_playbackControlManager?.IsInPlaybackMode(selectedPlayer.Index) ?? false);
                    }
                    StopVideoButton.IsEnabled = selectedPlayerHasContent;
                }

                // 全停按鈕：只要有任何播放或回放就啟用
                if (StopAllVideoButton != null)
                {
                    StopAllVideoButton.IsEnabled = hasAnyPlaying || hasAnyPlayback;
                }

                // 🔥 更新特殊功能按鈕狀態
                UpdateIVSButtonState();
                UpdateVoiceReceiveButtonState();
                UpdateVoiceTalkButtonState();

                // 調試信息，幫助了解按鈕狀態
                Console.WriteLine($"按鈕狀態更新: 選中設備={hasSelectedDevice}, 選中播放器={hasSelectedPlayer}, " +
                    $"有播放={hasAnyPlaying}, 有回放={hasAnyPlayback}, " +
                    $"停止按鈕={StopVideoButton?.IsEnabled}, 全停按鈕={StopAllVideoButton?.IsEnabled}");
            }
            catch (Exception ex)
            {
                ShowMessage($"更新按鈕狀態時發生錯誤：{ex.Message}");
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

        /// <summary>
        /// 🔥 新增：緊急清除所有選中狀態 - 解決 IVS 規則殘留問題
        /// </summary>
        public void EmergencyClearAllSelectedStates()
        {
            try
            {
                _splitScreenManager?.ForceClearAllSelectedStates();
                UpdateButtonStates();
                ShowMessage("🧹 已緊急清除所有分割區域的選中狀態");
            }
            catch (Exception ex)
            {
                ShowMessage($"緊急清除選中狀態時發生錯誤: {ex.Message}");
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _performanceManager?.StopMonitoring();
                _playbackControlManager?.Cleanup(); // 新增：清理回放資源
                _splitScreenManager?.StopAllVideoPlayers();
                
                // 🔥 新增：清理語音對講資源
                _voiceIntercomManager?.Dispose();
                
                SimpleVideoPlayer.GlobalCleanup();
                _deviceManager?.Close();
                _playbackControlDialog?.Close(); // 新增：關閉回放控制視窗
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