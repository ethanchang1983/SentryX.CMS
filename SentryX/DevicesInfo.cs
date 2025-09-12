// DeviceInfo.cs - 設備資訊模型
using System;

namespace SentryX
{

    /// <summary>
    /// 設備資訊類別 - 儲存每個設備的完整資訊
    /// </summary>
    public class DeviceInfo
    {
        /// <summary>
        /// 設備唯一識別碼 (通常用 IP)
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// 設備名稱 (用戶自定義)
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 設備 IP 地址
        /// </summary>
        public string IpAddress { get; set; } = "";

        /// <summary>
        /// 設備連接埠 (預設 37777)
        /// </summary>
        public int Port { get; set; } = 37777;

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
            // 使用 IP 作為預設的 ID
            Id = IpAddress;
        }

        /// <summary>
        /// 便利建構子 - 快速建立設備
        /// </summary>
        public DeviceInfo(string name, string ip, string username = "admin", string password = "123456")
        {
            Name = name;
            IpAddress = ip;
            Id = ip; // 使用 IP 作為 ID
            Username = username;
            Password = password;
        }

        /// <summary>
        /// 顯示用的字串格式
        /// </summary>
        public override string ToString()
        {
            var status = IsOnline ? "🟢" : "🔴";
            return $"{status} {Name} ({IpAddress})";
        }

        /// <summary>
        /// 狀態顯示文字
        /// </summary>
        public string StatusDisplay => IsOnline ? "🟢 在線" : "🔴 離線";

        /// <summary>
        /// 複製設備資訊
        /// </summary>
        public DeviceInfo Clone()
        {
            return new DeviceInfo
            {
                Id = this.Id,
                Name = this.Name,
                IpAddress = this.IpAddress,
                Port = this.Port,
                Username = this.Username,
                Password = this.Password,
                IsOnline = this.IsOnline,
                LoginHandle = this.LoginHandle,
                LastConnectTime = this.LastConnectTime,
                SerialNumber = this.SerialNumber,
                ChannelCount = this.ChannelCount
            };
        }
    }
}