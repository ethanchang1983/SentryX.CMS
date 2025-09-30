using System;
using System.Windows;
using System.Windows.Forms;
using System.Diagnostics;
using System.Windows.Threading;

namespace SentryX
{
    public partial class VideoPlayerWindow : Window
    {
        private SimpleVideoPlayer? _player;
        private Panel? _videoPanel;
        private string _deviceId = "";
        private int _channel = 0;
        private bool _isMainStream = true;
        private DispatcherTimer? _statsTimer;

        public VideoPlayerWindow()
        {
            InitializeComponent();
            this.Topmost = true;
            InitializeVideoPanel();
            InitializeStatsTimer();
        }

        private void InitializeVideoPanel()
        {
            _videoPanel = new Panel
            {
                BackColor = System.Drawing.Color.Black,
                Dock = DockStyle.Fill
            };
            VideoHost.Child = _videoPanel;
        }

        private void InitializeStatsTimer()
        {
            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statsTimer.Tick += UpdateStatistics;
        }

        public void StartPlay(string deviceId, int channel, string deviceName, string channelName)
        {
            try
            {
                _deviceId = deviceId;
                _channel = channel;

                // 更新標題資訊
                DeviceNameText.Text = deviceName;
                ChannelInfoText.Text = $"{channelName} (通道 {channel})";
                Title = $"{deviceName} - {channelName}";

                // 獲取設備登入句柄
                var device = DahuaSDK.GetDevice(deviceId);
                if (device == null || !device.IsOnline)
                {
                    System.Windows.MessageBox.Show("設備未連線或不存在", "錯誤",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 創建播放器（預設使用軟體解碼以支援 IVS）
                _player = new SimpleVideoPlayer(
                    decodeMode: DecodeMode.Software,
                    streamType: _isMainStream ? VideoStreamType.Main : VideoStreamType.Sub,
                    enableIVSByDefault: true
                );

                // 開始播放
                bool success = _player.StartPlay(
                    device.LoginHandle,
                    channel,
                    _videoPanel!.Handle,
                    deviceName
                );

                if (success)
                {
                    StatusText.Text = "播放中...";
                    _statsTimer?.Start();
                    UpdateIVSButtonState();
                }
                else
                {
                    StatusText.Text = "播放失敗";
                    System.Windows.MessageBox.Show("無法啟動視頻播放", "錯誤",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"播放視頻時發生錯誤: {ex.Message}");
                StatusText.Text = $"錯誤: {ex.Message}";
            }
        }

        private void IVSToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                bool newState = _player.ToggleIVSRender();
                UpdateIVSButtonState();
                StatusText.Text = newState ? "IVS 已啟用" : "IVS 已停用";
            }
        }

        private void StreamToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isMainStream = !_isMainStream;
            StreamToggleButton.Content = _isMainStream ? "📺 主碼流" : "📺 子碼流";

            // 重新啟動播放
            if (_player != null)
            {
                _player.StopPlay();
                _player.Dispose();

                var device = DahuaSDK.GetDevice(_deviceId);
                if (device != null && device.IsOnline)
                {
                    _player = new SimpleVideoPlayer(
                        decodeMode: DecodeMode.Software,
                        streamType: _isMainStream ? VideoStreamType.Main : VideoStreamType.Sub,
                        enableIVSByDefault: true
                    );

                    _player.StartPlay(device.LoginHandle, _channel, _videoPanel!.Handle, device.Name);
                    StatusText.Text = $"已切換到{(_isMainStream ? "主" : "子")}碼流";
                }
            }
        }

        private void SnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPanel == null) return;

            try
            {
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "SentryX_Screenshots"
                );

                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                string filename = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string filepath = System.IO.Path.Combine(folder, filename);

                using (var bitmap = new System.Drawing.Bitmap(_videoPanel.Width, _videoPanel.Height))
                {
                    _videoPanel.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(filepath, System.Drawing.Imaging.ImageFormat.Jpeg);
                }

                StatusText.Text = $"截圖已保存: {filename}";
                System.Windows.MessageBox.Show($"截圖已保存到:\n{filepath}", "成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "截圖失敗";
                Debug.WriteLine($"截圖錯誤: {ex.Message}");
            }
        }

        private void UpdateIVSButtonState()
        {
            if (_player != null)
            {
                IVSToggleButton.Content = _player.IsIVSRenderEnabled ? "🎯 IVS (開)" : "🎯 IVS (關)";
            }
        }

        private void UpdateStatistics(object? sender, EventArgs e)
        {
            if (_player?.CurrentVideoInfo != null)
            {
                var info = _player.CurrentVideoInfo;
                ResolutionText.Text = $"解析度: {info.Width}x{info.Height}";
                BitrateText.Text = $"碼率: {info.Bitrate:F1} Kbps";
                FpsText.Text = $"FPS: {info.Fps:F1}";
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _statsTimer?.Stop();

            if (_player != null)
            {
                _player.StopPlay();
                _player.Dispose();
                _player = null;
            }
        }
    }
}