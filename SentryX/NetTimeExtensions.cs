using System;
using NetSDKCS;

namespace SentryX
{
    /// <summary>
    /// NET_TIME 結構的擴充方法
    /// </summary>
    public static class NetTimeExtensions
    {
        /// <summary>
        /// 從 DateTime 建立 NET_TIME - 修正版本
        /// </summary>
        /// <param name="dateTime">要轉換的 DateTime</param>
        /// <returns>轉換後的 NET_TIME</returns>
        public static NET_TIME FromDateTime(DateTime dateTime)
        {
            return new NET_TIME
            {
                dwYear = (uint)dateTime.Year,
                dwMonth = (uint)dateTime.Month,
                dwDay = (uint)dateTime.Day,
                dwHour = (uint)dateTime.Hour,
                dwMinute = (uint)dateTime.Minute,
                dwSecond = (uint)dateTime.Second
            };
        }

        /// <summary>
        /// 將 NET_TIME 轉換為 DateTime - 修正版本
        /// </summary>
        /// <param name="netTime">要轉換的 NET_TIME</param>
        /// <returns>轉換後的 DateTime</returns>
        public static DateTime ToDateTime(this NET_TIME netTime)
        {
            try
            {
                return new DateTime(
                    (int)netTime.dwYear,
                    (int)netTime.dwMonth,
                    (int)netTime.dwDay,
                    (int)netTime.dwHour,
                    (int)netTime.dwMinute,
                    (int)netTime.dwSecond
                );
            }
            catch (ArgumentOutOfRangeException)
            {
                // 如果時間無效，返回 DateTime.MinValue
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 檢查 NET_TIME 是否有效
        /// </summary>
        /// <param name="netTime">要檢查的 NET_TIME</param>
        /// <returns>是否為有效時間</returns>
        public static bool IsValid(this NET_TIME netTime)
        {
            try
            {
                var dateTime = new DateTime(
                    (int)netTime.dwYear,
                    (int)netTime.dwMonth,
                    (int)netTime.dwDay,
                    (int)netTime.dwHour,
                    (int)netTime.dwMinute,
                    (int)netTime.dwSecond
                );
                return dateTime != DateTime.MinValue;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 格式化 NET_TIME 為字串
        /// </summary>
        /// <param name="netTime">要格式化的 NET_TIME</param>
        /// <returns>格式化後的字串</returns>
        public static string FormatString(this NET_TIME netTime)
        {
            return $"{netTime.dwYear:D4}-{netTime.dwMonth:D2}-{netTime.dwDay:D2} {netTime.dwHour:D2}:{netTime.dwMinute:D2}:{netTime.dwSecond:D2}";
        }
    }
}