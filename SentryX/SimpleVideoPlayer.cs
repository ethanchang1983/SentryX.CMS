// SimpleVideoPlayer.cs - 極簡視頻播放器 - 支援 IVS 顯示
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetSDKCS;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;

namespace SentryX
{
    /// <summary>
    /// 極簡視頻播放器 - 支援 IVS 畫線規則顯示和硬體/軟體解碼選擇
    /// </summary>
    public class SimpleVideoPlayer : IDisposable
    {
        // === 核心變數 ===
        private int _playPort = -1;
        private IntPtr _realPlayHandle = IntPtr.Zero;
        private IntPtr _displayHandle = IntPtr.Zero;
        private bool _isPlaying = false;
        private bool _disposed = false;
        // 新增的類別成員變數（加在 SimpleVideoPlayer 類別頂部）
        // 在類別頂部新增
        private DateTime _lastIVSSetTime = DateTime.MinValue;
        private bool _needRetryIVS = false;
        private int _ivsRetryCount = 0;
        private bool _ivsSetSuccessfully = false;
        // 將 _dataCallback 欄位宣告為可為 null，修正 CS8618
        private fRealDataCallBackEx2? _dataCallback;
        private DecodeMode _decodeMode = DecodeMode.Software;
        private VideoStreamType _streamType = VideoStreamType.Main;

        // 🔥 新增：IVS 相關變數
        private bool _ivsRenderEnabled = true;  // 預設啟用 IVS

        // === 統計變數 ===
        public VideoInfo? CurrentVideoInfo { get; private set; }
        private int _bufferResetCount = 0;
        private DateTime _lastBufferResetTime = DateTime.MinValue;
        private long _dataReceiveCount = 0;
        private int _consecutiveErrorCount = 0;
        private long _droppedFrameCount = 0;
        private DateTime _lastPerformanceReport = DateTime.MinValue;

        // === 靜態變數 ===
        private static bool _sdkInitialized = false;
        private static readonly object _initLock = new object();
        private static int _globalPlayerCount = 0;

        // === 建構子 ===
        /// <summary>
        /// 🔥 修正：建構子 - 徹底避免軟體解碼模式的數據重複處理
        /// </summary>
        public SimpleVideoPlayer(DecodeMode decodeMode = DecodeMode.Software, VideoStreamType streamType = VideoStreamType.Main, bool enableIVSByDefault = true)
        {
            _decodeMode = decodeMode;
            _streamType = streamType;
            _ivsRenderEnabled = enableIVSByDefault;

            EnsureSDKInitialized();
            
            // 🔥 關鍵修正：只有硬體解碼模式才需要數據回調
            // 軟體解碼模式完全不創建回調，確保數據不被重複處理
            _dataCallback = null; // 明確設為 null
            
            if (_decodeMode == DecodeMode.Hardware || _decodeMode == DecodeMode.Auto)
            {
                _dataCallback = new fRealDataCallBackEx2(OnVideoDataReceived);
                Debug.WriteLine($"已建立數據回調（{_decodeMode} 模式）");
            }
            else
            {
                Debug.WriteLine($"跳過數據回調建立（軟體解碼模式，確保無重複處理）");
            }
            
            Interlocked.Increment(ref _globalPlayerCount);

            Debug.WriteLine($"SimpleVideoPlayer 建立：解碼={decodeMode}, 碼流={streamType}, IVS={enableIVSByDefault}");
        }

        // === 🔥 IVS 相關公開方法 - 修正版本，支援硬體解碼 ===

        /// <summary>
        /// 🔥 取得當前 IVS 顯示狀態
        /// </summary>
        public bool IsIVSRenderEnabled => _ivsRenderEnabled;

        /// <summary>
        /// 🔥 檢查當前解碼模式是否支援 IVS - 修正：硬體解碼也支援
        /// </summary>
        public bool IsIVSSupported()
        {
            // 🔥 重要修正：兩種解碼模式都支援 IVS，但使用不同的 API
            return true; // 軟體解碼使用 NETClient.RenderPrivateData，硬體解碼使用 PlaySDK.PLAY_RenderPrivateData
        }

