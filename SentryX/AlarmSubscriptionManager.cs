using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using NetSDKCS;
using System.Collections.Generic;

namespace SentryX
{
    /// <summary>
    /// 警報訂閱管理器 - 使用正確的大華 SDK API
    /// </summary>
    public class AlarmSubscriptionManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private bool _isSubscribed = false;
        private bool _disposed = false;

        /// <summary>
        /// 警報事件集合（綁定到UI）
        /// </summary>
        public ObservableCollection<AlarmEvent> AlarmEvents { get; } = new();

        /// <summary>
        /// 是否正在訂閱警報
        /// </summary>
        public bool IsSubscribed => _isSubscribed;

        /// <summary>
        /// 當前過濾的警報類型
        /// </summary>
        public AlarmType? CurrentFilter { get; set; } = null;

        /// <summary>
        /// 未讀警報數量
        /// </summary>
        public int UnreadCount => AlarmEvents.Count(a => !a.IsRead);

        /// <summary>
        /// 警報事件發生時觸發
        /// </summary>
        public event Action<AlarmEvent>? AlarmReceived;

        /// <summary>
        /// 訂閱狀態改變時觸發
        /// </summary>
        public event Action<bool>? SubscriptionStatusChanged;

        // === 回調函數 ===
        private readonly fMessCallBackEx _alarmCallback;
        private readonly fAnalyzerDataCallBack _intelligentCallback;

        // === 訂閱句柄記錄 ===
        private readonly List<IntPtr> _alarmHandles = new();
        private readonly List<IntPtr> _intelligentHandles = new();

        // === 在類別開頭增加新的欄位 ===
        private bool _isInitializing = false;
        private DateTime _subscriptionStartTime = DateTime.MinValue;
        private readonly TimeSpan _initializationPeriod = TimeSpan.FromSeconds(10); // 訂閱後10秒內視為初始化期間

        public AlarmSubscriptionManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

            // 初始化回調函數
            _alarmCallback = new fMessCallBackEx(OnAlarmCallback);
            // 修正 fAnalyzerDataCallBack 委派的使用方式
            // 根據您提供的委派簽名：public delegate int fAnalyzerDataCallBack();
            // 需將 OnIntelligentEventCallback 方法簽名改為無參數，並移除原有的參數

            // 1. 修改委派初始化
            _intelligentCallback = new fAnalyzerDataCallBack(OnIntelligentEventCallback);

