using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Serialization;

namespace SentryX
{
    // Device 類別 - 增加 Width 和 Height 屬性
    public class MapDevice
    {
        public required string Name { get; set; }
        public required string IP { get; set; }
        public int Port { get; set; } = 37777;
        public bool IsOnline { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 40;
        public double Height { get; set; } = 40;

        // 新增屬性以儲存原始設備資訊
        public string? DeviceId { get; set; }
        public int ChannelCount { get; set; } = 0;
        public string DeviceType { get; set; } = "";

        // 顯示用的屬性
        public string DisplayText => $"{Name} ({IP}:{Port})";
        public string StatusText => IsOnline ? "🟢 線上" : "🔴 離線";
        public string TypeIcon => ChannelCount switch
        {
            <= 1 => "📹",
            <= 4 => "🔲",
            <= 8 => "🔳",
            <= 16 => "📺",
            _ => "🏢"
        };
    }

    // ResizeHandle 枚舉 - 定義縮放控制點位置
    public enum ResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Top,
        Bottom,
        Left,
        Right
    }

    // DeviceControl - 自訂的設備控件類別
    public class DeviceControl : Grid
    {
        public MapDevice Device { get; set; }
        public Border DeviceBorder { get; private set; }
        public Border SelectionBorder { get; private set; }
        public List<Ellipse> ResizeHandles { get; private set; }
        public bool IsSelected { get; set; }

        public DeviceControl(MapDevice device)
        {
            Device = device;
            ResizeHandles = new List<Ellipse>();
            DeviceBorder = new Border();
            SelectionBorder = new Border();
            CreateControl();
        }

        private void CreateControl()
        {
            // 根據設備狀態設定顏色
            var bgColor = Device.IsOnline ? Colors.LightGreen : Colors.LightCoral;

            DeviceBorder = new Border
            {
                Width = Device.Width,
                Height = Device.Height,
                Background = new SolidColorBrush(bgColor),
                BorderBrush = System.Windows.Media.Brushes.Black,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5)
            };

            // 將 HorizontalAlignment = HorizontalAlignment.Center,
            // VerticalAlignment = VerticalAlignment.Center
            // 改為使用類型名稱 System.Windows.HorizontalAlignment.Center
            // 及 System.Windows.VerticalAlignment.Center

            var stackPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            // 設備類型圖標
            var iconText = new TextBlock
            {
                Text = Device.TypeIcon,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 14
            };

            // 設備名稱
            var nameText = new TextBlock
            {
                Text = Device.Name,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 10,
                MaxWidth = Device.Width - 4
            };

            stackPanel.Children.Add(iconText);
            stackPanel.Children.Add(nameText);

            DeviceBorder.Child = stackPanel;
            this.Children.Add(DeviceBorder);

