using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetSDKCS;
using MessageBox = System.Windows.MessageBox;

namespace SentryX
{
    public partial class DeviceManagerWindow : Window
    {
        // === 私有變數 ===
        private readonly ObservableCollection<DeviceInfo> _deviceCollection = new();
        private readonly ObservableCollection<SearchedDeviceInfo> _searchResultCollection = new();
        private DeviceInfo? _selectedDevice = null;
        private SearchedDeviceInfo? _selectedSearchResult = null;

        // 自動刷新計時器
        private readonly DispatcherTimer _autoRefreshTimer;

        // 搜尋相關變數
        private readonly List<string> _localIPList = new();
        private readonly List<IntPtr> _searchIDList = new();
        private readonly fSearchDevicesCBEx _searchDevicesCBEx;
        private int _deviceSearchCount = 0;
        private bool _isSearching = false;

        // IP 範圍搜尋相關變數
        private List<string> _customIPList = new();
        private bool _isIPRangeSearch = false;
        private string _currentSearchMode = "自動偵測模式";

        /// <summary>
        /// 設備管理視窗建構子
        /// </summary>
        public DeviceManagerWindow()
        {
            InitializeComponent();

            // 初始化搜尋回調
            _searchDevicesCBEx = new fSearchDevicesCBEx(SearchDevicesCBEx);

            InitializeUI();
            SubscribeToEvents();
            LoadExistingDevices();

            // 初始化自動刷新計時器
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // 每2秒刷新一次
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _autoRefreshTimer.Start();

            AddStatusMessage("自動刷新功能已啟動（每2秒更新）");
        }

        /// <summary>
        /// 初始化 UI
        /// </summary>
        private void InitializeUI()
        {
            DeviceDataGrid.ItemsSource = _deviceCollection;
            SearchResultDataGrid.ItemsSource = _searchResultCollection;

            DeviceNameTextBox.Text = "";
            DeviceIPTextBox.Text = "192.168.1.";
            DevicePortTextBox.Text = "37777";
            UsernameTextBox.Text = "admin";
            PasswordBox.Password = "123456";

            EditDeviceButton.IsEnabled = false;
            RemoveDeviceButton.IsEnabled = false;
            LogoutDeviceButton.IsEnabled = false;
            FillFromSearchButton.IsEnabled = false;

            // 初始化新的 UI 元素
            SearchIPRangeButton.IsEnabled = false;
            UpdateSearchModeDisplay();
            UpdateDeviceCountDisplay();
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

        /// <summary>
        /// 自動刷新計時器事件
        /// </summary>
        private void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            // 自動刷新 DataGrid 顯示
            DeviceDataGrid.Items.Refresh();

            // 更新按鈕狀態
            UpdateButtonStates();
        }

        // === IP 範圍搜尋相關方法 ===

        /// <summary>
        /// 設定 IP 範圍按鈕點擊
        /// </summary>
        private void SetIPRangeButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new IPRangeDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                _customIPList = dialog.IPList;
                SearchIPRangeButton.IsEnabled = _customIPList.Count > 0;

                // 更新狀態顯示
                IPRangeStatusText.Text = $"已設定範圍：{_customIPList.Count} 個 IP 地址";
                AddStatusMessage($"IP 範圍設定完成：包含 {_customIPList.Count} 個 IP 地址");

