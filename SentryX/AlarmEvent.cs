using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SentryX
{
    /// <summary>
    /// 警報事件資料類別
    /// </summary>
    public class AlarmEvent : INotifyPropertyChanged
    {
        private bool _isRead = false;

        /// <summary>
        /// 警報事件唯一ID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 警報類型
        /// </summary>
        public AlarmType Type { get; set; }

        /// <summary>
        /// 警報類型名稱
        /// </summary>
        public string TypeName => GetAlarmTypeName(Type);

        /// <summary>
        /// 警報類型圖示
        /// </summary>
        public string TypeIcon => GetAlarmTypeIcon(Type);

        /// <summary>
        /// 設備ID
        /// </summary>
        public string DeviceId { get; set; } = "";

        /// <summary>
        /// 設備名稱
        /// </summary>
        public string DeviceName { get; set; } = "";

        /// <summary>
        /// 通道號
        /// </summary>
        public int Channel { get; set; } = 0;

        /// <summary>
        /// 警報描述
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 發生時間
        /// </summary>
        public DateTime Time { get; set; } = DateTime.Now;

        /// <summary>
        /// 時間顯示格式
        /// </summary>
        public string TimeDisplay => Time.ToString("HH:mm:ss");

        /// <summary>
        /// 警報優先級
        /// </summary>
        public AlarmPriority Priority { get; set; } = AlarmPriority.Normal;

        /// <summary>
        /// 是否已讀
        /// </summary>
        public bool IsRead
        {
            get => _isRead;
            set
            {
                if (_isRead != value)
                {
                    _isRead = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 額外資料（JSON格式）
        /// </summary>
        public string ExtraData { get; set; } = "";

        /// <summary>
        /// 取得警報類型名稱
        /// </summary>
        private static string GetAlarmTypeName(AlarmType type)
        {
            return type switch
            {
                AlarmType.MotionDetect => "移動偵測",
                AlarmType.VideoLoss => "視頻丟失",
                AlarmType.VideoBlind => "視頻遮蔽",
                AlarmType.IVSRule => "IVS規則",
                AlarmType.DeviceError => "設備異常",
                AlarmType.DiskFull => "硬碟滿",
                AlarmType.DiskError => "硬碟錯誤",
                AlarmType.NetworkDisconnect => "網路斷線",
                AlarmType.Tampering => "畫面篡改",
                AlarmType.AudioDetect => "音頻偵測",
                _ => "未知警報"
            };
        }

        /// <summary>
        /// 取得警報類型圖示
        /// </summary>
        private static string GetAlarmTypeIcon(AlarmType type)
        {
            return type switch
            {
                AlarmType.MotionDetect => "🏃",
                AlarmType.VideoLoss => "📺",
                AlarmType.VideoBlind => "👁️",
                AlarmType.IVSRule => "🎯",
                AlarmType.DeviceError => "⚠️",
                AlarmType.DiskFull => "💾",
                AlarmType.DiskError => "🔴",
                AlarmType.NetworkDisconnect => "📶",
                AlarmType.Tampering => "🔒",
                AlarmType.AudioDetect => "🔊",
                _ => "❓"
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 警報類型枚舉
    /// </summary>
    public enum AlarmType
    {
        /// <summary>
        /// 移動偵測
        /// </summary>
        MotionDetect = 1,

        /// <summary>
        /// 視頻信號丟失
        /// </summary>
        VideoLoss = 2,

        /// <summary>
        /// 視頻遮蔽
        /// </summary>
        VideoBlind = 3,

        /// <summary>
        /// IVS智能分析規則
        /// </summary>
        IVSRule = 4,

        /// <summary>
        /// 設備異常
        /// </summary>
        DeviceError = 5,

        /// <summary>
        /// 硬碟滿
        /// </summary>
        DiskFull = 6,

        /// <summary>
        /// 硬碟錯誤
        /// </summary>
        DiskError = 7,

        /// <summary>
        /// 網路斷線
        /// </summary>
        NetworkDisconnect = 8,

        /// <summary>
        /// 畫面篡改
        /// </summary>
        Tampering = 9,

        /// <summary>
        /// 音頻偵測
        /// </summary>
        AudioDetect = 10
    }

    /// <summary>
    /// 警報優先級
    /// </summary>
    public enum AlarmPriority
    {
        /// <summary>
        /// 低優先級
        /// </summary>
        Low = 1,

        /// <summary>
        /// 普通優先級
        /// </summary>
        Normal = 2,

        /// <summary>
        /// 高優先級
        /// </summary>
        High = 3
    }
}