            // 建立選擇框（初始隱藏）
            SelectionBorder = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.DodgerBlue,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 30, 144, 255)),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(-5)
            };
            this.Children.Add(SelectionBorder);

            CreateResizeHandles();
        }

        private void CreateResizeHandles()
        {
            // 控制點的大小和樣式
            const double handleSize = 8;
            var handleBrush = System.Windows.Media.Brushes.White;
            var handleStroke = System.Windows.Media.Brushes.DodgerBlue;

            // 建立8個控制點：4個角落 + 4個邊緣中點
            for (int i = 0; i < 8; i++)
            {
                var handle = new Ellipse
                {
                    Width = handleSize,
                    Height = handleSize,
                    Fill = handleBrush,
                    Stroke = handleStroke,
                    StrokeThickness = 1.5,
                    Visibility = Visibility.Collapsed,
                    Cursor = GetCursorForHandle((ResizeHandle)(i + 1))
                };

                ResizeHandles.Add(handle);
                this.Children.Add(handle);
            }
        }

        public void UpdateDeviceStatus(bool isOnline)
        {
            Device.IsOnline = isOnline;
            var bgColor = isOnline ? Colors.LightGreen : Colors.LightCoral;
            DeviceBorder.Background = new SolidColorBrush(bgColor);
        }

        private System.Windows.Input.Cursor GetCursorForHandle(ResizeHandle handle)
        {
            return handle switch
            {
                ResizeHandle.TopLeft or ResizeHandle.BottomRight => System.Windows.Input.Cursors.SizeNWSE,
                ResizeHandle.TopRight or ResizeHandle.BottomLeft => System.Windows.Input.Cursors.SizeNESW,
                ResizeHandle.Top or ResizeHandle.Bottom => System.Windows.Input.Cursors.SizeNS,
                ResizeHandle.Left or ResizeHandle.Right => System.Windows.Input.Cursors.SizeWE,
                _ => System.Windows.Input.Cursors.Arrow
            };
        }

        public void ShowSelection()
        {
            IsSelected = true;
            SelectionBorder.Visibility = Visibility.Visible;

            // 更新選擇框大小
            SelectionBorder.Width = DeviceBorder.Width + 10;
            SelectionBorder.Height = DeviceBorder.Height + 10;

            // 顯示並定位所有控制點
            PositionResizeHandles();
            foreach (var handle in ResizeHandles)
            {
                handle.Visibility = Visibility.Visible;
            }
        }

        public void HideSelection()
        {
            IsSelected = false;
            SelectionBorder.Visibility = Visibility.Collapsed;
            foreach (var handle in ResizeHandles)
            {
                handle.Visibility = Visibility.Collapsed;
            }
        }

        private void PositionResizeHandles()
        {
            double w = DeviceBorder.Width;
            double h = DeviceBorder.Height;
            double hw = 4; // handle width / 2

            // 定位8個控制點
            if (ResizeHandles.Count >= 8)
            {
                // 角落
                SetHandlePosition(ResizeHandles[0], -hw - 5, -hw - 5); // TopLeft
                SetHandlePosition(ResizeHandles[1], w - hw + 5, -hw - 5); // TopRight
                SetHandlePosition(ResizeHandles[2], -hw - 5, h - hw + 5); // BottomLeft
                SetHandlePosition(ResizeHandles[3], w - hw + 5, h - hw + 5); // BottomRight

                // 邊緣中點
                SetHandlePosition(ResizeHandles[4], w / 2 - hw, -hw - 5); // Top
                SetHandlePosition(ResizeHandles[5], w / 2 - hw, h - hw + 5); // Bottom
                SetHandlePosition(ResizeHandles[6], -hw - 5, h / 2 - hw); // Left
                SetHandlePosition(ResizeHandles[7], w - hw + 5, h / 2 - hw); // Right
            }
        }

        private void SetHandlePosition(Ellipse handle, double left, double top)
        {
            handle.Margin = new Thickness(left, top, 0, 0);
            handle.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            handle.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        }

        public void UpdateSize(double width, double height)
        {
            // 限制最小尺寸
            width = Math.Max(20, width);
            height = Math.Max(20, height);

            Device.Width = width;
            Device.Height = height;
            DeviceBorder.Width = width;
            DeviceBorder.Height = height;

            if (IsSelected)
            {
                SelectionBorder.Width = width + 10;
                SelectionBorder.Height = height + 10;
                PositionResizeHandles();
            }
        }

        public ResizeHandle GetHandleAt(System.Windows.Point point)
        {
            for (int i = 0; i < ResizeHandles.Count; i++)
            {
                var handle = ResizeHandles[i];
                if (handle.Visibility == Visibility.Visible)
                {
                    var handleBounds = new Rect(
                        handle.Margin.Left - 2,
                        handle.Margin.Top - 2,
                        handle.Width + 4,
                        handle.Height + 4
                    );

                    if (handleBounds.Contains(point))
                    {
                        return (ResizeHandle)(i + 1);
                    }
                }
            }
            return ResizeHandle.None;
        }
    }

    // LayerItem 類別
    public class LayerItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; set; } = string.Empty;
        public UIElement? Element { get; set; }

        private Visibility _visibility = Visibility.Visible;
        public Visibility Visibility
        {
            get => _visibility;
            set
            {
                if (_visibility != value)
                {
                    _visibility = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visibility)));
                    if (Element != null)
                    {
                        Element.Visibility = value;
                    }
                }
            }
        }
    }

    // MapConfiguration 類別
    public class MapConfiguration
    {
        public string MapImagePath { get; set; } = string.Empty;
        [XmlArray("Devices")]
        [XmlArrayItem("Device")]
        public List<MapDevice> Devices { get; set; } = new List<MapDevice>();
    }

    // MapEditorWindow 類別
    public partial class MapEditorWindow : Window
    {
        private List<MapDevice> devices = new List<MapDevice>();
        private List<LayerItem> layers = new List<LayerItem>();
        private bool isEditMode = false;
        private bool isDragging = false;
        private bool isResizing = false;
        private bool isDraggingMap = false;
        private System.Windows.Point dragStartPoint;
        private System.Windows.Point resizeStartPoint;
        private DeviceControl? draggedControl;
        private DeviceControl? selectedControl;
        private ResizeHandle activeResizeHandle = ResizeHandle.None;
        private double initialWidth;
        private double initialHeight;
        private double currentZoom = 1.0;
        private const double ZOOM_STEP = 0.1;
        private const double MAX_ZOOM = 4.9;
        private const double MIN_ZOOM = 0.2;
        private DispatcherTimer? _refreshTimer;

        private string mapDataFolder = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "MapData");
        private string configFilePath => System.IO.Path.Combine(mapDataFolder, "config.xml");

        public MapEditorWindow()
        {
            InitializeComponent();
            InitializeRealDeviceList(); // 改用真實設備
            UpdateButtonStates();
            EnsureMapImageExists();

            if (!Directory.Exists(mapDataFolder))
            {
                Directory.CreateDirectory(mapDataFolder);
            }

            LayersList.ItemsSource = layers;
            UpdateLayersList();

            // 初始化自動刷新計時器
            InitializeRefreshTimer();
        }

        private void InitializeRefreshTimer()
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3) // 每3秒刷新一次
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            RefreshDeviceList();
        }

        private void InitializeRealDeviceList()
        {
            devices.Clear();

            // 從 DahuaSDK 取得所有已加入的設備
            var realDevices = DahuaSDK.GetAllDevices();

            if (realDevices.Count > 0)
            {
                foreach (var device in realDevices)
                {
                    devices.Add(new MapDevice
                    {
                        Name = device.Name,
                        IP = device.IpAddress,
                        Port = device.Port,
                        IsOnline = device.IsOnline,
                        DeviceId = device.Id,
                        ChannelCount = device.ChannelCount,
                        DeviceType = GetDeviceTypeName(device.ChannelCount),
                        Width = 60,  // 稍微加大以顯示圖標和文字
                        Height = 50
                    });
                }

                StatusText.Text = $"已載入 {realDevices.Count} 個設備";
            }
            else
            {
                // 顯示提示訊息
                devices.Add(new MapDevice
                {
                    Name = "尚未加入設備",
                    IP = "0.0.0.0",
                    Port = 0,
                    IsOnline = false,
                    Width = 80,
                    Height = 50
                });

                StatusText.Text = "請先在設備管理中加入設備";
            }

            AvailableDevicesList.ItemsSource = devices;
        }

        private string GetDeviceTypeName(int channelCount)
        {
            return channelCount switch
            {
                <= 1 => "攝影機",
                <= 4 => "4路NVR",
                <= 8 => "8路NVR",
                <= 16 => "16路NVR",
                _ => "大型NVR"
            };
        }

        private void RefreshDeviceList()
        {
            // 保存當前選中的設備
            var selectedDevice = AvailableDevicesList.SelectedItem as MapDevice;

            // 取得最新設備清單
            var realDevices = DahuaSDK.GetAllDevices();

            devices.Clear();

            if (realDevices.Count > 0)
            {
                foreach (var device in realDevices)
                {
                    devices.Add(new MapDevice
                    {
                        Name = device.Name,
                        IP = device.IpAddress,
                        Port = device.Port,
                        IsOnline = device.IsOnline,
                        DeviceId = device.Id,
                        ChannelCount = device.ChannelCount,
                        DeviceType = GetDeviceTypeName(device.ChannelCount),
                        Width = 60,
                        Height = 50
                    });
                }
            }

            // 更新清單顯示
            AvailableDevicesList.ItemsSource = null;
            AvailableDevicesList.ItemsSource = devices;

            // 恢復選中狀態
            if (selectedDevice != null)
            {
                var newSelection = devices.FirstOrDefault(d =>
                    d.DeviceId == selectedDevice.DeviceId);
                if (newSelection != null)
                {
                    AvailableDevicesList.SelectedItem = newSelection;
                }
            }

            // 更新地圖上已放置設備的狀態
            UpdatePlacedDevicesStatus();
        }

        private void UpdatePlacedDevicesStatus()
        {
            foreach (var control in MapCanvas.Children.OfType<DeviceControl>())
            {
                if (control.Device != null)
                {
                    // 找到對應的真實設備
                    var realDevice = devices.FirstOrDefault(d =>
                        d.DeviceId == control.Device.DeviceId);

                    if (realDevice != null)
                    {
                        control.UpdateDeviceStatus(realDevice.IsOnline);
                    }
                }
            }
        }

        private void UpdateDeviceVisual(DeviceControl control)
        {
            if (control.DeviceBorder != null)
            {
                // 根據線上狀態改變顏色
                control.DeviceBorder.Background = control.Device.IsOnline
                    ? new SolidColorBrush(Colors.LightGreen)
                    : new SolidColorBrush(Colors.LightCoral);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
            base.OnClosed(e);
        }

        private void InitializeDeviceList()
        {
            // 不再使用假資料，改為從 DahuaSDK 取得真實設備
            devices.Clear();

            // 從 SDK 取得所有已加入的設備
            var realDevices = DahuaSDK.GetAllDevices();

            foreach (var device in realDevices)
            {
                devices.Add(new MapDevice
                {
                    Name = device.Name,
                    IP = $"{device.IpAddress}:{device.Port}", // 包含 Port 資訊
                    IsOnline = device.IsOnline,
                    Width = 40,
                    Height = 40
                });
            }

            // 如果沒有設備，顯示提示
            if (devices.Count == 0)
            {
                devices.Add(new MapDevice
                {
                    Name = "尚未加入設備",
                    IP = "請先在設備管理中加入",
                    IsOnline = false,
                    Width = 40,
                    Height = 40
                });
            }

            AvailableDevicesList.ItemsSource = devices;
        }

        private void LoadMapButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string originalFilePath = openFileDialog.FileName;
                    string fileName = System.IO.Path.GetFileName(originalFilePath);
                    string newFilePath = System.IO.Path.Combine(mapDataFolder, fileName);

                    File.Copy(originalFilePath, newFilePath, true);

                    MapImage.Source = new BitmapImage(new Uri(newFilePath));
                    MapInfoText.Text = $"地圖: {fileName}";
                    UpdateButtonStates();
                    UpdateLayersList();
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
            var existingDevices = MapCanvas.Children.OfType<DeviceControl>().ToList();
            foreach (var dev in existingDevices)
            {
                MapCanvas.Children.Remove(dev);
            }

            MapImage.Source = null;
            devices.ForEach(d => { d.X = 0; d.Y = 0; d.Width = 40; d.Height = 40; });

            MapInfoText.Text = "未載入地圖";
            DeviceCountText.Text = "設備數量: 0";
            UpdateButtonStates();
            UpdateLayersList();
        }

        private void EnsureMapImageExists()
        {
            if (!MapCanvas.Children.Contains(MapImage))
            {
                MapImage = new System.Windows.Controls.Image
                {
                    Name = "MapImage",
                    Stretch = Stretch.None
                };
                Canvas.SetLeft(MapImage, 0);
                Canvas.SetTop(MapImage, 0);
                MapCanvas.Children.Insert(0, MapImage);
            }
        }

        // 將 AddDeviceToCanvas 的參數型別從 Device 改為 MapDevice
        private void AddDeviceToCanvas(MapDevice device, double x, double y)
        {
            device.X = x;
            device.Y = y;

            var deviceControl = new DeviceControl(device)
            {
                Tag = device
            };

            Canvas.SetLeft(deviceControl, x);
            Canvas.SetTop(deviceControl, y);
            MapCanvas.Children.Add(deviceControl);

            DeviceCountText.Text = $"設備數量: {MapCanvas.Children.OfType<DeviceControl>().Count()}";
            UpdateLayersList();
        }

        // 修正所有呼叫 AddDeviceToCanvas 的地方，將 Device 型別改為 MapDevice
        private void AddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableDevicesList.SelectedItem is MapDevice selectedDevice)
            {
                // 檢查是否為提示訊息
                if (selectedDevice.DeviceId == null)
                {
                    System.Windows.MessageBox.Show("請先在設備管理中加入真實設備。",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 建立新的設備副本
                var newDevice = new MapDevice
                {
                    Name = selectedDevice.Name,
                    IP = selectedDevice.IP,
                    Port = selectedDevice.Port,
                    IsOnline = selectedDevice.IsOnline,
                    DeviceId = selectedDevice.DeviceId,
                    ChannelCount = selectedDevice.ChannelCount,
                    DeviceType = selectedDevice.DeviceType,
                    Width = selectedDevice.Width,
                    Height = selectedDevice.Height
                };

                AddDeviceToCanvas(newDevice, 50, 50);
                UpdateButtonStates();
            }
            else
            {
                System.Windows.MessageBox.Show("請先選擇一個設備。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditModeButton_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = !isEditMode;
            EditModeText.Text = isEditMode ? "編輯模式" : "檢視模式";

            if (!isEditMode && selectedControl != null)
            {
                selectedControl.HideSelection();
                selectedControl = null;
            }

            UpdateButtonStates();
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedControl != null)
            {
                MapCanvas.Children.Remove(selectedControl);
                selectedControl = null;
                DeviceCountText.Text = $"設備數量: {MapCanvas.Children.OfType<DeviceControl>().Count()}";
                UpdateButtonStates();
                UpdateLayersList();
            }
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

        private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(MapCanvas);
            var hitElement = MapCanvas.InputHitTest(point) as FrameworkElement;

            DeviceControl? clickedControl = null;
            FrameworkElement? current = hitElement;

            while (current != null && current != MapCanvas)
            {
                if (current is DeviceControl dc)
                {
                    clickedControl = dc;
                    break;
                }
                current = current.Parent as FrameworkElement;
            }

            if (clickedControl != null && isEditMode)
            {
                var localPoint = e.GetPosition(clickedControl);
                var handle = clickedControl.GetHandleAt(localPoint);

                if (handle != ResizeHandle.None)
                {
                    isResizing = true;
                    activeResizeHandle = handle;
                    resizeStartPoint = point;
                    initialWidth = clickedControl.Device.Width;
                    initialHeight = clickedControl.Device.Height;
                    MapCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }

                if (selectedControl != null && selectedControl != clickedControl)
                {
                    selectedControl.HideSelection();
                }

                selectedControl = clickedControl;
                selectedControl.ShowSelection();

                isDragging = true;
                draggedControl = clickedControl;
                dragStartPoint = point;
                MapCanvas.CaptureMouse();
                e.Handled = true;
            }
            else
            {
                if (selectedControl != null && isEditMode)
                {
                    selectedControl.HideSelection();
                    selectedControl = null;
                }

                if (MapImage.Source != null && !isEditMode)
                {
                    isDraggingMap = true;
                    dragStartPoint = e.GetPosition(MapCanvas);
                    MapCanvas.CaptureMouse();
                    MapCanvas.Cursor = System.Windows.Input.Cursors.Hand;
                    e.Handled = true;
                }
            }

            UpdateButtonStates();
        }

        private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var currentPoint = e.GetPosition(MapCanvas);

            if (isResizing && selectedControl != null)
            {
                var deltaX = currentPoint.X - resizeStartPoint.X;
                var deltaY = currentPoint.Y - resizeStartPoint.Y;

                double newWidth = initialWidth;
                double newHeight = initialHeight;

                switch (activeResizeHandle)
                {
                    case ResizeHandle.TopLeft:
                        newWidth = initialWidth - deltaX;
                        newHeight = initialHeight - deltaY;
                        break;
                    case ResizeHandle.TopRight:
                        newWidth = initialWidth + deltaX;
                        newHeight = initialHeight - deltaY;
                        break;
                    case ResizeHandle.BottomLeft:
                        newWidth = initialWidth - deltaX;
                        newHeight = initialHeight + deltaY;
                        break;
                    case ResizeHandle.BottomRight:
                        newWidth = initialWidth + deltaX;
                        newHeight = initialHeight + deltaY;
                        break;
                    case ResizeHandle.Top:
                        newHeight = initialHeight - deltaY;
                        break;
                    case ResizeHandle.Bottom:
                        newHeight = initialHeight + deltaY;
                        break;
                    case ResizeHandle.Left:
                        newWidth = initialWidth - deltaX;
                        break;
                    case ResizeHandle.Right:
                        newWidth = initialWidth + deltaX;
                        break;
                }

                selectedControl.UpdateSize(newWidth, newHeight);
                MousePositionText.Text = $"大小: {newWidth:0} x {newHeight:0}";
            }
            else if (isDragging && draggedControl != null)
            {
                var offset = currentPoint - dragStartPoint;
                dragStartPoint = currentPoint;

                double newX = Canvas.GetLeft(draggedControl) + offset.X;
                double newY = Canvas.GetTop(draggedControl) + offset.Y;

                Canvas.SetLeft(draggedControl, newX);
                Canvas.SetTop(draggedControl, newY);

                if (draggedControl.Device != null)
                {
                    draggedControl.Device.X = newX;
                    draggedControl.Device.Y = newY;
                }

                MousePositionText.Text = $"座標: {newX:0}, {newY:0}";
            }
            else if (isDraggingMap)
            {
                var offset = currentPoint - dragStartPoint;
                dragStartPoint = currentPoint;

                double newMapLeft = Canvas.GetLeft(MapImage) + offset.X;
                double newMapTop = Canvas.GetTop(MapImage) + offset.Y;
                Canvas.SetLeft(MapImage, newMapLeft);
                Canvas.SetTop(MapImage, newMapTop);

                foreach (var control in MapCanvas.Children.OfType<DeviceControl>())
                {
                    double devLeft = Canvas.GetLeft(control) + offset.X;
                    double devTop = Canvas.GetTop(control) + offset.Y;
                    Canvas.SetLeft(control, devLeft);
                    Canvas.SetTop(control, devTop);

                    if (control.Device != null)
                    {
                        control.Device.X = devLeft;
                        control.Device.Y = devTop;
                    }
                }

                MousePositionText.Text = $"底圖座標: {newMapLeft:0}, {newMapTop:0}";
            }
            else
            {
                MousePositionText.Text = $"座標: {currentPoint.X:0}, {currentPoint.Y:0}";
            }
        }

        private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isResizing)
            {
                isResizing = false;
                activeResizeHandle = ResizeHandle.None;
            }

            if (isDragging)
            {
                isDragging = false;
                draggedControl = null;
            }

            if (isDraggingMap)
            {
                isDraggingMap = false;
                MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
            }

            MapCanvas.ReleaseMouseCapture();
        }

        private void MapCanvas_MouseRightButtonDown(object sender, System.Windows.Input.MouseEventArgs e)
        {
            e.Handled = true;
        }

        private void SaveMapButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(mapDataFolder))
                {
                    Directory.CreateDirectory(mapDataFolder);
                }

                string currentMapPath = MapImage.Source is BitmapImage bitmap && bitmap.UriSource != null
                    ? bitmap.UriSource.LocalPath
                    : string.Empty;

                var placedDevices = new List<MapDevice>();
                foreach (var control in MapCanvas.Children.OfType<DeviceControl>())
                {
                    if (control.Device != null)
                    {
                        placedDevices.Add(control.Device);
                    }
                }

                var config = new MapConfiguration
                {
                    MapImagePath = currentMapPath,
                    Devices = placedDevices
                };

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
                    var config = serializer.Deserialize(reader) as MapConfiguration
                        ?? throw new InvalidOperationException("反序列化 MapConfiguration 失敗。");

                    var existingDevices = MapCanvas.Children.OfType<DeviceControl>().ToList();
                    foreach (var dev in existingDevices)
                    {
                        MapCanvas.Children.Remove(dev);
                    }

                    if (!string.IsNullOrEmpty(config.MapImagePath) && File.Exists(config.MapImagePath))
                    {
                        MapImage.Source = new BitmapImage(new Uri(config.MapImagePath));
                        MapInfoText.Text = $"地圖: {System.IO.Path.GetFileName(config.MapImagePath)}";
                    }
                    else
                    {
                        MapImage.Source = null;
                        MapInfoText.Text = "未載入地圖";
                        System.Windows.MessageBox.Show("載入的圖片路徑無效或檔案不存在。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    // 原本錯誤的程式碼：
                    // devices = config.Devices ?? new List<Device>();

                    // 修正方式：將 new List<Device>() 改為 new List<MapDevice>()
                    devices = config.Devices ?? new List<MapDevice>();
                    foreach (var device in devices)
                    {
                        if (device.X != 0 || device.Y != 0)
                        {
                            var deviceControl = new DeviceControl(device)
                            {
                                Tag = device
                            };
                            Canvas.SetLeft(deviceControl, device.X);
                            Canvas.SetTop(deviceControl, device.Y);
                            MapCanvas.Children.Add(deviceControl);
                        }
                    }

                    AvailableDevicesList.ItemsSource = devices;
                    DeviceCountText.Text = $"設備數量: {MapCanvas.Children.OfType<DeviceControl>().Count()}";
                    UpdateButtonStates();
                    UpdateLayersList();
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

        // 修正 AvailableDevicesList_MouseDoubleClick 事件
        private void AvailableDevicesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (isEditMode && AvailableDevicesList.SelectedItem is MapDevice selectedDevice)
            {
                var newDevice = new MapDevice
                {
                    Name = selectedDevice.Name,
                    IP = selectedDevice.IP,
                    Port = selectedDevice.Port,
                    IsOnline = selectedDevice.IsOnline,
                    DeviceId = selectedDevice.DeviceId,
                    ChannelCount = selectedDevice.ChannelCount,
                    DeviceType = selectedDevice.DeviceType,
                    Width = selectedDevice.Width,
                    Height = selectedDevice.Height
                };
                AddDeviceToCanvas(newDevice, 50, 50);
                UpdateButtonStates();
            }
        }

        // 修正 MapCanvas_Drop 事件
        private void MapCanvas_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(MapDevice)))
            {
                var device = e.Data.GetData(typeof(MapDevice)) as MapDevice;
                if (device != null)
                {
                    var point = e.GetPosition(MapCanvas);
                    AddDeviceToCanvas(device, point.X, point.Y);
                }
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
            bool hasSelection = selectedControl != null;

            ClearMapButton.IsEnabled = hasMap;
            AddDeviceButton.IsEnabled = hasMap && isEditMode;
            EditModeButton.IsEnabled = hasMap;
            DeleteSelectedButton.IsEnabled = hasMap && isEditMode && hasSelection;
            SaveMapButton.IsEnabled = hasMap;
        }

        private void UpdateLayersList()
        {
            layers.Clear();

            if (MapImage.Source != null)
            {
                layers.Add(new LayerItem
                {
                    Name = "底圖層",
                    Element = MapImage,
                    Visibility = MapImage.Visibility
                });
            }

            int deviceIndex = 1;
            foreach (var control in MapCanvas.Children.OfType<DeviceControl>())
            {
                if (control.Device != null)
                {
                    layers.Add(new LayerItem
                    {
                        Name = $"設備層 {deviceIndex}: {control.Device.Name}",
                        Element = control,
                        Visibility = control.Visibility
                    });
                    deviceIndex++;
                }
            }

            LayersList.ItemsSource = null;
            LayersList.ItemsSource = layers;
        }
    }

    // Converter for CheckBox to Visibility
    [ValueConversion(typeof(Visibility), typeof(bool))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isChecked)
            {
                return isChecked ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
    }
}