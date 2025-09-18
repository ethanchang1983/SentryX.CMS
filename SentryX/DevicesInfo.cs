// DeviceInfo.cs - 設備資訊模型
using System;

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
                UpdateDeviceId(); // 自動更新 ID
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
                UpdateDeviceId(); // 自動更新 ID
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

        /// <summary>
        /// 建構子 - 建立新的設備資訊
        /// </summary>
        public DeviceInfo()
        {
            UpdateDeviceId();
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
        }

        /// <summary>
        /// 🔥 更新設備唯一識別碼 - 使用 IP:Port 格式
        /// </summary>
        private void UpdateDeviceId()
        {
            if (!string.IsNullOrEmpty(_ipAddress))
            {
                Id = $"{_ipAddress}:{_port}";
            }
        }

        /// <summary>
        /// 🔥 手動設定 ID（用於向後相容）
        /// </summary>
        public void SetId(string customId)
        {
            Id = customId;
        }

        /// <summary>
        /// 顯示用的字串格式
        /// </summary>
        public override string ToString()
        {
            var status = IsOnline ? "🟢" : "🔴";
            return $"{status} {Name} ({IpAddress}:{Port})";
        }

        /// <summary>
        /// 狀態顯示文字
        /// </summary>
        public string StatusDisplay => IsOnline ? "🟢 在線" : "🔴 離線";

        /// <summary>
        /// 🔥 取得設備的簡短顯示名稱
        /// </summary>
        public string DisplayName => $"{Name} ({IpAddress}:{Port})";

        /// <summary>
        /// 複製設備資訊
        /// </summary>
        public DeviceInfo Clone()
        {
            return new DeviceInfo
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
                ChannelCount = this.ChannelCount
            };
        }

        /// <summary>
        /// 🔥 檢查兩個設備是否為同一個（IP 和 Port 都相同）
        /// </summary>
        public bool IsSameDevice(DeviceInfo other)
        {
            return other != null && 
                   this.IpAddress == other.IpAddress && 
                   this.Port == other.Port;
        }

        /// <summary>
        /// 🔥 檢查是否與指定的 IP 和 Port 匹配
        /// </summary>
        public bool MatchesAddress(string ip, int port)
        {
            return this.IpAddress == ip && this.Port == port;
        }
    }
}