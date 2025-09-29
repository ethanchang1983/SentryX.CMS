using System;
using System.Linq;

namespace SentryX
{
    public class DeviceListManager
    {
        private readonly MainWindow _mainWindow;
        private string? _selectedDeviceId = null;
        private bool _isDeviceSelected = false;
        private bool _isAlarmInputSelected = false;  // 新增：是否選中警報輸入
        private bool _isAlarmOutputSelected = false; // 新增：是否選中警報輸出
        private int _selectedAlarmIndex = -1;        // 新增：選中的警報索引

        public string? SelectedDeviceId => _selectedDeviceId;
        public bool IsDeviceSelected => _isDeviceSelected;
        public bool IsAlarmInputSelected => _isAlarmInputSelected;
        public bool IsAlarmOutputSelected => _isAlarmOutputSelected;
        public int SelectedAlarmIndex => _selectedAlarmIndex;

        public DeviceListManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void RefreshDeviceList()
        {
            if (!_mainWindow.UIManager.IsUIInitialized) return;

            try
            {
                if (_mainWindow.DeviceListBox == null) return;

                _mainWindow.DeviceListBox.Items.Clear();
                var devices = DahuaSDK.GetAllDevices();

                if (devices.Count == 0)
                {
                    _mainWindow.DeviceListBox.Items.Add("尚未添加任何攝影機設備");
                    _mainWindow.DeviceListBox.Items.Add("點擊「設備管理」開始添加");
                }
                else
                {
                    var onlineDevices = devices.Where(d => d.IsOnline).ToList();
                    var offlineDevices = devices.Where(d => !d.IsOnline).ToList();

                    if (onlineDevices.Count > 0)
                    {
                        _mainWindow.DeviceListBox.Items.Add($"在線設備 ({onlineDevices.Count})");
                        foreach (var device in onlineDevices)
                        {
                            // 使用設備自己的圖標方法
                            string deviceIcon = device.GetDeviceIcon();

                            // 顯示設備主項目（包含警報能力指示）
                            var deviceDisplay = device.HasAlarmCapability
                                ? $"{deviceIcon} {device.Name} ({device.IpAddress}) [🔔 A:{device.AlarmInPortCount}/{device.AlarmOutPortCount}]"
                                : $"{deviceIcon} {device.Name} ({device.IpAddress})";

                            _mainWindow.DeviceListBox.Items.Add(deviceDisplay);

                            // 顯示通道
                            if (device.ChannelCount > 0)
                            {
                                _mainWindow.DeviceListBox.Items.Add($"  📹 視頻通道");
                                for (int i = 0; i < device.ChannelCount; i++)
                                {
                                    var channelName = i < device.ChannelNames.Count
                                        ? device.ChannelNames[i]
                                        : $"通道 {i + 1}";
                                    _mainWindow.DeviceListBox.Items.Add($"    └─ {channelName} (CH{i})");
                                }
                            }
                            else
                            {
                                _mainWindow.DeviceListBox.Items.Add($"  📹 視頻通道");
                                _mainWindow.DeviceListBox.Items.Add($"    └─ 通道 1 (CH0)");
                            }

                            // 顯示警報輸入
                            if (device.AlarmInPortCount > 0)
                            {
                                _mainWindow.DeviceListBox.Items.Add($"  🔔 警報輸入 ({device.AlarmInPortCount})");
                                for (int i = 0; i < device.AlarmInPortCount; i++)
                                {
                                    var alarmName = i < device.AlarmInputNames.Count
                                        ? device.AlarmInputNames[i]
                                        : $"警報輸入 {i + 1}";

                                    // 檢查警報狀態
                                    var isTriggered = device.AlarmInputStates.ContainsKey(i) && device.AlarmInputStates[i];
                                    var statusIcon = isTriggered ? "🔴" : "⚪";

                                    _mainWindow.DeviceListBox.Items.Add($"    └─ {statusIcon} {alarmName} (IN{i})");
                                }
                            }

                            // 顯示警報輸出
                            if (device.AlarmOutPortCount > 0)
                            {
                                _mainWindow.DeviceListBox.Items.Add($"  🚨 警報輸出 ({device.AlarmOutPortCount})");
                                for (int i = 0; i < device.AlarmOutPortCount; i++)
                                {
                                    var alarmName = i < device.AlarmOutputNames.Count
                                        ? device.AlarmOutputNames[i]
                                        : $"警報輸出 {i + 1}";

                                    // 檢查輸出狀態
                                    var isActive = device.AlarmOutputStates.ContainsKey(i) && device.AlarmOutputStates[i];
                                    var statusIcon = isActive ? "🟢" : "⚫";

                                    _mainWindow.DeviceListBox.Items.Add($"    └─ {statusIcon} {alarmName} (OUT{i})");
                                }
                            }

                            // 顯示硬碟資訊（如果有）
                            if (device.DiskCount > 0)
                            {
                                _mainWindow.DeviceListBox.Items.Add($"  💾 硬碟 ({device.DiskCount} 個)");
                            }

                            _mainWindow.DeviceListBox.Items.Add(""); // 空行分隔
                        }
                    }

                    if (offlineDevices.Count > 0)
                    {
                        _mainWindow.DeviceListBox.Items.Add($"離線設備 ({offlineDevices.Count})");
                        foreach (var device in offlineDevices)
                        {
                            string deviceIcon = device.GetDeviceIcon();
                            var alarmInfo = device.HasAlarmCapability
                                ? $" [🔔 A:{device.AlarmInPortCount}/{device.AlarmOutPortCount}]"
                                : "";
                            _mainWindow.DeviceListBox.Items.Add($"{deviceIcon} {device.Name} ({device.IpAddress}){alarmInfo} - 離線");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RefreshDeviceList 發生錯誤: {ex.Message}");
            }
        }

        public void HandleDeviceSelection(string selectedText)
        {
            DeviceInfo? selectedDevice = null;
            int selectedChannel = 0;
            _isDeviceSelected = false;
            _isAlarmInputSelected = false;
            _isAlarmOutputSelected = false;
            _selectedAlarmIndex = -1;

            // 處理視頻通道選擇
            if (selectedText.Contains("(CH") && selectedText.Contains("└─"))
            {
                selectedChannel = ExtractChannelFromSelection();
                selectedDevice = FindDeviceFromSelection();
                _isDeviceSelected = false;
            }
            // 處理警報輸入選擇
            else if (selectedText.Contains("(IN") && selectedText.Contains("└─"))
            {
                _selectedAlarmIndex = ExtractAlarmIndexFromSelection("IN");
                selectedDevice = FindDeviceFromSelection();
                _isAlarmInputSelected = true;
            }
            // 處理警報輸出選擇
            else if (selectedText.Contains("(OUT") && selectedText.Contains("└─"))
            {
                _selectedAlarmIndex = ExtractAlarmIndexFromSelection("OUT");
                selectedDevice = FindDeviceFromSelection();
                _isAlarmOutputSelected = true;
            }
            // 處理設備本身選擇
            else if (IsDeviceItem(selectedText))
            {
                var devices = DahuaSDK.GetAllDevices();
                selectedDevice = devices.FirstOrDefault(d =>
                    selectedText.Contains(d.Name) && selectedText.Contains(d.IpAddress));
                selectedChannel = -1; // -1 表示選中整個設備
                _isDeviceSelected = true;
            }

            if (selectedDevice != null)
            {
                _selectedDeviceId = selectedDevice.Id;

                // 根據選擇類型顯示不同訊息
                if (_isAlarmInputSelected)
                {
                    var alarmName = _selectedAlarmIndex < selectedDevice.AlarmInputNames.Count
                        ? selectedDevice.AlarmInputNames[_selectedAlarmIndex]
                        : $"警報輸入 {_selectedAlarmIndex + 1}";
                    _mainWindow.ShowMessage($"🔔 已選中: {selectedDevice.Name} - {alarmName}");
                }
                else if (_isAlarmOutputSelected)
                {
                    var alarmName = _selectedAlarmIndex < selectedDevice.AlarmOutputNames.Count
                        ? selectedDevice.AlarmOutputNames[_selectedAlarmIndex]
                        : $"警報輸出 {_selectedAlarmIndex + 1}";
                    _mainWindow.ShowMessage($"🚨 已選中: {selectedDevice.Name} - {alarmName}");
                    _mainWindow.ShowMessage($"💡 提示：您可以右鍵點擊來控制警報輸出的開/關");
                }
                else if (_isDeviceSelected)
                {
                    if (selectedDevice.ChannelCount > 1)
                    {
                        _mainWindow.ShowMessage($"已選中設備: {selectedDevice.Name} (共 {selectedDevice.ChannelCount} 個通道)");
                        if (selectedDevice.HasAlarmCapability)
                        {
                            _mainWindow.ShowMessage($"🔔 設備具有警報功能: {selectedDevice.AlarmInPortCount} 個輸入, {selectedDevice.AlarmOutPortCount} 個輸出");
                        }
                    }
                    else
                    {
                        _mainWindow.ShowMessage($"已選中設備: {selectedDevice.Name} ({selectedDevice.DeviceType})");
                    }
                }
                else
                {
                    var channelName = selectedChannel < selectedDevice.ChannelNames.Count
                        ? selectedDevice.ChannelNames[selectedChannel]
                        : $"通道{selectedChannel + 1}";
                    _mainWindow.ShowMessage($"📹 已選中: {selectedDevice.Name} - {channelName}");
                }
            }
            else
            {
                _selectedDeviceId = null;
                _isDeviceSelected = false;
                _isAlarmInputSelected = false;
                _isAlarmOutputSelected = false;
                _selectedAlarmIndex = -1;
            }
        }

        /// <summary>
        /// 判斷是否為設備項目
        /// </summary>
        private bool IsDeviceItem(string text)
        {
            return (text.Contains("📹") || text.Contains("🔲") ||
                    text.Contains("🔳") || text.Contains("📺") ||
                    text.Contains("🏢") || text.Contains("🏭")) &&
                   !text.Contains("└─") && !text.Contains("視頻通道") &&
                   !text.Contains("警報輸入") && !text.Contains("警報輸出") &&
                   !text.Contains("硬碟");
        }

        /// <summary>
        /// 從選擇中找到對應的設備
        /// </summary>
        private DeviceInfo? FindDeviceFromSelection()
        {
            if (_mainWindow.DeviceListBox == null) return null;

            int selectedIndex = _mainWindow.DeviceListBox.SelectedIndex;

            // 向上查找最近的設備項目
            for (int i = selectedIndex - 1; i >= 0; i--)
            {
                if (_mainWindow.DeviceListBox.Items[i] is string itemText && IsDeviceItem(itemText))
                {
                    var devices = DahuaSDK.GetAllDevices();
                    return devices.FirstOrDefault(d =>
                        itemText.Contains(d.Name) && itemText.Contains(d.IpAddress));
                }
            }

            return null;
        }

        public int ExtractChannelFromSelection()
        {
            if (_mainWindow.DeviceListBox?.SelectedItem is string selectedText)
            {
                if (selectedText.Contains("(CH"))
                {
                    var chIndex = selectedText.IndexOf("(CH");
                    if (chIndex >= 0)
                    {
                        var chText = selectedText.Substring(chIndex + 3);
                        var endIndex = chText.IndexOf(')');
                        if (endIndex > 0)
                        {
                            chText = chText.Substring(0, endIndex);
                            if (int.TryParse(chText, out int channel))
                            {
                                return channel;
                            }
                        }
                    }
                }
            }
            return 0;
        }

        /// <summary>
        /// 提取警報索引
        /// </summary>
        private int ExtractAlarmIndexFromSelection(string prefix)
        {
            if (_mainWindow.DeviceListBox?.SelectedItem is string selectedText)
            {
                var pattern = $"({prefix}";
                if (selectedText.Contains(pattern))
                {
                    var index = selectedText.IndexOf(pattern);
                    if (index >= 0)
                    {
                        var indexText = selectedText.Substring(index + pattern.Length);
                        var endIndex = indexText.IndexOf(')');
                        if (endIndex > 0)
                        {
                            indexText = indexText.Substring(0, endIndex);
                            if (int.TryParse(indexText, out int alarmIndex))
                            {
                                return alarmIndex;
                            }
                        }
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// 處理警報輸出控制（右鍵菜單觸發）
        /// </summary>
        public void ToggleAlarmOutput()
        {
            if (!_isAlarmOutputSelected || string.IsNullOrEmpty(_selectedDeviceId) || _selectedAlarmIndex < 0)
            {
                _mainWindow.ShowMessage("⚠ 請先選擇一個警報輸出");
                return;
            }

            var device = DahuaSDK.GetDevice(_selectedDeviceId);
            if (device == null || !device.IsOnline)
            {
                _mainWindow.ShowMessage("❌ 設備不在線");
                return;
            }

            // 獲取當前狀態並切換
            var currentState = device.AlarmOutputStates.ContainsKey(_selectedAlarmIndex) &&
                              device.AlarmOutputStates[_selectedAlarmIndex];
            var newState = !currentState;

            // 控制警報輸出
            if (DahuaSDK.TriggerAlarmOutput(_selectedDeviceId, _selectedAlarmIndex, newState))
            {
                RefreshDeviceList(); // 刷新顯示
            }
        }
    }
}