// VideoEnums.cs - 視頻相關的枚舉定義

namespace SentryX
{
    /// <summary>
    /// 解碼模式 - 告訴播放器用什麼方式解碼視頻
    /// </summary>
    public enum DecodeMode
    {
        /// <summary>
        /// 軟體解碼 - 用電腦CPU來解碼，比較慢但相容性最好（新的預設值）
        /// </summary>
        Software,

        /// <summary>
        /// 硬體解碼 - 用顯示卡GPU來解碼，很快但可能不相容某些設備
        /// </summary>
        Hardware,

        /// <summary>
        /// 自動選擇 - 先試軟體解碼，不行再用硬體解碼（調整順序）
        /// </summary>
        Auto
    }

    /// <summary>
    /// 視頻碼流類型 - 定義主碼流和輔碼流
    /// </summary>
    public enum VideoStreamType
    {
        /// <summary>
        /// 主碼流 - 高清晰度，高碼率，適合錄影和高品質顯示
        /// </summary>
        Main = 0,

        /// <summary>
        /// 輔碼流 - 低解析度，低碼率，適合網路傳輸和多路預覽
        /// </summary>
        Sub = 1
    }
}
