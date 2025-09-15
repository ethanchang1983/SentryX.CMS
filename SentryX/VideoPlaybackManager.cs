using System;
using System.Linq;

namespace SentryX
{
    public class VideoPlaybackManager
    {
        private readonly MainWindow _mainWindow;
        private readonly SplitScreenManager _splitScreenManager;
        private DecodeMode _currentDecodeMode = DecodeMode.Software;
        private VideoStreamType _currentStreamType = VideoStreamType.Main;

        public DecodeMode CurrentDecodeMode
        {
            get => _currentDecodeMode;
            set => _currentDecodeMode = value;
        }

        public VideoStreamType CurrentStreamType
        {
            get => _currentStreamType;
            set => _currentStreamType = value;
        }

        public VideoPlaybackManager(MainWindow mainWindow, SplitScreenManager splitScreenManager)
        {
            _mainWindow = mainWindow;
            _splitScreenManager = splitScreenManager;
        }

        /// <summary>
        /// 開始單個通道的視頻播放
        /// </summary>
        public bool StartVideoPlayback(string deviceId, int channel)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                _mainWindow.ShowMessage("請先在左側選擇一個攝影機設備或通道");
                return false;
            }

            if (_splitScreenManager.SelectedPlayer == null)
            {
                _mainWindow.ShowMessage("請先點擊選中一個分割區域");
                return false;
            }

            var device = DahuaSDK.GetDevice(deviceId);
            if (device == null || !device.IsOnline)
            {
                _mainWindow.ShowMessage("選中的設備不在線，請先連接設備");
                return false;
            }

            try
            {
                var targetPlayer = _splitScreenManager.SelectedPlayer;
                string decodeModeText = GetDecodeModeText();
                string streamTypeText = _currentStreamType == VideoStreamType.Main ? "主碼流" : "輔碼流";

                _mainWindow.ShowMessage($"準備使用{decodeModeText}在分割區域 {targetPlayer.Index + 1} 播放 {device.Name} 通道{channel + 1} 的{streamTypeText}視頻...");

                if (targetPlayer.StartPlay(device.LoginHandle, channel, _currentDecodeMode, _currentStreamType, device.Name))
                {
                    _mainWindow.ShowMessage($"開始播放 {device.Name} 通道{channel + 1} 的即時視頻 ({decodeModeText}, {streamTypeText}) - 分割區域 {targetPlayer.Index + 1}");
                    _splitScreenManager.SelectNextAvailablePlayer();
                    return true;
                }
                else
                {
                    _mainWindow.ShowMessage("視頻播放啟動失敗，請檢查設備連接或嘗試其他解碼模式");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"視頻播放發生錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 新增：DVR/NVR 多通道自動播放功能
        /// </summary>
        /// <param name="deviceId">設備 ID</param>
        /// <returns>成功播放的通道數量</returns>
        public int StartMultiChannelPlayback(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                _mainWindow.ShowMessage("請先選擇一個 DVR/NVR 設備");
                return 0;
            }

            var device = DahuaSDK.GetDevice(deviceId);
            if (device == null || !device.IsOnline)
            {
                _mainWindow.ShowMessage("選中的設備不在線，請先連接設備");
                return 0;
            }

            if (device.ChannelCount <= 0)
            {
                _mainWindow.ShowMessage($"設備 {device.Name} 沒有可用的通道資訊");
                return 0;
            }

            // 檢查可用的分割區域數量
            var availablePlayers = _splitScreenManager.VideoPlayers
                .Where(p => !p.IsPlaying)
                .ToList();

            if (availablePlayers.Count == 0)
            {
                _mainWindow.ShowMessage("沒有可用的分割區域，請停止一些視頻播放或增加分割數量");
                return 0;
            }

            int channelsToPlay = Math.Min(device.ChannelCount, availablePlayers.Count);
            int successCount = 0;
            string decodeModeText = GetDecodeModeText();
            string streamTypeText = _currentStreamType == VideoStreamType.Main ? "主碼流" : "輔碼流";

            _mainWindow.ShowMessage($"開始自動播放 {device.Name} 的 {channelsToPlay} 個通道...");

            try
            {
                for (int channel = 0; channel < channelsToPlay; channel++)
                {
                    var targetPlayer = availablePlayers[channel];
                    
                    _mainWindow.ShowMessage($"正在播放 {device.Name} 通道{channel + 1} 到分割區域 {targetPlayer.Index + 1}...");
                    
                    if (targetPlayer.StartPlay(device.LoginHandle, channel, _currentDecodeMode, _currentStreamType, device.Name))
                    {
                        successCount++;
                        _mainWindow.ShowMessage($"✅ 通道{channel + 1} 播放成功 - 分割區域 {targetPlayer.Index + 1}");
                    }
                    else
                    {
                        _mainWindow.ShowMessage($"❌ 通道{channel + 1} 播放失敗");
                    }

                    // 短暫延遲避免同時啟動太多播放
                    System.Threading.Thread.Sleep(100);
                }

                if (successCount > 0)
                {
                    _mainWindow.ShowMessage($"🎉 DVR 多通道播放完成：成功播放 {successCount}/{channelsToPlay} 個通道 ({decodeModeText}, {streamTypeText})");
                    
                    // 自動選擇下一個可用區域
                    _splitScreenManager.SelectNextAvailablePlayer();
                }
                else
                {
                    _mainWindow.ShowMessage("❌ 所有通道播放都失敗，請檢查設備連接狀態");
                }

                return successCount;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 多通道播放過程中發生錯誤：{ex.Message}");
                return successCount;
            }
        }

        /// <summary>
        /// 智能播放功能：根據選擇的內容決定是單通道還是多通道播放
        /// </summary>
        /// <param name="deviceId">設備 ID</param>
        /// <param name="channel">通道號 (-1 表示播放所有通道)</param>
        /// <returns>播放結果</returns>
        public bool StartSmartPlayback(string deviceId, int channel = -1)
        {
            if (channel >= 0)
            {
                // 播放指定通道
                return StartVideoPlayback(deviceId, channel);
            }
            else
            {
                // 播放所有通道
                var successCount = StartMultiChannelPlayback(deviceId);
                return successCount > 0;
            }
        }

        public int StopAllPlayback()
        {
            try
            {
                int stoppedCount = 0;
                foreach (var player in _splitScreenManager.VideoPlayers)
                {
                    if (player.IsPlaying)
                    {
                        player.StopPlay();
                        stoppedCount++;
                    }
                }

                if (stoppedCount > 0)
                {
                    _mainWindow.ShowMessage($"已停止 {stoppedCount} 個視頻播放");
                }
                else
                {
                    _mainWindow.ShowMessage("沒有正在播放的視頻");
                }

                return stoppedCount;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"停止播放時發生錯誤：{ex.Message}");
                return 0;
            }
        }

