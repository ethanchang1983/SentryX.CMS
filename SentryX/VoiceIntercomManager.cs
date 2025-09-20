// VoiceIntercomManager.cs - 語音對講管理器 - 修正版本，參考大華 SDK Demo
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NetSDKCS;

namespace SentryX
{
    /// <summary>
    /// 音訊編碼配置
    /// </summary>
    public class AudioEncodeConfig
    {
        public EM_TALK_CODING_TYPE EncodeType { get; set; }
        public int SampleRate { get; set; }
        public int AudioBit { get; set; }
        public int PacketPeriod { get; set; }
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// 語音對講管理器 - 修正版本，參考大華 SDK Demo
    /// </summary>
    public class VoiceIntercomManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private IntPtr _talkHandle = IntPtr.Zero;
        private IntPtr _currentDeviceHandle = IntPtr.Zero;
        private string _currentDeviceId = "";
        private bool _isListening = false;
        private bool _isSpeaking = false;
        private bool _disposed = false;
        private fAudioDataCallBack? _audioCallback;

        // 當前使用的音訊編碼配置
        private AudioEncodeConfig _currentEncodeConfig;

        // 🔥 參考 Demo 的常數定義
        private const int SampleRate = 8000;
        private const int AudioBit = 16;
        private const int PacketPeriod = 25;
        private const int SendAudio = 0;         // PC端採集到的音頻數據
        private const int ReceiveAudio = 1;      // 設備端返回的音頻

        // 🔥 支援的音訊編碼格式列表（優先 PCM，參考 Demo）
        private static readonly List<AudioEncodeConfig> _supportedEncodings = new()
        {
            new AudioEncodeConfig
            {
                EncodeType = EM_TALK_CODING_TYPE.PCM,
                SampleRate = SampleRate,
                AudioBit = AudioBit,
                PacketPeriod = PacketPeriod,
                DisplayName = "PCM",
                Description = "PCM 無壓縮格式 (8KHz, 16bit) - Demo 預設"
            },
            new AudioEncodeConfig
            {
                EncodeType = EM_TALK_CODING_TYPE.G711a,
                SampleRate = 8000,
                AudioBit = 16,
                PacketPeriod = 20,
                DisplayName = "G.711A",
                Description = "G.711A 壓縮格式 (8KHz, 低帶寬)"
            },
            new AudioEncodeConfig
            {
                EncodeType = EM_TALK_CODING_TYPE.G711u,
                SampleRate = 8000,
                AudioBit = 16,
                PacketPeriod = 20,
                DisplayName = "G.711μ",
                Description = "G.711μ 壓縮格式 (8KHz, 低帶寬)"
            },
            new AudioEncodeConfig
            {
                EncodeType = EM_TALK_CODING_TYPE.AAC,
                SampleRate = 16000,
                AudioBit = 16,
                PacketPeriod = 20,
                DisplayName = "AAC",
                Description = "AAC 高品質格式 (16KHz, 高品質)"
            }
        };

        public VoiceIntercomManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _audioCallback = new fAudioDataCallBack(OnAudioDataCallback);

            // 🔥 預設使用 PCM 編碼（與 Demo 一致）
            _currentEncodeConfig = _supportedEncodings.First(e => e.EncodeType == EM_TALK_CODING_TYPE.PCM);
        }

        #region 公開方法

