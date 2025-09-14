// MultiViewPlayer.cs - 多視圖播放器
// 這個檔案負責管理單個分割畫面的視頻播放和資訊顯示

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace SentryX
{
    /// <summary>
    /// 多視圖播放器 - 管理單個分割畫面的播放
    /// </summary>
    public class MultiViewPlayer : IDisposable
    {
        // === 屬性和變數 ===

        /// <summary>
        /// 播放器索引（在分割畫面中的位置）
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Windows Forms Host 控制項
        /// </summary>
        public WindowsFormsHost HostControl { get; }

        /// <summary>
        /// 外層容器面板（用於邊框效果）
        /// </summary>
        private Panel _containerPanel;

        /// <summary>
        /// 視頻顯示面板
        /// </summary>
        private Panel _videoPanel;

        /// <summary>
        /// 視頻播放器
        /// </summary>
        private SimpleVideoPlayer? _videoPlayer;

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying => _videoPlayer?.IsPlaying ?? false;

        /// <summary>
        /// 視頻資訊
        /// </summary>
        public VideoInfo? VideoInfo => _videoPlayer?.CurrentVideoInfo;

        /// <summary>
        /// 是否已釋放資源
        /// </summary>
        private bool _disposed = false;

        /// <summary>
        /// 是否被選中
        /// </summary>
        private bool _isSelected = false;

        /// <summary>
        /// 當被選中時觸發的事件
        /// </summary>
        public event Action<MultiViewPlayer>? Selected;

        /// <summary>
        /// 正常邊框顏色（灰色分割線）
        /// </summary>
        private static readonly Color NormalBorderColor = Color.Gray;

        /// <summary>
        /// 選中時的邊框顏色（紅色）
        /// </summary>
        private static readonly Color SelectedBorderColor = Color.Red;

        /// <summary>
        /// 是否被選中
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    UpdateBorderColor();
                }
            }
        }

        // === 建構子 ===

        /// <summary>
        /// 建立新的多視圖播放器 - 立即顯示灰色分割線
        /// </summary>
        /// <param name="index">播放器索引</param>
        public MultiViewPlayer(int index)
        {
            Index = index;

            // 建立外層容器面板（用於邊框效果）
            _containerPanel = new Panel
            {
                BackColor = NormalBorderColor, // 立即設定灰色背景作為邊框
                Dock = DockStyle.Fill,
                Padding = new Padding(1) // 1像素的灰色邊框
            };

            // 建立視頻顯示面板（內層，黑色背景）
            _videoPanel = new Panel
            {
                BackColor = Color.Black,
                Dock = DockStyle.Fill
            };

            // 註冊滑鼠點擊事件
            _containerPanel.MouseClick += OnContainer_MouseClick;
            _videoPanel.MouseClick += OnVideoPanel_MouseClick;

            // 組裝控制項層次：容器面板（灰色邊框）-> 視頻面板（黑色內容）
            _containerPanel.Controls.Add(_videoPanel);

            // 建立 WindowsFormsHost
            HostControl = new WindowsFormsHost
            {
                Child = _containerPanel
            };

            // 重要：在建構子最後確保邊框顏色正確設定
            UpdateBorderColor();

            Debug.WriteLine($"MultiViewPlayer {index} 已建立（立即顯示灰色分割線）");
        }

        // === 公開方法 ===

        /// <summary>
        /// 開始播放視頻
        /// </summary>
        /// <param name="deviceHandle">設備句柄</param>
        /// <param name="channel">通道號</param>
        /// <param name="decodeMode">解碼模式</param>
        /// <param name="streamType">碼流類型</param>
        /// <param name="deviceName">設備名稱</param>
        /// <returns>是否成功開始播放</returns>
        public bool StartPlay(IntPtr deviceHandle, int channel, DecodeMode decodeMode, VideoStreamType streamType, string deviceName = "")
        {
            try
            {
                // 如果已經在播放，先停止
                if (_videoPlayer != null)
                {
                    StopPlay();
                }

                // 建立新的播放器
                _videoPlayer = new SimpleVideoPlayer(decodeMode, streamType);

                // 取得視頻面板的句柄
                IntPtr windowHandle = _videoPanel.Handle;
                if (windowHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 無法取得視頻面板句柄");
                    return false;
                }

                // 開始播放
                if (_videoPlayer.StartPlay(deviceHandle, channel, windowHandle, deviceName))
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 開始播放 {deviceName} 通道 {channel} ({streamType})");
                    return true;
                }
                else
                {
                    _videoPlayer.Dispose();
                    _videoPlayer = null;
                    Debug.WriteLine($"MultiViewPlayer {Index}: 播放失敗");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 開始播放時發生異常 - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止播放視頻
        /// </summary>
        public void StopPlay()
        {
            try
            {
                if (_videoPlayer != null)
                {
                    _videoPlayer.StopPlay();
                    _videoPlayer.Dispose();
                    _videoPlayer = null;

                    Debug.WriteLine($"MultiViewPlayer {Index}: 播放已停止");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 停止播放時發生異常 - {ex.Message}");
            }
        }

        // === 私有方法 ===

        /// <summary>
        /// 容器面板滑鼠點擊事件
        /// </summary>
        private void OnContainer_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // 觸發選中事件
                Selected?.Invoke(this);
                Debug.WriteLine($"MultiViewPlayer {Index}: 被點擊選中 (容器)");
            }
        }

        /// <summary>
        /// 視頻面板滑鼠點擊事件
        /// </summary>
        private void OnVideoPanel_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // 觸發選中事件
                Selected?.Invoke(this);
                Debug.WriteLine($"MultiViewPlayer {Index}: 被點擊選中 (視頻區域)");
            }
        }

        /// <summary>
        /// 更新邊框顏色 - 保持灰色分割線，選中時變紅色
        /// </summary>
        private void UpdateBorderColor()
        {
            if (_containerPanel != null)
            {
                if (_isSelected)
                {
                    // 選中時：紅色邊框，較粗的邊框
                    _containerPanel.BackColor = SelectedBorderColor;
                    _containerPanel.Padding = new Padding(3); // 3像素紅色邊框
                }
                else
                {
                    // 正常時：灰色邊框（分割線），標準邊框
                    _containerPanel.BackColor = NormalBorderColor;
                    _containerPanel.Padding = new Padding(1); // 1像素灰色邊框（分割線）
                }

                // 確保視頻面板始終保持黑色背景
                if (_videoPanel != null)
                {
                    _videoPanel.BackColor = Color.Black;
                }

                // 強制重新繪製控制項
                _containerPanel.Invalidate();
                _containerPanel.Update();
            }
        }

        // === IDisposable 實作 ===

        /// <summary>
        /// 釋放資源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                StopPlay();
                
                // 移除事件處理器
                if (_containerPanel != null)
                {
                    _containerPanel.MouseClick -= OnContainer_MouseClick;
                }
                
                if (_videoPanel != null)
                {
                    _videoPanel.MouseClick -= OnVideoPanel_MouseClick;
                }
                
                _videoPanel?.Dispose();
                _containerPanel?.Dispose();
                HostControl?.Dispose();
                
                _disposed = true;
                Debug.WriteLine($"MultiViewPlayer {Index} 已釋放");
            }
        }
    }
}
