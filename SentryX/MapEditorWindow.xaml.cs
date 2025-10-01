using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
// === 解決命名空間衝突的 using alias ===
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using VerticalAlignment = System.Windows.VerticalAlignment;


namespace SentryX
{
    // MapDevice 類別
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

        public string? DeviceId { get; set; }
        public int ChannelCount { get; set; } = 0;
        public string DeviceType { get; set; } = "";

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

    // ResizeHandle 枚舉
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

        private bool isUpdatingProperties = false;

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
            var bgColor = Device.IsOnline ? Colors.LightGreen : Colors.LightCoral;

            DeviceBorder = new Border
            {
                Width = Device.Width,
                Height = Device.Height,
                Background = new SolidColorBrush(bgColor),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5)
            };

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconText = new TextBlock
            {
                Text = Device.TypeIcon,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 14
            };

            var nameText = new TextBlock
            {
                Text = Device.Name,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 10,
                MaxWidth = Device.Width - 4
            };

            stackPanel.Children.Add(iconText);
            stackPanel.Children.Add(nameText);

            DeviceBorder.Child = stackPanel;
            this.Children.Add(DeviceBorder);

            SelectionBorder = new Border
            {
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(20, 30, 144, 255)),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(-5)
            };
            this.Children.Add(SelectionBorder);

            CreateResizeHandles();
        }

        private void CreateResizeHandles()
        {
            const double handleSize = 8;
            var handleBrush = Brushes.White;
            var handleStroke = Brushes.DodgerBlue;

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

        private Cursor GetCursorForHandle(ResizeHandle handle)
        {
            return handle switch
            {
                ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
                ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
                ResizeHandle.Top or ResizeHandle.Bottom => Cursors.SizeNS,
                ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
                _ => Cursors.Arrow
            };
        }

        public void ShowSelection()
        {
            IsSelected = true;
            SelectionBorder.Visibility = Visibility.Visible;
            SelectionBorder.Width = DeviceBorder.Width + 10;
            SelectionBorder.Height = DeviceBorder.Height + 10;
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
            double hw = 4;

            if (ResizeHandles.Count >= 8)
            {
                SetHandlePosition(ResizeHandles[0], -hw - 5, -hw - 5);
                SetHandlePosition(ResizeHandles[1], w - hw + 5, -hw - 5);
                SetHandlePosition(ResizeHandles[2], -hw - 5, h - hw + 5);
                SetHandlePosition(ResizeHandles[3], w - hw + 5, h - hw + 5);
                SetHandlePosition(ResizeHandles[4], w / 2 - hw, -hw - 5);
                SetHandlePosition(ResizeHandles[5], w / 2 - hw, h - hw + 5);
                SetHandlePosition(ResizeHandles[6], -hw - 5, h / 2 - hw);
                SetHandlePosition(ResizeHandles[7], w - hw + 5, h / 2 - hw);
            }
        }

        private void SetHandlePosition(Ellipse handle, double left, double top)
        {
            handle.Margin = new Thickness(left, top, 0, 0);
            handle.HorizontalAlignment = HorizontalAlignment.Left;
            handle.VerticalAlignment = VerticalAlignment.Top;
        }

        public void UpdateSize(double width, double height)
        {
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

        public ResizeHandle GetHandleAt(Point point)
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
        private Point dragStartPoint;
        private Point resizeStartPoint;
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

        private string mapDataFolder = Path.Combine(Directory.GetCurrentDirectory(), "MapData");
        private string configFilePath => Path.Combine(mapDataFolder, "config.xml");

        private bool isUpdatingProperties = false;
        public MapEditorWindow()
        {
            InitializeComponent();
            InitializeRealDeviceList();
            UpdateButtonStates();
            EnsureMapImageExists();

            if (!Directory.Exists(mapDataFolder))
            {
                Directory.CreateDirectory(mapDataFolder);
            }

            LayersList.ItemsSource = layers;
            UpdateLayersList();

            InitializeRefreshTimer();
        }

        private void InitializeRefreshTimer()
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            RefreshDeviceList();
        }

        // === 核心方法：載入設備列表結構 ===
        private void LoadDeviceListStructure()
        {
            devices.Clear();
            var realDevices = DahuaSDK.GetAllDevices();

            if (realDevices.Count > 0)
            {
                foreach (var device in realDevices)
                {
                    // 1. 添加設備本體
                    devices.Add(new MapDevice
                    {
                        Name = $"{device.GetDeviceIcon()} {device.Name} ({device.IpAddress}:{device.Port})",
                        IP = device.IpAddress,
                        Port = device.Port,
                        IsOnline = device.IsOnline,
                        DeviceId = device.Id,
                        ChannelCount = device.ChannelCount,
                        DeviceType = "Device",
                        Width = 60,
                        Height = 50
                    });

                    // 2. 添加視頻通道
                    if (device.ChannelCount > 0)
                    {
                        devices.Add(new MapDevice
                        {
                            Name = "  📹 視頻通道",
                            IP = "",
                            Port = 0,
                            IsOnline = false,
                            DeviceId = $"{device.Id}_ChannelHeader",
                            DeviceType = "Header",
                            Width = 0,
                            Height = 0
                        });

                        for (int i = 0; i < device.ChannelCount; i++)
                        {
                            var channelName = i < device.ChannelNames.Count
                                ? device.ChannelNames[i]
                                : $"通道 {i + 1}";

                            devices.Add(new MapDevice
                            {
                                Name = $"    └─ {channelName} (CH{i})",
                                IP = device.IpAddress,
                                Port = device.Port,
                                IsOnline = device.IsOnline,
                                DeviceId = $"{device.Id}_CH{i}",
                                ChannelCount = 1,
                                DeviceType = "Channel",
                                Width = 50,
                                Height = 40
                            });
                        }
                    }

                    // 3. 添加警報輸入
                    if (device.AlarmInPortCount > 0)
                    {
                        devices.Add(new MapDevice
                        {
                            Name = $"  🔔 警報輸入 ({device.AlarmInPortCount})",
                            IP = "",
                            Port = 0,
                            IsOnline = false,
                            DeviceId = $"{device.Id}_AlarmInHeader",
                            DeviceType = "Header",
                            Width = 0,
                            Height = 0
                        });

                        for (int i = 0; i < device.AlarmInPortCount; i++)
                        {
                            var alarmName = i < device.AlarmInputNames.Count
                                ? device.AlarmInputNames[i]
                                : $"警報輸入 {i + 1}";

                            var isTriggered = device.AlarmInputStates.ContainsKey(i) && device.AlarmInputStates[i];
                            var statusIcon = isTriggered ? "🔴" : "⚪";

                            devices.Add(new MapDevice
                            {
                                Name = $"    └─ {statusIcon} {alarmName} (IN{i})",
                                IP = device.IpAddress,
                                Port = device.Port,
                                IsOnline = device.IsOnline,
                                DeviceId = $"{device.Id}_IN{i}",
                                DeviceType = "AlarmIn",
                                Width = 45,
                                Height = 35
                            });
                        }
                    }

                    // 4. 添加警報輸出
                    if (device.AlarmOutPortCount > 0)
                    {
                        devices.Add(new MapDevice
                        {
                            Name = $"  🚨 警報輸出 ({device.AlarmOutPortCount})",
                            IP = "",
                            Port = 0,
                            IsOnline = false,
                            DeviceId = $"{device.Id}_AlarmOutHeader",
                            DeviceType = "Header",
                            Width = 0,
                            Height = 0
                        });

                        for (int i = 0; i < device.AlarmOutPortCount; i++)
                        {
                            var alarmName = i < device.AlarmOutputNames.Count
                                ? device.AlarmOutputNames[i]
                                : $"警報輸出 {i + 1}";

                            var isActive = device.AlarmOutputStates.ContainsKey(i) && device.AlarmOutputStates[i];
                            var statusIcon = isActive ? "🟢" : "⚫";

                            devices.Add(new MapDevice
                            {
                                Name = $"    └─ {statusIcon} {alarmName} (OUT{i})",
                                IP = device.IpAddress,
                                Port = device.Port,
                                IsOnline = device.IsOnline,
                                DeviceId = $"{device.Id}_OUT{i}",
                                DeviceType = "AlarmOut",
                                Width = 45,
                                Height = 35
                            });
                        }
                    }

                    // 5. 添加硬碟資訊
                    if (device.DiskCount > 0)
                    {
                        devices.Add(new MapDevice
                        {
                            Name = $"  💾 硬碟 ({device.DiskCount} 個)",
                            IP = "",
                            Port = 0,
                            IsOnline = false,
                            DeviceId = $"{device.Id}_DiskHeader",
                            DeviceType = "Header",
                            Width = 0,
                            Height = 0
                        });
                    }

                    // 6. 添加空行分隔
                    devices.Add(new MapDevice
                    {
                        Name = "",
                        IP = "",
                        Port = 0,
                        IsOnline = false,
                        DeviceId = $"{device.Id}_Separator",
                        DeviceType = "Separator",
                        Width = 0,
                        Height = 0
                    });
                }
            }
            else
            {
                devices.Add(new MapDevice
                {
                    Name = "尚未添加任何攝影機設備",
                    IP = "0.0.0.0",
                    Port = 0,
                    IsOnline = false,
                    DeviceType = "Empty",
                    Width = 80,
                    Height = 50
                });

                devices.Add(new MapDevice
                {
                    Name = "點擊「設備管理」開始添加",
                    IP = "0.0.0.0",
                    Port = 0,
                    IsOnline = false,
                    DeviceType = "Empty",
                    Width = 80,
                    Height = 50
                });
            }
        }

        private void InitializeRealDeviceList()
        {
            LoadDeviceListStructure();
            AvailableDevicesList.ItemsSource = devices;

            var realDevices = DahuaSDK.GetAllDevices();
            StatusText.Text = realDevices.Count > 0
                ? $"已載入 {realDevices.Count} 個設備"
                : "請先在設備管理中加入設備";
        }

        private void RefreshDeviceList()
        {
            var selectedDevice = AvailableDevicesList.SelectedItem as MapDevice;

            LoadDeviceListStructure();

            AvailableDevicesList.ItemsSource = null;
            AvailableDevicesList.ItemsSource = devices;

            if (selectedDevice != null)
            {
                var newSelection = devices.FirstOrDefault(d =>
                    d.DeviceId == selectedDevice.DeviceId);
                if (newSelection != null)
                {
                    AvailableDevicesList.SelectedItem = newSelection;
                }
            }

            UpdatePlacedDevicesStatus();
        }

        private void UpdatePlacedDevicesStatus()
        {
            foreach (var control in MapCanvas.Children.OfType<DeviceControl>())
            {
                if (control.Device?.DeviceId != null)
                {
                    var realDevice = devices.FirstOrDefault(d =>
                        d.DeviceId == control.Device.DeviceId);

                    if (realDevice != null)
                    {
                        control.UpdateDeviceStatus(realDevice.IsOnline);
                    }
                }
            }
        }

        private void EnsureMapImageExists()
        {
            if (!MapCanvas.Children.Contains(MapImage))
            {
                MapImage = new Image
                {
                    Name = "MapImage",
                    Stretch = Stretch.None
                };
                Canvas.SetLeft(MapImage, 0);
                Canvas.SetTop(MapImage, 0);
                MapCanvas.Children.Insert(0, MapImage);
            }
        }

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

        private void AddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableDevicesList.SelectedItem is MapDevice selectedDevice)
            {
                if (selectedDevice.DeviceType == "Header" ||
                    selectedDevice.DeviceType == "Separator" ||
                    selectedDevice.DeviceType == "Empty" ||
                    string.IsNullOrEmpty(selectedDevice.IP) ||
                    selectedDevice.IP == "0.0.0.0")
                {
                    MessageBox.Show("請選擇具體的設備、通道或警報項目。",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var newDevice = new MapDevice
                {
                    Name = selectedDevice.Name
                        .Replace("└─", "")
                        .Replace("🔴", "")
                        .Replace("⚪", "")
                        .Replace("🟢", "")
                        .Replace("⚫", "")
                        .Trim(),
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
                MessageBox.Show("請先選擇一個設備、通道或警報項目。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LoadMapButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string originalFilePath = openFileDialog.FileName;
                    string fileName = Path.GetFileName(originalFilePath);
                    string newFilePath = Path.Combine(mapDataFolder, fileName);

                    File.Copy(originalFilePath, newFilePath, true);

                    MapImage.Source = new BitmapImage(new Uri(newFilePath));
                    MapInfoText.Text = $"地圖: {fileName}";
                    UpdateButtonStates();
                    UpdateLayersList();
                    StatusText.Text = "地圖已載入並複製到 MapData 資料夾";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"載入地圖失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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

            MapInfoText.Text = "未載入地圖";
            DeviceCountText.Text = "設備數量: 0";
            UpdateButtonStates();
            UpdateLayersList();
        }

        private void EditModeButton_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = !isEditMode;
            EditModeText.Text = isEditMode ? "編輯模式" : "檢視模式";

            if (!isEditMode && selectedControl != null)
            {
                selectedControl.HideSelection();
                selectedControl = null;
                UpdateDevicePropertiesPanel(); // 切換模式時更新面板
            }

            UpdateButtonStates();
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedControl != null)
            {
                MapCanvas.Children.Remove(selectedControl);
                selectedControl = null;
                UpdateDevicePropertiesPanel(); // 刪除後更新面板
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

            if (clickedControl != null)
            {
                // === 檢視模式：點擊播放視頻 ===
                if (!isEditMode)
                {
                    OpenVideoPlayer(clickedControl.Device);
                    e.Handled = true;
                    return;
                }

                // === 編輯模式：原有的拖拽和調整大小邏輯 ===
                if (isEditMode)
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
                    UpdateDevicePropertiesPanel(); // 選中設備時更新面板

                    isDragging = true;
                    draggedControl = clickedControl;
                    dragStartPoint = point;
                    MapCanvas.CaptureMouse();
                    e.Handled = true;
                }
            }
            else
            {
                if (selectedControl != null && isEditMode)
                {
                    selectedControl.HideSelection();
                    selectedControl = null;
                    UpdateDevicePropertiesPanel(); // 取消選擇時更新面板
                }

                if (MapImage.Source != null && !isEditMode)
                {
                    isDraggingMap = true;
                    dragStartPoint = e.GetPosition(MapCanvas);
                    MapCanvas.CaptureMouse();
                    MapCanvas.Cursor = Cursors.Hand;
                    e.Handled = true;
                }
            }

            UpdateButtonStates();
        }

        // === 新增方法：開啟視頻播放器 ===
        private void OpenVideoPlayer(MapDevice mapDevice)
        {
            try
            {
                if (mapDevice == null || string.IsNullOrEmpty(mapDevice.DeviceId))
                {
                    MessageBox.Show("無效的設備資訊", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 解析 DeviceId 獲取設備和通道資訊
                string deviceId = mapDevice.DeviceId;
                int channel = 0;

                // 如果是通道項目，提取通道號
                if (mapDevice.DeviceType == "Channel" && deviceId.Contains("_CH"))
                {
                    var parts = deviceId.Split(new[] { "_CH" }, StringSplitOptions.None);
                    if (parts.Length == 2 && int.TryParse(parts[1], out int ch))
                    {
                        deviceId = parts[0]; // 設備 ID
                        channel = ch;        // 通道號
                    }
                }

                // 獲取設備資訊
                var device = DahuaSDK.GetDevice(deviceId);
                if (device == null)
                {
                    MessageBox.Show("找不到對應的設備", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!device.IsOnline)
                {
                    MessageBox.Show($"設備 {device.Name} 目前離線", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 獲取通道名稱
                string channelName = channel < device.ChannelNames.Count
                    ? device.ChannelNames[channel]
                    : $"通道 {channel + 1}";

                // 創建並顯示播放視窗
                var playerWindow = new VideoPlayerWindow();
                playerWindow.StartPlay(deviceId, channel, device.Name, channelName);
                playerWindow.Show();

                StatusText.Text = $"已開啟 {device.Name} - {channelName} 的視頻播放";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"開啟視頻播放器失敗: {ex.Message}", "錯誤",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"OpenVideoPlayer 錯誤: {ex.Message}");
            }
        }

        private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
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

                // 即時更新面板顯示
                isUpdatingProperties = true;
                DeviceWidthTextBox.Text = newWidth.ToString("F0");
                DeviceHeightTextBox.Text = newHeight.ToString("F0");
                isUpdatingProperties = false;

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

                    // 即時更新面板顯示
                    isUpdatingProperties = true;
                    DeviceXTextBox.Text = newX.ToString("F0");
                    DeviceYTextBox.Text = newY.ToString("F0");
                    isUpdatingProperties = false;
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
                MapCanvas.Cursor = Cursors.Arrow;
            }

            MapCanvas.ReleaseMouseCapture();
        }

        private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
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
                MessageBox.Show($"儲存配置失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        MapInfoText.Text = $"地圖: {Path.GetFileName(config.MapImagePath)}";
                    }
                    else
                    {
                        MapImage.Source = null;
                        MapInfoText.Text = "未載入地圖";
                        MessageBox.Show("載入的圖片路徑無效或檔案不存在。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    foreach (var device in config.Devices)
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

                    DeviceCountText.Text = $"設備數量: {MapCanvas.Children.OfType<DeviceControl>().Count()}";
                    UpdateButtonStates();
                    UpdateLayersList();
                    StatusText.Text = "配置已從 MapData/config.xml 載入";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"載入配置失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "載入配置失敗";
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DeviceSearchBox.Text == "搜尋設備...")
            {
                DeviceSearchBox.Text = "";
                DeviceSearchBox.Foreground = Brushes.Black;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DeviceSearchBox.Text))
            {
                DeviceSearchBox.Text = "搜尋設備...";
                DeviceSearchBox.Foreground = Brushes.Gray;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = DeviceSearchBox.Text.ToLower();
            if (searchText == "搜尋設備...") return;

            var filteredDevices = devices
                .Where(d => d.Name.ToLower().Contains(searchText) || d.IP.ToLower().Contains(searchText))
                .ToList();

            AvailableDevicesList.ItemsSource = filteredDevices;
        }

        private void AvailableDevicesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (isEditMode && AvailableDevicesList.SelectedItem is MapDevice selectedDevice)
            {
                if (selectedDevice.DeviceType == "Header" ||
                    selectedDevice.DeviceType == "Separator" ||
                    selectedDevice.DeviceType == "Empty" ||
                    string.IsNullOrEmpty(selectedDevice.IP) ||
                    selectedDevice.IP == "0.0.0.0")
                {
                    return;
                }

                var newDevice = new MapDevice
                {
                    Name = selectedDevice.Name
                        .Replace("└─", "")
                        .Replace("🔴", "")
                        .Replace("⚪", "")
                        .Replace("🟢", "")
                        .Replace("⚫", "")
                        .Trim(),
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

        private void MapCanvas_Drop(object sender, DragEventArgs e)
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

        private void MapCanvas_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(MapDevice)))
            {
                e.Effects = DragDropEffects.None;
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

        // === 更新選中設備時調用 ===
        private void UpdateDevicePropertiesPanel()
        {
            if (selectedControl != null && selectedControl.Device != null)
            {
                isUpdatingProperties = true;

                DevicePropertiesPanel.IsEnabled = true;
                SelectedDeviceNameText.Text = selectedControl.Device.Name;
                SelectedDeviceNameText.Foreground = Brushes.Black;

                DeviceXTextBox.Text = selectedControl.Device.X.ToString("F0");
                DeviceYTextBox.Text = selectedControl.Device.Y.ToString("F0");
                DeviceWidthTextBox.Text = selectedControl.Device.Width.ToString("F0");
                DeviceHeightTextBox.Text = selectedControl.Device.Height.ToString("F0");

                isUpdatingProperties = false;
            }
            else
            {
                DevicePropertiesPanel.IsEnabled = false;
                SelectedDeviceNameText.Text = "未選擇設備";
                SelectedDeviceNameText.Foreground = Brushes.Gray;

                DeviceXTextBox.Text = "0";
                DeviceYTextBox.Text = "0";
                DeviceWidthTextBox.Text = "60";
                DeviceHeightTextBox.Text = "60";
            }
        }

        // === 座標文字框變更事件 ===
        private void DevicePositionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingProperties || selectedControl == null) return;

            if (double.TryParse(DeviceXTextBox.Text, out double x) &&
                double.TryParse(DeviceYTextBox.Text, out double y))
            {
                Canvas.SetLeft(selectedControl, x);
                Canvas.SetTop(selectedControl, y);

                selectedControl.Device.X = x;
                selectedControl.Device.Y = y;

                MousePositionText.Text = $"座標: {x:0}, {y:0}";
            }
        }

        // === 大小文字框變更事件 ===
        private void DeviceSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingProperties || selectedControl == null) return;

            if (double.TryParse(DeviceWidthTextBox.Text, out double width) &&
                double.TryParse(DeviceHeightTextBox.Text, out double height))
            {
                selectedControl.UpdateSize(width, height);
                MousePositionText.Text = $"大小: {width:0} x {height:0}";
            }
        }

        // === 重置大小按鈕 ===
        private void ResetSize_Click(object sender, RoutedEventArgs e)
        {
            if (selectedControl != null)
            {
                DeviceWidthTextBox.Text = "60";
                DeviceHeightTextBox.Text = "60";
            }
        }

        // === 對齊網格按鈕 ===
        private void AlignToGrid_Click(object sender, RoutedEventArgs e)
        {
            if (selectedControl != null)
            {
                double gridSize = 10;

                double x = Math.Round(selectedControl.Device.X / gridSize) * gridSize;
                double y = Math.Round(selectedControl.Device.Y / gridSize) * gridSize;

                DeviceXTextBox.Text = x.ToString("F0");
                DeviceYTextBox.Text = y.ToString("F0");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
            base.OnClosed(e);
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