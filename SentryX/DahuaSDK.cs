// DahuaSDK.cs - 加入設備管理功能
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
        /// 添加設備到管理清單 (但不連接) - 🔥 修正版本，支援相同 IP 不同 Port
        /// </summary>
        public static bool AddDevice(DeviceInfo deviceInfo)
        {
            if (string.IsNullOrEmpty(deviceInfo.IpAddress))
            {
                StatusMessage?.Invoke("❌ 設備 IP 不能為空");
                return false;
            }

            // 🔥 使用新的 ID 格式檢查重複（IP:Port）
            var deviceId = $"{deviceInfo.IpAddress}:{deviceInfo.Port}";
            deviceInfo.SetId(deviceId); // 確保 ID 正確設定

            // 檢查是否已經存在相同的 IP:Port 組合
            if (_devices.ContainsKey(deviceInfo.Id))
            {
                StatusMessage?.Invoke($"⚠ 設備 {deviceInfo.IpAddress}:{deviceInfo.Port} 已存在");
                return false;
            }

            // 🔥 額外檢查：是否有相同 IP:Port 但不同 ID 的設備
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
        /// 向後相容的 Login 方法 - 簡化版本
        /// </summary>
        /// <param name="ip">設備 IP</param>
        /// <param name="user">用戶名</param>
        /// <param name="pass">密碼</param>
        /// <returns>登入句柄</returns>
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
        /// 連接指定設備
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
                    // 更新設備資訊
                    device.LoginHandle = handle;
                    device.IsOnline = true;
                    device.LastConnectTime = DateTime.Now;
                    device.SerialNumber = deviceInfo.sSerialNumber;
                    device.ChannelCount = deviceInfo.nChanNum;

                    StatusMessage?.Invoke($"✅ 設備 {device.Name} 連接成功");
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
        /// 🔥 新增：根據 IP 和 Port 查找設備
        /// </summary>
        public static DeviceInfo? GetDeviceByAddress(string ipAddress, int port)
        {
            return _devices.Values.FirstOrDefault(d => d.MatchesAddress(ipAddress, port));
        }

        /// <summary>
        /// 🔥 新增：檢查指定地址是否已存在設備
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
        /// 🔥 修正：處理設備重連事件 - 使用 IP:Port 查找
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