// VideoInfo.cs - 視頻資訊類別
using System;

namespace SentryX
{
    /// <summary>
    /// 視頻資訊類別 - 儲存視頻的詳細資訊
    /// </summary>
    public class VideoInfo
    {
        /// <summary>
        /// 視頻寬度（解析度）
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 視頻高度（解析度）
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 視頻碼率（kbps）
        /// </summary>
        public double Bitrate { get; set; }

        /// <summary>
        /// 視頻幀率（FPS）
        /// </summary>
        public double Fps { get; set; }

        /// <summary>
        /// 碼流類型
        /// </summary>
        public VideoStreamType StreamType { get; set; }

        /// <summary>
        /// 設備名稱
        /// </summary>
        public string DeviceName { get; set; } = "";

        /// <summary>
        /// 通道號
        /// </summary>
        public int Channel { get; set; }

        /// <summary>
        /// 最後更新時間
        /// </summary>
        public DateTime LastUpdate { get; set; } = DateTime.Now;

        /// <summary>
        /// 累計幀數
        /// </summary>
        public long TotalFrames { get; set; }

        /// <summary>
        /// 累計數據量（位元組）
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// 開始播放時間
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 格式化顯示解析度
        /// </summary>
        public string ResolutionText => $"{Width}x{Height}";

        /// <summary>
        /// 格式化顯示碼流類型
        /// </summary>
        public string StreamTypeText => StreamType == VideoStreamType.Main ? "主碼流" : "輔碼流";

        /// <summary>
        /// 計算實際FPS
        /// </summary>
        public double CalculateActualFps()
        {
            var elapsed = (DateTime.Now - StartTime).TotalSeconds;
            return elapsed > 0 ? TotalFrames / elapsed : 0;
        }

        /// <summary>
        /// 計算實際碼率
        /// </summary>
        public double CalculateActualBitrate()
        {
            var elapsed = (DateTime.Now - StartTime).TotalSeconds;
            return elapsed > 0 ? (TotalBytes * 8) / (elapsed * 1000) : 0; // kbps
        }
    }
}