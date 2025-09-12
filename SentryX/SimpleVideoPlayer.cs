// SimpleVideoPlayer.cs - 極簡視頻播放器
// 這個檔案負責播放攝影機的視頻，支援不同的解碼模式

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetSDKCS;

namespace SentryX
{
    /// <summary>
    /// 解碼模式 - 告訴播放器用什麼方式解碼視頻
    /// </summary>
    public enum DecodeMode
    {
        /// <summary>
        /// 自動選擇 - 先試GPU硬體解碼，不行再用CPU軟體解碼
        /// </summary>
        Auto,

        /// <summary>
        /// 軟體解碼 - 用電腦CPU來解碼，比較慢但相容性最好
        /// </summary>
        Software,

        /// <summary>
        /// 硬體解碼 - 用顯示卡GPU來解碼，很快但可能不相容某些設備
        /// </summary>
        Hardware
    }


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
        /// 當前使用的解碼模式 - 記住用戶選擇了哪種解碼方式
        /// </summary>
        private DecodeMode _decodeMode = DecodeMode.Auto;

        // === 靜態變數：全部程式共用的資源 ===

        /// <summary>
        /// Play SDK 是否已經初始化（整個程式只需要初始化一次）
        /// </summary>
        private static bool _sdkInitialized = false;

        /// <summary>
        /// 執行緒鎖 - 確保只有一個執行緒可以初始化 SDK
        /// </summary>
        private static readonly object _initLock = new object();

        // === 建構子：建立播放器時執行 ===

        /// <summary>
        /// 建立新的視頻播放器
        /// </summary>
        /// <param name="decodeMode">指定要使用的解碼模式</param>
        public SimpleVideoPlayer(DecodeMode decodeMode = DecodeMode.Auto)
        {
            // 記住用戶選擇的解碼模式
            _decodeMode = decodeMode;

            // 確保 Play SDK 已經初始化（全域只做一次）
            EnsureSDKInitialized();

            // 建立數據回調函數 - 當有視頻數據時會呼叫 OnVideoDataReceived
            _dataCallback = new fRealDataCallBackEx2(OnVideoDataReceived);

            Debug.WriteLine($"SimpleVideoPlayer 已建立，解碼模式: {decodeMode}");
        }

        // === 公開方法：使用者會呼叫的方法 ===

        /// <summary>
        /// 開始播放指定攝影機的視頻
        /// </summary>
        /// <param name="deviceHandle">攝影機設備的登入句柄</param>
        /// <param name="channel">要播放的通道號（0=第1個通道）</param>
        /// <param name="windowHandle">要顯示視頻的窗口句柄</param>
        /// <returns>是否成功開始播放</returns>
        public bool StartPlay(IntPtr deviceHandle, int channel, IntPtr windowHandle)
        {
            try
            {
                // 第1步：檢查輸入參數是否正確
                if (deviceHandle == IntPtr.Zero || windowHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("錯誤：設備句柄或窗口句柄為空");
                    return false;
                }

                // 第2步：如果已經在播放其他視頻，先停止
                if (_isPlaying)
                {
                    StopPlay();
                }

                // 第3步：記住要在哪個窗口顯示視頻
                _displayHandle = windowHandle;

                // 第4步：初始化 Play SDK 播放器（準備解碼環境）
                if (!InitializePlaySDK())
                {
                    Debug.WriteLine("Play SDK 初始化失敗");
                    return false;
                }

                // 第5步：開始從大華攝影機接收數據
                if (!StartReceiveData(deviceHandle, channel))
                {
                    Debug.WriteLine("開始接收視頻數據失敗");
                    CleanupPlaySDK(); // 失敗了就清理資源
                    return false;
                }

                // 第6步：標記為正在播放
                _isPlaying = true;
                Debug.WriteLine($"視頻播放開始成功，通道: {channel}");
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

                Debug.WriteLine("視頻播放已停止");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止播放時發生異常：{ex.Message}");
            }
        }

        // === 私有方法：內部使用的方法，外部不能直接呼叫 ===

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
        /// 初始化 Play SDK 播放器（每個播放器都要做）
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

