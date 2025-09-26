// MultiViewPlayer.cs - 多視圖播放器 - 增強版本
// 增加雙擊切換單/多分割畫面功能

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Runtime.InteropServices;

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
        /// 是否正在播放 - 修正版本，支援回放模式檢測
        /// </summary>
        public bool IsPlaying
        {
            get
            {
                // 檢查實況播放
                bool isLivePlaying = _videoPlayer?.IsPlaying ?? false;

                // 檢查回放模式（需要通過外部回放管理器檢查）
                bool isInPlaybackMode = false;
                try
                {
                    // 這裡可以通過靜態方法或其他方式檢查回放狀態
                    // 暫時先檢查是否有播放狀態記錄
                    isInPlaybackMode = CurrentPlaybackState != null && HasActiveContent;
                }
                catch
                {
                    // 如果檢查失敗，忽略錯誤
                }

                return isLivePlaying || isInPlaybackMode;
            }
        }

        /// <summary>
        /// 新增：是否有活躍內容（實況或回放）
        /// </summary>
        public bool HasActiveContent { get; set; } = false;

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
        /// 當被雙擊時觸發的事件 - 新增
        /// </summary>
        public event Action<MultiViewPlayer>? DoubleClicked;

        /// <summary>
        /// 正常邊框顏色（極深灰色，幾乎看不見）
        /// </summary>
        private static readonly Color NormalBorderColor = Color.FromArgb(24, 24, 24);

        /// <summary>
        /// 選中時的邊框顏色（紅色）
        /// </summary>
        private static readonly Color SelectedBorderColor = Color.Red;

        /// <summary>
        /// 邊框寬度
        /// </summary>
        private const int BorderWidth = 1;

        public readonly MainWindow _mainWindow;  // 新增：注入 MainWindow

        /// <summary>
        /// 記錄播放狀態的相關資訊 - 用於恢復播放
        /// </summary>
        public class PlaybackState
        {
            public IntPtr DeviceHandle { get; set; }
            public int Channel { get; set; }
            public DecodeMode DecodeMode { get; set; }
            public VideoStreamType StreamType { get; set; }
            public string DeviceName { get; set; } = "";
            public string DeviceId { get; set; } = ""; // 新增：設備ID
        }

        /// <summary>
        /// 當前播放狀態 - 用於單/多分割切換時保持播放
        /// </summary>
        public PlaybackState? CurrentPlaybackState { get; private set; }

        /// <summary>
        /// 🔥 統一的 IsSelected 屬性 - 加強調試和狀態管理
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 選中狀態變更 {_isSelected} -> {value}");
                    
                    _isSelected = value;

                    // 使用 BeginInvoke 確保在正確的線程上執行
                    if (_containerPanel?.InvokeRequired == true)
                    {
                        _containerPanel.BeginInvoke(new Action(UpdateBorderColor));
                    }
                    else
                    {
                        UpdateBorderColor();
                    }
                }
            }
        }

        // Win32 API 聲明
        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        // === 建構子 ===

        /// <summary>
        /// 建立新的多視圖播放器 - 增強版本
        /// </summary>
        /// <param name="index">播放器索引</param>
        public MultiViewPlayer(int index, MainWindow mainWindow)
        {
            Index = index;

            _mainWindow = mainWindow;

            // 建立視頻顯示面板（內層，黑色背景）
            _videoPanel = new Panel
            {
                BackColor = Color.Black,
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            // 建立外層容器面板（用於邊框效果）
            _containerPanel = new Panel
            {
                BackColor = NormalBorderColor, // 初始設為正常邊框顏色
                Dock = DockStyle.Fill
            };

            // 設定視頻面板的初始位置和大小
            UpdateVideoPanelBounds();

            // 註冊滑鼠事件 - 包括雙擊
            _containerPanel.MouseClick += OnContainer_MouseClick;
            _containerPanel.MouseDoubleClick += OnContainer_MouseDoubleClick;
            _videoPanel.MouseClick += OnVideoPanel_MouseClick;
            _videoPanel.MouseDoubleClick += OnVideoPanel_MouseDoubleClick;

            // 註冊容器面板的 Resize 事件
            _containerPanel.Resize += OnContainer_Resize;

            // 組裝控制項層次
            _containerPanel.Controls.Add(_videoPanel);

            // 建立 WindowsFormsHost
            HostControl = new WindowsFormsHost
            {
                Child = _containerPanel
            };

            Debug.WriteLine($"MultiViewPlayer {index} 已建立（增強版本，支援雙擊切換）");
        }

        // === 公開方法 ===

        /// <summary>
        /// 開始播放視頻 - 增強版本，記錄播放狀態
        /// </summary>
        /// <param name="deviceHandle">設備句handles</param>
        /// <param name="channel">通道號</param>
        /// <param name="decodeMode">解碼模式</param>
        /// <param name="streamType">碼流類型</param>
        /// <param name="deviceName">設備名稱</param>
        /// <param name="deviceId">設備ID</param>
        /// <returns>是否成功開始播放</returns>
        public bool StartPlay(IntPtr deviceHandle, int channel, DecodeMode decodeMode, VideoStreamType streamType, string deviceName = "", string deviceId = "")
        {
            try
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 準備開始播放 {deviceName} 通道 {channel}");

                // 如果已經在播放，先完全停止
                if (_videoPlayer != null)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 檢測到現有播放器，先停止");
                    StopPlay();

                    // 等待清理完成
                    System.Threading.Thread.Sleep(100);
                }

                // 記錄播放狀態 - 用於單/多分割切換
                CurrentPlaybackState = new PlaybackState
                {
                    DeviceHandle = deviceHandle,
                    Channel = channel,
                    DecodeMode = decodeMode,
                    StreamType = streamType,
                    DeviceName = deviceName,
                    DeviceId = !string.IsNullOrEmpty(deviceId) ? deviceId : deviceName // 使用傳入的deviceId，如果沒有則用deviceName
                };

                // 確保顯示狀態正確
                EnsureProperDisplayState();

                // ✅ 修正：兩種解碼模式都預設啟用 IVS
                bool enableIVS = true; // 改為都啟用

                // 建立新的播放器
                _videoPlayer = new SimpleVideoPlayer(decodeMode, streamType, enableIVS);
                
                Debug.WriteLine($"MultiViewPlayer {Index}: 創建播放器 - 解碼={decodeMode}, IVS={enableIVS}");

                // 取得視頻面板的句handles
                IntPtr windowHandle = _videoPanel.Handle;
                if (windowHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 無法取得視頻面板句柄");
                    return false;
                }

                // 開始播放
                if (_videoPlayer.StartPlay(deviceHandle, channel, windowHandle, deviceName))
                {
                    HasActiveContent = true; // 標記為有活躍內容
                    Debug.WriteLine($"MultiViewPlayer {Index}: 開始播放成功 {deviceName} 通道 {channel} ({streamType})");
                    return true;
                }
                else
                {
                    _videoPlayer.Dispose();
                    _videoPlayer = null;
                    CurrentPlaybackState = null; // 播放失敗，清除狀態
                    HasActiveContent = false;
                    Debug.WriteLine($"MultiViewPlayer {Index}: 播放失敗");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 開始播放時發生異常 - {ex.Message}");
                CurrentPlaybackState = null; // 異常時清除狀態
                HasActiveContent = false;
                return false;
            }
        }

        /// <summary>
        /// 停止播放視頻 - 徹底修正版本，確保清除所有狀態
        /// </summary>
        /// <param name="keepPlaybackState">是否保留播放狀態（用於單/多分割切換）</param>
        public void StopPlay(bool keepPlaybackState = false)
        {
            try
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 開始停止播放，保留狀態: {keepPlaybackState}");

                if (_videoPlayer != null)
                {
                    _videoPlayer.StopPlay();  // SDK 停止
                    _videoPlayer.Dispose();
                    _videoPlayer = null;
                    Debug.WriteLine($"MultiViewPlayer {Index}: 視頻播放器已清理");
                }

                if (!keepPlaybackState)
                {
                    CurrentPlaybackState = null;
                    HasActiveContent = false;
                    SetPlaybackMode(false);  // 明確設定非回放
                    ForceClearSelectedState();  // 清除選中
                }

                // 只呼叫一次重置 (避免重複)
                RefreshDisplay();

                Debug.WriteLine($"MultiViewPlayer {Index}: 播放已停止並完全重置狀態");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 停止播放時發生異常 - {ex.Message}");
            }
        }

        public void ResetForReuse()
        {
            StopPlay(keepPlaybackState: false);
            IsSelected = false;
            RefreshDisplay();
            // 如果有 IVS 或其他狀態，重置它們
            SetIVSRender(false);
        }

        /// <summary>
        /// 新增：設定回放模式狀態
        /// </summary>
        /// <param name="isInPlayback">是否在回放模式</param>
        public void SetPlaybackMode(bool isInPlayback)
        {
            RunOnDispatcher(() =>
            {
                try
                {
                    HasActiveContent = isInPlayback || (_videoPlayer?.IsPlaying ?? false);
                    Debug.WriteLine($"MultiViewPlayer {Index}: 回放模式設定為 {isInPlayback}, 活躍內容: {HasActiveContent}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 設定回放模式時發生異常 - {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 恢復播放 - 使用保存的播放狀態
        /// </summary>
        /// <returns>是否成功恢復播放</returns>
        public bool RestorePlay()
        {
            if (CurrentPlaybackState == null)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 沒有保存的播放狀態，無法恢復");
                return false;
            }

            Debug.WriteLine($"MultiViewPlayer {Index}: 恢復播放 {CurrentPlaybackState.DeviceName}");

            return StartPlay(
                CurrentPlaybackState.DeviceHandle,
                CurrentPlaybackState.Channel,
                CurrentPlaybackState.DecodeMode,
                CurrentPlaybackState.StreamType,
                CurrentPlaybackState.DeviceName
            );
        }

        /// <summary>
        /// 強制重新整理顯示
        /// </summary>
        public void RefreshDisplay()
        {
            RunOnDispatcher(() =>
            {
                try
                {
                    CompletelyResetDisplayState();  // 只呼叫一次重置
                    Debug.WriteLine($"MultiViewPlayer {Index}: 已強制重新整理顯示");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 強制重新整理顯示時發生異常 - {ex.Message}");
                }
            });
        }

        // 🔥 修正：所有 UI 操作使用 WPF Dispatcher
        private void RunOnDispatcher(Action action)
        {
            if (_disposed)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: RunOnDispatcher 跳過 - 物件已處置");
                return;
            }

            System.Windows.Threading.Dispatcher dispatcher = HostControl?.Dispatcher ?? _mainWindow.Dispatcher;  // fallback 到 MainWindow Dispatcher

            if (dispatcher == null)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: RunOnDispatcher 跳過 - Dispatcher 是 null");
                return;
            }

            if (dispatcher.CheckAccess())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: Dispatcher 直接執行時異常 - {ex.Message}");
                }
            }
            else
            {
                try
                {
                    dispatcher.BeginInvoke(action);  // 異步
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: Dispatcher BeginInvoke 時異常 - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 快速將視頻面板變成黑屏，提供即時視覺反饋
        /// </summary>
        public void QuickBlackScreen()
        {
            RunOnDispatcher(() =>
            {
                try
                {
                    if (_videoPanel != null)
                    {
                        _videoPanel.BackColor = Color.Black;
                        _videoPanel.Refresh();
                        Debug.WriteLine($"MultiViewPlayer {Index}: 快速黑屏已執行");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 快速黑屏時發生異常 - {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 取得當前播放狀態 - 為回放功能提供支援
        /// </summary>
        /// <returns>當前播放狀態，如果沒有播放則返回 null</returns>
        public PlaybackState? GetCurrentPlaybackState()
        {
            return CurrentPlaybackState;
        }

        /// <summary>
        /// 取得視頻面板 - 為回放功能提供視窗句柄
        /// </summary>
        public Panel VideoPanel => _videoPanel;

        /// <summary>
        /// 設備 ID - 從播放狀態中提取
        /// </summary>
        public string? DeviceId => CurrentPlaybackState?.DeviceId;

        /// <summary>
        /// 通道號 - 從播放狀態中提取
        /// </summary>
        public int? Channel => CurrentPlaybackState?.Channel;

        /// <summary>
        /// 🔥 新增：取得 SimpleVideoPlayer 實例
        /// </summary>
        public SimpleVideoPlayer? GetVideoPlayer()
        {
            return _videoPlayer;
        }

        /// <summary>
        /// 🔥 新增：取得當前 IVS 顯示狀態
        /// </summary>
        public bool IsIVSRenderEnabled => _videoPlayer?.IsIVSRenderEnabled ?? false;

        /// <summary>
        /// 🔥 新增：檢查是否支援 IVS
        /// </summary>
        public bool IsIVSSupported()
        {
            return _videoPlayer?.IsIVSSupported() ?? false;
        }

        /// <summary>
        /// 🔥 新增：強制清除選中狀態 - 專門解決 IVS 規則殘留問題
        /// </summary>
        public void ForceClearSelectedState()
        {
            try
            {
                if (_containerPanel.InvokeRequired)
                {
                    _containerPanel.BeginInvoke(new Action(ForceClearSelectedState));  // 異步呼叫自己
                    return;
                }

                Debug.WriteLine($"MultiViewPlayer {Index}: 強制清除選中狀態（之前狀態: {_isSelected})");
                _isSelected = false;
                UpdateBorderColor();  // 這會更新邊框顏色，已有跨執行緒處理
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 強制清除選中狀態時發生異常 - {ex.Message}");
            }
        }

        // === 私有方法 ===

        /// <summary>
        /// 容器面板 Resize 事件
        /// </summary>
        private void OnContainer_Resize(object? sender, EventArgs e)
        {
            UpdateVideoPanelBounds();
        }

        /// <summary>
        /// 容器面板滑鼠單擊事件
        /// </summary>
        private void OnContainer_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // 🔥 關鍵修正：檢查是否為真實的用戶點擊
                // 防止 IVS 渲染過程觸發的虛假事件
                if (IsRealUserClick(e))
                {
                    Selected?.Invoke(this);
                    Debug.WriteLine($"MultiViewPlayer {Index}: 被點擊選中 (容器) - 真實用戶點擊");
                }
                else
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 忽略非用戶觸發的點擊事件 (可能是 IVS 渲染)");
                }
            }
        }

        /// <summary>
        /// 容器面板滑鼠雙擊事件 - 新增
        /// </summary>
        private void OnContainer_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                DoubleClicked?.Invoke(this);
                Debug.WriteLine($"MultiViewPlayer {Index}: 被雙擊 (容器)");
            }
        }

        /// <summary>
        /// 視頻面板滑鼠單擊事件
        /// </summary>
        private void OnVideoPanel_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // 🔥 關鍵修正：檢查是否為真實的用戶點擊
                // 防止 IVS 渲染過程觸發的虛假事件
                if (IsRealUserClick(e))
                {
                    Selected?.Invoke(this);
                    Debug.WriteLine($"MultiViewPlayer {Index}: 被點擊選中 (視頻區域) - 真實用戶點擊");
                }
                else
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 忽略非用戶觸發的點擊事件 (可能是 IVS 渲染)");
                }
            }
        }

        /// <summary>
        /// 視頻面板滑鼠雙擊事件 - 新增
        /// </summary>
        private void OnVideoPanel_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                DoubleClicked?.Invoke(this);
                Debug.WriteLine($"MultiViewPlayer {Index}: 被雙擊 (視頻區域)");
            }
        }

        /// <summary>
        /// 更新視頻面板的位置和大小 - 修正版本
        /// </summary>
        private void UpdateVideoPanelBounds()
        {
            RunOnDispatcher(() =>
            {
                try
                {
                    if (_containerPanel == null || _videoPanel == null) return;

                    _videoPanel.Location = new Point(BorderWidth, BorderWidth);
                    _videoPanel.Size = new Size(
                        Math.Max(0, _containerPanel.ClientSize.Width - BorderWidth * 2),
                        Math.Max(0, _containerPanel.ClientSize.Height - BorderWidth * 2)
                    );

                    Debug.WriteLine($"MultiViewPlayer {Index}: 視頻面板位置已更新 - Location: {_videoPanel.Location}, Size: {_videoPanel.Size}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 更新視頻面板位置時發生異常 - {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 確保正確的顯示狀態
        /// </summary>
        private void EnsureProperDisplayState()
        {
            try
            {
                if (_videoPanel != null && _containerPanel != null)
                {
                    // 確保視頻面板為黑色
                    _videoPanel.BackColor = Color.Black;

                    // 確保容器面板顏色正確（根據選中狀態）
                    _containerPanel.BackColor = _isSelected ? SelectedBorderColor : NormalBorderColor;

                    // 確保視頻面板位置正確
                    UpdateVideoPanelBounds();

                    Debug.WriteLine($"MultiViewPlayer {Index}: 顯示狀態已確保正確");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 確保顯示狀態時發生異常 - {ex.Message}");
            }
        }



        /// <summary>
        /// 完全重置顯示狀態 - 徹底修正版本，強制清除選中狀態
        /// </summary>
        private void CompletelyResetDisplayState()
        {
            RunOnDispatcher(() =>
            {
                try
                {
                    if (_videoPanel != null && _containerPanel != null)
                    {
                        // 清除子控制項
                        _videoPanel.Controls.Clear();

                        // 重設背景
                        _videoPanel.BackColor = Color.Black;

                        // 強制非選中邊框
                        _containerPanel.BackColor = NormalBorderColor;

                        // 清除顯示 (只用一次 Graphics 和 Win32)
                        if (_videoPanel.IsHandleCreated)
                        {
                            using (var graphics = _videoPanel.CreateGraphics())
                            {
                                graphics.Clear(Color.Black);
                                graphics.Flush();
                            }
                            InvalidateRect(_videoPanel.Handle, IntPtr.Zero, true);
                            UpdateWindow(_videoPanel.Handle);
                        }

                        // 更新位置 (只一次)
                        UpdateVideoPanelBounds();

                        // 重繪 (只一次)
                        _videoPanel.Invalidate(true);
                        _videoPanel.Update();
                        _containerPanel.Invalidate(true);
                        _containerPanel.Update();

                        if (_containerPanel.Parent != null)
                        {
                            _containerPanel.Parent.Invalidate(true);
                            _containerPanel.Parent.Update();
                        }

                        Debug.WriteLine($"MultiViewPlayer {Index}: 顯示狀態已完全重置，強制清除選中邊框");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 完全重置顯示狀態時發生異常 - {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 更新邊框顏色 - 修正版本，加強調試
        /// </summary>
        private void UpdateBorderColor()
        {
            RunOnDispatcher(() =>
            {
                try
                {
                    if (_containerPanel != null)
                    {
                        _containerPanel.BackColor = _isSelected ? SelectedBorderColor : NormalBorderColor;
                        UpdateVideoPanelBounds();  // 確保邊框調整後更新位置
                        Debug.WriteLine($"MultiViewPlayer {Index}: 邊框顏色已更新 - 選中狀態: {_isSelected}, 顏色: {(_isSelected ? "紅色" : "深灰色")}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MultiViewPlayer {Index}: 更新邊框顏色時發生異常 - {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 🔥 修正：Dispose 方法 - 確保完全清除選中狀態
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; // 先設定旗標
            try
            {
                // ... 原有清理
                _videoPlayer?.Dispose();
                _videoPanel.Dispose();
                _containerPanel.Dispose();
                HostControl.Child = null; // 斷開 Child
                HostControl.Dispose(); // Dispose HostControl
                                       // 移除 HostControl = null; 因為 HostControl 是唯讀屬性，不能指派值
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: Dispose 時異常 - {ex.Message}");
            }
        }

        /// <summary>
        /// 🔥 新增：檢查是否為真實的用戶點擊 - 防止 IVS 渲染觸發虛假事件
        /// </summary>
        private bool IsRealUserClick(MouseEventArgs e)
        {
            try
            {
                // 檢查 1：確保滑鼠位置在合理範圍內
                if (e.X < 0 || e.Y < 0 || e.X > _videoPanel.Width || e.Y > _videoPanel.Height)
                {
                    return false;
                }

                // 檢查 2：檢查是否在播放啟動後的短時間內（IVS 渲染通常在啟動時觸發）
                if (_videoPlayer?.IsPlaying == true)
                {
                    var timeSinceStart = DateTime.Now - (_videoPlayer.CurrentVideoInfo?.StartTime ?? DateTime.Now);
                    if (timeSinceStart.TotalSeconds < 2.0) // 播放開始後 2 秒內，更謹慎處理點擊事件
                    {
                        Debug.WriteLine($"播放啟動後 {timeSinceStart.TotalSeconds:F1} 秒內的點擊，可能是 IVS 渲染觸發");
                        
                        // 在這個時間窗口內，需要更嚴格的驗證
                        // 檢查是否有真實的滑鼠光標位置
                        var cursorPos = Cursor.Position;
                        var panelPos = _videoPanel.PointToScreen(Point.Empty);
                        var relativeCursor = new Point(cursorPos.X - panelPos.X, cursorPos.Y - panelPos.Y);
                        
                        // 如果事件位置與實際光標位置差距太大，可能是虛假事件
                        var distance = Math.Sqrt(Math.Pow(e.X - relativeCursor.X, 2) + Math.Pow(e.Y - relativeCursor.Y, 2));
                        if (distance > 50) // 距離超過 50 像素認為是虛假事件
                        {
                            Debug.WriteLine($"事件位置 ({e.X}, {e.Y}) 與光標位置 ({relativeCursor.X}, {relativeCursor.Y}) 差距太大: {distance:F1}px");
                            return false;
                        }
                    }
                }

                // 檢查 3：確保不是在 IVS 切換過程中
                if (_videoPlayer?.IsIVSSupported() == true && _videoPlayer.IsIVSRenderEnabled)
                {
                    // 如果正在進行 IVS 相關操作，短暫延遲處理點擊事件
                    var lastIVSUpdate = GetLastIVSUpdateTime();
                    if (lastIVSUpdate.HasValue && (DateTime.Now - lastIVSUpdate.Value).TotalMilliseconds < 500)
                    {
                        Debug.WriteLine("IVS 更新後 500ms 內的點擊事件，可能是渲染觸發");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"檢查真實點擊時發生異常: {ex.Message}");
                return true; // 異常時默認為真實點擊
            }
        }

        /// <summary>
        /// 🔥 新增：記錄和取得最後一次 IVS 更新時間
        /// </summary>
        private DateTime? _lastIVSUpdateTime = null;
        private DateTime? GetLastIVSUpdateTime() => _lastIVSUpdateTime;
        private void UpdateLastIVSUpdateTime() => _lastIVSUpdateTime = DateTime.Now;

        /// <summary>
        /// 🔥 修正：設定 IVS 顯示狀態 - 解決紅框問題
        /// </summary>
        public bool SetIVSRender(bool enable)
        {
            try
            {
                UpdateLastIVSUpdateTime(); // 記錄 IVS 更新時間
                
                bool result = _videoPlayer?.SetIVSRender(enable) ?? false;
                
                Debug.WriteLine($"MultiViewPlayer {Index}: IVS 設定為 {enable}, 結果: {result}");
                
                // 🔥 關鍵修正：如果關閉 IVS，立即清除選中狀態
                if (!enable && result)
                {
                    // 延遲清除選中狀態，避免 IVS 渲染過程的干擾
                    Task.Run(async () =>
                    {
                        await Task.Delay(300); // 等待 IVS 完全關閉
                        
                        if (_containerPanel?.InvokeRequired == true)
                        {
                            _containerPanel.BeginInvoke(new Action(() =>
                            {
                                Debug.WriteLine($"MultiViewPlayer {Index}: IVS 關閉後清除選中狀態");
                                ForceClearSelectedState();
                            }));
                        }
                        else
                        {
                            Debug.WriteLine($"MultiViewPlayer {Index}: IVS 關閉後清除選中狀態");
                            ForceClearSelectedState();
                        }
                    });
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 設定 IVS 時發生異常 - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 🔥 修正：切換 IVS 顯示狀態 - 解決紅框問題
        /// </summary>
        public bool ToggleIVSRender()
        {
            try
            {
                UpdateLastIVSUpdateTime(); // 記錄 IVS 更新時間
                
                bool currentState = _videoPlayer?.IsIVSRenderEnabled ?? false;
                bool newState = !currentState;
                
                bool result = _videoPlayer?.SetIVSRender(newState) ?? false;
                
                Debug.WriteLine($"MultiViewPlayer {Index}: IVS 切換 {currentState} -> {newState}, 結果: {result}");
                
                // 🔥 關鍵修正：如果關閉 IVS，立即清除選中狀態
                if (!newState && result)
                {
                    // 延遲清除選中狀態，避免 IVS 渲染過程的干擾
                    Task.Run(async () =>
                    {
                        await Task.Delay(300); // 等待 IVS 完全關閉
                        
                        if (_containerPanel?.InvokeRequired == true)
                        {
                            _containerPanel.BeginInvoke(new Action(() =>
                            {
                                Debug.WriteLine($"MultiViewPlayer {Index}: IVS 切換關閉後清除選中狀態");
                                ForceClearSelectedState();
                            }));
                        }
                        else
                        {
                            Debug.WriteLine($"MultiViewPlayer {Index}: IVS 切換關閉後清除選中狀態");
                            ForceClearSelectedState();
                        }
                    });
                }
                
                return newState;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiViewPlayer {Index}: 切換 IVS 時發生異常 - {ex.Message}");
                return false;
            }
        }
    }
}