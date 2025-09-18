// SimpleVideoPlayer.cs - 極簡視頻播放器 - 混合模式版本
// 同時支援 IVS 顯示和硬體/軟體解碼選擇

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetSDKCS;
using System.Threading;
using System.Drawing; // GDI+ 繪圖
using System.Windows.Forms; // Windows Forms 控件支援

namespace SentryX
{
    /// <summary>
    /// 極簡視頻播放器 - 支援 IVS 畫線規則顯示和硬體/軟體解碼選擇
    /// </summary>
    public class SimpleVideoPlayer : IDisposable
    {
        // === Geohot 風格：把所有變數放最上面，一目了然 ===

        /// <summary>
        /// Play SDK 的播放端口號 - 每個播放器需要一個獨立的端口號
        /// </summary>
        private int _playPort = -1;

        /// <summary>
        /// 大華 SDK 的實時播放句 handle - 用來控制從攝影機接收數據和 IVS 顯示
        /// </summary>
        private IntPtr _realPlayHandle = IntPtr.Zero;

        /// <summary>
        /// 顯示視頻的窗口句 handle - 告訴播放器要在哪個窗口顯示視頻
        /// </summary>
        private IntPtr _displayHandle = IntPtr.Zero;

        /// <summary>
        /// 是否正在播放視頻
        /// </summary>
        private bool _isPlaying = false;

        /// <summary>
        /// 是否已經被銷毀（釋放資源）
        /// </summary>
        private bool _disposed = false;

        /// <summary>
        /// 數據回調函數 - 當收到視頻數據時會自動呼叫這個函數
        /// </summary>
        private fRealDataCallBackEx2 _dataCallback;

        /// <summary>
        /// 當前使用的解碼模式 - 記住用戶選擇了哪種解碼方式（預設為軟體解碼）
        /// </summary>
        private DecodeMode _decodeMode = DecodeMode.Software;

        /// <summary>
        /// 當前使用的碼流類型
        /// </summary>
        private VideoStreamType _streamType = VideoStreamType.Main;

        /// <summary>
        /// IVS 畫線規則是否啟用 - 預設為啟用
        /// </summary>
        private bool _ivsRenderEnabled = true;

        /// <summary>
        /// 視頻資訊 - 用於監控性能
        /// </summary>
        public VideoInfo? CurrentVideoInfo { get; private set; }

        /// <summary>
        /// 緩衝區重置計數器 - 用於監控緩衝區重置頻率
        /// </summary>
        private int _bufferResetCount = 0;

        /// <summary>
        /// 最後一次緩衝區重置時間
        /// </summary>
        private DateTime _lastBufferResetTime = DateTime.MinValue;

        /// <summary>
        /// 數據接收計數器 - 用於監控數據流
        /// </summary>
        private long _dataReceiveCount = 0;

        /// <summary>
        /// 連續錯誤計數器
        /// </summary>
        private int _consecutiveErrorCount = 0;

        /// <summary>
        /// 數據丟包計數器 - 用於監控性能
        /// </summary>
        private long _droppedFrameCount = 0;

        /// <summary>
        /// 最後一次性能報告時間
        /// </summary>
        private DateTime _lastPerformanceReport = DateTime.MinValue;

        // === 靜態變數：全部程式共用的資源 ===

        /// <summary>
        /// Play SDK 是否已經初始化（整個程式只需要初始化一次）
        /// </summary>
        private static bool _sdkInitialized = false;

        /// <summary>
        /// 執行緒鎖 - 確保只有一個執行緒可以初始化 SDK
        /// </summary>
        private static readonly object _initLock = new object();

        /// <summary>
        /// 全域播放器計數器 - 用於監控同時播放的數量
        /// </summary>
        private static int _globalPlayerCount = 0;

        // === 建構子：建立播放器時執行 ===