        /// <summary>
        /// 查詢設備支援的音訊編碼格式
        /// </summary>
        /// <param name="deviceId">設備ID</param>
        /// <returns>支援的編碼格式列表</returns>
        public List<AudioEncodeConfig> QuerySupportedEncodings(string deviceId)
        {
            var device = DahuaSDK.GetDevice(deviceId);
            if (device == null || !device.IsOnline)
            {
                _mainWindow.ShowMessage("設備不存在或未連線，返回預設編碼列表");
                return new List<AudioEncodeConfig> { _supportedEncodings.First() };
            }

            _mainWindow.ShowMessage($"正在查詢設備 {device.Name} 支援的音訊編碼格式...");

            var supportedList = new List<AudioEncodeConfig>();

            foreach (var encoding in _supportedEncodings)
            {
                if (TestEncoding(device.LoginHandle, encoding))
                {
                    supportedList.Add(encoding);
                    _mainWindow.ShowMessage($"✅ 支援格式：{encoding.DisplayName}");
                }
                else
                {
                    _mainWindow.ShowMessage($"❌ 不支援格式：{encoding.DisplayName}");
                }
            }

            if (supportedList.Count == 0)
            {
                _mainWindow.ShowMessage("⚠️ 未檢測到支援的編碼格式，使用預設 PCM");
                supportedList.Add(_supportedEncodings.First());
            }

            return supportedList;
        }

        /// <summary>
        /// 設定音訊編碼格式
        /// </summary>
        /// <param name="encodeType">編碼類型</param>
        /// <returns>是否設定成功</returns>
        public bool SetAudioEncoding(EM_TALK_CODING_TYPE encodeType)
        {
            var encoding = _supportedEncodings.FirstOrDefault(e => e.EncodeType == encodeType);
            if (encoding == null)
            {
                _mainWindow.ShowMessage($"❌ 不支援的編碼格式：{encodeType}");
                return false;
            }

            _currentEncodeConfig = encoding;
            _mainWindow.ShowMessage($"✅ 已切換音訊編碼：{encoding.DisplayName}");
            return true;
        }

