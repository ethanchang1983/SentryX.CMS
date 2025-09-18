using System;
using NetSDKCS;

namespace SentryX
{
    /// <summary>
    /// PTZ控制類型枚舉
    /// </summary>
    public enum PTZControlType
    {
        Up,
        Down,
        Left,
        Right,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        ZoomIn,
        ZoomOut,
        FocusNear,
        FocusFar,
        IrisOpen,
        IrisClose
    }

    /// <summary>
    /// PTZ控制管理器
    /// </summary>
    public class PTZManager
    {
        private readonly IntPtr _loginHandle;
        private readonly int _channelId;

        public PTZManager(IntPtr loginHandle, int channelId)
        {
            _loginHandle = loginHandle;
            _channelId = channelId;
        }

        /// <summary>
        /// 開始PTZ移動
        /// </summary>
        /// <param name="controlType">控制類型</param>
        /// <param name="speed">速度 (1-8)</param>
        /// <returns>是否成功</returns>
        public bool StartMovement(PTZControlType controlType, int speed = 4)
        {
            try
            {
                speed = Math.Max(1, Math.Min(8, speed)); // 確保速度在有效範圍內

                (uint command, int param1, int param2, int param3) = GetPTZCommand(controlType, speed);

                bool result = NETClient.PTZControl(
                    _loginHandle,
                    _channelId,
                    command,
                    param1,
                    param2,
                    param3,
                    false, // dwStop = false，開始移動
                    IntPtr.Zero
                );

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PTZ開始移動錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止PTZ移動
        /// </summary>
        /// <param name="controlType">控制類型</param>
        /// <param name="speed">速度 (1-8)</param>
        /// <returns>是否成功</returns>
        public bool StopMovement(PTZControlType controlType, int speed = 4)
        {
            try
            {
                speed = Math.Max(1, Math.Min(8, speed)); // 確保速度在有效範圍內

                (uint command, int param1, int param2, int param3) = GetPTZCommand(controlType, speed);

                bool result = NETClient.PTZControl(
                    _loginHandle,
                    _channelId,
                    command,
                    param1,
                    param2,
                    param3,
                    true, // dwStop = true，停止移動
                    IntPtr.Zero
                );

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PTZ停止移動錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 轉到預置點
        /// </summary>
        /// <param name="presetNumber">預置點編號 (1-255)</param>
        /// <returns>是否成功</returns>
        public bool GotoPreset(int presetNumber)
        {
            try
            {
                presetNumber = Math.Max(1, Math.Min(255, presetNumber));

                bool result = NETClient.PTZControl(
                    _loginHandle,
                    _channelId,
                    (uint)EM_A_PTZ_ControlType.EM_A_PTZ_POINT_MOVE_CONTROL,
                    0,
                    presetNumber,
                    0,
                    false,
                    IntPtr.Zero
                );

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PTZ轉到預置點錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 設定預置點
        /// </summary>
        /// <param name="presetNumber">預置點編號 (1-255)</param>
        /// <returns>是否成功</returns>
        public bool SetPreset(int presetNumber)
        {
            try
            {
                presetNumber = Math.Max(1, Math.Min(255, presetNumber));

                bool result = NETClient.PTZControl(
                    _loginHandle,
                    _channelId,
                    (uint)EM_A_PTZ_ControlType.EM_A_PTZ_POINT_SET_CONTROL,
                    0,
                    presetNumber,
                    0,
                    false,
                    IntPtr.Zero
                );

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PTZ設定預置點錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 刪除預置點
        /// </summary>
        /// <param name="presetNumber">預置點編號 (1-255)</param>
        /// <returns>是否成功</returns>
        public bool DeletePreset(int presetNumber)
        {
            try
            {
                presetNumber = Math.Max(1, Math.Min(255, presetNumber));

                bool result = NETClient.PTZControl(
                    _loginHandle,
                    _channelId,
                    (uint)EM_A_PTZ_ControlType.EM_A_PTZ_POINT_DEL_CONTROL,
                    0,
                    presetNumber,
                    0,
                    false,
                    IntPtr.Zero
                );

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PTZ刪除預置點錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根據控制類型獲取PTZ命令參數
        /// </summary>
        /// <param name="controlType">控制類型</param>
        /// <param name="speed">速度</param>
        /// <returns>命令參數元組</returns>
        private static (uint command, int param1, int param2, int param3) GetPTZCommand(PTZControlType controlType, int speed)
        {
            return controlType switch
            {
                PTZControlType.Up => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_UP_CONTROL, 0, speed, 0),
                PTZControlType.Down => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_DOWN_CONTROL, 0, speed, 0),
                PTZControlType.Left => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_LEFT_CONTROL, 0, speed, 0),
                PTZControlType.Right => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_RIGHT_CONTROL, 0, speed, 0),
                PTZControlType.TopLeft => ((uint)EM_A_EXTPTZ_ControlType.EM_A_EXTPTZ_LEFTTOP, speed, speed, 0),
                PTZControlType.TopRight => ((uint)EM_A_EXTPTZ_ControlType.EM_A_EXTPTZ_RIGHTTOP, speed, speed, 0),
                PTZControlType.BottomLeft => ((uint)EM_A_EXTPTZ_ControlType.EM_A_EXTPTZ_LEFTDOWN, speed, speed, 0),
                PTZControlType.BottomRight => ((uint)EM_A_EXTPTZ_ControlType.EM_A_EXTPTZ_RIGHTDOWN, speed, speed, 0),
                PTZControlType.ZoomIn => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_ZOOM_ADD_CONTROL, 0, speed, 0),
                PTZControlType.ZoomOut => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_ZOOM_DEC_CONTROL, 0, speed, 0),
                PTZControlType.FocusNear => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_FOCUS_DEC_CONTROL, 0, speed, 0),
                PTZControlType.FocusFar => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_FOCUS_ADD_CONTROL, 0, speed, 0),
                PTZControlType.IrisOpen => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_APERTURE_ADD_CONTROL, 0, speed, 0),
                PTZControlType.IrisClose => ((uint)EM_A_PTZ_ControlType.EM_A_PTZ_APERTURE_DEC_CONTROL, 0, speed, 0),
                _ => throw new ArgumentException($"不支援的PTZ控制類型：{controlType}")
            };
        }
    }
}