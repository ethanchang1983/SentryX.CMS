using System;
using System.Linq;

namespace SentryX
{
    public class DeviceListManager
    {
        private readonly MainWindow _mainWindow;
        private string? _selectedDeviceId = null;
        private bool _isDeviceSelected = false; // 新增：標記是否選中的是設備本身

        public string? SelectedDeviceId => _selectedDeviceId;
        public bool IsDeviceSelected => _isDeviceSelected; // 新增：是否選中設備（而非通道）

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
                            // 根據通道數量判斷設備類型並顯示適當的圖標
                            string deviceIcon = GetDeviceIcon(device.ChannelCount);
                            _mainWindow.DeviceListBox.Items.Add($"{deviceIcon} {device.Name} ({device.IpAddress})");

                            if (device.ChannelCount > 0)
                            {
                                for (int channel = 0; channel < device.ChannelCount; channel++)
                                {
                                    _mainWindow.DeviceListBox.Items.Add($"    └─ 通道 {channel + 1} (CH{channel})");
                                }
                            }
                            else
                            {
                                _mainWindow.DeviceListBox.Items.Add($"    └─ 通道 1 (CH0)");
                            }
                            _mainWindow.DeviceListBox.Items.Add("");
                        }
                    }

                    if (offlineDevices.Count > 0)
                    {
                        _mainWindow.DeviceListBox.Items.Add($"離線設備 ({offlineDevices.Count})");
                        foreach (var device in offlineDevices)
                        {
                            string deviceIcon = GetDeviceIcon(device.ChannelCount);
                            _mainWindow.DeviceListBox.Items.Add($"{deviceIcon} {device.Name} ({device.IpAddress}) - 離線");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RefreshDeviceList 發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 根據通道數量返回適當的設備圖標
        /// </summary>
        private string GetDeviceIcon(int channelCount)
        {
            return channelCount switch
            {
                <= 1 => "📹", // 單路攝影機
                <= 4 => "🔲", // 4路 DVR/NVR
                <= 8 => "🔳", // 8路 DVR/NVR
                <= 16 => "📺", // 16路 DVR/NVR
                _ => "🏢" // 大型 NVR 系統
            };
        }

        public void HandleDeviceSelection(string selectedText)
        {
            DeviceInfo? selectedDevice = null;
            int selectedChannel = 0;
            _isDeviceSelected = false;

            if (selectedText.Contains("通道"))
            {
                // 選中的是通道
                selectedChannel = ExtractChannelFromSelection();

                int selectedIndex = _mainWindow.DeviceListBox.SelectedIndex;
                for (int i = selectedIndex - 1; i >= 0; i--)
                {
                    if (_mainWindow.DeviceListBox.Items[i] is string itemText && 
                        (itemText.Contains("📹") || itemText.Contains("🔲") || 
                         itemText.Contains("🔳") || itemText.Contains("📺") || itemText.Contains("🏢")))
                    {
                        var devices = DahuaSDK.GetAllDevices();
                        selectedDevice = devices.FirstOrDefault(d =>
                            itemText.Contains(d.Name) && itemText.Contains(d.IpAddress));
                        break;
                    }
                }
                _isDeviceSelected = false;
            }
            else if (selectedText.Contains("📹") || selectedText.Contains("🔲") || 
                     selectedText.Contains("🔳") || selectedText.Contains("📺") || selectedText.Contains("🏢"))
            {
                // 選中的是設備本身
                var devices = DahuaSDK.GetAllDevices();
                selectedDevice = devices.FirstOrDefault(d =>
                    selectedText.Contains(d.Name) && selectedText.Contains(d.IpAddress));
                selectedChannel = -1; // -1 表示選中整個設備
                _isDeviceSelected = true;
            }

            if (selectedDevice != null)
            {
                _selectedDeviceId = selectedDevice.Id;

                if (_isDeviceSelected && selectedDevice.ChannelCount > 1)
                {
                    _mainWindow.ShowMessage($"已選中設備: {selectedDevice.Name} (共 {selectedDevice.ChannelCount} 個通道)");
                    _mainWindow.ShowMessage($"💡 點擊「開始播放」將自動播放所有通道到可用的分割區域");
                }
                else if (_isDeviceSelected)
                {
                    _mainWindow.ShowMessage($"已選中設備: {selectedDevice.Name} (單通道設備)");
                }
                else
                {
                    _mainWindow.ShowMessage($"已選中: {selectedDevice.Name} 通道{selectedChannel + 1}");
                }
            }
            else
            {
                _selectedDeviceId = null;
                _isDeviceSelected = false;
            }
        }

        public int ExtractChannelFromSelection()
        {
            if (_mainWindow.DeviceListBox?.SelectedItem is string selectedText)
            {
                if (selectedText.Contains("通道") && selectedText.Contains("CH"))
                {
                    var chIndex = selectedText.IndexOf("CH");
                    if (chIndex >= 0)
                    {
                        var chText = selectedText.Substring(chIndex + 2);
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
    }
}