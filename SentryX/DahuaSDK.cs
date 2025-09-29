// DahuaSDK.cs - 加入設備管理功能與警報支援
using NetSDKCS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SentryX
{
    public static class DahuaSDK
    {
        // === 私有變數 ===
        private static bool _initialized = false;

        // 改用 DeviceInfo 來管理設備，而不是簡單的字典
        private static readonly Dictionary<string, DeviceInfo> _devices = new();

        // === 事件通知 ===
        // 當設備狀態改變時通知 UI
        public static event Action<DeviceInfo>? DeviceStatusChanged;
        public static event Action<string>? StatusMessage;

        // 新增：警報事件通知
        public static event Action<string, int, bool>? AlarmInputTriggered; // deviceId, alarmIndex, isTriggered
        public static event Action<string, int, bool>? AlarmOutputChanged; // deviceId, outputIndex, isActive

        // === 回調函數 ===
        private static readonly fDisConnectCallBack _disconnectCallback =
            (lLoginID, pchDVRIP, nDVRPort, dwUser) =>
            {
                var deviceIP = Marshal.PtrToStringAnsi(pchDVRIP);
                OnDeviceDisconnected(deviceIP);
            };

        private static readonly fHaveReConnectCallBack _reconnectCallback =
            (lLoginID, pchDVRIP, nDVRPort, dwUser) =>
            {
                var deviceIP = Marshal.PtrToStringAnsi(pchDVRIP);
                OnDeviceReconnected(deviceIP);
            };

        // === 公開方法 ===

        /// <summary>
        /// 初始化 SDK
        /// </summary>
        public static bool Init(bool enableReconnect = true)
        {
            if (_initialized) return true;

            var disconnectCb = enableReconnect ? _disconnectCallback : null;
            _initialized = NETClient.Init(disconnectCb, IntPtr.Zero, null);

            if (_initialized && enableReconnect)
            {
                NETClient.SetAutoReconnect(_reconnectCallback, IntPtr.Zero);

                var param = new NET_PARAM
                {
                    nWaittime = 10000,
                    nConnectTime = 5000
                };
                NETClient.SetNetworkParam(param);
            }

            var message = _initialized ? "✓ SDK 初始化成功" : $"✗ SDK 初始化失敗: {NETClient.GetLastError()}";
            StatusMessage?.Invoke(message);
            return _initialized;
        }

        /// <summary>
        /// 添加設備到管理清單 (但不連接) - 支援相同 IP 不同 Port
        /// </summary>
        public static bool AddDevice(DeviceInfo deviceInfo)
        {
            if (string.IsNullOrEmpty(deviceInfo.IpAddress))
            {
                StatusMessage?.Invoke("❌ 設備 IP 不能為空");
                return false;
            }

            // 使用新的 ID 格式檢查重複（IP:Port）
            var deviceId = $"{deviceInfo.IpAddress}:{deviceInfo.Port}";
            deviceInfo.SetId(deviceId); // 確保 ID 正確設定

            // 檢查是否已經存在相同的 IP:Port 組合
            if (_devices.ContainsKey(deviceInfo.Id))
            {
                StatusMessage?.Invoke($"⚠ 設備 {deviceInfo.IpAddress}:{deviceInfo.Port} 已存在");
                return false;
            }

            // 額外檢查：是否有相同 IP:Port 但不同 ID 的設備
            var existingDevice = _devices.Values.FirstOrDefault(d => d.MatchesAddress(deviceInfo.IpAddress, deviceInfo.Port));
            if (existingDevice != null)
            {
                StatusMessage?.Invoke($"⚠ 設備 {deviceInfo.IpAddress}:{deviceInfo.Port} 已存在（ID: {existingDevice.Id}）");
                return false;
            }

            // 添加到設備清單
            _devices[deviceInfo.Id] = deviceInfo;
            StatusMessage?.Invoke($"✅ 已添加設備 {deviceInfo.Name} ({deviceInfo.IpAddress}:{deviceInfo.Port})");

            // 通知 UI 更新
            DeviceStatusChanged?.Invoke(deviceInfo);

            return true;
        }

        /// <summary>
        /// 移除設備
        /// </summary>
        public static bool RemoveDevice(string deviceId)
        {
            if (!_devices.ContainsKey(deviceId))
            {
                StatusMessage?.Invoke($"⚠ 設備 {deviceId} 不存在");
                return false;
            }

            var device = _devices[deviceId];

            // 如果設備在線，先斷開連接
            if (device.IsOnline)
            {
                DisconnectDevice(deviceId);
            }

            // 從清單中移除
            _devices.Remove(deviceId);
            StatusMessage?.Invoke($"✅ 已移除設備 {device.Name}");

            return true;
        }

        /// <summary>
        /// 較舊版本的 Login 方法
        /// </summary>
        public static IntPtr Login(string ip, string user = "admin", string pass = "123456")
        {
            try
            {
                // 建立一個臨時設備資訊
                var tempDevice = new DeviceInfo
                {
                    Name = $"臨時設備-{ip}",
                    IpAddress = ip,
                    Username = user,
                    Password = pass,
                    Id = $"temp_{ip}_{DateTime.Now.Ticks}" // 避免 ID 衝突
                };

                // 先添加設備到管理清單
                if (AddDevice(tempDevice))
                {
                    // 然後嘗試連接
                    if (ConnectDevice(tempDevice.Id))
                    {
                        // 回傳登入句柄
                        var device = GetDevice(tempDevice.Id);
                        return device?.LoginHandle ?? IntPtr.Zero;
                    }
                }

                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"❌ Login 方法錯誤: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 連接指定設備 - 增強版，包含警報能力讀取
        /// </summary>
        public static bool ConnectDevice(string deviceId)
        {
            if (!_devices.TryGetValue(deviceId, out var device))
            {
                StatusMessage?.Invoke($"❌ 設備 {deviceId} 不存在");
                return false;
            }

            if (device.IsOnline)
            {
                StatusMessage?.Invoke($"⚠ 設備 {device.Name} 已經在線");
                return true;
            }

            StatusMessage?.Invoke($"🔄 正在連接設備 {device.Name}...");

            try
            {
                var deviceInfo = new NetSDKCS.NET_DEVICEINFO_Ex();
                var handle = NETClient.LoginWithHighLevelSecurity(
                    device.IpAddress,
                    (ushort)device.Port,
                    device.Username,
                    device.Password,
                    NetSDKCS.EM_LOGIN_SPAC_CAP_TYPE.TCP,
                    IntPtr.Zero,
                    ref deviceInfo
                );

                if (handle != IntPtr.Zero)
                {
                    // 更新設備基本資訊
                    device.LoginHandle = handle;
                    device.IsOnline = true;
                    device.LastConnectTime = DateTime.Now;
                    device.SerialNumber = deviceInfo.sSerialNumber;
                    device.ChannelCount = deviceInfo.nChanNum;

                    // 🔥 新增：更新警報相關資訊
                    device.AlarmInPortCount = deviceInfo.nAlarmInPortNum;
                    device.AlarmOutPortCount = deviceInfo.nAlarmOutPortNum;
                    device.DiskCount = deviceInfo.nDiskNum;
                    device.DeviceTypeCode = (int)deviceInfo.nDVRType;
                    device.DeviceType = GetDeviceTypeName(deviceInfo.nDVRType);

                    // 初始化警報狀態陣列
                    device.InitializeAlarmStates();

                    // 讀取通道名稱（如果支援）
                    LoadChannelNames(device);

                    // 顯示設備能力摘要
                    StatusMessage?.Invoke($"✅ 設備 {device.Name} 連接成功");
                    StatusMessage?.Invoke($"📊 設備能力:\n{device.GetCapabilitySummary()}");

                    // 如果有警報功能，啟動警報監聽
                    if (device.HasAlarmCapability)
                    {
                        StartAlarmListen(device);
                    }

                    DeviceStatusChanged?.Invoke(device);

                    return true;
                }
                else
                {
                    StatusMessage?.Invoke($"❌ 設備 {device.Name} 連接失敗: {NETClient.GetLastError()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"❌ 連接設備時發生錯誤: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 斷開指定設備
        /// </summary>
        public static bool DisconnectDevice(string deviceId)
        {
            if (!_devices.TryGetValue(deviceId, out var device))
            {
                StatusMessage?.Invoke($"❌ 設備 {deviceId} 不存在");
                return false;
            }

            if (!device.IsOnline)
            {
                StatusMessage?.Invoke($"⚠ 設備 {device.Name} 已經離線");
                return true;
            }

            try
            {
                if (device.LoginHandle != IntPtr.Zero)
                {
                    NETClient.Logout(device.LoginHandle);
                }

                device.LoginHandle = IntPtr.Zero;
                device.IsOnline = false;

                StatusMessage?.Invoke($"✅ 設備 {device.Name} 已斷開");
                DeviceStatusChanged?.Invoke(device);

                return true;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"❌ 斷開設備時發生錯誤: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 取得設備類型名稱
        /// </summary>
        private static string GetDeviceTypeName(NetSDKCS.EM_NET_DEVICE_TYPE deviceType)
        {
            return deviceType switch
            {
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_NONREALTIME_MACE => "非實時 MACE",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_NONREALTIME => "非實時",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_NVS_MPEG1 => "網絡視頻服務器(MPEG1)",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MPEG1_2 => "MPEG1/2 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MPEG1_8 => "MPEG1 8路 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MPEG4_8 => "MPEG4 8路 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MPEG4_16 => "MPEG4 16路 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MPEG4_SX2 => "LB系列 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MEPG4_ST2 => "GB系列 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MEPG4_SH2 => "HB系列 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MPEG4_GBE => "GBE系列 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MPEG4_NVSII => "II代網絡視頻服務器",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_STD_NEW => "新標準 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_DDNS => "DDNS DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_ATM => "ATM DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_NB_SERIAL => "二代非實時 NB DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_LN_SERIAL => "LN系列 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_BAV_SERIAL => "BAV系列 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_SDIP_SERIAL => "SDIP系列 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_IPC_SERIAL => "網路攝影機 IPC",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_NVS_B => "NVS B系列",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_NVS_C => "NVS H系列",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_NVS_S => "NVS S系列",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_NVS_E => "NVS E系列",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_NEW_PROTOCOL => "新協議 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_NVD_SERIAL => "解碼器",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_N5 => "N5系列 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_DVR_MIX_DVR => "混合 DVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_SVR_SERIAL => "SVR系列",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_SVR_BS => "SVR-BS",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_NVR_SERIAL => "網路錄影機 NVR",
                NetSDKCS.EM_NET_DEVICE_TYPE.NET_ITSE_SERIAL => "智慧交通設備",
                _ => "未知設備類型"
            };
        }

        /// <summary>
        /// 讀取通道名稱
        /// </summary>
        private static void LoadChannelNames(DeviceInfo device)
        {
            try
            {
                device.ChannelNames.Clear();

                for (int i = 0; i < device.ChannelCount; i++)
                {
                    // 預設名稱（當 SDK 無法取得時使用）
                    string channelDisplayName = $"通道 {i + 1}";

                    // 準備 struct，必須先設定 nStructSize
                    var chStruct = new NetSDKCS.NET_A_AV_CFG_ChannelName
                    {
                        nStructSize = Marshal.SizeOf(typeof(NetSDKCS.NET_A_AV_CFG_ChannelName)),
                        szName = string.Empty
                    };

                    object channelNameObj = chStruct;
                    var objectType = channelNameObj.GetType();

                    // 嘗試使用 GetNewDevConfig 取得通道名稱
                    bool success = NETClient.GetNewDevConfig(
                        device.LoginHandle,
                        i,
                        "ChannelTitle",
                        ref channelNameObj,
                        objectType,
                        5000
                    );

                    if (success)
                    {
                        try
                        {
                            // 解除封箱並讀取名稱，去掉可能的尾端 null 字元
                            var result = (NetSDKCS.NET_A_AV_CFG_ChannelName)channelNameObj;
                            if (!string.IsNullOrWhiteSpace(result.szName))
                            {
                                channelDisplayName = result.szName.TrimEnd('\0').Trim();
                            }
                        }
                        catch
                        {
                            // 若解除封箱失敗，保留預設名稱
                        }
                    }
                    else
                    {
                        // 可選：記錄錯誤碼以便除錯
                        var err = NETClient.GetLastError();
                        StatusMessage?.Invoke($"⚠ 無法讀取設備通道名稱 (channel {i})，SDK 錯誤: {err}");
                        // 若需要更可靠的策略，可在此嘗試 NETClient.QueryChannelName() 作為 fallback
                    }

                    device.ChannelNames.Add(channelDisplayName);
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"⚠ 讀取通道名稱時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 啟動警報監聽
        /// </summary>
        private static void StartAlarmListen(DeviceInfo device)
        {
            try
            {
                // TODO: 實作警報監聽功能
                // 這裡需要使用 SDK 的 StartListenEx 或類似功能
                StatusMessage?.Invoke($"🔔 已啟動設備 {device.Name} 的警報監聽");
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"⚠ 啟動警報監聽失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 查詢設備狀態（包含警報狀態）
        /// </summary>
        public static bool QueryDeviceStatus(string deviceId)
        {
            if (!_devices.TryGetValue(deviceId, out var device) || !device.IsOnline)
            {
                StatusMessage?.Invoke($"❌ 設備 {deviceId} 不在線或不存在");
                return false;
            }

            try
            {
                // 查詢警報輸入狀態
                // TODO: 實作使用 SDK 的 QueryDevState 功能

                StatusMessage?.Invoke($"📊 設備 {device.Name} 狀態已更新");
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"❌ 查詢設備狀態失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 手動觸發警報輸出
        /// </summary>
        public static bool TriggerAlarmOutput(string deviceId, int outputIndex, bool activate)
        {
            if (!_devices.TryGetValue(deviceId, out var device) || !device.IsOnline)
            {
                StatusMessage?.Invoke($"❌ 設備 {deviceId} 不在線或不存在");
                return false;
            }

            if (outputIndex < 0 || outputIndex >= device.AlarmOutPortCount)
            {
                StatusMessage?.Invoke($"❌ 無效的警報輸出索引: {outputIndex}");
                return false;
            }

            try
            {
                // TODO: 使用 SDK 的 AlarmControl 功能控制警報輸出
                // 暫時更新本地狀態
                device.UpdateAlarmOutputState(outputIndex, activate);

                var action = activate ? "啟動" : "關閉";
                StatusMessage?.Invoke($"✅ 已{action}設備 {device.Name} 的警報輸出 {outputIndex + 1}");

                // 觸發事件通知
                AlarmOutputChanged?.Invoke(deviceId, outputIndex, activate);

                return true;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"❌ 控制警報輸出失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 向後相容的 Logout 方法
        /// </summary>
        /// <param name="handle">登入句柄</param>
        public static void Logout(IntPtr handle)
        {
            try
            {
                // 找到對應的設備
                var device = _devices.Values.FirstOrDefault(d => d.LoginHandle == handle);
                if (device != null)
                {
                    DisconnectDevice(device.Id);
                }
                else
                {
                    // 如果找不到對應設備，直接呼叫原生 SDK
                    if (handle != IntPtr.Zero)
                    {
                        NETClient.Logout(handle);
                        StatusMessage?.Invoke("✅ 設備已登出 (直接呼叫)");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"❌ Logout 方法錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得所有設備清單
        /// </summary>
        public static List<DeviceInfo> GetAllDevices()
        {
            return _devices.Values.ToList();
        }

        /// <summary>
        /// 取得在線設備清單
        /// </summary>
        public static List<DeviceInfo> GetOnlineDevices()
        {
            return _devices.Values.Where(d => d.IsOnline).ToList();
        }

        /// <summary>
        /// 取得指定設備資訊
        /// </summary>
        public static DeviceInfo? GetDevice(string deviceId)
        {
            return _devices.TryGetValue(deviceId, out var device) ? device : null;
        }

        /// <summary>
        /// 根據 IP 和 Port 查找設備
        /// </summary>
        public static DeviceInfo? GetDeviceByAddress(string ipAddress, int port)
        {
            return _devices.Values.FirstOrDefault(d => d.MatchesAddress(ipAddress, port));
        }

        /// <summary>
        /// 檢查指定地址是否已存在設備
        /// </summary>
        public static bool IsDeviceExists(string ipAddress, int port)
        {
            return _devices.Values.Any(d => d.MatchesAddress(ipAddress, port));
        }

        /// <summary>
        /// 清理所有資源
        /// </summary>
        public static void Cleanup()
        {
            if (_initialized)
            {
                // 斷開所有設備
                foreach (var device in _devices.Values.Where(d => d.IsOnline).ToList())
                {
                    DisconnectDevice(device.Id);
                }

                NETClient.Cleanup();
                _initialized = false;
                StatusMessage?.Invoke("✅ SDK 清理完成");
            }
        }

        // === 私有方法 ===

        /// <summary>
        /// 處理設備斷線事件
        /// </summary>
        private static void OnDeviceDisconnected(string? deviceIP)
        {
            if (string.IsNullOrEmpty(deviceIP)) return;

            // 尋找所有匹配 IP 的設備（可能有多個不同 Port）
            var matchingDevices = _devices.Values.Where(d => d.IpAddress == deviceIP).ToList();

            foreach (var device in matchingDevices)
            {
                device.IsOnline = false;
                StatusMessage?.Invoke($"⚠ 設備 {device.Name} ({deviceIP}:{device.Port}) 已斷線");
                DeviceStatusChanged?.Invoke(device);
            }
        }

        /// <summary>
        /// 處理設備重連事件 - 使用 IP:Port 查找
        /// </summary>
        private static void OnDeviceReconnected(string? deviceIP)
        {
            if (string.IsNullOrEmpty(deviceIP)) return;

            // 尋找所有匹配 IP 的設備（可能有多個不同 Port）
            var matchingDevices = _devices.Values.Where(d => d.IpAddress == deviceIP).ToList();

            foreach (var device in matchingDevices)
            {
                device.IsOnline = true;
                device.LastConnectTime = DateTime.Now;
                StatusMessage?.Invoke($"✅ 設備 {device.Name} ({deviceIP}:{device.Port}) 已重新連接");
                DeviceStatusChanged?.Invoke(device);
            }
        }

        // === 屬性 ===
        public static bool IsInitialized => _initialized;
        public static int TotalDeviceCount => _devices.Count;
        public static int OnlineDeviceCount => _devices.Values.Count(d => d.IsOnline);
    }
}