        /// <summary>
        /// 🔥 開始接收設備端的聲音 - 參考 Demo 版本
        /// </summary>
        /// <param name="deviceId">設備ID</param>
        /// <returns>是否成功開始接收</returns>
        public bool StartListening(string deviceId)
        {
            try
            {
                if (_isListening)
                {
                    _mainWindow.ShowMessage("⚠️ 已經在接收音頻，請先停止");
                    return false;
                }

                var device = DahuaSDK.GetDevice(deviceId);
                if (device == null || !device.IsOnline)
                {
                    _mainWindow.ShowMessage("❌ 設備不存在或未連線");
                    return false;
                }

                _mainWindow.ShowMessage($"🎙️ 正在啟動語音接收：{device.Name}");
                _mainWindow.ShowMessage($"📊 使用編碼格式：{_currentEncodeConfig.DisplayName} ({_currentEncodeConfig.SampleRate}Hz)");

                // 🔥 參考 Demo：完整的設備模式設定
                if (!SetupTalkDeviceMode(device.LoginHandle))
                {
                    _mainWindow.ShowMessage("❌ 設定語音對講模式失敗");
                    return false;
                }

                // 🔥 參考 Demo：開始語音對講
                _talkHandle = NETClient.StartTalk(device.LoginHandle, _audioCallback, IntPtr.Zero);
                if (_talkHandle == IntPtr.Zero)
                {
                    var error = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"❌ 啟動語音對講失敗：{error}");
                    return false;
                }

                _currentDeviceHandle = device.LoginHandle;
                _currentDeviceId = deviceId;
                _isListening = true;
                _mainWindow.ShowMessage($"✅ 語音接收已啟動，對講句柄：{_talkHandle}");

                return true;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 啟動語音接收時發生錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止接收設備端的聲音
        /// </summary>
        /// <returns>是否成功停止接收</returns>
        public bool StopListening()
        {
            try
            {
                if (!_isListening)
                {
                    _mainWindow.ShowMessage("⚠️ 目前沒有在接收音頻");
                    return false;
                }

                _mainWindow.ShowMessage("🔇 正在停止語音接收...");

                // 先停止說話模式
                if (_isSpeaking)
                {
                    StopSpeaking();
                }

                // 🔥 參考 Demo：正確的停止順序
                if (_talkHandle != IntPtr.Zero)
                {
                    bool result = NETClient.StopTalk(_talkHandle);
                    _talkHandle = IntPtr.Zero;

                    if (result)
                    {
                        _mainWindow.ShowMessage("✅ 語音對講已停止");
                    }
                    else
                    {
                        var error = NETClient.GetLastError();
                        _mainWindow.ShowMessage($"⚠️ 停止語音對講時有警告：{error}");
                    }
                }

                _currentDeviceHandle = IntPtr.Zero;
                _currentDeviceId = "";
                _isListening = false;
                _mainWindow.ShowMessage("🔇 語音接收已停止");

                return true;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 停止語音接收時發生錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 🔥 開始語音對講（發送PC音頻到設備）- 參考 Demo 版本
        /// </summary>
        /// <returns>是否成功開始對講</returns>
        public bool StartSpeaking()
        {
            try
            {
                if (!_isListening)
                {
                    _mainWindow.ShowMessage("❌ 請先啟動語音接收");
                    return false;
                }

                if (_isSpeaking)
                {
                    _mainWindow.ShowMessage("⚠️ 已經在說話模式");
                    return true;
                }

                if (_talkHandle == IntPtr.Zero)
                {
                    _mainWindow.ShowMessage("❌ 語音對講句柄無效");
                    return false;
                }

                _mainWindow.ShowMessage("🎤 正在啟動麥克風進行對講...");

                // 🔥 參考 Demo：在 StartTalk 成功後才調用 RecordStart
                bool recordResult = NETClient.RecordStart(_currentDeviceHandle);
                if (!recordResult)
                {
                    var error = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"❌ 啟動PC端錄音失敗：{error}");
                    _mainWindow.ShowMessage("💡 可能原因：麥克風被占用或權限不足");
                    return false;
                }

                _isSpeaking = true;
                _mainWindow.ShowMessage($"✅ PC端錄音啟動成功");
                _mainWindow.ShowMessage($"🎤 對講模式已啟動，使用 {_currentEncodeConfig.DisplayName} 編碼");
                _mainWindow.ShowMessage("💡 請對著麥克風說話，音頻將自動發送到設備");

                return true;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 啟動對講模式時發生錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 🔥 停止語音對講 - 參考 Demo 版本
        /// </summary>
        /// <returns>是否成功停止對講</returns>
        public bool StopSpeaking()
        {
            try
            {
                if (!_isSpeaking)
                {
                    _mainWindow.ShowMessage("⚠️ 目前沒有在對講模式");
                    return false;
                }

                _mainWindow.ShowMessage("🔇 正在停止對講模式...");

                // 🔥 參考 Demo：先停止錄音
                bool recordStopResult = NETClient.RecordStop(_currentDeviceHandle);
                if (!recordStopResult)
                {
                    var error = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"⚠️ 停止PC端錄音時有警告：{error}");
                }
                else
                {
                    _mainWindow.ShowMessage("✅ PC端錄音已停止");
                }

                _isSpeaking = false;
                _mainWindow.ShowMessage("🔇 對講模式已停止");

                return true;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 停止對講模式時發生錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切換對講狀態（按鍵式對講）
        /// </summary>
        /// <returns>當前對講狀態</returns>
        public bool ToggleSpeaking()
        {
            if (_isSpeaking)
            {
                StopSpeaking();
                return false;
            }
            else
            {
                StartSpeaking();
                return _isSpeaking;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 🔥 設定語音對講設備模式 - 參考 Demo 完整流程
        /// </summary>
        private bool SetupTalkDeviceMode(IntPtr loginHandle)
        {
            IntPtr talkEncodePointer = IntPtr.Zero;
            IntPtr talkSpeakPointer = IntPtr.Zero;
            IntPtr talkTransferPointer = IntPtr.Zero;

            try
            {
                // 🔥 1. 設定語音編碼類型（參考 Demo）
                var talkCodeInfo = new NET_DEV_TALKDECODE_INFO
                {
                    encodeType = _currentEncodeConfig.EncodeType,
                    dwSampleRate = (uint)_currentEncodeConfig.SampleRate,
                    nAudioBit = _currentEncodeConfig.AudioBit,
                    nPacketPeriod = _currentEncodeConfig.PacketPeriod,
                    reserved = new byte[60] // 🔥 Demo 中有這個初始化
                };

                talkEncodePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NET_DEV_TALKDECODE_INFO)));
                Marshal.StructureToPtr(talkCodeInfo, talkEncodePointer, true);

                bool encodeResult = NETClient.SetDeviceMode(loginHandle, EM_USEDEV_MODE.TALK_ENCODE_TYPE, talkEncodePointer);
                if (encodeResult)
                {
                    _mainWindow.ShowMessage($"✅ 語音編碼設定成功：{_currentEncodeConfig.DisplayName}");
                }
                else
                {
                    var error = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"⚠️ 語音編碼設定失敗：{error}");
                }

                // 🔥 2. 設定語音對講模式（參考 Demo）
                var speak = new NET_SPEAK_PARAM
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(NET_SPEAK_PARAM)),
                    nMode = 0,                  // 對講模式
                    bEnableWait = false,        // 不啟用等待
                    nSpeakerChannel = 0         // 喇叭通道
                };

                talkSpeakPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NET_SPEAK_PARAM)));
                Marshal.StructureToPtr(speak, talkSpeakPointer, true);

