using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Xml.Serialization;
using System.IO;

namespace SentryX
{
    // Device 類別
    public class Device
    {
        public required string Name { get; set; }
        public required string IP { get; set; }
        public bool IsOnline { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    // MapConfiguration 類別
    public class MapConfiguration
    {
        public string MapImagePath { get; set; } = string.Empty;
        [XmlArray("Devices")]
        [XmlArrayItem("Device")]
        public List<Device> Devices { get; set; } = new List<Device>();
    }

    // MapEditorWindow 類別
    public partial class MapEditorWindow : Window
    {
        private List<Device> devices = new List<Device>();
        private bool isEditMode = false;
        private bool isDragging = false;
        // 將 Point 的宣告明確指定為 System.Windows.Point
        private System.Windows.Point dragStartPoint;
        private UIElement? draggedElement; // 允許為 null
        private double currentZoom = 1.0;
        private const double ZOOM_STEP = 0.1;
        private const double MAX_ZOOM = 2.0;
        private const double MIN_ZOOM = 0.5;

        // 在類別中新增私有變數，用於儲存資料夾路徑
        private string mapDataFolder = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "MapData");
        private string configFilePath => System.IO.Path.Combine(mapDataFolder, "config.xml");

        public MapEditorWindow()
        {
            InitializeComponent();
            InitializeDeviceList();
            UpdateButtonStates();
            // 確保 MapData 資料夾存在
            if (!Directory.Exists(mapDataFolder))
            {
                Directory.CreateDirectory(mapDataFolder);
            }
        }

        private void InitializeDeviceList()
        {
            // Sample device data for testing
            devices.AddRange(new[]
            {
                new Device { Name = "Camera 1", IP = "192.168.1.101", IsOnline = true },
                new Device { Name = "Camera 2", IP = "192.168.1.102", IsOnline = false },
                new Device { Name = "Sensor 1", IP = "192.168.1.103", IsOnline = true }
            });
            AvailableDevicesList.ItemsSource = devices;
        }

        // 更新載入地圖方法：載入後自動複製圖片到 MapData 資料夾
        private void LoadMapButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string originalFilePath = openFileDialog.FileName;
                    string fileName = System.IO.Path.GetFileName(originalFilePath);
                    string newFilePath = System.IO.Path.Combine(mapDataFolder, fileName);

                    // 複製圖片到 MapData 資料夾（覆蓋如果存在）
                    File.Copy(originalFilePath, newFilePath, true);

                    // 更新 MapImage.Source 為新路徑
                    MapImage.Source = new BitmapImage(new Uri(newFilePath));
                    MapInfoText.Text = $"地圖: {fileName}";
                    UpdateButtonStates();
                    StatusText.Text = "地圖已載入並複製到 MapData 資料夾";
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"載入地圖失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ClearMapButton_Click(object sender, RoutedEventArgs e)
        {
            MapImage.Source = null;
            MapCanvas.Children.Clear();
            devices.ForEach(d => { d.X = 0; d.Y = 0; });
            MapInfoText.Text = "未載入地圖";
            DeviceCountText.Text = "設備數量: 0";
            UpdateButtonStates();
        }

        private void AddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableDevicesList.SelectedItem is Device selectedDevice)
            {
                AddDeviceToCanvas(selectedDevice, 50, 50);
                UpdateButtonStates();
            }
        }

