using System;

namespace SentryX
{
    /// <summary>
    /// 搜尋到的設備資訊類別
    /// </summary>
    public class SearchedDeviceInfo
    {
        public int Index { get; set; }
        public bool IsInitialized { get; set; }
        public string InitStatusDisplay => IsInitialized ? "已初始化" : "未初始化";
        public int IPVersion { get; set; }
        public string IP { get; set; } = "";
        public int Port { get; set; }
        public string SubMask { get; set; } = "";
        public string Gateway { get; set; } = "";
        public string MAC { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string DetailType { get; set; } = "";
        public int HttpPort { get; set; }
        public string LocalIP { get; set; } = "";

        /// <summary>
        /// 轉換為 DeviceInfo 以便加入管理清單
        /// </summary>
        public DeviceInfo ToDeviceInfo()
        {
            return new DeviceInfo
            {
                Name = $"{DeviceType}-{IP}",
                IpAddress = IP,
                Port = Port,
                Username = "admin",
                Password = "123456",
                Id = IP
            };
        }
    }
}