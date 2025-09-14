// SimpleVideoPlayer.cs - 極簡視頻播放器
// 這個檔案負責播放攝影機的視頻，支援不同的解碼模式和碼流類型

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetSDKCS;
using System.Threading;

namespace SentryX
{
    /// <summary>
    /// 極簡視頻播放器 - 專門播放大華攝影機的視頻
    /// </summary>
    public class SimpleVideoPlayer : IDisposable
    {
        // === Geohot 風格：把所有變數放最上面，一目了然 ===

        /// <summary>
        /// Play SDK 的播放端口號 - 每個播放器需要一個獨立的端口號
        /// </summary>
        private int _playPort = -1;

        /// <summary>
        /// 大華 SDK 的實時播放句柄 - 用來控制從攝影機接收數據
        /// </summary>
        private IntPtr _realPlayHandle = IntPtr.Zero;

        /// <summary>
        /// 顯示視頻的窗口句柄 - 告訴播放器要在哪個窗口顯示視頻
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
        public SimpleVideoPlayer(DecodeMode decodeMode = DecodeMode.Software, VideoStreamType streamType = VideoStreamType.Main)
        {
            // 記住用戶選擇的解碼模式和碼流類型
            _decodeMode = decodeMode;
            _streamType = streamType;

            // 確保 Play SDK 已經初始化（全域只做一次）
            EnsureSDKInitialized();

            // 建立數據回調函數 - 當有視頻數據時會呼叫 OnVideoDataReceived
            _dataCallback = new fRealDataCallBackEx2(OnVideoDataReceived);

            // 增加全域播放器計數
            Interlocked.Increment(ref _globalPlayerCount);

            Debug.WriteLine($"SimpleVideoPlayer 已建立，解碼模式: {decodeMode}, 碼流: {streamType}, 全域播放器數量: {_globalPlayerCount}");
        }

        // === 公開方法：使用者會呼叫的方法 ===

        /// <summary>
        /// 開始播放指定攝影機的視頻
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

                // 第3步：如果已經在播放其他視頻，先停止
                if (_isPlaying)
                {
                    StopPlay();
                }

                // 第4步：記住要在哪個窗口顯示視頻
                _displayHandle = windowHandle;

                // 第5步：初始化視頻資訊
                InitializeVideoInfo(deviceName, channel);

                // 第6步：初始化 Play SDK 播放器（準備解碼環境）
                if (!InitializePlaySDK())
                {
                    Debug.WriteLine("Play SDK 初始化失敗");
                    return false;
                }

                // 第7步：開始從大華攝影機接收數據
                if (!StartReceiveData(deviceHandle, channel))
                {
                    Debug.WriteLine("開始接收視頻數據失敗");
                    CleanupPlaySDK(); // 失敗了就清理資源
                    return false;
                }

                // 第8步：標記為正在播放
                _isPlaying = true;
                _consecutiveErrorCount = 0; // 重置錯誤計數器
                Debug.WriteLine($"視頻播放開始成功，設備: {deviceName}, 通道: {channel}, 碼流: {_streamType}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"開始播放時發生異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止視頻播放
        /// </summary>
        public void StopPlay()
        {
            try
            {
                // 如果沒在播放，就不需要停止
                if (!_isPlaying) return;

                // 標記為不在播放
                _isPlaying = false;

                // 第1步：停止從攝影機接收數據
                StopReceiveData();

                // 第2步：清理 Play SDK 資源
                CleanupPlaySDK();

                // 第3步：清理視頻資訊
                CurrentVideoInfo = null;

                Debug.WriteLine($"視頻播放已停止，緩衝區重置次數: {_bufferResetCount}, 數據接收次數: {_dataReceiveCount}, 丟包次數: {_droppedFrameCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止播放時發生異常：{ex.Message}");
            }
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

                // 第3步：根據用戶選擇設定解碼引擎
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
        /// 根據用戶選擇設定解碼引擎 - 修改為優先軟體解碼
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
        /// 開始從大華攝影機接收視頻數據 - 支援碼流選擇
        /// </summary>
        /// <param name="deviceHandle">攝影機設備句柄</param>
        /// <param name="channel">通道號</param>
        /// <returns>是否成功</returns>
        private bool StartReceiveData(IntPtr deviceHandle, int channel)
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
                    deviceHandle,                                 // 攝影機設備的登入句Handlesns
                    channel,                                      // 要播放的通道號
                    IntPtr.Zero,                                  // 不直接顯示到窗口
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
        /// 停止從攝影機接收數據
        /// </summary>
        private void StopReceiveData()
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
        /// 清理 Play SDK 資源
        /// </summary>
        private void CleanupPlaySDK()
        {
            if (_playPort != -1)
            {
                try
                {
                    // 停止播放
                    PlaySDK.PLAY_Stop(_playPort);

                    // 關閉串流
                    PlaySDK.PLAY_CloseStream(_playPort);

                    // 釋放端口
                    PlaySDK.PLAY_ReleasePort(_playPort);

                    Debug.WriteLine($"清理 Play SDK 端口：{_playPort}");
                    _playPort = -1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"清理 Play SDK 時異常：{ex.Message}");
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
                
                Debug.WriteLine($"性能報告 - 端口{_playPort}: FPS={avgFps:F1}, 碼率={avgBitrate:F1}kbps, 重置={_bufferResetCount}, 丟包={_droppedFrameCount}");
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

                // 更新時間戳
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