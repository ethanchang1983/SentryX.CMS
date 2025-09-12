using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SentryX
{

    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// 應用程式啟動時執行 - 這是 WPF 程式的真正入口點
        /// </summary>
        /// <param name="e">啟動參數</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            // 首先呼叫基底類別的 OnStartup
            // 這會處理 WPF 框架的基本初始化
            base.OnStartup(e);

            // 在 console 顯示啟動訊息（debug 用）
            // 注意：WPF 程式預設沒有 console，但開發時很好用
            Console.WriteLine("=== CCTV 系統啟動中 ===");

            // 嘗試初始化大華 SDK
            if (!DahuaSDK.Init(enableReconnect: true))
            {
                // 如果 SDK 初始化失敗，顯示錯誤訊息給用戶
                System.Windows.MessageBox.Show(
                    "大華 SDK 初始化失敗！\n" +
                    "請確認：\n" +
                    "1. NETSDKCS 相關 DLL 檔案是否存在\n" +
                    "2. 系統環境是否支援\n" +
                    "3. 防毒軟體是否阻擋",
                    "初始化錯誤",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                // 關閉應用程式
                this.Shutdown(1); // 1 表示錯誤結束
                return;
            }

            Console.WriteLine("✓ SDK 初始化成功");
            Console.WriteLine("✓ WPF 主視窗即將顯示");

            // 註：MainWindow 會由 StartupUri="MainWindow.xaml" 自動建立和顯示
            // 我們不需要手動建立 MainWindow
        }

        /// <summary>
        /// 應用程式結束時執行清理工作
        /// </summary>
        /// <param name="e">結束參數</param>
        protected override void OnExit(ExitEventArgs e)
        {
            Console.WriteLine("=== 應用程式結束，清理資源 ===");

            // 清理大華 SDK 資源
            // 這會自動登出所有設備並釋放記憶體
            DahuaSDK.Cleanup();

            Console.WriteLine("✓ 清理完成");

            // 呼叫基底類別的 OnExit
            base.OnExit(e);
        }

        // 直接在同一個檔案中定義轉換器
        public class BoolToStatusConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is bool isOnline)
                {
                    return isOnline ? "🟢 在線" : "🔴 離線";
                }
                return "⚪ 未知";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }
    }
}
