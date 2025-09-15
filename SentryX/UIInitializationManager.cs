using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SentryX
{
    public class UIInitializationManager
    {
        private readonly MainWindow _mainWindow;
        private bool _isUIInitialized = false;

        public bool IsUIInitialized => _isUIInitialized;

        public UIInitializationManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public bool InitializeUI()
        {
            try
            {
                if (!ValidateUIComponents())
                {
                    System.Windows.MessageBox.Show("UI 控制項載入失敗，程式可能無法正常運行", "初始化錯誤",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                _isUIInitialized = true;
                SetupInitialState();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UI 初始化異常：{ex}");
                return false;
            }
        }

        private bool ValidateUIComponents()
        {
            var missingComponents = new List<string>();

            if (_mainWindow.VideoDisplayGrid == null) missingComponents.Add("VideoDisplayGrid");
            if (_mainWindow.DeviceListBox == null) missingComponents.Add("DeviceListBox");
            if (_mainWindow.SplitScreenComboBox == null) missingComponents.Add("SplitScreenComboBox");
            if (_mainWindow.StreamTypeComboBox == null) missingComponents.Add("StreamTypeComboBox");
            if (_mainWindow.DecodeTypeComboBox == null) missingComponents.Add("DecodeTypeComboBox");
            if (_mainWindow.StatusTextBlock == null) missingComponents.Add("StatusTextBlock");
            if (_mainWindow.StatusScrollViewer == null) missingComponents.Add("StatusScrollViewer");

            if (missingComponents.Count > 0)
            {
                string missing = string.Join(", ", missingComponents);
                Console.WriteLine($"以下 UI 控制項載入失敗：{missing}");
                Console.WriteLine("請檢查 XAML 檔案中的控制項命名是否正確，並重新建置專案。");
                return false;
            }

            Console.WriteLine("✅ 所有重要 UI 控制項已成功載入");
            return true;
        }

        private void SetupInitialState()
        {
            _mainWindow.Title = $"SentryX CCTV 系統 ({DateTime.Now:yyyy-MM-dd})";

            _mainWindow.ShowMessage("✅ 系統啟動完成，SDK 已就緒");
            _mainWindow.ShowMessage("💡 點擊「設備管理」開始添加攝影機");
            _mainWindow.ShowMessage("🔧 預設解碼模式已設為 CPU 軟體解碼（相容性最佳）");
            _mainWindow.ShowMessage("📡 預設碼流類型已設為主碼流（高畫質）");
            _mainWindow.ShowMessage("🖱️ 點擊分割區域選中，雙擊設備通道加入選中區域");
        }

        public void SubscribeEvents()
        {
            if (!_isUIInitialized) return;

            DahuaSDK.DeviceStatusChanged += _mainWindow.OnDeviceChanged;
            DahuaSDK.StatusMessage += _mainWindow.OnSDKMessage;
        }

        public void UnsubscribeEvents()
        {
            DahuaSDK.DeviceStatusChanged -= _mainWindow.OnDeviceChanged;
            DahuaSDK.StatusMessage -= _mainWindow.OnSDKMessage;
        }
    }
}