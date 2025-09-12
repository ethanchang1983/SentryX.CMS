using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace SentryX
{
    public partial class DeviceManagerWindow : Window
    {
        // === 私有變數 ===
        private readonly ObservableCollection<DeviceInfo> _deviceCollection = new();
        private DeviceInfo? _selectedDevice = null;

        // ✅ 自動刷新計時器
        private readonly DispatcherTimer _autoRefreshTimer;

        /// <summary>
        /// 設備管理視窗建構子
        /// </summary>
        public DeviceManagerWindow()
        {
            InitializeComponent();
            InitializeUI();
            SubscribeToEvents();
            LoadExistingDevices();

            // ✅ 初始化自動刷新計時器
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // 每2秒刷新一次
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _autoRefreshTimer.Start();

            AddStatusMessage("🔄 自動刷新功能已啟動（每2秒更新）");
        }

        /// <summary>
        /// 初始化 UI
        /// </summary>
        private void InitializeUI()
        {
            DeviceDataGrid.ItemsSource = _deviceCollection;

            DeviceNameTextBox.Text = "";
            DeviceIPTextBox.Text = "192.168.1.";
            DevicePortTextBox.Text = "37777";
            UsernameTextBox.Text = "admin";
            PasswordBox.Password = "123456";

            EditDeviceButton.IsEnabled = false;
            RemoveDeviceButton.IsEnabled = false;
            LogoutDeviceButton.IsEnabled = false;
        }

        /// <summary>
        /// 訂閱 SDK 事件
        /// </summary>
        private void SubscribeToEvents()
        {
            DahuaSDK.DeviceStatusChanged += OnDeviceStatusChanged;
            DahuaSDK.StatusMessage += OnStatusMessage;
        }

        /// <summary>
        /// 載入已存在的設備
        /// </summary>
        private void LoadExistingDevices()
        {
            var devices = DahuaSDK.GetAllDevices();
            _deviceCollection.Clear();
            foreach (var device in devices)
            {
                _deviceCollection.Add(device);
            }

            AddStatusMessage($"載入了 {devices.Count} 個設備");
        }

        // 修正 CS8622：將 AutoRefreshTimer_Tick 的 sender 參數標記為非 nullable
        private void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            // 自動刷新 DataGrid 顯示
            DeviceDataGrid.Items.Refresh();

            // 更新按鈕狀態
            UpdateButtonStates();
        }

        // === 事件處理方法 ===

        /// <summary>
        /// 添加設備按鈕點擊
        /// </summary>
        private void AddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            ClearInputFields();
            DeviceNameTextBox.Focus();
            AddStatusMessage("請輸入新設備資訊，點擊「儲存並連接」一次完成");
        }

        /// <summary>
        /// 編輯設備按鈕點擊
        /// </summary>
        private void EditDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null)
            {
                AddStatusMessage("❌ 請先選擇要編輯的設備");
                return;
            }

            LoadDeviceToInputFields(_selectedDevice);
            AddStatusMessage($"正在編輯設備: {_selectedDevice.Name}，修改後點擊「儲存並連接」");
        }

        /// <summary>
        /// 移除設備按鈕點擊
        /// </summary>
        private void RemoveDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null)
            {
                AddStatusMessage("❌ 請先選擇要移除的設備");
                return;
            }

            var result = MessageBox.Show(
                $"確定要移除設備「{_selectedDevice.Name}」({_selectedDevice.IpAddress}) 嗎？\n\n" +
                "移除後設備將從系統中完全刪除。",
                "確認移除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                if (DahuaSDK.RemoveDevice(_selectedDevice.Id))
                {
                    _deviceCollection.Remove(_selectedDevice);
                    _selectedDevice = null;
                    UpdateButtonStates();
                    ClearInputFields();
                }
            }
        }

        /// <summary>
        /// ✅ 登出設備按鈕點擊（原本的斷開功能）
        /// </summary>
        private void LogoutDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null)
            {
                AddStatusMessage("❌ 請先選擇要登出的設備");
                return;
            }

            if (!_selectedDevice.IsOnline)
            {
                AddStatusMessage("⚠️ 設備已經是離線狀態");
                return;
            }

            AddStatusMessage($"📤 正在登出設備: {_selectedDevice.Name}...");
            DahuaSDK.DisconnectDevice(_selectedDevice.Id);
        }

        /// <summary>
        /// ✅ 儲存並自動連接設備
        /// </summary>
        private void SaveDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            // 驗證輸入
            if (!ValidateInput())
            {
                return;
            }

            // 建立設備資訊
            var deviceInfo = new DeviceInfo
            {
                Name = DeviceNameTextBox.Text.Trim(),
                IpAddress = DeviceIPTextBox.Text.Trim(),
                Port = int.Parse(DevicePortTextBox.Text),
                Username = UsernameTextBox.Text.Trim(),
                Password = PasswordBox.Password,
                Id = DeviceIPTextBox.Text.Trim()
            };

            SaveDeviceButton.IsEnabled = false; // 防止重複點擊

            try
            {
                bool isNewDevice = true;

                // 檢查是否是編輯現有設備
                if (_selectedDevice != null && _selectedDevice.IpAddress == deviceInfo.IpAddress)
                {
                    // 更新現有設備
                    _selectedDevice.Name = deviceInfo.Name;
                    _selectedDevice.Port = deviceInfo.Port;
                    _selectedDevice.Username = deviceInfo.Username;
                    _selectedDevice.Password = deviceInfo.Password;

                    AddStatusMessage($"✅ 設備 {deviceInfo.Name} 資訊已更新");
                    isNewDevice = false;
                    deviceInfo = _selectedDevice; // 使用現有設備對象
                }
                else
                {
                    // 添加新設備
                    if (!DahuaSDK.AddDevice(deviceInfo))
                    {
                        return; // 添加失敗，錯誤訊息已由 SDK 處理
                    }

                    _deviceCollection.Add(deviceInfo);
                    AddStatusMessage($"✅ 新設備 {deviceInfo.Name} 已添加");
                }

                // ✅ 自動嘗試連接設備
                AddStatusMessage($"🔄 正在自動連接設備 {deviceInfo.Name}...");

                bool connectResult = DahuaSDK.ConnectDevice(deviceInfo.Id);

                if (connectResult)
                {
                    AddStatusMessage($"🎉 設備 {deviceInfo.Name} 儲存並連接成功！");

                    // 連接成功後清空輸入欄位，準備下一個設備
                    if (isNewDevice)
                    {
                        ClearInputFields();
                    }
                }
                else
                {
                    AddStatusMessage($"⚠️ 設備 {deviceInfo.Name} 已儲存，但連接失敗，請檢查網路和設備狀態");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"❌ 儲存設備時發生錯誤: {ex.Message}");
                MessageBox.Show($"儲存設備時發生錯誤：\n{ex.Message}",
                               "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveDeviceButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// DataGrid 選擇變更事件
        /// </summary>
        private void DeviceDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedDevice = DeviceDataGrid.SelectedItem as DeviceInfo;
            UpdateButtonStates();

            if (_selectedDevice != null)
            {
                AddStatusMessage($"選中設備: {_selectedDevice.Name} ({_selectedDevice.IpAddress}) - {_selectedDevice.StatusDisplay}");
            }
        }

        // === 事件回調方法 ===

        /// <summary>
        /// 設備狀態變化回調
        /// </summary>
        private void OnDeviceStatusChanged(DeviceInfo device)
        {
            Dispatcher.Invoke(() =>
            {
                // 自動刷新會處理顯示更新
                UpdateButtonStates();
            });
        }

        /// <summary>
        /// 狀態訊息回調
        /// </summary>
        private void OnStatusMessage(string message)
        {
            Dispatcher.Invoke(() => AddStatusMessage(message));
        }

        // === 私有輔助方法 ===

        /// <summary>
        /// 驗證用戶輸入
        /// </summary>
        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(DeviceNameTextBox.Text))
            {
                MessageBox.Show("請輸入設備名稱", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                DeviceNameTextBox.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(DeviceIPTextBox.Text))
            {
                MessageBox.Show("請輸入設備 IP 地址", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                DeviceIPTextBox.Focus();
                return false;
            }

            if (!int.TryParse(DevicePortTextBox.Text, out int port) || port <= 0 || port > 65535)
            {
                MessageBox.Show("請輸入有效的埠號 (1-65535)", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                DevicePortTextBox.Focus();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 清空輸入欄位
        /// </summary>
        private void ClearInputFields()
        {
            DeviceNameTextBox.Text = "";
            DeviceIPTextBox.Text = "192.168.1.";
            DevicePortTextBox.Text = "37777";
            UsernameTextBox.Text = "admin";
            PasswordBox.Password = "123456";
        }

        /// <summary>
        /// 載入設備資訊到輸入欄位
        /// </summary>
        private void LoadDeviceToInputFields(DeviceInfo device)
        {
            DeviceNameTextBox.Text = device.Name;
            DeviceIPTextBox.Text = device.IpAddress;
            DevicePortTextBox.Text = device.Port.ToString();
            UsernameTextBox.Text = device.Username;
            PasswordBox.Password = device.Password;
        }

        /// <summary>
        /// 更新按鈕狀態
        /// </summary>
        private void UpdateButtonStates()
        {
            bool hasSelection = _selectedDevice != null;
            bool isOnline = _selectedDevice?.IsOnline ?? false;

            EditDeviceButton.IsEnabled = hasSelection;
            RemoveDeviceButton.IsEnabled = hasSelection && !isOnline; // 在線設備不能移除
            LogoutDeviceButton.IsEnabled = hasSelection && isOnline;  // 只有在線設備才能登出
        }

        /// <summary>
        /// 添加狀態訊息
        /// </summary>
        private void AddStatusMessage(string message)
        {
            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            StatusTextBlock.Text += timestampedMessage + "\n";
            Console.WriteLine(timestampedMessage);

            if (StatusTextBlock.Parent is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToEnd();
            }
        }

        /// <summary>
        /// 視窗關閉事件
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // ✅ 停止自動刷新計時器
            _autoRefreshTimer?.Stop();

            // 取消事件訂閱
            DahuaSDK.DeviceStatusChanged -= OnDeviceStatusChanged;
            DahuaSDK.StatusMessage -= OnStatusMessage;

            AddStatusMessage("🔄 自動刷新功能已停止");
            base.OnClosed(e);
        }
    }
}