                // 顯示範圍預覽（只顯示前幾個和最後幾個）
                if (_customIPList.Count > 0)
                {
                    string preview = "";
                    if (_customIPList.Count <= 5)
                    {
                        preview = string.Join(", ", _customIPList);
                    }
                    else
                    {
                        preview = $"{_customIPList[0]}, {_customIPList[1]}, ... , {_customIPList[_customIPList.Count - 2]}, {_customIPList[_customIPList.Count - 1]}";
                    }
                    AddStatusMessage($"IP 範圍預覽：{preview}");
                }
            }
        }

        /// <summary>
        /// IP 範圍搜尋按鈕點擊
        /// </summary>
        private void SearchIPRangeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                AddStatusMessage("搜尋正在進行中，請等待完成或點擊停止");
                return;
            }

            if (_customIPList.Count == 0)
            {
                AddStatusMessage("請先設定 IP 搜尋範圍");
                return;
            }

            _isIPRangeSearch = true;
            _currentSearchMode = "IP 範圍模式";
            StartDeviceSearchWithCustomIPs();
        }

        /// <summary>
        /// 使用自訂 IP 清單開始搜尋
        /// </summary>
        private void StartDeviceSearchWithCustomIPs()
        {
            try
            {
                _isSearching = true;
                SearchDevicesButton.IsEnabled = false;
                SearchIPRangeButton.IsEnabled = false;
                StopSearchButton.IsEnabled = true;
                SearchStatusText.Text = "正在範圍搜尋...";
                UpdateSearchModeDisplay();

                _searchResultCollection.Clear();
                _deviceSearchCount = 0;
                UpdateDeviceCountDisplay();

                AddStatusMessage($"開始在指定 IP 範圍內搜尋設備（共 {_customIPList.Count} 個地址）...");

                Task.Run(() =>
                {
                    try
                    {
                        // 針對每個自訂 IP 開始搜尋
                        foreach (var localIP in _customIPList)
                        {
                            if (!_isSearching) break;

                            var stuIn = new NET_IN_STARTSERACH_DEVICE
                            {
                                dwSize = (uint)Marshal.SizeOf(typeof(NET_IN_STARTSERACH_DEVICE)),
                                emSendType = EM_SEND_SEARCH_TYPE.MULTICAST_AND_BROADCAST,
                                cbSearchDevices = _searchDevicesCBEx,
                                szLocalIp = localIP
                            };

                            var stuOut = new NET_OUT_STARTSERACH_DEVICE
                            {
                                dwSize = (uint)Marshal.SizeOf(typeof(NET_OUT_STARTSERACH_DEVICE))
                            };

                            IntPtr searchID = NETClient.StartSearchDevicesEx(ref stuIn, ref stuOut);
                            if (searchID != IntPtr.Zero)
                            {
                                lock (_searchIDList)
                                {
                                    _searchIDList.Add(searchID);
                                }
                            }

                            // 每個 IP 間隔一點時間
                            System.Threading.Thread.Sleep(50);
                        }

                        // 搜尋15秒後自動停止（範圍搜尋時間稍長）
                        System.Threading.Thread.Sleep(15000);

                        if (_isSearching)
                        {
                            Dispatcher.Invoke(() => StopDeviceSearch());
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AddStatusMessage($"範圍搜尋過程中發生錯誤: {ex.Message}");
                            StopDeviceSearch();
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                AddStatusMessage($"啟動範圍搜尋時發生錯誤: {ex.Message}");
                StopDeviceSearch();
            }
        }

        /// <summary>
        /// 更新搜尋模式顯示
        /// </summary>
        private void UpdateSearchModeDisplay()
        {
            SearchModeText.Text = _currentSearchMode;
        }

        /// <summary>
        /// 更新設備數量顯示
        /// </summary>
        private void UpdateDeviceCountDisplay()
        {
            DeviceCountText.Text = _deviceSearchCount.ToString();
        }

        // === 設備搜尋相關方法 ===

        /// <summary>
        /// 搜尋設備按鈕點擊（自動偵測模式）
        /// </summary>
        private void SearchDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                AddStatusMessage("搜尋正在進行中，請等待完成或點擊停止");
                return;
            }

            _isIPRangeSearch = false;
            _currentSearchMode = "自動偵測模式";
            StartDeviceSearch();
        }

        /// <summary>
        /// 停止搜尋按鈕點擊
        /// </summary>
        private void StopSearchButton_Click(object sender, RoutedEventArgs e)
        {
            StopDeviceSearch();
        }

        /// <summary>
        /// 搜尋結果選擇變更
        /// </summary>
        private void SearchResultDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedSearchResult = SearchResultDataGrid.SelectedItem as SearchedDeviceInfo;
            FillFromSearchButton.IsEnabled = _selectedSearchResult != null;

            if (_selectedSearchResult != null)
            {
                AddStatusMessage($"選中搜尋結果: {_selectedSearchResult.IP} ({_selectedSearchResult.DeviceType}) - {_selectedSearchResult.InitStatusDisplay}");
            }
        }

        /// <summary>
        /// 搜尋結果雙擊 - 直接填入詳情
        /// </summary>
        private void SearchResultDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_selectedSearchResult != null)
            {
                FillFromSearchResult();
            }
        }

        /// <summary>
        /// 從搜尋結果填入按鈕點擊
        /// </summary>
        private void FillFromSearchButton_Click(object sender, RoutedEventArgs e)
        {
            FillFromSearchResult();
        }

        /// <summary>
        /// 開始設備搜尋（自動偵測網卡模式）
        /// </summary>
        private void StartDeviceSearch()
        {
            try
            {
                _isSearching = true;
                SearchDevicesButton.IsEnabled = false;
                SearchIPRangeButton.IsEnabled = false;
                StopSearchButton.IsEnabled = true;
                SearchStatusText.Text = "正在搜尋...";
                UpdateSearchModeDisplay();

                _searchResultCollection.Clear();
                _deviceSearchCount = 0;
                UpdateDeviceCountDisplay();

                AddStatusMessage("開始搜尋網路設備...");

                Task.Run(() =>
                {
                    try
                    {
                        // 取得所有網路介面
                        GetAllNetworkInterface();

                        Dispatcher.Invoke(() =>
                        {
                            if (_isSearching)
                            {
                                SearchStatusText.Text = $"找到 {_localIPList.Count} 個網路介面，開始搜尋...";
                            }
                        });

                        // 針對每個本地 IP 開始搜尋
                        foreach (var localIP in _localIPList)
                        {
                            if (!_isSearching) break;

                            var stuIn = new NET_IN_STARTSERACH_DEVICE
                            {
                                dwSize = (uint)Marshal.SizeOf(typeof(NET_IN_STARTSERACH_DEVICE)),
                                emSendType = EM_SEND_SEARCH_TYPE.MULTICAST_AND_BROADCAST,
                                cbSearchDevices = _searchDevicesCBEx,
                                szLocalIp = localIP
                            };

                            var stuOut = new NET_OUT_STARTSERACH_DEVICE
                            {
                                dwSize = (uint)Marshal.SizeOf(typeof(NET_OUT_STARTSERACH_DEVICE))
                            };

                            IntPtr searchID = NETClient.StartSearchDevicesEx(ref stuIn, ref stuOut);
                            if (searchID != IntPtr.Zero)
                            {
                                lock (_searchIDList)
                                {
                                    _searchIDList.Add(searchID);
                                }
                            }
                        }

                        // 搜尋10秒後自動停止
                        System.Threading.Thread.Sleep(10000);

                        if (_isSearching)
                        {
                            Dispatcher.Invoke(() => StopDeviceSearch());
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AddStatusMessage($"搜尋過程中發生錯誤: {ex.Message}");
                            StopDeviceSearch();
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                AddStatusMessage($"啟動搜尋時發生錯誤: {ex.Message}");
                StopDeviceSearch();
            }
        }

        /// <summary>
        /// 停止設備搜尋
        /// </summary>
        private void StopDeviceSearch()
        {
            try
            {
                _isSearching = false;
                SearchDevicesButton.IsEnabled = true;
                SearchIPRangeButton.IsEnabled = _customIPList.Count > 0;
                StopSearchButton.IsEnabled = false;

                Task.Run(() =>
                {
                    try
                    {
                        // 停止所有搜尋
                        lock (_searchIDList)
                        {
                            foreach (var searchID in _searchIDList)
                            {
                                if (searchID != IntPtr.Zero)
                                {
                                    NETClient.StopSearchDevice(searchID);
                                }
                            }
                            _searchIDList.Clear();
                        }

                        Dispatcher.Invoke(() =>
                        {
                            string searchModeText = _isIPRangeSearch ? "範圍搜尋" : "自動搜尋";
                            SearchStatusText.Text = $"{searchModeText}完成，找到 {_deviceSearchCount} 個設備";
                            AddStatusMessage($"{searchModeText}完成，找到 {_deviceSearchCount} 個設備");

                            // 重置搜尋模式
                            _isIPRangeSearch = false;
                            _currentSearchMode = "自動偵測模式";
                            UpdateSearchModeDisplay();
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AddStatusMessage($"停止搜尋時發生錯誤: {ex.Message}");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                AddStatusMessage($"停止搜尋時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 搜尋回調函數
        /// </summary>
        private void SearchDevicesCBEx(IntPtr lSearchHandle, IntPtr pDevNetInfo, IntPtr dwUser)
        {
            try
            {
                if (!_isSearching || pDevNetInfo == IntPtr.Zero)
                {
                    return;
                }

                var info = Marshal.PtrToStructure<NET_DEVICE_NET_INFO_EX2>(pDevNetInfo);
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action<NET_DEVICE_NET_INFO_EX2>(UpdateSearchUI), info);
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    AddStatusMessage($"搜尋回調錯誤: {ex.Message}");
                }));
            }
        }

        /// <summary>
        /// 更新搜尋 UI
        /// </summary>
        private void UpdateSearchUI(NET_DEVICE_NET_INFO_EX2 info)
        {
            try
            {
                if (!_isSearching)
                {
                    return;
                }

                // 檢查是否已經存在相同 MAC 的設備
                var existingDevice = _searchResultCollection.FirstOrDefault(d => d.MAC == info.stuDevInfo.szMac);
                if (existingDevice != null)
                {
                    return;
                }

                _deviceSearchCount++;

                var searchedDevice = new SearchedDeviceInfo
                {
                    Index = _deviceSearchCount,
                    IsInitialized = (info.stuDevInfo.byInitStatus & 0x1) != 1,
                    IPVersion = info.stuDevInfo.iIPVersion,
                    IP = info.stuDevInfo.szIP,
                    Port = info.stuDevInfo.nPort,
                    SubMask = info.stuDevInfo.szSubmask,
                    Gateway = info.stuDevInfo.szGateway,
                    MAC = info.stuDevInfo.szMac,
                    DeviceType = info.stuDevInfo.szDeviceType,
                    DetailType = info.stuDevInfo.szNewDetailType,
                    HttpPort = info.stuDevInfo.nHttpPort,
                    LocalIP = info.szLocalIp
                };

                _searchResultCollection.Add(searchedDevice);

                if (_isSearching && SearchStatusText != null)
                {
                    string modeText = _isIPRangeSearch ? "範圍搜尋" : "自動搜尋";
                    SearchStatusText.Text = $"{modeText}中，已找到 {_deviceSearchCount} 個設備...";
                }

                // 更新設備數量顯示
                UpdateDeviceCountDisplay();

                string searchType = _isIPRangeSearch ? "範圍搜尋" : "自動偵測";
                AddStatusMessage($"[{searchType}] 發現設備: {searchedDevice.IP} ({searchedDevice.DeviceType}) - {searchedDevice.InitStatusDisplay}");
            }
            catch (Exception ex)
            {
                AddStatusMessage($"更新搜尋 UI 錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 儲存並自動連接設備
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
                Password = PasswordBox.Password
            };

            SaveDeviceButton.IsEnabled = false; // 防止重複點擊

            try
            {
                bool isNewDevice = true;

                // 修正：檢查是否是編輯現有設備 - 使用 IP:Port 組合檢查
                if (_selectedDevice != null && _selectedDevice.MatchesAddress(deviceInfo.IpAddress, deviceInfo.Port))
                {
                    // 更新現有設備
                    _selectedDevice.Name = deviceInfo.Name;
                    _selectedDevice.Port = deviceInfo.Port;
                    _selectedDevice.Username = deviceInfo.Username;
                    _selectedDevice.Password = deviceInfo.Password;

                    AddStatusMessage($"設備 {deviceInfo.Name} 資訊已更新");
                    isNewDevice = false;
                    deviceInfo = _selectedDevice; // 使用現有設備對象
                }
                else
                {
                    // 新增前檢查：是否已存在相同 IP:Port 組合
                    if (DahuaSDK.IsDeviceExists(deviceInfo.IpAddress, deviceInfo.Port))
                    {
                        AddStatusMessage($"❌ 設備 {deviceInfo.IpAddress}:{deviceInfo.Port} 已存在！");
                        MessageBox.Show($"設備 {deviceInfo.IpAddress}:{deviceInfo.Port} 已經存在！\n\n" +
                                      "如果您要添加相同 IP 的不同設備，請使用不同的 Port。",
                                      "設備重複", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 添加新設備
                    if (!DahuaSDK.AddDevice(deviceInfo))
                    {
                        return; // 添加失敗，錯誤訊息已由 SDK 處理
                    }

                    _deviceCollection.Add(deviceInfo);
                    AddStatusMessage($"新設備 {deviceInfo.Name} ({deviceInfo.IpAddress}:{deviceInfo.Port}) 已添加");
                }

                // 自動嘗試連接設備
                AddStatusMessage($"正在自動連接設備 {deviceInfo.Name}...");

                bool connectResult = DahuaSDK.ConnectDevice(deviceInfo.Id);

                if (connectResult)
                {
                    AddStatusMessage($"設備 {deviceInfo.Name} 儲存並連接成功！");

                    // 連接成功後清空輸入欄位，準備下一個設備
                    if (isNewDevice)
                    {
                        ClearInputFields();
                    }
                }
                else
                {
                    AddStatusMessage($"設備 {deviceInfo.Name} 已儲存，但連接失敗，請檢查網路和設備狀態");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"儲存設備時發生錯誤: {ex.Message}");
                MessageBox.Show($"儲存設備時發生錯誤：\n{ex.Message}",
                               "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveDeviceButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 修正：從搜尋結果填入設備詳情 - 使用 IP:Port 格式
        /// </summary>
        private void FillFromSearchResult()
        {
            if (_selectedSearchResult == null) return;

            DeviceNameTextBox.Text = $"{_selectedSearchResult.DeviceType}-{_selectedSearchResult.IP}:{_selectedSearchResult.Port}";
            DeviceIPTextBox.Text = _selectedSearchResult.IP;
            DevicePortTextBox.Text = _selectedSearchResult.Port.ToString();
            UsernameTextBox.Text = "admin";
            PasswordBox.Password = "123456";

            AddStatusMessage($"已從搜尋結果填入設備資訊: {_selectedSearchResult.IP}:{_selectedSearchResult.Port}");
            DeviceNameTextBox.Focus();
        }

        /// <summary>
        /// 取得所有網路介面
        /// </summary>
        private void GetAllNetworkInterface()
        {
            _localIPList.Clear();

            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface adapter in nics)
                {
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    {
                        IPInterfaceProperties ip = adapter.GetIPProperties();
                        UnicastIPAddressInformationCollection ipCollection = ip.UnicastAddresses;

                        foreach (UnicastIPAddressInformation ipadd in ipCollection)
                        {
                            if (ipadd.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                string tempIP = ipadd.Address.ToString();
                                if (!_localIPList.Contains(tempIP))
                                {
                                    _localIPList.Add(tempIP);
                                }
                            }
                        }
                    }
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AddStatusMessage($"找到 {_localIPList.Count} 個本地網路介面");
                }));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AddStatusMessage($"取得網路介面失敗: {ex.Message}");
                }));
            }
        }

        // === 原有的事件處理方法 ===

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
                AddStatusMessage("請先選擇要編輯的設備");
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
                AddStatusMessage("請先選擇要移除的設備");
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
        /// 登出設備按鈕點擊（原本的斷開功能）
        /// </summary>
        private void LogoutDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null)
            {
                AddStatusMessage("請先選擇要登出的設備");
                return;
            }

            if (!_selectedDevice.IsOnline)
            {
                AddStatusMessage("設備已經是離線狀態");
                return;
            }

            AddStatusMessage($"正在登出設備: {_selectedDevice.Name}...");
            DahuaSDK.DisconnectDevice(_selectedDevice.Id);
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
            try
            {
                // 停止設備搜尋
                if (_isSearching)
                {
                    StopDeviceSearch();
                }

                // 停止自動刷新計時器
                _autoRefreshTimer?.Stop();

                // 取消事件訂閱
                DahuaSDK.DeviceStatusChanged -= OnDeviceStatusChanged;
                DahuaSDK.StatusMessage -= OnStatusMessage;

                AddStatusMessage("自動刷新功能已停止");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"視窗關閉時發生錯誤: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
    }
}