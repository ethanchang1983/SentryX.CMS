namespace SentryX
{
    internal class Device : MapDevice
    {
        // 移除 Name 屬性，避免隱藏 MapDevice 的必要成員
        // 移除 IP 屬性，避免隱藏 MapDevice 的必要成員
        // IsOnline 也已在 MapDevice 定義，無需重複定義

        // 使用 new 關鍵字以明確隱藏 MapDevice 的成員，或直接移除以避免衝突
        public new int Width { get; set; }
        public new int Height { get; set; }
    }
}