            Debug.WriteLine("AlarmSubscriptionManager 已初始化");
        }

        /// <summary>
        /// 開始訂閱警報事件 - 修正版本
        /// </summary>
        public bool StartSubscription()
        {
            try
            {
                if (_isSubscribed)
                {
                    Debug.WriteLine("警報訂閱已經啟動");
                    return true;
                }

                Debug.WriteLine("開始訂閱警報事件...");

                // 🔥 設定初始化狀態
                _isInitializing = true;
                _subscriptionStartTime = DateTime.Now;

                // 🔥 第一步：設置報警回調函數
                NETClient.SetDVRMessCallBackEx1(_alarmCallback, IntPtr.Zero);
                Debug.WriteLine("已設置報警回調函數");

                var onlineDevices = DahuaSDK.GetOnlineDevices();
                int alarmSuccessCount = 0;
                int intelligentSuccessCount = 0;

                foreach (var device in onlineDevices)
                {
                    try
                    {
                        // 🔥 第二步：訂閱一般報警事件 (StartListen)
                        bool alarmResult = NETClient.StartListen(device.LoginHandle);
                        if (alarmResult)
                        {
                            _alarmHandles.Add(device.LoginHandle);
                            alarmSuccessCount++;
                            Debug.WriteLine($"成功訂閱設備 {device.Name} 的一般報警");
                        }
                        else
                        {
                            Debug.WriteLine($"訂閱設備 {device.Name} 的一般報警失敗: {NETClient.GetLastError()}");
                        }

                        // 🔥 第三步：訂閱智能事件 (RealLoadPicture)
                        // 為每個通道訂閱智能事件
                        for (int channel = 0; channel < device.ChannelCount; channel++)
                        {
                            IntPtr intelligentHandle = NETClient.RealLoadPicture(
                                device.LoginHandle,
                                channel,
                                0xFFFFFFFF,  // 所有智能事件類型
                                true,        // 需要圖片
                                _intelligentCallback,
                                IntPtr.Zero,
                                IntPtr.Zero
                            );

                            if (intelligentHandle != IntPtr.Zero)
                            {
                                _intelligentHandles.Add(intelligentHandle);
                                intelligentSuccessCount++;
                                Debug.WriteLine($"成功訂閱設備 {device.Name} 通道 {channel} 的智能事件");
                            }
                            else
                            {
                                Debug.WriteLine($"訂閱設備 {device.Name} 通道 {channel} 的智能事件失敗: {NETClient.GetLastError()}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"訂閱設備 {device.Name} 時發生異常: {ex.Message}");
                    }
                }

                bool hasAnySubscription = alarmSuccessCount > 0 || intelligentSuccessCount > 0;
                _isSubscribed = hasAnySubscription;

                if (_isSubscribed)
                {
                    _mainWindow.ShowMessage($"✅ 警報訂閱已啟動");
                    _mainWindow.ShowMessage($"  📺 一般報警: {alarmSuccessCount}/{onlineDevices.Count} 個設備");
                    _mainWindow.ShowMessage($"  🎯 智能事件: {intelligentSuccessCount} 個通道");
                    _mainWindow.ShowMessage($"⏳ 正在同步設備狀態，請稍候...");

                    // 🔥 移除測試警報事件的添加
                    // AddTestAlarmEvents(); // 註釋掉這行

                    // 🔥 設定定時器，10秒後結束初始化期間
                    var initTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = _initializationPeriod
                    };
                    initTimer.Tick += (s, e) =>
                    {
                        _isInitializing = false;
                        initTimer.Stop();
                        _mainWindow.ShowMessage("✅ 設備狀態同步完成，現在接收實時警報事件");
                        Debug.WriteLine("警報訂閱初始化期間結束，開始接收真實警報");
                    };
                    initTimer.Start();
                }
                else
                {
                    _mainWindow.ShowMessage("❌ 警報訂閱啟動失敗，沒有成功訂閱任何設備");
                    _isInitializing = false;
                }

                SubscriptionStatusChanged?.Invoke(_isSubscribed);
                return _isSubscribed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"啟動警報訂閱時發生異常: {ex.Message}");
                _mainWindow.ShowMessage($"❌ 啟動警報訂閱失敗: {ex.Message}");
                _isInitializing = false;
                return false;
            }
        }

        /// <summary>
        /// 停止訂閱警報事件
        /// </summary>
        public bool StopSubscription()
        {
            try
            {
                if (!_isSubscribed)
                {
                    Debug.WriteLine("警報訂閱沒有啟動");
                    return true;
                }

                Debug.WriteLine("停止訂閱警報事件...");

                int alarmStopCount = 0;
                int intelligentStopCount = 0;

                // 🔥 停止智能事件訂閱
                foreach (var handle in _intelligentHandles.ToList())
                {
                    try
                    {
                        if (NETClient.StopLoadPic(handle))
                        {
                            intelligentStopCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"停止智能事件訂閱時發生異常: {ex.Message}");
                    }
                }
                _intelligentHandles.Clear();

                // 🔥 停止一般報警訂閱
                foreach (var loginHandle in _alarmHandles.ToList())
                {
                    try
                    {
                        if (NETClient.StopListen(loginHandle))
                        {
                            alarmStopCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"停止報警訂閱時發生異常: {ex.Message}");
                    }
                }
                _alarmHandles.Clear();

                // 🔥 清除報警回調函數
                NETClient.SetDVRMessCallBack(null, IntPtr.Zero);

                _isSubscribed = false;
                _mainWindow.ShowMessage($"🔔 警報訂閱已停止");
                _mainWindow.ShowMessage($"  📺 停止一般報警: {alarmStopCount} 個設備");
                _mainWindow.ShowMessage($"  🎯 停止智能事件: {intelligentStopCount} 個通道");

                SubscriptionStatusChanged?.Invoke(_isSubscribed);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止警報訂閱時發生異常: {ex.Message}");
                _mainWindow.ShowMessage($"❌ 停止警報訂閱失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切換訂閱狀態
        /// </summary>
        public bool ToggleSubscription()
        {
            return _isSubscribed ? StopSubscription() : StartSubscription();
        }

        /// <summary>
        /// 添加警報事件
        /// </summary>
        public void AddAlarmEvent(AlarmEvent alarmEvent)
        {
            try
            {
                if (alarmEvent == null) return;

                // 在UI線程上添加警報事件
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    // 插入到列表開頭（最新的在上面）
                    AlarmEvents.Insert(0, alarmEvent);

                    // 限制警報事件數量，避免記憶體洩漏
                    while (AlarmEvents.Count > 1000)
                    {
                        AlarmEvents.RemoveAt(AlarmEvents.Count - 1);
                    }

                    Debug.WriteLine($"新增警報事件: {alarmEvent.TypeName} - {alarmEvent.DeviceName}");

                    // 觸發警報事件
                    AlarmReceived?.Invoke(alarmEvent);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"添加警報事件時發生異常: {ex.Message}");
            }
        }

        /// <summary>
        /// 清除所有警報事件
        /// </summary>
        public void ClearAllAlarms()
        {
            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    AlarmEvents.Clear();
                    Debug.WriteLine("已清除所有警報事件");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除警報事件時發生異常: {ex.Message}");
            }
        }

        /// <summary>
        /// 根據類型過濾警報事件
        /// </summary>
        public void FilterAlarmsByType(AlarmType? filterType)
        {
            CurrentFilter = filterType;
            Debug.WriteLine($"設定警報過濾類型: {filterType?.ToString() ?? "全部"}");
        }

        /// <summary>
        /// 標記警報為已讀
        /// </summary>
        public void MarkAlarmAsRead(string alarmId)
        {
            try
            {
                var alarm = AlarmEvents.FirstOrDefault(a => a.Id == alarmId);
                if (alarm != null)
                {
                    alarm.IsRead = true;
                    Debug.WriteLine($"警報 {alarmId} 已標記為已讀");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"標記警報已讀時發生異常: {ex.Message}");
            }
        }

        /// <summary>
        /// 標記所有警報為已讀
        /// </summary>
        public void MarkAllAlarmsAsRead()
        {
            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    foreach (var alarm in AlarmEvents)
                    {
                        alarm.IsRead = true;
                    }
                    Debug.WriteLine("所有警報已標記為已讀");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"標記所有警報已讀時發生異常: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔥 一般報警回調函數 (fMessCallBackEx) - 增加初始化過濾
        /// </summary>
        private bool OnAlarmCallback(int lCommand, IntPtr lLoginID, IntPtr pBuf, uint dwBufLen,
    IntPtr pchDVRIP, int nDVRPort, bool bAlarmAckFlag, int nEventID, IntPtr dwUser)
        {
            try
            {
                string? deviceIP = Marshal.PtrToStringAnsi(pchDVRIP);
                var device = DahuaSDK.GetOnlineDevices().FirstOrDefault(d => d.IpAddress == deviceIP);

                if (device == null)
                {
                    Debug.WriteLine($"收到未知設備 {deviceIP} 的報警，命令: {lCommand}");
                    return true;
                }

                // 初始化期間過濾
                if (_isInitializing)
                {
                    var timeSinceStart = DateTime.Now - _subscriptionStartTime;
                    if (timeSinceStart < _initializationPeriod)
                    {
                        Debug.WriteLine($"🔄 初始化期間收到狀態同步: 設備={device.Name}, 命令={lCommand} (0x{lCommand:X}), 忽略");
                        return true;
                    }
                    else
                    {
                        _isInitializing = false;
                        Debug.WriteLine("⏰ 警報訂閱初始化期間自動結束");
                    }
                }

                var type = (EM_ALARM_TYPE)lCommand;

                // 陣列型報警（每個 byte 對應一個通道）
                switch (type)
                {
                    case EM_ALARM_TYPE.ALARM_ALARM_EX:
                    case EM_ALARM_TYPE.MOTION_ALARM_EX:
                    case EM_ALARM_TYPE.VIDEOLOST_ALARM_EX:
                    case EM_ALARM_TYPE.SHELTER_ALARM_EX:
                    case EM_ALARM_TYPE.SOUND_DETECT_ALARM_EX:
                    case EM_ALARM_TYPE.DISKFULL_ALARM_EX:
                    case EM_ALARM_TYPE.DISKERROR_ALARM_EX:
                    case EM_ALARM_TYPE.ALARM_DISK:
                        if (pBuf != IntPtr.Zero && dwBufLen > 0)
                        {
                            var bytes = new byte[dwBufLen];
                            Marshal.Copy(pBuf, bytes, 0, (int)dwBufLen);
                            for (int i = 0; i < bytes.Length; i++)
                            {
                                // SDK Demo: 1 表示開始，0 表示停止
                                if (bytes[i] == 1)
                                {
                                    // 使用 1-based 顯示頻道（較直觀）
                                    int channelNumber = i + 1;
                                    var alarmEvent = CreateAlarmFromCommand(lCommand, device, pBuf, dwBufLen, bAlarmAckFlag, nEventID, channelNumber);
                                    if (alarmEvent != null) AddAlarmEvent(alarmEvent);
                                }
                                else
                                {
                                    // 若需要顯示停止事件，可在此建立停止型事件或更新狀態
                                }
                            }
                        }
                        return true;
                }

                // 結構型智能事件：移動偵測
                if (type == EM_ALARM_TYPE.EVENT_MOTIONDETECT)
                {
                    if (pBuf != IntPtr.Zero)
                    {
                        try
                        {
                            // 使用泛型版本，直接取得 struct（不會產生 unboxing null 警告）
                            var stu = Marshal.PtrToStructure<NET_ALARM_MOTIONDETECT_INFO>(pBuf);
                            // nChannelID 通常是 0-based，顯示使用 1-based
                            int channelNumber = stu.nChannelID + 1;
                            if (stu.nEventAction == 1) // start
                            {
                                var alarmEvent = CreateAlarmFromCommand(lCommand, device, pBuf, dwBufLen, bAlarmAckFlag, nEventID, channelNumber);
                                if (alarmEvent != null) AddAlarmEvent(alarmEvent);
                            }
                            else if (stu.nEventAction == 2) // stop
                            {
                                var alarmEvent = CreateAlarmFromCommand(lCommand, device, pBuf, dwBufLen, bAlarmAckFlag, nEventID, channelNumber);
                                if (alarmEvent != null) AddAlarmEvent(alarmEvent);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"解析 EVENT_MOTIONDETECT 失敗: {ex.Message}");
                        }
                    }
                    return true;
                }

                // 結構型智能事件：警戒區（cross region）
                if (type == EM_ALARM_TYPE.EVENT_CROSSREGION_DETECTION)
                {
                    if (pBuf != IntPtr.Zero)
                    {
                        try
                        {
                            var stu = Marshal.PtrToStructure<NET_ALARM_EVENT_CROSSREGION_INFO>(pBuf);
                            int channelNumber = stu.nChannelID + 1;
                            if (stu.nEventAction == 1 || stu.nEventAction == 2)
                            {
                                var alarmEvent = CreateAlarmFromCommand(lCommand, device, pBuf, dwBufLen, bAlarmAckFlag, nEventID, channelNumber);
                                if (alarmEvent != null) AddAlarmEvent(alarmEvent);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"解析 EVENT_CROSSREGION_DETECTION 失敗: {ex.Message}");
                        }
                    }
                    return true;
                }

                // 其他單一事件
                if (!IsValidAlarmCommand(lCommand))
                {
                    Debug.WriteLine($"⚠️ 收到不支援的警報命令: {lCommand} (0x{lCommand:X}), 忽略");
                    return true;
                }

                var genericEvent = CreateAlarmFromCommand(lCommand, device, pBuf, dwBufLen, bAlarmAckFlag, nEventID, null);
                if (genericEvent != null)
                {
                    AddAlarmEvent(genericEvent);
                    Debug.WriteLine($"✅ 處理真實警報: 設備={device.Name}, 類型={genericEvent.TypeName}, 命令={lCommand}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"處理一般報警回調時發生異常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 🔥 新增：檢查是否為有效的警報命令
        /// </summary>
        private bool IsValidAlarmCommand(int lCommand)
        {
            // 定義我們關心的警報類型
            var validCommands = new HashSet<int>
            {
                0x2102, // MOTION_ALARM_EX - 移動偵測報警
                0x218F, // EVENT_MOTIONDETECT - 視頻移動偵測事件
                0x2103, // VIDEOLOST_ALARM_EX - 視頻丟失報警
                0x2104, // SHELTER_ALARM_EX - 視頻遮蔽報警
                0x2131, // ALARM_STORAGE_FAILURE - 存儲異常報警
                0x2132, // ALARM_FRONTDISCONNECT - 前端斷網報警
                0x2105, // SOUND_DETECT_ALARM_EX - 音頻檢測報警
                0x2106, // DISKFULL_ALARM_EX - 硬碟滿報警
                0x2107, // DISKERROR_ALARM_EX - 硬碟錯誤報警
                0x2140, // ALARM_IVS - IVS智能分析報警

                // 您設備實際發送的命令
                8449, 8450, 8451, 8452, 8454, 8455, 8591
            };

            return validCommands.Contains(lCommand);
        }

        /// <summary>
        /// 🔥 根據報警命令創建警報事件 - 增加詳細的十進制映射
        /// </summary>
        private AlarmEvent? CreateAlarmFromCommand(int lCommand, DeviceInfo device, IntPtr pBuf,
            uint dwBufLen, bool bAlarmAckFlag, int nEventID, int? channel = null)
        {
            try
            {
                var type = (EM_ALARM_TYPE)lCommand;
                AlarmType alarmType = type switch
                {
                    EM_ALARM_TYPE.ALARM_ALARM_EX => AlarmType.DeviceError,
                    EM_ALARM_TYPE.MOTION_ALARM_EX => AlarmType.MotionDetect,
                    EM_ALARM_TYPE.VIDEOLOST_ALARM_EX => AlarmType.VideoLoss,
                    EM_ALARM_TYPE.SHELTER_ALARM_EX => AlarmType.VideoBlind,
                    EM_ALARM_TYPE.DISKFULL_ALARM_EX => AlarmType.DiskFull,
                    EM_ALARM_TYPE.DISKERROR_ALARM_EX => AlarmType.DiskError,
                    EM_ALARM_TYPE.EVENT_MOTIONDETECT => AlarmType.MotionDetect,
                    EM_ALARM_TYPE.ALARM_FRONTDISCONNECT => AlarmType.NetworkDisconnect,
                    EM_ALARM_TYPE.ALARM_STORAGE_FAILURE => AlarmType.DiskError,
                    EM_ALARM_TYPE.ALARM_IVS => AlarmType.IVSRule,
                    // ... 其他需要的類型
                    _ => AlarmType.DeviceError
                };

                var priority = alarmType switch
                {
                    AlarmType.VideoLoss => AlarmPriority.High,
                    AlarmType.NetworkDisconnect => AlarmPriority.High,
                    AlarmType.DiskError => AlarmPriority.High,
                    AlarmType.DiskFull => AlarmPriority.High,
                    AlarmType.MotionDetect => AlarmPriority.Low,
                    _ => AlarmPriority.Normal
                };

                // 嘗試建立更具體的描述（若需更詳盡，可依 type/struct 解析 pBuf）
                string description = GetAlarmDescription(lCommand, pBuf, dwBufLen, channel);

                return new AlarmEvent
                {
                    Type = alarmType,
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    Channel = channel ?? 0,
                    Description = description,
                    Time = DateTime.Now,
                    Priority = priority,
                    ExtraData = $"Command={lCommand},CanAck={bAlarmAckFlag},EventID={nEventID}"
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"創建警報事件時發生異常: {ex.Message}");
                return null;
            }
        }

        private string GetAlarmDescription(int lCommand, IntPtr pBuf, uint dwBufLen, int? channel = null)
        {
            try
            {
                var type = (EM_ALARM_TYPE)lCommand;
                int ch = channel ?? 0;
                // 以可讀訊息為主，不顯示原始命令（除非是未知類型）
                return type switch
                {
                    EM_ALARM_TYPE.MOTION_ALARM_EX => $"通道 {ch}：移動偵測",
                    EM_ALARM_TYPE.EVENT_MOTIONDETECT => $"通道 {ch}：移動偵測（智能事件）",
                    EM_ALARM_TYPE.VIDEOLOST_ALARM_EX => $"通道 {ch}：視頻信號丟失",
                    EM_ALARM_TYPE.SHELTER_ALARM_EX => $"通道 {ch}：視頻遮蔽",
                    EM_ALARM_TYPE.DISKFULL_ALARM_EX => "硬碟空間已滿",
                    EM_ALARM_TYPE.DISKERROR_ALARM_EX => "硬碟錯誤",
                    EM_ALARM_TYPE.ALARM_FRONTDISCONNECT => "前端 IPC 斷線",
                    EM_ALARM_TYPE.ALARM_ALARM_EX => $"外部警報 (通道 {ch})",
                    EM_ALARM_TYPE.ALARM_IVS => $"IVS 智能規則觸發 (通道 {ch})",
                    EM_ALARM_TYPE.SOUND_DETECT_ALARM_EX => $"通道 {ch}：音頻偵測",
                    EM_ALARM_TYPE.ALARM_STORAGE_FAILURE => "存儲設備異常",
                    EM_ALARM_TYPE.EVENT_CROSSREGION_DETECTION => $"通道 {ch}：警戒區事件",
                    _ => $"未知報警類型 (命令: {lCommand}, 0x{lCommand:X})"
                };
            }
            catch
            {
                return $"警報命令: {lCommand} (0x{lCommand:X})";
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    StopSubscription();
                    ClearAllAlarms();

                    Debug.WriteLine("AlarmSubscriptionManager 已釋放");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"釋放 AlarmSubscriptionManager 時發生異常: {ex.Message}");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
        // 將 OnIntelligentEventCallback 方法的簽名從 private int OnIntelligentEventCallback() 改為符合委派 fAnalyzerDataCallBack 的簽名
        private int OnIntelligentEventCallback(IntPtr lAnalyzerHandle, uint dwEventType, IntPtr pEventInfo,
            IntPtr pBuffer, uint dwBufSize, IntPtr dwUser, int nSequence, IntPtr reserved)
        {
            // 處理智能事件回調的邏輯
            Debug.WriteLine($"智能事件回調: 事件類型={dwEventType}, 序列={nSequence}");
            return 0; // 回傳 0 表示成功
        }
    }
}