        // AddDeviceToCanvas
        private void AddDeviceToCanvas(Device device, double x, double y)
        {
            device.X = x;
            device.Y = y;

            var deviceIcon = new Border
            {
                Width = 40,
                Height = 40,
                Background = System.Windows.Media.Brushes.LightBlue,
                BorderBrush = System.Windows.Media.Brushes.Black,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Tag = device
            };

            var label = new TextBlock
            {
                Text = device.Name,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            deviceIcon.Child = label;
            Canvas.SetLeft(deviceIcon, x);
            Canvas.SetTop(deviceIcon, y);
            MapCanvas.Children.Add(deviceIcon);
            DeviceCountText.Text = $"設備數量: {MapCanvas.Children.Count}";
        }

        private void EditModeButton_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = !isEditMode;
            EditModeText.Text = isEditMode ? "編輯模式" : "檢視模式";
            UpdateButtonStates();
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            // 將 Brushes 明確指定為 System.Windows.Media.Brushes
            var selectedElements = MapCanvas.Children.OfType<Border>().Where(b => b.BorderBrush == System.Windows.Media.Brushes.Red).ToList();
            foreach (var element in selectedElements)
            {
                MapCanvas.Children.Remove(element);
            }
            DeviceCountText.Text = $"設備數量: {MapCanvas.Children.Count}";
            UpdateButtonStates();
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentZoom < MAX_ZOOM)
            {
                currentZoom += ZOOM_STEP;
                UpdateZoom();
            }
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentZoom > MIN_ZOOM)
            {
                currentZoom -= ZOOM_STEP;
                UpdateZoom();
            }
        }

        private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
        {
            currentZoom = 1.0;
            UpdateZoom();
        }

        private void UpdateZoom()
        {
            MapScaleTransform.ScaleX = currentZoom;
            MapScaleTransform.ScaleY = currentZoom;
            ZoomLevelText.Text = $"{(currentZoom * 100):0}%";
        }

        private void MapContainer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0 && currentZoom < MAX_ZOOM)
            {
                currentZoom += ZOOM_STEP;
            }
            else if (e.Delta < 0 && currentZoom > MIN_ZOOM)
            {
                currentZoom -= ZOOM_STEP;
            }
            UpdateZoom();
            e.Handled = true;
        }

        // 更新儲存配置方法：直接儲存到固定路徑 MapData/config.xml
        private void SaveMapButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 確保資料夾存在
                if (!Directory.Exists(mapDataFolder))
                {
                    Directory.CreateDirectory(mapDataFolder);
                }

                // 建立配置物件，使用複製後的圖片路徑
                string currentMapPath = MapImage.Source is BitmapImage bitmap && bitmap.UriSource != null
                    ? bitmap.UriSource.LocalPath
                    : string.Empty;
                var config = new MapConfiguration
                {
                    MapImagePath = currentMapPath,
                    Devices = devices.Where(d => d.X != 0 || d.Y != 0).ToList() // 僅包含已放置的設備
                };

                // 序列化為 XML 並寫入固定檔案
                XmlSerializer serializer = new XmlSerializer(typeof(MapConfiguration));
                using (StreamWriter writer = new StreamWriter(configFilePath))
                {
                    serializer.Serialize(writer, config);
                }

                StatusText.Text = "配置已儲存到 MapData/config.xml";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"儲存配置失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "儲存配置失敗";
            }
        }

        private void LoadConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(configFilePath))
                {
                    throw new FileNotFoundException("配置檔案不存在。");
                }

                XmlSerializer serializer = new XmlSerializer(typeof(MapConfiguration));
                using (StreamReader reader = new StreamReader(configFilePath))
                {
                    // 原本的程式碼
                    // var config = (MapConfiguration)serializer.Deserialize(reader);

                    // 修正後，使用 as 並加上 null 檢查
                    var config = serializer.Deserialize(reader) as MapConfiguration
                        ?? throw new InvalidOperationException("反序列化 MapConfiguration 失敗，結果為 null。");

                    // 先移除現有的設備圖標（Border），但保留 MapImage
                    var existingDevices = MapCanvas.Children.OfType<Border>().ToList();
                    foreach (var dev in existingDevices)
                    {
                        MapCanvas.Children.Remove(dev);
                    }

                    // 載入底圖
                    if (!string.IsNullOrEmpty(config.MapImagePath) && File.Exists(config.MapImagePath))
                    {
                        MapImage.Source = new BitmapImage(new Uri(config.MapImagePath));
                        MapInfoText.Text = $"地圖: {System.IO.Path.GetFileName(config.MapImagePath)}";
                    }
                    else
                    {
                        MapImage.Source = null; // 清空舊圖片
                        MapInfoText.Text = "未載入地圖";
                        System.Windows.MessageBox.Show("載入的圖片路徑無效或檔案不存在。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    // 載入設備
                    devices = config.Devices ?? new List<Device>();
                    foreach (var device in devices)
                    {
                        if (device.X != 0 || device.Y != 0)
                            AddDeviceToCanvas(device, device.X, device.Y);
                    }
                    AvailableDevicesList.ItemsSource = devices;
                    DeviceCountText.Text = $"設備數量: {MapCanvas.Children.Count - 1}"; // 減 1，因為 MapImage 佔一個
                    UpdateButtonStates();
                    StatusText.Text = "配置已從 MapData/config.xml 載入";
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"載入配置失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "載入配置失敗";
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DeviceSearchBox.Text == "搜尋設備...")
            {
                DeviceSearchBox.Text = "";
                DeviceSearchBox.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DeviceSearchBox.Text))
            {
                DeviceSearchBox.Text = "搜尋設備...";
                DeviceSearchBox.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = DeviceSearchBox.Text.ToLower();
            if (searchText == "搜尋設備...") return;

            AvailableDevicesList.ItemsSource = devices
                .Where(d => d.Name.ToLower().Contains(searchText) || d.IP.ToLower().Contains(searchText))
                .ToList();
        }

        private void AvailableDevicesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (isEditMode && AvailableDevicesList.SelectedItem is Device selectedDevice)
            {
                AddDeviceToCanvas(selectedDevice, 50, 50);
                UpdateButtonStates();
            }
        }

        private void MapCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!isEditMode) return;

            var point = e.GetPosition(MapCanvas);
            var hitElement = MapCanvas.InputHitTest(point) as UIElement;

            if (hitElement is Border border && border.Tag is Device)
            {
                isDragging = true;
                draggedElement = border;
                dragStartPoint = point;
                border.BorderBrush = System.Windows.Media.Brushes.Red;
                e.Handled = true;
            }
        }

        private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging && draggedElement != null)
            {
                var currentPoint = e.GetPosition(MapCanvas);
                var offset = currentPoint - dragStartPoint;
                dragStartPoint = currentPoint;

                double newX = Canvas.GetLeft(draggedElement) + offset.X;
                double newY = Canvas.GetTop(draggedElement) + offset.Y;

                Canvas.SetLeft(draggedElement, newX);
                Canvas.SetTop(draggedElement, newY);

                if (draggedElement is Border border && border.Tag is Device device)
                {
                    device.X = newX;
                    device.Y = newY;
                }

                MousePositionText.Text = $"座標: {newX:0}, {newY:0}";
            }
        }

        private void MapCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging && draggedElement is Border border)
            {
                border.BorderBrush = System.Windows.Media.Brushes.Black;
                isDragging = false;
                draggedElement = null;
            }
        }

        private void MapCanvas_MouseRightButtonDown(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!isEditMode) return;

            var point = e.GetPosition(MapCanvas);
            var hitElement = MapCanvas.InputHitTest(point) as UIElement;

            if (hitElement is Border border)
            {
                border.BorderBrush = border.BorderBrush == System.Windows.Media.Brushes.Red
                    ? System.Windows.Media.Brushes.Black
                    : System.Windows.Media.Brushes.Red;
            }
        }

        private void MapCanvas_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Device)) && e.Data.GetData(typeof(Device)) is Device device)
            {
                var point = e.GetPosition(MapCanvas);
                AddDeviceToCanvas(device, point.X, point.Y);
            }
        }

        private void MapCanvas_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(Device)))
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void UpdateButtonStates()
        {
            bool hasMap = MapImage.Source != null;
            ClearMapButton.IsEnabled = hasMap;
            AddDeviceButton.IsEnabled = hasMap && isEditMode;
            EditModeButton.IsEnabled = hasMap;
            DeleteSelectedButton.IsEnabled = hasMap && isEditMode && MapCanvas.Children.Count > 0;
            SaveMapButton.IsEnabled = hasMap;
        }
    }
}