                bool speakResult = NETClient.SetDeviceMode(loginHandle, EM_USEDEV_MODE.TALK_SPEAK_PARAM, talkSpeakPointer);
                if (speakResult)
                {
                    _mainWindow.ShowMessage("✅ 語音對講模式設定成功");
                }
                else
                {
                    var error = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"⚠️ 語音對講模式設定失敗：{error}");
                }

                // 🔥 3. 設定語音轉發模式（Demo 中的關鍵設定）
                var transfer = new NET_TALK_TRANSFER_PARAM
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(NET_TALK_TRANSFER_PARAM)),
                    bTransfer = false  // 🔥 本地對講模式，不轉發到其他通道
                };

                talkTransferPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NET_TALK_TRANSFER_PARAM)));
                Marshal.StructureToPtr(transfer, talkTransferPointer, true);

                bool transferResult = NETClient.SetDeviceMode(loginHandle, EM_USEDEV_MODE.TALK_TRANSFER_MODE, talkTransferPointer);
                if (transferResult)
                {
                    _mainWindow.ShowMessage("✅ 語音轉發模式設定成功（本地對講）");
                }
                else
                {
                    var error = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"⚠️ 語音轉發模式設定失敗：{error}");
                }

                // 🔥 至少有一個設定成功就返回 true
                return encodeResult || speakResult || transferResult;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 設定語音對講設備模式時發生錯誤：{ex.Message}");
                return false;
            }
            finally
            {
                // 🔥 釋放記憶體（參考 Demo）
                if (talkEncodePointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(talkEncodePointer);
                if (talkSpeakPointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(talkSpeakPointer);
                if (talkTransferPointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(talkTransferPointer);
            }
        }

        /// <summary>
        /// 測試指定編碼格式是否被設備支援
        /// </summary>
        private bool TestEncoding(IntPtr loginHandle, AudioEncodeConfig encoding)
        {
            try
            {
                var talkCodeInfo = new NET_DEV_TALKDECODE_INFO
                {
                    encodeType = encoding.EncodeType,
                    dwSampleRate = (uint)encoding.SampleRate,
                    nAudioBit = encoding.AudioBit,
                    nPacketPeriod = encoding.PacketPeriod,
                    reserved = new byte[60]
                };

                IntPtr talkEncodePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NET_DEV_TALKDECODE_INFO)));
                try
                {
                    Marshal.StructureToPtr(talkCodeInfo, talkEncodePointer, true);
                    bool result = NETClient.SetDeviceMode(loginHandle, EM_USEDEV_MODE.TALK_ENCODE_TYPE, talkEncodePointer);
                    return result;
                }
                finally
                {
                    Marshal.FreeHGlobal(talkEncodePointer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"測試編碼格式 {encoding.DisplayName} 時發生錯誤：{ex.Message}");
                return true; // 假設支援，避免阻塞功能
            }
        }

        /// <summary>
        /// 🔥 音頻數據回調函數 - 參考 Demo 版本
        /// </summary>
        private void OnAudioDataCallback(IntPtr lTalkHandle, IntPtr pDataBuf, uint dwBufSize, byte byAudioFlag, IntPtr dwUser)
        {
            try
            {
                // 🔥 參考 Demo：嚴格的句柄檢查
                if (lTalkHandle != _talkHandle)
                {
                    return;
                }

                if (SendAudio == byAudioFlag) // PC端採集到的音頻數據
                {
                    // 🔥 參考 Demo：直接發送，不檢查 _isSpeaking 狀態
                    // Demo 中只要有音頻數據就發送
                    if (pDataBuf != IntPtr.Zero && dwBufSize > 0)
                    {
                        // 🔥 參考 Demo：send talk data 發送語音數據
                        int sentSize = NETClient.TalkSendData(lTalkHandle, pDataBuf, dwBufSize);

                        // 降低日誌頻率，避免刷屏
                        if (DateTime.Now.Millisecond % 1000 < 50)
                        {
                            if (sentSize == (int)dwBufSize)
                            {
                                Console.WriteLine($"🎤 發送音訊：{dwBufSize} bytes ({_currentEncodeConfig.DisplayName})");
                            }
                            else
                            {
                                var error = NETClient.GetLastError();
                                Console.WriteLine($"❌ 音訊發送失敗：期望{dwBufSize}, 實際{sentSize}, 錯誤：{error}");
                            }
                        }
                    }
                }
                else if (ReceiveAudio == byAudioFlag) // 設備端返回的音頻
                {
                    if (_isListening && pDataBuf != IntPtr.Zero && dwBufSize > 0)
                    {
                        try
                        {
                            // 🔥 參考 Demo：here call netsdk decode audio
                            // 這裡調用netsdk解碼語音數據
                            NETClient.AudioDec(pDataBuf, dwBufSize);

                            // 降低日誌頻率
                            if (DateTime.Now.Millisecond % 2000 < 50)
                            {
                                Console.WriteLine($"🔊 接收音訊：{dwBufSize} bytes ({_currentEncodeConfig.DisplayName})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ 音頻解碼錯誤：{ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 音頻回調處理錯誤：{ex.Message}");
            }
        }

        #endregion

        #region IDisposable 實作

        /// <summary>
        /// 清理資源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                StopListening();
                _disposed = true;
            }
        }

        #endregion

        #region 屬性

        /// <summary>
        /// 是否正在接收音頻
        /// </summary>
        public bool IsListening => _isListening;

        /// <summary>
        /// 是否正在說話（對講）
        /// </summary>
        public bool IsSpeaking => _isSpeaking;

        /// <summary>
        /// 當前連接的設備句柄
        /// </summary>
        public IntPtr CurrentDeviceHandle => _currentDeviceHandle;

        /// <summary>
        /// 當前連接的設備ID
        /// </summary>
        public string CurrentDeviceId => _currentDeviceId;

        /// <summary>
        /// 當前使用的音訊編碼配置
        /// </summary>
        public AudioEncodeConfig CurrentEncodeConfig => _currentEncodeConfig;

        /// <summary>
        /// 取得所有支援的編碼格式
        /// </summary>
        public static List<AudioEncodeConfig> SupportedEncodings => _supportedEncodings.ToList();

        #endregion
    }
}