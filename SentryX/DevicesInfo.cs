// DeviceInfo.cs - 設備資訊模型（加強版）
using System;
using System.Collections.Generic;

namespace SentryX
{
    /// <summary>
    /// 設備資訊類別 - 儲存每個設備的完整資訊
    /// </summary>
    public class DeviceInfo
    {
        private string _ipAddress = "";
        private int _port = 37777;

        /// <summary>
        /// 設備唯一識別碼 (使用 IP:Port 格式)
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// 設備名稱 (用戶自定義)
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 設備 IP 地址
        /// </summary>
        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                _ipAddress = value;
                UpdateDeviceId();
            }
        }

        /// <summary>
        /// 設備連接埠 (預設 37777)
        /// </summary>
        public int Port
        {
            get => _port;
            set
            {
                _port = value;
                UpdateDeviceId();
            }
        }

        /// <summary>
        /// 登入帳號
        /// </summary>
        public string Username { get; set; } = "admin";

        /// <summary>
        /// 登入密碼
        /// </summary>
        public string Password { get; set; } = "";

        /// <summary>
        /// 設備是否在線
        /// </summary>
        public bool IsOnline { get; set; } = false;

        /// <summary>
        /// 登入句柄 (SDK 回傳的)
        /// </summary>
        public IntPtr LoginHandle { get; set; } = IntPtr.Zero;

        /// <summary>
        /// 最後連接時間
        /// </summary>
        public DateTime LastConnectTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// 設備序號 (從設備取得)
        /// </summary>
        public string SerialNumber { get; set; } = "";

        /// <summary>
        /// 通道數量
        /// </summary>
        public int ChannelCount { get; set; } = 0;

        // ========== 新增：警報相關屬性 ==========

        /// <summary>
        /// 警報輸入端口數量
        /// </summary>
        public int AlarmInPortCount { get; set; } = 0;

        /// <summary>
        /// 警報輸出端口數量
        /// </summary>
        public int AlarmOutPortCount { get; set; } = 0;

        /// <summary>
        /// 硬碟數量
        /// </summary>
        public int DiskCount { get; set; } = 0;

        /// <summary>
        /// 設備類型 (DVR, NVR, IPC 等)
        /// </summary>
        public string DeviceType { get; set; } = "Unknown";

        /// <summary>
        /// 設備類型代碼 (來自 SDK 的 EM_NET_DEVICE_TYPE)
        /// </summary>
        public int DeviceTypeCode { get; set; } = 0;

        /// <summary>
        /// 警報輸入狀態 (每個輸入的當前狀態)
        /// Key: 警報輸入索引, Value: 是否觸發
        /// </summary>
        public Dictionary<int, bool> AlarmInputStates { get; set; } = new();

        /// <summary>
        /// 警報輸出狀態 (每個輸出的當前狀態)
        /// Key: 警報輸出索引, Value: 是否啟動
        /// </summary>
        public Dictionary<int, bool> AlarmOutputStates { get; set; } = new();

        /// <summary>
        /// 通道名稱列表 (從設備讀取的通道自定義名稱)
        /// </summary>
        public List<string> ChannelNames { get; set; } = new();

        /// <summary>
        /// 警報輸入名稱列表
        /// </summary>
        public List<string> AlarmInputNames { get; set; } = new();

        /// <summary>
        /// 警報輸出名稱列表
        /// </summary>
        public List<string> AlarmOutputNames { get; set; } = new();

        // ========== 建構子 ==========

        /// <summary>
        /// 建構子 - 建立新的設備資訊
        /// </summary>
        public DeviceInfo()
        {
            UpdateDeviceId();
            InitializeCollections();
        }

        /// <summary>
        /// 便利建構子 - 快速建立設備
        /// </summary>
        public DeviceInfo(string name, string ip, int port = 37777, string username = "admin", string password = "123456")
        {
            Name = name;
            _ipAddress = ip;
            _port = port;
            Username = username;
            Password = password;
            UpdateDeviceId();
            InitializeCollections();
        }

        // ========== 私有方法 ==========

        /// <summary>
        /// 初始化集合
        /// </summary>
        private void InitializeCollections()
        {
            AlarmInputStates = new Dictionary<int, bool>();
            AlarmOutputStates = new Dictionary<int, bool>();
            ChannelNames = new List<string>();
            AlarmInputNames = new List<string>();
            AlarmOutputNames = new List<string>();
        }

        /// <summary>
        /// 更新設備唯一識別碼 - 使用 IP:Port 格式
        /// </summary>
        private void UpdateDeviceId()
        {
            if (!string.IsNullOrEmpty(_ipAddress))
            {
                Id = $"{_ipAddress}:{_port}";
            }
        }

        // ========== 公開方法 ==========

        /// <summary>
        /// 手動設定 ID（用於向後相容）
        /// </summary>
        public void SetId(string customId)
        {
            Id = customId;
        }

        /// <summary>
        /// 初始化警報狀態陣列
        /// </summary>
        public void InitializeAlarmStates()
        {
            // 初始化警報輸入狀態
            for (int i = 0; i < AlarmInPortCount; i++)
            {
                AlarmInputStates[i] = false;
                if (AlarmInputNames.Count <= i)
                {
                    AlarmInputNames.Add($"警報輸入 {i + 1}");
                }
            }

            // 初始化警報輸出狀態
            for (int i = 0; i < AlarmOutPortCount; i++)
            {
                AlarmOutputStates[i] = false;
                if (AlarmOutputNames.Count <= i)
                {
                    AlarmOutputNames.Add($"警報輸出 {i + 1}");
                }
            }

            // 初始化通道名稱
            for (int i = 0; i < ChannelCount; i++)
            {
                if (ChannelNames.Count <= i)
                {
                    ChannelNames.Add($"通道 {i + 1}");
                }
            }
        }

        /// <summary>
        /// 更新警報輸入狀態
        /// </summary>
        public void UpdateAlarmInputState(int index, bool isTriggered)
        {
            if (index >= 0 && index < AlarmInPortCount)
            {
                AlarmInputStates[index] = isTriggered;
            }
        }

        /// <summary>
        /// 更新警報輸出狀態
        /// </summary>
        public void UpdateAlarmOutputState(int index, bool isActive)
        {
            if (index >= 0 && index < AlarmOutPortCount)
            {
                AlarmOutputStates[index] = isActive;
            }
        }

        /// <summary>
        /// 取得設備能力摘要
        /// </summary>
        public string GetCapabilitySummary()
        {
            var summary = $"設備類型: {DeviceType}\n";
            summary += $"視頻通道: {ChannelCount} 個\n";

            if (AlarmInPortCount > 0)
                summary += $"警報輸入: {AlarmInPortCount} 個\n";

            if (AlarmOutPortCount > 0)
                summary += $"警報輸出: {AlarmOutPortCount} 個\n";

            if (DiskCount > 0)
                summary += $"硬碟: {DiskCount} 個\n";

            return summary;
        }

        /// <summary>
        /// 是否有警報功能
        /// </summary>
        public bool HasAlarmCapability => AlarmInPortCount > 0 || AlarmOutPortCount > 0;

        /// <summary>
        /// 是否為 NVR/DVR (多通道設備)
        /// </summary>
        public bool IsMultiChannelDevice => ChannelCount > 1;

        /// <summary>
        /// 是否為 IPC (網路攝影機)
        /// </summary>
        public bool IsIPCamera => ChannelCount == 1 && DeviceType.Contains("IPC");

        // ========== 顯示相關 ==========

        /// <summary>
        /// 顯示用的字串格式
        /// </summary>
        public override string ToString()
        {
            var status = IsOnline ? "🟢" : "🔴";
            var alarmInfo = HasAlarmCapability ? $" [A:{AlarmInPortCount}/{AlarmOutPortCount}]" : "";
            return $"{status} {Name} ({IpAddress}:{Port}) CH:{ChannelCount}{alarmInfo}";
        }

        /// <summary>
        /// 狀態顯示文字
        /// </summary>
        public string StatusDisplay => IsOnline ? "🟢 在線" : "🔴 離線";

        /// <summary>
        /// 取得設備的簡短顯示名稱
        /// </summary>
        public string DisplayName => $"{Name} ({IpAddress}:{Port})";

        /// <summary>
        /// 取得設備圖標 (根據設備類型和能力)
        /// </summary>
        public string GetDeviceIcon()
        {
            if (HasAlarmCapability && ChannelCount > 1)
                return "🏢"; // NVR/DVR with alarm
            else if (ChannelCount <= 1)
                return "📹"; // Single camera
            else if (ChannelCount <= 4)
                return "🔲"; // 4-channel
            else if (ChannelCount <= 8)
                return "🔳"; // 8-channel
            else if (ChannelCount <= 16)
                return "📺"; // 16-channel
            else
                return "🏭"; // Large system
        }

        // ========== 複製與比較 ==========

        /// <summary>
        /// 複製設備資訊
        /// </summary>
        public DeviceInfo Clone()
        {
            var clone = new DeviceInfo
            {
                Id = this.Id,
                Name = this.Name,
                _ipAddress = this._ipAddress,
                _port = this._port,
                Username = this.Username,
                Password = this.Password,
                IsOnline = this.IsOnline,
                LoginHandle = this.LoginHandle,
                LastConnectTime = this.LastConnectTime,
                SerialNumber = this.SerialNumber,
                ChannelCount = this.ChannelCount,
                AlarmInPortCount = this.AlarmInPortCount,
                AlarmOutPortCount = this.AlarmOutPortCount,
                DiskCount = this.DiskCount,
                DeviceType = this.DeviceType,
                DeviceTypeCode = this.DeviceTypeCode
            };

            // 複製集合
            clone.AlarmInputStates = new Dictionary<int, bool>(this.AlarmInputStates);
            clone.AlarmOutputStates = new Dictionary<int, bool>(this.AlarmOutputStates);
            clone.ChannelNames = new List<string>(this.ChannelNames);
            clone.AlarmInputNames = new List<string>(this.AlarmInputNames);
            clone.AlarmOutputNames = new List<string>(this.AlarmOutputNames);

            return clone;
        }

        /// <summary>
        /// 檢查兩個設備是否為同一個（IP 和 Port 都相同）
        /// </summary>
        public bool IsSameDevice(DeviceInfo other)
        {
            return other != null &&
                   this.IpAddress == other.IpAddress &&
                   this.Port == other.Port;
        }

        /// <summary>
        /// 檢查是否與指定的 IP 和 Port 匹配
        /// </summary>
        public bool MatchesAddress(string ip, int port)
        {
            return this.IpAddress == ip && this.Port == port;
        }
    }
}