        /// <summary>
        /// 🔥 設定 IVS 顯示狀態 - 統一支援軟體和硬體解碼，增強版本
        /// </summary>
        public bool SetIVSRender(bool enable)
        {
            try
            {
                _ivsRenderEnabled = enable;

                if (!_isPlaying)
                {
                    Debug.WriteLine($"IVS 狀態已更新為 {(enable ? "啟用" : "停用")}，將在播放時生效");
                    return true;
                }

                bool result = false;

                if (_decodeMode == DecodeMode.Software)
                {
                    if (_realPlayHandle != IntPtr.Zero)
                    {
                        result = NETClient.RenderPrivateData(_realPlayHandle, enable);
                        Debug.WriteLine($"軟體解碼 IVS 設定結果: {result}");
                    }
                }
                else
                {
                    if (_playPort != -1)
                    {
                        // 硬體解碼需要多次嘗試
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            result = PlaySDK.PLAY_RenderPrivateData(_playPort, enable, 0);

                            if (result)
                            {
                                Debug.WriteLine($"🎯 硬體解碼 IVS 設定成功 (第{attempt + 1}次嘗試): {(enable ? "啟用" : "停用")}");

                                // 記錄設定時間
                                _lastIVSSetTime = DateTime.Now;

                                // 驗證設定是否真的生效
                                Thread.Sleep(50);

                                // 強制刷新
                                if (_displayHandle != IntPtr.Zero)
                                {
                                    InvalidateRect(_displayHandle, IntPtr.Zero, true); // 使用 true 強制擦除背景
                                    UpdateWindow(_displayHandle);
                                }

                                // 更新狀態標記
                                _ivsSetSuccessfully = enable;

                                break;
                            }
                            else
                            {
                                uint error = PlaySDK.PLAY_GetLastErrorEx();
                                Debug.WriteLine($"硬體解碼 IVS 設定失敗 (嘗試{attempt + 1}), 錯誤: {error}");
                                Thread.Sleep(100);
                            }
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"設定 IVS 時發生異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 🔥 切換 IVS 顯示狀態
        /// </summary>
        public bool ToggleIVSRender()
        {
            bool newState = !_ivsRenderEnabled;
            SetIVSRender(newState);
            return newState;
        }

        // 🔥 新增 Win32 API 聲明
        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        // === 🔥 修正：開始播放 - 根據解碼模式選擇不同的播放方式 ===
        public bool StartPlay(IntPtr deviceHandle, int channel, IntPtr windowHandle, string deviceName = "")
        {
            try
            {
                if (deviceHandle == IntPtr.Zero || windowHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("錯誤：設備句柄或窗口句柄為空");
                    return false;
                }

                if (_globalPlayerCount > 32)
                {
                    Debug.WriteLine($"警告：當前播放器數量過多 ({_globalPlayerCount})，可能影響性能");
                }

                if (_isPlaying)
                {
                    Debug.WriteLine("檢測到正在播放，先完全停止");
                    StopPlay();
                    System.Threading.Thread.Sleep(100);
                }

                if (_playPort != -1)
                {
                    Debug.WriteLine("檢測到殘留的播放端口，先清理");
                    CleanupPlaySDK();
                }

                _displayHandle = windowHandle;
                InitializeVideoInfo(deviceName, channel);

                // 🔥 關鍵修正：根據解碼模式選擇播放方式
                bool success = false;
                
                if (_decodeMode == DecodeMode.Software)
                {
                    // 軟體解碼：使用大華 SDK 直接顯示（支援 IVS）
                    success = StartSoftwareDecodeWithIVS(deviceHandle, channel);
                }
                else
                {
                    // 硬體解碼：使用 Play SDK 架構（現在也支援 IVS）
                    success = StartHardwareDecodeWithIVS(deviceHandle, channel);
                }

                if (success)
                {
                    _isPlaying = true;
                    _consecutiveErrorCount = 0;
                    Debug.WriteLine($"🎉 播放成功！解碼:{_decodeMode}, IVS:{_ivsRenderEnabled}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"開始播放異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 🔥 軟體解碼模式 - 大華 SDK 直接顯示（支援 IVS）- 修正版本
        /// </summary>
        private bool StartSoftwareDecodeWithIVS(IntPtr deviceHandle, int channel)
        {
            try
            {
                EM_RealPlayType playType = _streamType == VideoStreamType.Main
                    ? EM_RealPlayType.EM_A_RType_Realplay_0
                    : EM_RealPlayType.EM_A_RType_Realplay_1;

                Debug.WriteLine($"🎯 軟體解碼：使用純大華 SDK 顯示，避免任何數據回調");

                // 🔥 100% 純軟體解碼：大華 SDK 直接處理一切
                _realPlayHandle = NETClient.RealPlay(deviceHandle, channel, _displayHandle, playType);

                if (_realPlayHandle == IntPtr.Zero)
                {
                    string error = NETClient.GetLastError();
                    Debug.WriteLine($"❌ 軟體解碼啟動失敗：{error}");
                    return false;
                }

                // 🔥 軟體解碼：延遲設定 IVS，確保播放穩定
                if (_ivsRenderEnabled)
                {
                    // 等待播放穩定後再設定 IVS
                    Thread.Sleep(200);
                    bool ivsResult = NETClient.RenderPrivateData(_realPlayHandle, true);
                    Debug.WriteLine($"軟體解碼 IVS 設定結果: {ivsResult}");
                }

                // 🔥 絕對不設定任何數據回調！讓大華 SDK 100% 處理所有事情
                // 這確保數據流只被處理一次，不會有重複處理

                Debug.WriteLine($"✅ 軟體解碼啟動成功，純大華 SDK 處理，IVS: {_ivsRenderEnabled}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 軟體解碼異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 🔥 硬體解碼模式 - 使用 Play SDK 架構，現在支援 IVS - 增強版本
        /// </summary>
        private bool StartHardwareDecodeWithIVS(IntPtr deviceHandle, int channel)
        {
            try
            {
                Debug.WriteLine("🔧 開始硬體解碼模式（支援 IVS）");

                if (!InitializePlaySDK())
                {
                    Debug.WriteLine("❌ 硬體解碼：Play SDK 初始化失敗");
                    return false;
                }

                if (!StartReceiveData(deviceHandle, channel))
                {
                    Debug.WriteLine("❌ 硬體解碼：開始接收數據失敗");
                    CleanupPlaySDK();
                    return false;
                }

                // 🔥 重置 IVS 相關狀態
                _ivsRetryCount = 0;
                _ivsSetSuccessfully = false;

                // 🔥 立即嘗試設定 IVS（但不等待太久）
                if (_ivsRenderEnabled)
                {
                    Thread.Sleep(100); // 只等待 100ms

                    bool ivsResult = PlaySDK.PLAY_RenderPrivateData(_playPort, true, 0);
                    if (ivsResult)
                    {
                        _ivsSetSuccessfully = true;
                        Debug.WriteLine($"🎯 硬體解碼 IVS 初始設定成功");
                    }
                    else
                    {
                        uint error = PlaySDK.PLAY_GetLastErrorEx();
                        Debug.WriteLine($"⚠️ 硬體解碼 IVS 初始設定失敗，錯誤碼：{error}，將在數據流中重試");
                    }
                }

                Debug.WriteLine($"⚡ 硬體解碼模式啟動成功（GPU 解碼，IVS 狀態: {(_ivsSetSuccessfully ? "已啟用" : "待設定")}）");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"硬體解碼模式異常：{ex.Message}");
                CleanupPlaySDK();
                return false;
            }
        }


        // === 🔥 保持原有的方法確保硬體解碼正常工作 ===

        /// <summary>
        /// 確保 Play SDK 已經初始化（全域只執行一次）
        /// </summary>
        private static void EnsureSDKInitialized()
        {
            lock (_initLock)
            {
                if (!_sdkInitialized)
                {
                    try
                    {
                        uint version = PlaySDK.PLAY_GetSdkVersion();
                        PlaySDK.PLAY_InitDDraw();
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
        /// 初始化 Play SDK 播放器（硬體解碼專用）
        /// </summary>
        private bool InitializePlaySDK()
        {
            try
            {
                // 取得 SDK 版本
                uint version = PlaySDK.PLAY_GetSdkVersion();
                Debug.WriteLine($"✅ PlaySDK 版本: 0x{version:X8}");

                if (!PlaySDK.PLAY_GetFreePort(ref _playPort))
                {
                    Debug.WriteLine("無法取得 Play SDK 端口");
                    return false;
                }
                Debug.WriteLine($"✅ 取得播放端口：{_playPort}");

                if (!PlaySDK.PLAY_SetStreamOpenMode(_playPort, PlaySDK.STREAME_REALTIME))
                {
                    Debug.WriteLine("設定串流模式失敗");
                    return false;
                }

                SetupDecodeEngine();

                uint bufferSize = CalculateOptimalBufferSize();
                if (!PlaySDK.PLAY_OpenStream(_playPort, IntPtr.Zero, 0, bufferSize))
                {
                    uint error = PlaySDK.PLAY_GetLastErrorEx();
                    Debug.WriteLine($"開啟串流失敗，錯誤碼: {error}");
                    return false;
                }

                // 🔥 重要：在 PLAY_Play 之前設定 IVS
                if (_ivsRenderEnabled)
                {
                    bool prePlayIVS = PlaySDK.PLAY_RenderPrivateData(_playPort, true, 0);
                    Debug.WriteLine($"Play前 IVS 設定：{prePlayIVS}");
                }

                if (!PlaySDK.PLAY_Play(_playPort, _displayHandle))
                {
                    uint error = PlaySDK.PLAY_GetLastErrorEx();
                    Debug.WriteLine($"Play SDK 播放失敗，錯誤碼: {error}");
                    return false;
                }

                // 🔥 在 PLAY_Play 之後再設定一次
                if (_ivsRenderEnabled)
                {
                    Thread.Sleep(100);
                    bool postPlayIVS = PlaySDK.PLAY_RenderPrivateData(_playPort, true, 0);
                    Debug.WriteLine($"Play後 IVS 設定：{postPlayIVS}");

                    if (postPlayIVS)
                    {
                        _ivsSetSuccessfully = true;
                    }
                }

                Debug.WriteLine($"✅ Play SDK 播放器初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化 Play SDK 時異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 開始從大華攝影機接收視頻數據（硬體解碼專用）
        /// </summary>
        private bool StartReceiveData(IntPtr deviceHandle, int channel)
        {
            try
            {
                EM_RealPlayType playType = _streamType == VideoStreamType.Main
                    ? EM_RealPlayType.EM_A_RType_Realplay_0
                    : EM_RealPlayType.EM_A_RType_Realplay_1;

                // 🔥 硬體解碼：不直接顯示，通過 Play SDK 處理
                _realPlayHandle = NETClient.RealPlay(deviceHandle, channel, IntPtr.Zero, playType);

                if (_realPlayHandle == IntPtr.Zero)
                {
                    string error = NETClient.GetLastError();
                    Debug.WriteLine($"❌ 硬體解碼數據管道失敗：{error}");
                    return false;
                }

                // 🔥 硬體解碼模式必須確保有回調
                if (_dataCallback == null)
                {
                    Debug.WriteLine("🚨 嚴重錯誤：硬體解碼模式缺少數據回調！");
                    _dataCallback = new fRealDataCallBackEx2(OnVideoDataReceived);
                    Debug.WriteLine("已緊急建立數據回調");
                }

                if (!NETClient.SetRealDataCallBack(_realPlayHandle, _dataCallback, IntPtr.Zero, EM_REALDATA_FLAG.RAW_DATA))
                {
                    string error = NETClient.GetLastError();
                    Debug.WriteLine($"❌ 硬體解碼回調設定失敗：{error}");
                    return false;
                }

                Debug.WriteLine($"✅ 硬體解碼數據管道成功，通道={channel}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 硬體解碼數據管道異常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 視頻數據回調函數 - 優化版本
        /// </summary>
        private void OnVideoDataReceived(IntPtr lRealHandle, uint dwDataType, IntPtr pBuffer, uint dwBufSize, IntPtr param, IntPtr dwUser)
        {
            try
            {
                // 保護檢查（保持原樣）
                if (_decodeMode == DecodeMode.Software)
                {
                    Debug.WriteLine("🚨 嚴重警告：軟體解碼模式收到數據回調！");
                    return;
                }

                if (_decodeMode != DecodeMode.Hardware && _decodeMode != DecodeMode.Auto)
                {
                    return;
                }

                if (!_isPlaying || _playPort == -1)
                {
                    return;
                }

                if (pBuffer == IntPtr.Zero || dwBufSize == 0)
                {
                    _droppedFrameCount++;
                    return;
                }

                Interlocked.Increment(ref _dataReceiveCount);

                // 🔥 修正：IVS 重試機制
                if (_ivsRenderEnabled && !_ivsSetSuccessfully)
                {
                    // 在數據流穩定後嘗試
                    if (_dataReceiveCount == 30 || _dataReceiveCount == 60 || _dataReceiveCount == 90)
                    {
                        bool ivsResult = PlaySDK.PLAY_RenderPrivateData(_playPort, true, 0);

                        if (ivsResult)
                        {
                            _ivsSetSuccessfully = true;
                            Debug.WriteLine($"🎯 IVS 在第 {_dataReceiveCount} 幀成功啟用");

                            // 強制刷新
                            if (_displayHandle != IntPtr.Zero)
                            {
                                InvalidateRect(_displayHandle, IntPtr.Zero, true);
                                UpdateWindow(_displayHandle);
                            }
                        }
                        else
                        {
                            uint error = PlaySDK.PLAY_GetLastErrorEx();
                            Debug.WriteLine($"⚠️ IVS 設定失敗（幀 {_dataReceiveCount}），錯誤: {error}");
                        }
                    }
                }

                // 統計更新
                if (_dataReceiveCount % 30 == 0)
                {
                    UpdateVideoStatistics(dwBufSize, dwDataType);
                }

                // 數據輸入
                bool result = PlaySDK.PLAY_InputData(_playPort, pBuffer, dwBufSize);

                if (!result)
                {
                    uint error = PlaySDK.PLAY_GetLastErrorEx();
                    if (error == PlaySDK.PLAY_BUF_OVER)
                    {
                        _bufferResetCount++;
                        PlaySDK.PLAY_ResetSourceBuffer(_playPort);

                        if (_bufferResetCount % 20 == 0)
                        {
                            Debug.WriteLine($"🔄 緩衝區重置 (第{_bufferResetCount}次)");
                        }
                    }
                }
                else
                {
                    _consecutiveErrorCount = 0;

                    if (_dataReceiveCount % 100 == 0)
                    {
                        // 🔥 修正：正確顯示 IVS 狀態
                        string ivsStatus = _ivsSetSuccessfully && _ivsRenderEnabled ? "IVS啟用" :
                                          _ivsRenderEnabled ? "IVS待設定" : "IVS停用";
                        Debug.WriteLine($"✅ GPU 解碼運行，端口={_playPort}, 幀={_dataReceiveCount}, {ivsStatus}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 數據處理異常：{ex.Message}");
            }
        }

        // === 其他必要方法保持不變 ===
        public void StopPlay()
        {
            try
            {
                if (!_isPlaying) return;

                Debug.WriteLine($"開始停止視頻播放，端口: {_playPort}");
                _isPlaying = false;

                StopReceiveData();
                System.Threading.Thread.Sleep(50);
                CleanupPlaySDK();
                ClearDisplayArea();
                ResetPlayerStateButKeepHandle();

                Debug.WriteLine("播放已停止");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止播放異常：{ex.Message}");
            }
        }

        private void StopReceiveData()
        {
            if (_realPlayHandle != IntPtr.Zero)
            {
                // 🔥 軟體解碼模式：沒有設定回調，直接停止即可
                // 🔥 硬體解碼模式：有回調，停止時會自動清理
                NETClient.StopRealPlay(_realPlayHandle);
                _realPlayHandle = IntPtr.Zero;
                Debug.WriteLine($"停止接收視頻數據（{_decodeMode} 模式）");
            }
        }

        private void CleanupPlaySDK()
        {
            if (_playPort != -1)
            {
                try
                {
                    PlaySDK.PLAY_Stop(_playPort);
                    PlaySDK.PLAY_CloseStream(_playPort);
                    PlaySDK.PLAY_ReleasePort(_playPort);
                    _playPort = -1;
                    Debug.WriteLine("Play SDK 資源已清理");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"清理 Play SDK 異常：{ex.Message}");
                    _playPort = -1;
                }
            }
        }

        // === 工具方法（保持原有邏輯）===
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
                Width = _streamType == VideoStreamType.Main ? 1920 : 704,
                Height = _streamType == VideoStreamType.Main ? 1080 : 576,
                Fps = _streamType == VideoStreamType.Main ? 25.0 : 15.0,
                Bitrate = _streamType == VideoStreamType.Main ? 4000.0 : 512.0
            };

            _bufferResetCount = 0;
            _dataReceiveCount = 0;
            _droppedFrameCount = 0;
            _lastPerformanceReport = DateTime.Now;
        }

        private void SetupDecodeEngine()
        {
            bool success = false;
            switch (_decodeMode)
            {
                case DecodeMode.Hardware:
                    success = TryHardwareDecoding();
                    break;
                case DecodeMode.Software:
                case DecodeMode.Auto:
                default:
                    success = TrySoftwareDecoding();
                    break;
            }

            if (!success)
            {
                Debug.WriteLine("解碼引擎設定失敗");
            }
        }

        private bool TryHardwareDecoding()
        {
            if (PlaySDK.PLAY_SetEngine(_playPort, (uint)PlaySDK.DecodeType.DECODE_HW_FAST, (uint)PlaySDK.RenderType.RENDER_D3D11))
            {
                Debug.WriteLine("✅ GPU 快速解碼成功 (D3D11)");
                return true;
            }

            if (PlaySDK.PLAY_SetEngine(_playPort, (uint)PlaySDK.DecodeType.DECODE_HW, (uint)PlaySDK.RenderType.RENDER_D3D))
            {
                Debug.WriteLine("✅ GPU 解碼成功 (D3D9)");
                return true;
            }

            Debug.WriteLine("❌ GPU 解碼失敗");
            return false;
        }

        private bool TrySoftwareDecoding()
        {
            if (PlaySDK.PLAY_SetEngine(_playPort, (uint)PlaySDK.DecodeType.DECODE_SW, (uint)PlaySDK.RenderType.RENDER_GDI))
            {
                Debug.WriteLine("✅ CPU 解碼成功");
                return true;
            }
            return false;
        }

        private uint CalculateOptimalBufferSize()
        {
            uint baseSize = _globalPlayerCount <= 4 ? 5u * 1024u * 1024u :
                           _globalPlayerCount <= 16 ? 3u * 1024u * 1024u : 1u * 1024u * 1024u;

            if (_streamType == VideoStreamType.Sub)
                baseSize = baseSize / 2u;

            return baseSize;
        }

        // === 其他必要方法（簡化版本）===
        private void ClearDisplayArea() { /* 簡化實現 */ }
        private void ResetPlayerStateButKeepHandle() 
        { 
            _bufferResetCount = 0;
            _dataReceiveCount = 0;
            _droppedFrameCount = 0;
            _consecutiveErrorCount = 0;
            CurrentVideoInfo = null;
        }
        private void UpdateVideoStatistics(uint dataSize, uint dataType) { /* 簡化實現 */ }

        // === IDisposable 實作 ===
        public void Dispose()
        {
            if (!_disposed)
            {
                StopPlay();
                Interlocked.Decrement(ref _globalPlayerCount);
                _disposed = true;
                Debug.WriteLine($"SimpleVideoPlayer 已銷毀，剩餘: {_globalPlayerCount}");
            }
        }

        public void TestIVSToggle()
        {
            if (_playPort == -1) return;

            Debug.WriteLine("=== 測試 IVS 切換 ===");

            // 關閉
            bool offResult = PlaySDK.PLAY_RenderPrivateData(_playPort, false, 0);
            Debug.WriteLine($"IVS 關閉: {offResult}");
            Thread.Sleep(1000);

            // 開啟
            bool onResult = PlaySDK.PLAY_RenderPrivateData(_playPort, true, 0);
            Debug.WriteLine($"IVS 開啟: {onResult}");

            if (!onResult)
            {
                uint error = PlaySDK.PLAY_GetLastErrorEx();
                Debug.WriteLine($"錯誤碼: {error}");
            }
        }

        // === 屬性 ===
        public bool IsPlaying => _isPlaying;
        public VideoStreamType StreamType => _streamType;
        public DecodeMode DecodeMode => _decodeMode;
        public int BufferResetCount => _bufferResetCount;
        public long DataReceiveCount => _dataReceiveCount;
        public long DroppedFrameCount => _droppedFrameCount;
        public static int GlobalPlayerCount => _globalPlayerCount;

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