        /// <summary>
        /// 建立新的視頻播放器
        /// </summary>
        /// <param name="decodeMode">指定要使用的解碼模式（預設為軟體解碼）</param>
        /// <param name="streamType">指定要使用的碼流類型（預設為主碼流）</param>
        /// <param name="enableIVSByDefault">是否預設啟用 IVS 畫線規則顯示（預設為 true）</param>
        public SimpleVideoPlayer(DecodeMode decodeMode = DecodeMode.Software, VideoStreamType streamType = VideoStreamType.Main, bool enableIVSByDefault = true)
        {
            // 記住用戶選擇的解碼模式和碼流類型
            _decodeMode = decodeMode;
            _streamType = streamType;
            _ivsRenderEnabled = enableIVSByDefault;

            // 確保 Play SDK 已經初始化（全域只做一次）
            EnsureSDKInitialized();

            // 建立數據回調函數 - 當有視頻數據時會呼叫 OnVideoDataReceived
            _dataCallback = new fRealDataCallBackEx2(OnVideoDataReceived);

            // 增加全域播放器計數
            Interlocked.Increment(ref _globalPlayerCount);

            Debug.WriteLine($"SimpleVideoPlayer 已建立，解碼模式: {decodeMode}, 碼流: {streamType}, IVS預設: {enableIVSByDefault}, 全域播放器數量: {_globalPlayerCount}");
        }

        // === 公開方法：使用者會呼叫的方法 ===

