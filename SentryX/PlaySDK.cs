// PlaySDK.cs - 極簡的 Play SDK 封裝
using System;
using System.Runtime.InteropServices;

namespace SentryX
{
    /// <summary>
    /// Play SDK 的簡單封裝 - 只包含必要的函數
    /// </summary>
    public static class PlaySDK
    {
        // 🔥 修正：使用正確的 DLL 名稱
        private const string DLL_NAME = "Play.dll"; // 大部分大華 SDK 使用這個名稱

        // === 基本控制函數 ===

        /// <summary>
        /// 獲取 Play SDK 版本
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern uint PLAY_GetSdkVersion();

        /// <summary>
        /// 初始化 DirectDraw
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_InitDDraw();

        /// <summary>
        /// 釋放 DirectDraw
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_ReleaseDDraw();

        /// <summary>
        /// 獲取可用的播放端口
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_GetFreePort(ref int port);

        /// <summary>
        /// 釋放播放端口
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_ReleasePort(int port);

        /// <summary>
        /// 設置串流模式（實時或文件）
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_SetStreamOpenMode(int port, uint mode);

        /// <summary>
        /// 設置解碼和渲染引擎
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_SetEngine(int port, uint decodeType, uint renderType);

        /// <summary>
        /// 開啟串流
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_OpenStream(int port, IntPtr fileHead, uint size, uint bufSize);

        /// <summary>
        /// 關閉串流
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_CloseStream(int port);

        /// <summary>
        /// 開始播放到指定窗口
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_Play(int port, IntPtr hwnd);

        /// <summary>
        /// 停止播放
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_Stop(int port);

        /// <summary>
        /// 輸入數據進行解碼
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_InputData(int port, IntPtr buffer, uint size);

        /// <summary>
        /// 獲取最後的錯誤代碼
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern uint PLAY_GetLastErrorEx();

        /// <summary>
        /// 重置源緩衝區
        /// </summary>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_ResetSourceBuffer(int port);

        // 🔥 新增：IVS 私有數據渲染支援
        /// <summary>
        /// 顯示私有數據，如規則框、規則框報警、移動檢測等
        /// Display private data such as rule box, rule box alarm, mobile detection, etc.
        /// </summary>
        /// <param name="nPort">端口號</param>
        /// <param name="bEnable">TRUE：開啟 FALSE：關閉</param>
        /// <param name="nReserve">保留參數</param>
        /// <returns>TRUE--成功，FALSE--失敗</returns>
        [DllImport(DLL_NAME)]
        public static extern bool PLAY_RenderPrivateData(int nPort, bool bEnable, int nReserve = 0);

        // === 常數定義 ===

        /// <summary>
        /// 緩衝區溢出錯誤
        /// </summary>
        public const uint PLAY_BUF_OVER = 0x16; // 緩衝區滿

        /// <summary>
        /// 實時串流模式
        /// </summary>
        public const uint STREAME_REALTIME = 0; // 即時串流模式
        
        // === 枚舉定義 ===

        /// <summary>
        /// 解碼類型 - 修正為 uint 類型
        /// </summary>
        public enum DecodeType : uint
        {
            DECODE_SW = 0,              // 軟體解碼
            DECODE_HW = 1,              // 硬體解碼
            DECODE_HW_FAST = 2          // 快速硬體解碼
        }

        /// <summary>
        /// 渲染類型 - 修正為 uint 類型
        /// </summary>
        public enum RenderType : uint
        {
            RENDER_GDI = 0,             // GDI 渲染
            RENDER_D3D = 1,             // DirectX 9
            RENDER_D3D11 = 2            // DirectX 11
        }
    }
}