// VideoInfo.cs - ���W��T���O
using System;

namespace SentryX
{
    /// <summary>
    /// ���W��T���O - �x�s���W���ԲӸ�T
    /// </summary>
    public class VideoInfo
    {
        /// <summary>
        /// ���W�e�ס]�ѪR�ס^
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// ���W���ס]�ѪR�ס^
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// ���W�X�v�]kbps�^
        /// </summary>
        public double Bitrate { get; set; }

        /// <summary>
        /// ���W�V�v�]FPS�^
        /// </summary>
        public double Fps { get; set; }

        /// <summary>
        /// �X�y����
        /// </summary>
        public VideoStreamType StreamType { get; set; }

        /// <summary>
        /// �]�ƦW��
        /// </summary>
        public string DeviceName { get; set; } = "";

        /// <summary>
        /// �q�D��
        /// </summary>
        public int Channel { get; set; }

        /// <summary>
        /// �̫��s�ɶ�
        /// </summary>
        public DateTime LastUpdate { get; set; } = DateTime.Now;

        /// <summary>
        /// �֭p�V��
        /// </summary>
        public long TotalFrames { get; set; }

        /// <summary>
        /// �֭p�ƾڶq�]�줸�ա^
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// �}�l����ɶ�
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// �榡����ܸѪR��
        /// </summary>
        public string ResolutionText => $"{Width}x{Height}";

        /// <summary>
        /// �榡����ܽX�y����
        /// </summary>
        public string StreamTypeText => StreamType == VideoStreamType.Main ? "�D�X�y" : "���X�y";

        /// <summary>
        /// �p����FPS
        /// </summary>
        public double CalculateActualFps()
        {
            var elapsed = (DateTime.Now - StartTime).TotalSeconds;
            return elapsed > 0 ? TotalFrames / elapsed : 0;
        }

        /// <summary>
        /// �p���ڽX�v
        /// </summary>
        public double CalculateActualBitrate()
        {
            var elapsed = (DateTime.Now - StartTime).TotalSeconds;
            return elapsed > 0 ? (TotalBytes * 8) / (elapsed * 1000) : 0; // kbps
        }
    }
}