                // 第4步：開啟串流緩衝區（用來暫存視頻數據）
                const uint bufferSize = 5 * 1024 * 1024; // 5MB 緩衝區
                if (!PlaySDK.PLAY_OpenStream(_playPort, IntPtr.Zero, 0, bufferSize))
                {
                    Debug.WriteLine("開啟串流失敗");
                    return false;
                }

                // 第5步：開始播放到指定窗口
                if (!PlaySDK.PLAY_Play(_playPort, _displayHandle))
                {
                    Debug.WriteLine("Play SDK 播放失敗");
                    return false;
                }

                Debug.WriteLine("Play SDK 播放器初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化 Play SDK 時異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根據用戶選擇設定解碼引擎
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
                    // 自動選擇：先試硬體，不行再用軟體
                    Debug.WriteLine("自動選擇解碼模式：先試硬體，再試軟體");
                    success = TryHardwareDecoding();
                    if (!success)
                    {
                        Debug.WriteLine("硬體解碼失敗，改用軟體解碼");
                        success = TrySoftwareDecoding();
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
        /// 開始從大華攝影機接收視頻數據
        /// </summary>
        /// <param name="deviceHandle">攝影機設備句柄</param>
        /// <param name="channel">通道號</param>
        /// <returns>是否成功</returns>
        private bool StartReceiveData(IntPtr deviceHandle, int channel)
        {
            try
            {
                // 開始即時播放（從大華攝影機取得數據）
                // 注意：這裡的 hWnd 設為 IntPtr.Zero，因為我們用 Play SDK 來顯示
                _realPlayHandle = NETClient.RealPlay(
                    deviceHandle,                                 // 攝影機設備的登入句柄
                    channel,                                      // 要播放的通道號
                    IntPtr.Zero,                                  // 不直接顯示到窗口
                    EM_RealPlayType.EM_A_RType_Realplay_0        // 即時播放類型
                );

                // 檢查是否成功開始接收數據
                if (_realPlayHandle == IntPtr.Zero)
                {
                    string error = NETClient.GetLastError();
                    Debug.WriteLine($"開始即時播放失敗：{error}");
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

                Debug.WriteLine($"開始接收通道 {channel} 的視頻數據成功");
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
        /// 視頻數據回調函數 - 當收到視頻數據時自動呼叫
        /// 這個方法會被 SDK 自動呼叫，我們不需要手動呼叫
        /// </summary>
        /// <param name="lRealHandle">播放句柄</param>
        /// <param name="dwDataType">數據類型（1=檔頭資訊,2=視頻,3=音訊）</param>
        /// <param name="pBuffer">數據緩衝區</param>
        /// <param name="dwBufSize">數據大小</param>
        /// <param name="param">參數（未使用）</param>
        /// <param name="dwUser">用戶數據（未使用）</param>
        private void OnVideoDataReceived(IntPtr lRealHandle, uint dwDataType, IntPtr pBuffer, uint dwBufSize, IntPtr param, IntPtr dwUser)
        {
            try
            {
                // 如果不在播放狀態，忽略數據
                if (!_isPlaying || _playPort == -1) return;

                // 檢查數據是否有效
                if (pBuffer == IntPtr.Zero || dwBufSize == 0) return;

                // 將數據送給 Play SDK 進行解碼和顯示
                bool result = PlaySDK.PLAY_InputData(_playPort, pBuffer, dwBufSize);

                // 如果輸入失敗，檢查是否是緩衝區滿了
                if (!result)
                {
                    uint error = PlaySDK.PLAY_GetLastErrorEx();
                    if (error == PlaySDK.PLAY_BUF_OVER)
                    {
                        // 緩衝區滿了，重置一下（清空緩衝區）
                        PlaySDK.PLAY_ResetSourceBuffer(_playPort);
                        Debug.WriteLine("Play SDK 緩衝區已重置");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"處理視頻數據時異常：{ex.Message}");
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
                _disposed = true;
                Debug.WriteLine("SimpleVideoPlayer 已銷毀");
            }
        }

        // === 屬性：外部可以讀取的資訊 ===

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying => _isPlaying;

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