        public string GetDecodeModeText()
        {
            return _currentDecodeMode switch
            {
                DecodeMode.Software => "軟體解碼",
                DecodeMode.Hardware => "硬體解碼",
                DecodeMode.Auto => "自動解碼",
                _ => "未知模式"
            };
        }

        public void HandleDecodeTypeChanged(string tag)
        {
            switch (tag)
            {
                case "Software":
                    _currentDecodeMode = DecodeMode.Software;
                    _mainWindow.ShowMessage("已切換到軟體解碼模式 (使用CPU，相容性最佳)");
                    break;
                case "Hardware":
                    _currentDecodeMode = DecodeMode.Hardware;
                    _mainWindow.ShowMessage("已切換到硬體解碼模式 (使用GPU，性能最佳)");
                    break;
                case "Auto":
                    _currentDecodeMode = DecodeMode.Auto;
                    _mainWindow.ShowMessage("已切換到自動選擇模式 (先試硬體，再試軟體)");
                    break;
            }

            bool hasPlayingVideo = _splitScreenManager.VideoPlayers.Any(p => p.IsPlaying);
            if (hasPlayingVideo)
            {
                _mainWindow.ShowMessage("提示：解碼模式變更將在下次播放時生效");
            }
        }

        public void HandleStreamTypeChanged(string tag)
        {
            switch (tag)
            {
                case "Main":
                    _currentStreamType = VideoStreamType.Main;
                    _mainWindow.ShowMessage("已切換到主碼流模式 (高解析度，高碼率)");
                    break;
                case "Sub":
                    _currentStreamType = VideoStreamType.Sub;
                    _mainWindow.ShowMessage("已切換到輔碼流模式 (低解析度，低碼率，適合多路預覽)");
                    break;
            }

            bool hasPlayingVideo = _splitScreenManager.VideoPlayers.Any(p => p.IsPlaying);
            if (hasPlayingVideo)
            {
                _mainWindow.ShowMessage("提示：碼流類型變更將在下次播放時生效");
            }
        }
    }
}