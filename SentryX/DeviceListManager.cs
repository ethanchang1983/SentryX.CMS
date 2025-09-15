using System;
using System.Linq;

namespace SentryX
{
    public class DeviceListManager
    {
        private readonly MainWindow _mainWindow;
        private string? _selectedDeviceId = null;

        public string? SelectedDeviceId => _selectedDeviceId;

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
                            _mainWindow.DeviceListBox.Items.Add($"📹 {device.Name} ({device.IpAddress})");

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
                            _mainWindow.DeviceListBox.Items.Add($"📹 {device.Name} ({device.IpAddress}) - 離線");
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

            if (selectedText.Contains("通道"))
            {
                selectedChannel = ExtractChannelFromSelection();

                int selectedIndex = _mainWindow.DeviceListBox.SelectedIndex;
                for (int i = selectedIndex - 1; i >= 0; i--)
                {
                    if (_mainWindow.DeviceListBox.Items[i] is string itemText && itemText.Contains("📹"))
                    {
                        var devices = DahuaSDK.GetAllDevices();
                        selectedDevice = devices.FirstOrDefault(d =>
                            itemText.Contains(d.Name) && itemText.Contains(d.IpAddress));
                        break;
                    }
                }
            }
            else if (selectedText.Contains("📹"))
            {
                var devices = DahuaSDK.GetAllDevices();
                selectedDevice = devices.FirstOrDefault(d =>
                    selectedText.Contains(d.Name) && selectedText.Contains(d.IpAddress));
                selectedChannel = 0;
            }

            if (selectedDevice != null)
            {
                _selectedDeviceId = selectedDevice.Id;
                _mainWindow.ShowMessage($"已選中: {selectedDevice.Name} 通道{selectedChannel + 1}");
            }
            else
            {
                _selectedDeviceId = null;
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