        /// <summary>
        /// 開始播放指定攝影機的視頻 - 混合模式版本，同時支援 IVS 和硬體解碼
        /// </summary>
        /// <param name="deviceHandle">攝影機設備的登入句柄</param>
        /// <param name="channel">要播放的通道號（0=第1個通道）</param>
        /// <param name="windowHandle">要顯示視頻的窗口句柄</param>
        /// <param name="deviceName">設備名稱（用於監控顯示）</param>
        /// <returns>是否成功開始播放</returns>
        public bool StartPlay(IntPtr deviceHandle, int channel, IntPtr windowHandle, string deviceName = "")
        {
            try
            {
                // 第1步：檢查輸入參數是否正確
                if (deviceHandle == IntPtr.Zero || windowHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("錯誤：設備句柄或窗口句柄為空");
                    return false;
                }

                // 第2步：檢查全域播放器數量，避免過載
                if (_globalPlayerCount > 32)
                {
                    Debug.WriteLine($"警告：當前播放器數量過多 ({_globalPlayerCount})，可能影響性能");
                }

                // 第3步：如果已經在播放，完全停止並清理
                if (_isPlaying)
                {
                    Debug.WriteLine("檢測到正在播放，先完全停止");
                    StopPlay();
                    System.Threading.Thread.Sleep(100);
                }

                // 第4步：確保所有資源都已清理
                if (_playPort != -1)
                {
                    Debug.WriteLine("檢測到殘留的播放端口，先清理");
                    CleanupPlaySDK();
                }

                // 第5步：記住要在哪個窗口顯示視頻
                _displayHandle = windowHandle;

                // 第6步：初始化視頻資訊
                InitializeVideoInfo(deviceName, channel);

                // 第7步：開始從大華攝影機接收數據（用於 IVS 顯示）
                if (!StartReceiveDataForIVS(deviceHandle, channel))
                {
                    Debug.WriteLine("開始接收視頻數據失敗");
                    return false;
                }

                // 第8步：關鍵 - 在取得 _realPlayHandle 後立即設定 IVS
                if (_ivsRenderEnabled && _realPlayHandle != IntPtr.Zero)
                {
                    EnableIVSRenderImmediate();
                }

                // 第9步：初始化 Play SDK 播放器（準備解碼環境，支援硬體/軟體解碼選擇）
                if (!InitializePlaySDK())
                {
                    Debug.WriteLine("Play SDK 初始化失敗");
                    CleanupReceiveData(); // 失敗了就清理接收資源
                    return false;
                }

                // 第10步：標記為正在播放
                _isPlaying = true;
                _consecutiveErrorCount = 0; // 重置錯誤計數器
                Debug.WriteLine($"視頻播放開始成功，設備: {deviceName}, 通道: {channel}, 碼流: {_streamType}, 解碼: {_decodeMode}, IVS: {_ivsRenderEnabled}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"開始播放時發生異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 開始從大華攝影機接收視頻數據（專門用於 IVS 顯示）
        /// </summary>
        /// <param name="deviceHandle">攝影機設備句柄</param>
        /// <param name="channel">通道號</param>
        /// <returns>是否成功</returns>
        private bool StartReceiveDataForIVS(IntPtr deviceHandle, int channel)
        {
            try
            {
                // 根據碼流類型選擇相應的播放類型
                EM_RealPlayType playType = _streamType == VideoStreamType.Main
                    ? EM_RealPlayType.EM_A_RType_Realplay_0      // 主碼流
                    : EM_RealPlayType.EM_A_RType_Realplay_1;     // 輔碼流

                // 開始即時播放（從大華攝影機取得數據）
                // 注意：這裡的 hWnd 設為 IntPtr.Zero，因為我們用 Play SDK 來顯示
                _realPlayHandle = NETClient.RealPlay(
                    deviceHandle,                                 // 攝影機設備的登入句 handle
                    channel,                                      // 要播放的通道號
                    IntPtr.Zero,                                  // 不直接顯示到窗口（重要：保持與原本邏輯一致）
                    playType                                      // 播放類型（主碼流或輔碼流）
                );

                // 檢查是否成功開始接收數據
                if (_realPlayHandle == IntPtr.Zero)
                {
                    string error = NETClient.GetLastError();
                    Debug.WriteLine($"開始即時播放失敗：{error}");

                    // 如果是連線失敗，可能是設備負載過重
                    if (error.Contains("Failed to get connect session information"))
                    {
                        Debug.WriteLine($"設備連線會話信息獲取失敗，可能原因：設備負載過重或網路問題，當前全域播放器數量: {_globalPlayerCount}");
                    }

                    return false;
                }

                // 設定數據回調 - 當有視頻數據時會呼叫我們的 OnVideoDataReceived 方法
                if (!NETClient.SetRealDataCallBack(
                    _realPlayHandle,                 // 播放句柄
                    _dataCallback,                   // 回調函數
                    IntPtr.Zero,                     // 用戶數據（我們不需要）
                    EM_REALDATA_FLAG.RAW_DATA        // 接收原始數據
                ))
                {
                    string error = NETClient.GetLastError();
                    Debug.WriteLine($"設定數據回調失敗：{error}");
                    return false;
                }

                Debug.WriteLine($"開始接收通道 {channel} 的視頻數據成功（{_streamType}）");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"開始接收數據時異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 啟用 IVS 畫線規則顯示（立即執行版本 - 參考DEMO）
        /// </summary>
        private void EnableIVSRenderImmediate()
        {
            try
            {
                if (_realPlayHandle != IntPtr.Zero)
                {
                    // 參考DEMO：在RealPlay之後立即呼叫RenderPrivateData
                    bool result = NETClient.RenderPrivateData(_realPlayHandle, true);
                    
                    if (result)
                    {
                        Debug.WriteLine("IVS 畫線規則顯示已立即啟用（參考DEMO方式）");
                    }
                    else
                    {
                        string error = NETClient.GetLastError();
                        Debug.WriteLine($"立即啟用 IVS 畫線規則失敗：{error}");
                    }
                }
                else
                {
                    Debug.WriteLine("無法立即啟用 IVS：_realPlayHandle 為空");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"立即啟用 IVS 畫線規則時發生異常：{ex.Message}");
            }
        }

        /// <summary>
        /// 啟用或停用 IVS 畫線規則顯示
        /// </summary>
        /// <param name="enable">true=啟用，false=停用</param>
        /// <returns>是否成功設定</returns>
        public bool SetIVSRender(bool enable)
        {
            try
            {
                // 更新內部狀態
                _ivsRenderEnabled = enable;

                // 如果正在播放，立即應用設定
                if (_isPlaying && _realPlayHandle != IntPtr.Zero)
                {
                    bool result = NETClient.RenderPrivateData(_realPlayHandle, enable);
                    
                    if (result)
                    {
                        Debug.WriteLine($"IVS 畫線規則顯示已{(enable ? "啟用" : "停用")}");
                    }
                    else
                    {
                        string error = NETClient.GetLastError();
                        Debug.WriteLine($"設定 IVS 畫線規則失敗：{error}");
                    }
                    
                    return result;
                }
                else
                {
                    Debug.WriteLine($"IVS 狀態已更新為 {(enable ? "啟用" : "停用")}，將在下次播放時生效");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"設定 IVS 畫線規則時發生異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切換 IVS 畫線規則顯示狀態
        /// </summary>
        /// <returns>切換後的狀態（true=啟用，false=停用）</returns>
        public bool ToggleIVSRender()
        {
            bool newState = !_ivsRenderEnabled;
            SetIVSRender(newState);
            return newState;
        }

        /// <summary>
        /// 取得當前 IVS 畫線規則顯示狀態
        /// </summary>
        /// <returns>true=啟用，false=停用</returns>
        public bool IsIVSRenderEnabled => _ivsRenderEnabled;

        /// <summary>
        /// 停止視頻播放 - 完全修正版本，確保可以重新播放
        /// </summary>
        public void StopPlay()
        {
            try
            {
                // 如果沒在播放，就不需要停止
                if (!_isPlaying) return;

                Debug.WriteLine($"開始停止視頻播放，端口: {_playPort}");

                // 標記為不在播放（提早設定，避免回調函數繼續處理）
                _isPlaying = false;

                // 第1步：停止從攝影機接收數據
                CleanupReceiveData();

                // 第2步：等待一下讓數據回調完全停止
                System.Threading.Thread.Sleep(50);

                // 第3步：清理 Play SDK 資源
                CleanupPlaySDK();

                // 第4步：清除顯示區域（但不清除 displayHandle）
                ClearDisplayArea();

                // 第5步：重置統計變數（但保留 displayHandle）
                ResetPlayerStateButKeepHandle();

                Debug.WriteLine($"視頻播放已完全停止，緩衝區重置次數: {_bufferResetCount}, 數據接收次數: {_dataReceiveCount}, 丟包次數: {_droppedFrameCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止播放時發生異常：{ex.Message}");
            }
        }

        /// <summary>
        /// 重置播放器狀態但保留顯示句柄 - 修正版本
        /// </summary>
        private void ResetPlayerStateButKeepHandle()
        {
            try
            {
                // 重置統計計數器
                _bufferResetCount = 0;
                _dataReceiveCount = 0;
                _droppedFrameCount = 0;
                _consecutiveErrorCount = 0;

                // 重置時間戱
                _lastBufferResetTime = DateTime.MinValue;
                _lastPerformanceReport = DateTime.MinValue;

                // 清理視頻資訊
                CurrentVideoInfo = null;

                // 重要：保留 _displayHandle 和 _ivsRenderEnabled，因為下次播放還需要用到
                // _displayHandle = IntPtr.Zero; // <-- 不要重置這個
                // _ivsRenderEnabled = true; // <-- 保留 IVS 設定狀態

                Debug.WriteLine("播放器狀態已重置（保留 displayHandle 和 IVS 設定）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"重置播放器狀態時發生異常：{ex.Message}");
            }
        }

        /// <summary>
        /// 清除顯示區域 - 改進版本，更溫和的清除方式
        /// </summary>
        private void ClearDisplayArea()
        {
            try
            {
                if (_displayHandle != IntPtr.Zero)
                {
                    // 方法1：使用 Control.FromHandle 進行 WinForms 特定清除
                    try
                    {
                        var control = Control.FromHandle(_displayHandle);
                        if (control != null && !control.IsDisposed)
                        {
                            // 檢查是否需要 Invoke
                            if (control.InvokeRequired)
                            {
                                control.Invoke(new Action(() =>
                                {
                                    control.BackColor = Color.Black;
                                    control.Invalidate(true);
                                    control.Update();
                                }));
                            }
                            else
                            {
                                control.BackColor = Color.Black;
                                control.Invalidate(true);
                                control.Update();
                            }

                            Debug.WriteLine("使用 Control 方式清除顯示區域成功");
                            return; // 如果成功就直接返回
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Control 方式清除失敗：{ex.Message}，嘗試其他方法");
                    }

                    // 方法2：使用 GDI+ 清除（備用方法）
                    try
                    {
                        using (var graphics = Graphics.FromHwnd(_displayHandle))
                        {
                            graphics.Clear(Color.Black);
                            graphics.Flush();
                        }
                        Debug.WriteLine("使用 GDI+ 方式清除顯示區域成功");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GDI+ 方式清除失敗：{ex.Message}");

                        // 方法3：最後備用 - Win32 API
                        try
                        {
                            ClearDisplayAreaWithWin32();
                        }
                        catch (Exception ex2)
                        {
                            Debug.WriteLine($"Win32 方式清除也失敗：{ex2.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除顯示區域時發生異常：{ex.Message}");
            }
        }

        /// <summary>
        /// 使用 Win32 API 清除顯示區域
        /// </summary>
        private void ClearDisplayAreaWithWin32()
        {
            if (_displayHandle != IntPtr.Zero)
            {
                var hdc = GetDC(_displayHandle);
                if (hdc != IntPtr.Zero)
                {
                    try
                    {
                        // 修正：使用正確的 RECT 結構類型
                        RECT rect = ConvertToWin32Rect(GetControlClientRect(_displayHandle));
                        var brush = CreateSolidBrush(0x000000); // 黑色

                        if (brush != IntPtr.Zero)
                        {
                            FillRect(hdc, ref rect, brush);
                            DeleteObject(brush);
                        }
                    }
                    finally
                    {
                        ReleaseDC(_displayHandle, hdc);
                    }
                }
            }
        }

        /// <summary>
        /// 將 System.Drawing.Rectangle 轉換為 Win32 RECT 結構
        /// </summary>
        private RECT ConvertToWin32Rect(Rectangle rectangle)
        {
            return new RECT
            {
                Left = rectangle.Left,
                Top = rectangle.Top,
                Right = rectangle.Right,
                Bottom = rectangle.Bottom
            };
        }

        /// <summary>
        /// 取得控件的客戶區矩形
        /// </summary>
        private Rectangle GetControlClientRect(IntPtr handle)
        {
            try
            {
                // 方法1：嘗試將句柄轉換為 Control 並取得其大小
                var control = Control.FromHandle(handle);
                if (control != null && !control.IsDisposed)
                {
                    return control.ClientRectangle;
                }
            }
            catch
            {
                // 如果轉換失敗，使用 Win32 API
            }

            // 方法2：使用 Win32 API 取得窗口大小
            if (GetClientRect(handle, out RECT rect))
            {
                return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }

            // 如果都失敗，返回預設大小
            return new Rectangle(0, 0, 320, 240);
        }

        // Win32 API 聲明
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint crColor);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // === 私有方法：內部使用的方法，外部不能直接呼叫 ===

        /// <summary>
        /// 初始化視頻資訊
        /// </summary>
        private void InitializeVideoInfo(string deviceName, int channel)
        {
            CurrentVideoInfo = new VideoInfo
            {
                DeviceName = deviceName,
                Channel = channel,
                StreamType = _streamType,
                StartTime = DateTime.Now,
                LastUpdate = DateTime.Now,
                TotalFrames = 0,
                TotalBytes = 0,

                // 根據碼流類型設定預設解析度
                Width = _streamType == VideoStreamType.Main ? 1920 : 704,
                Height = _streamType == VideoStreamType.Main ? 1080 : 576,

                // 預估FPS和碼率（實際數值會在播放時更新）
                Fps = _streamType == VideoStreamType.Main ? 25.0 : 15.0,
                Bitrate = _streamType == VideoStreamType.Main ? 4000.0 : 512.0
            };

            // 重置統計計數器
            _bufferResetCount = 0;
            _dataReceiveCount = 0;
            _droppedFrameCount = 0;
            _lastBufferResetTime = DateTime.MinValue;
            _lastPerformanceReport = DateTime.Now;
        }

        /// <summary>
        /// 確保 Play SDK 已經初始化（全域只執行一次）
        /// </summary>
        private static void EnsureSDKInitialized()
        {
            // 使用鎖確保只有一個執行緒可以初始化
            lock (_initLock)
            {
                // 如果已經初始化過了，就不用再做了
                if (!_sdkInitialized)
                {
                    try
                    {
                        // 取得 Play SDK 版本資訊
                        uint version = PlaySDK.PLAY_GetSdkVersion();
                        Debug.WriteLine($"Play SDK 版本：{version:X8}");

                        // 初始化 DirectDraw（某些環境需要）
                        PlaySDK.PLAY_InitDDraw();

                        // 標記為已初始化
                        _sdkInitialized = true;
                        Debug.WriteLine("Play SDK 全域初始化完成");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Play SDK 初始化失敗：{ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 初始化 Play SDK 播放器（每個播放器都要做）- 進一步優化多播放器支援
        /// </summary>
        /// <returns>是否初始化成功</returns>
        private bool InitializePlaySDK()
        {
            try
            {
                // 第1步：取得可用的播放端口（每個播放器需要一個端口）
                if (!PlaySDK.PLAY_GetFreePort(ref _playPort))
                {
                    Debug.WriteLine("無法取得 Play SDK 端口");
                    return false;
                }
                Debug.WriteLine($"取得播放端口：{_playPort}");

                // 第2步：設定為即時串流模式（不是播放檔案）
                if (!PlaySDK.PLAY_SetStreamOpenMode(_playPort, PlaySDK.STREAME_REALTIME))
                {
                    Debug.WriteLine("設定串流模式失敗");
                    return false;
                }

                // 第3步：根據用戶選擇設定解碼引擎（重要：這裡恢復硬體/軟體解碼選擇功能）
                SetupDecodeEngine();

                // 第4步：根據播放器數量動態調整緩衝區大小
                uint bufferSize = CalculateOptimalBufferSize();
                if (!PlaySDK.PLAY_OpenStream(_playPort, IntPtr.Zero, 0, bufferSize))
                {
                    Debug.WriteLine($"開啟串流失敗，緩衝區大小: {bufferSize}");
                    return false;
                }

                // 第5步：開始播放到指定窗口
                if (!PlaySDK.PLAY_Play(_playPort, _displayHandle))
                {
                    Debug.WriteLine("Play SDK 播放失敗");
                    return false;
                }

                Debug.WriteLine($"Play SDK 播放器初始化完成，緩衝區大小: {bufferSize}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化 Play SDK 時異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 計算最佳緩衝區大小
        /// </summary>
        private uint CalculateOptimalBufferSize()
        {
            // 根據播放器數量和碼流類型動態調整
            uint baseSize;

            if (_globalPlayerCount <= 4)
            {
                baseSize = 5 * 1024 * 1024; // 5MB - 少量播放器時較大緩衝區
            }
            else if (_globalPlayerCount <= 16)
            {
                baseSize = 3 * 1024 * 1024; // 3MB - 中等數量時中等緩衝區
            }
            else if (_globalPlayerCount <= 32)
            {
                baseSize = 1 * 1024 * 1024; // 1MB - 大量播放器時較小緩衝區
            }
            else
            {
                baseSize = 512 * 1024; // 512KB - 極大量播放器時最小緩衝區
            }

            // 輔碼流使用更小的緩衝區
            if (_streamType == VideoStreamType.Sub)
            {
                baseSize = baseSize / 2;
            }

            return baseSize;
        }

        /// <summary>
        /// 根據用戶選擇設定解碼引擎 - 恢復完整功能
        /// </summary>
        private void SetupDecodeEngine()
        {
            bool success = false;

            // 根據用戶選擇的模式來決定解碼方式
            switch (_decodeMode)
            {
                case DecodeMode.Software:
                    // 用戶選擇軟體解碼，只嘗試 CPU 解碼
                    success = TrySoftwareDecoding();
                    Debug.WriteLine("用戶選擇：只使用軟體解碼");
                    break;

                case DecodeMode.Hardware:
                    // 用戶選擇硬體解碼，只嘗試 GPU 解碼
                    success = TryHardwareDecoding();
                    Debug.WriteLine("用戶選擇：只使用硬體解碼");
                    break;

                case DecodeMode.Auto:
                default:
                    // 自動選擇：調整為先試軟體，再試硬體（考慮相容性）
                    Debug.WriteLine("自動選擇解碼模式：先試軟體，再試硬體");
                    success = TrySoftwareDecoding();
                    if (!success)
                    {
                        Debug.WriteLine("軟體解碼失敗，改用硬體解碼");
                        success = TryHardwareDecoding();
                    }
                    break;
            }

            if (!success)
            {
                Debug.WriteLine("所有解碼模式都設定失敗，使用預設模式");
            }
        }

        /// <summary>
        /// 嘗試硬體解碼 - 用顯示卡 GPU 來解碼視頻
        /// </summary>
        /// <returns>是否成功設定</returns>
        private bool TryHardwareDecoding()
        {
            // 嘗試1：最快的硬體加速 (DirectX 11)
            if (PlaySDK.PLAY_SetEngine(_playPort, PlaySDK.DecodeType.DECODE_HW_FAST, PlaySDK.RenderType.RENDER_D3D11))
            {
                Debug.WriteLine("硬體解碼成功：使用 GPU 快速解碼 (D3D11)");
                return true;
            }

            // 嘗試2：一般硬體解碼 (DirectX 9)
            if (PlaySDK.PLAY_SetEngine(_playPort, PlaySDK.DecodeType.DECODE_HW, PlaySDK.RenderType.RENDER_D3D))
            {
                Debug.WriteLine("硬體解碼成功：使用 GPU 解碼 (D3D9)");
                return true;
            }

            Debug.WriteLine("硬體解碼失敗：顯示卡不支援或驅動程式問題");
            return false;
        }

        /// <summary>
        /// 嘗試軟體解碼 - 用電腦 CPU 來解碼視頻
        /// </summary>
        /// <returns>是否成功設定</returns>
        private bool TrySoftwareDecoding()
        {
            if (PlaySDK.PLAY_SetEngine(_playPort, PlaySDK.DecodeType.DECODE_SW, PlaySDK.RenderType.RENDER_GDI))
            {
                Debug.WriteLine("軟體解碼成功：使用 CPU 解碼 (GDI)");
                return true;
            }

            Debug.WriteLine("軟體解碼失敗：這很罕見，可能是系統問題");
            return false;
        }

        /// <summary>
        /// 清理接收數據資源
        /// </summary>
        private void CleanupReceiveData()
        {
            if (_realPlayHandle != IntPtr.Zero)
            {
                // 停止即時播放
                NETClient.StopRealPlay(_realPlayHandle);
                _realPlayHandle = IntPtr.Zero;
                Debug.WriteLine("停止接收視頻數據");
            }
        }

        /// <summary>
        /// 清理 Play SDK 資源 - 加強版本
        /// </summary>
        private void CleanupPlaySDK()
        {
            if (_playPort != -1)
            {
                try
                {
                    // 停止播放
                    bool stopResult = PlaySDK.PLAY_Stop(_playPort);
                    Debug.WriteLine($"PLAY_Stop 結果: {stopResult}");

                    // 關閉串流
                    bool closeResult = PlaySDK.PLAY_CloseStream(_playPort);
                    Debug.WriteLine($"PLAY_CloseStream 結果: {closeResult}");

                    // 釋放端口
                    bool releaseResult = PlaySDK.PLAY_ReleasePort(_playPort);
                    Debug.WriteLine($"PLAY_ReleasePort 結果: {releaseResult}");

                    Debug.WriteLine($"清理 Play SDK 端口：{_playPort}");
                    _playPort = -1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"清理 Play SDK 時異常：{ex.Message}");
                    // 即使出現異常，也要重置端口號
                    _playPort = -1;
                }
            }
        }

        /// <summary>
        /// 視頻數據回調函數 - 當收到視頻數據時自動呼叫並更新統計資訊（優化版本）
        /// </summary>
        private void OnVideoDataReceived(IntPtr lRealHandle, uint dwDataType, IntPtr pBuffer, uint dwBufSize, IntPtr param, IntPtr dwUser)
        {
            try
            {
                // 如果不在播放狀態，忽略數據
                if (!_isPlaying || _playPort == -1) return;

                // 檢查數據是否有效
                if (pBuffer == IntPtr.Zero || dwBufSize == 0)
                {
                    _droppedFrameCount++;
                    return;
                }

                // 增加數據接收計數
                Interlocked.Increment(ref _dataReceiveCount);

                // 更新視頻統計資訊（降低頻率）
                if (_dataReceiveCount % 30 == 0) // 每30幀更新一次統計
                {
                    UpdateVideoStatistics(dwBufSize, dwDataType);
                }

                // 將數據送給 Play SDK 進行解碼和顯示
                bool result = PlaySDK.PLAY_InputData(_playPort, pBuffer, dwBufSize);

                // 如果輸入失敗，檢查是否是緩衝區滿了
                if (!result)
                {
                    uint error = PlaySDK.PLAY_GetLastErrorEx();
                    if (error == PlaySDK.PLAY_BUF_OVER)
                    {
                        // 增加緩衝區重置計數
                        _bufferResetCount++;

                        // 檢查重置頻率，如果過於頻繁則記錄警告（減少日誌頻率）
                        var now = DateTime.Now;
                        if (_lastBufferResetTime != DateTime.MinValue)
                        {
                            var timeSinceLastReset = (now - _lastBufferResetTime).TotalSeconds;
                            if (timeSinceLastReset < 2.0 && _bufferResetCount % 5 == 0) // 降低警告頻率
                            {
                                Debug.WriteLine($"警告：緩衝區重置頻繁，間隔: {timeSinceLastReset:F2}秒，重置次數: {_bufferResetCount}，全域播放器: {_globalPlayerCount}");
                            }
                        }
                        _lastBufferResetTime = now;

                        // 緩衝區滿了，重置一下（清空緩衝區）
                        PlaySDK.PLAY_ResetSourceBuffer(_playPort);

                        // 減少日誌頻率 - 每20次重置才記錄一次
                        if (_bufferResetCount % 20 == 0)
                        {
                            Debug.WriteLine($"Play SDK 緩衝區已重置 (第{_bufferResetCount}次)，全域播放器數量: {_globalPlayerCount}");
                        }
                    }
                    else
                    {
                        _consecutiveErrorCount++;
                        if (_consecutiveErrorCount > 20) // 提高錯誤閾值
                        {
                            Debug.WriteLine($"連續輸入數據失敗，錯誤代碼: {error}，連續錯誤次數: {_consecutiveErrorCount}");
                        }
                    }
                }
                else
                {
                    _consecutiveErrorCount = 0; // 成功時重置錯誤計數器
                }

                // 定期性能報告（每60秒一次）
                var timeSinceLastReport = (DateTime.Now - _lastPerformanceReport).TotalSeconds;
                if (timeSinceLastReport >= 60)
                {
                    ReportPerformanceStatistics();
                    _lastPerformanceReport = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"處理視頻數據時異常：{ex.Message}");
            }
        }

        /// <summary>
        /// 報告性能統計
        /// </summary>
        private void ReportPerformanceStatistics()
        {
            if (CurrentVideoInfo != null)
            {
                var elapsed = (DateTime.Now - CurrentVideoInfo.StartTime).TotalSeconds;
                var avgFps = elapsed > 0 ? CurrentVideoInfo.TotalFrames / elapsed : 0;
                var avgBitrate = elapsed > 0 ? (CurrentVideoInfo.TotalBytes * 8) / (elapsed * 1000) : 0;

                Debug.WriteLine($"性能報告 - 端口{_playPort}: FPS={avgFps:F1}, 碼率={avgBitrate:F1}kbps, 重置={_bufferResetCount}, 丟包={_droppedFrameCount}, 解碼={_decodeMode}, IVS={_ivsRenderEnabled}");
            }
        }

        /// <summary>
        /// 更新視頻統計資訊
        /// </summary>
        private void UpdateVideoStatistics(uint dataSize, uint dataType)
        {
            if (CurrentVideoInfo == null) return;

            try
            {
                // 累加數據量
                CurrentVideoInfo.TotalBytes += dataSize;

                // 如果是視頻幀數據，增加幀計數
                if (dataType == 2) // 2 = 視頻數據
                {
                    CurrentVideoInfo.TotalFrames++;
                }

                // 更新時間戱
                CurrentVideoInfo.LastUpdate = DateTime.Now;

                // 每5秒更新一次計算結果（降低計算頻率）
                var elapsed = (DateTime.Now - CurrentVideoInfo.StartTime).TotalSeconds;
                if (elapsed >= 5.0)
                {
                    CurrentVideoInfo.Fps = CurrentVideoInfo.CalculateActualFps();
                    CurrentVideoInfo.Bitrate = CurrentVideoInfo.CalculateActualBitrate();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新視頻統計時發生異常：{ex.Message}");
            }
        }

        // === IDisposable 實作 - 資源清理 ===

        /// <summary>
        /// 釋放所有資源 - 當播放器不再使用時呼叫
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                StopPlay();

                // 減少全域播放器計數
                Interlocked.Decrement(ref _globalPlayerCount);

                _disposed = true;
                Debug.WriteLine($"SimpleVideoPlayer 已銷毀，剩餘全域播放器數量: {_globalPlayerCount}");
            }
        }

        // === 屬性：外部可以讀取的資訊 ===

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying => _isPlaying;

        /// <summary>
        /// 當前碼流類型
        /// </summary>
        public VideoStreamType StreamType => _streamType;

        /// <summary>
        /// 當前解碼模式
        /// </summary>
        public DecodeMode DecodeMode => _decodeMode;

        /// <summary>
        /// 緩衝區重置次數
        /// </summary>
        public int BufferResetCount => _bufferResetCount;

        /// <summary>
        /// 數據接收次數
        /// </summary>
        public long DataReceiveCount => _dataReceiveCount;

        /// <summary>
        /// 丟包次數
        /// </summary>
        public long DroppedFrameCount => _droppedFrameCount;

        /// <summary>
        /// 全域播放器數量
        /// </summary>
        public static int GlobalPlayerCount => _globalPlayerCount;

        /// <summary>
        /// 全域清理 Play SDK（程式結束時呼叫）
        /// </summary>
        public static void GlobalCleanup()
        {
            lock (_initLock)
            {
                if (_sdkInitialized)
                {
                    PlaySDK.PLAY_ReleaseDDraw();
                    _sdkInitialized = false;
                    Debug.WriteLine("Play SDK 全域清理完成");
                }
            }
        }
    }
}