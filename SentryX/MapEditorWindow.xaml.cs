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
using static SentryX.DeviceControl;
// === 解決命名空間衝突的 using alias ===
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
// 注意：改用 IOPath 來代表 System.IO.Path，避免與 WPF 的 Path 類型衝突
using IOPath = System.IO.Path;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using ShapesPath = System.Windows.Shapes.Path;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Size = System.Windows.Size;
using Panel = System.Windows.Controls.Panel;


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

        // 新增：攝影機視野屬性
        public bool ShowFieldOfView { get; set; } = true; // 是否顯示視野
        public double ViewAngle { get; set; } = 90; // 視野角度（度）
        public double ViewDistance { get; set; } = 100; // 視野距離（像素）
        public double ViewDirection { get; set; } = 0; // 視野方向（度，0為正上方，順時針）

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

        // 角點 (Corners)
        TopLeft,      // 左上
        TopRight,     // 右上
        BottomLeft,   // 左下
        BottomRight,  // 右下

        // 邊中點 (Middles)
        Top,          // 上
        Bottom,       // 下
        Left,         // 左
        Right         // 右
    }

    // FieldOfViewManager - 管理所有設備的視野繪製
    public class FieldOfViewManager
    {
        private Canvas fieldOfViewCanvas;
        private Canvas handlesCanvas;
        private Dictionary<string, FieldOfViewElements> fieldOfViews;

        public FieldOfViewManager(Canvas fovCanvas, Canvas handleCanvas)
        {
            fieldOfViewCanvas = fovCanvas;
            handlesCanvas = handleCanvas;
            fieldOfViews = new Dictionary<string, FieldOfViewElements>();
        }

        // 視野元素容器
        private class FieldOfViewElements
        {
            public ShapesPath? FieldPath { get; set; }
            public Line? DirectionLine { get; set; }
            public Ellipse? DirectionHandle { get; set; }
            public Ellipse? LeftAngleHandle { get; set; }
            public Ellipse? RightAngleHandle { get; set; }
            public bool IsVisible { get; set; }
        }

        // 為設備創建視野
        public void CreateFieldOfView(MapDevice device)
        {
            if (device.DeviceId == null) return;

            var elements = new FieldOfViewElements
            {
                IsVisible = device.ShowFieldOfView
            };

            // 創建視野扇形
            elements.FieldPath = new ShapesPath
            {
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 150, 255)),
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                Visibility = device.ShowFieldOfView ? Visibility.Visible : Visibility.Collapsed
            };
            fieldOfViewCanvas.Children.Add(elements.FieldPath);

            // 創建方向線
            elements.DirectionLine = new Line
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed
            };
            handlesCanvas.Children.Add(elements.DirectionLine);

            // 創建方向控制點（紅色）
            elements.DirectionHandle = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Red,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Hand,
                Tag = $"Direction_{device.DeviceId}",
                IsHitTestVisible = true  // ✅ 明確設置
            };
            handlesCanvas.Children.Add(elements.DirectionHandle);

            // 創建左側角度控制點（橙色）
            elements.LeftAngleHandle = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.Orange,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.SizeAll,
                Tag = $"LeftAngle_{device.DeviceId}",
                IsHitTestVisible = true  // ✅ 明確設置
            };
            handlesCanvas.Children.Add(elements.LeftAngleHandle);

            // 創建右側角度控制點（橙色）
            elements.RightAngleHandle = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.Orange,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.SizeAll,
                Tag = $"RightAngle_{device.DeviceId}",
                IsHitTestVisible = true  // ✅ 明確設置
            };
            handlesCanvas.Children.Add(elements.RightAngleHandle);

            fieldOfViews[device.DeviceId] = elements;
            UpdateFieldOfView(device);
        }

        // 更新視野顯示
        public void UpdateFieldOfView(MapDevice device)
        {
            if (device.DeviceId == null || !fieldOfViews.ContainsKey(device.DeviceId))
                return;

            var elements = fieldOfViews[device.DeviceId];
            if (elements.FieldPath == null) return;

            // 計算設備中心點（Canvas 座標）
            double centerX = device.X + device.Width / 2;
            double centerY = device.Y + device.Height / 2;

            // 轉換角度為弧度
            double directionRad = device.ViewDirection * Math.PI / 180;
            double halfAngleRad = (device.ViewAngle / 2) * Math.PI / 180;

            // 計算扇形的起始和結束角度
            double startAngle = directionRad - halfAngleRad;
            double endAngle = directionRad + halfAngleRad;

            // 計算扇形邊緣的兩個點
            double x1 = centerX + device.ViewDistance * Math.Sin(startAngle);
            double y1 = centerY - device.ViewDistance * Math.Cos(startAngle);
            double x2 = centerX + device.ViewDistance * Math.Sin(endAngle);
            double y2 = centerY - device.ViewDistance * Math.Cos(endAngle);

            // 創建扇形路徑
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(centerX, centerY) };

            figure.Segments.Add(new LineSegment(new Point(x1, y1), true));
            figure.Segments.Add(new ArcSegment(
                new Point(x2, y2),
                new Size(device.ViewDistance, device.ViewDistance),
                0,
                device.ViewAngle > 180,
                SweepDirection.Clockwise,
                true
            ));
            figure.Segments.Add(new LineSegment(new Point(centerX, centerY), true));

            geometry.Figures.Add(figure);
            elements.FieldPath.Data = geometry;

            // 更新方向線
            if (elements.DirectionLine != null)
            {
                elements.DirectionLine.X1 = centerX;
                elements.DirectionLine.Y1 = centerY;
                elements.DirectionLine.X2 = centerX + device.ViewDistance * Math.Sin(directionRad);
                elements.DirectionLine.Y2 = centerY - device.ViewDistance * Math.Cos(directionRad);
            }

            // 更新控制點位置
            if (elements.DirectionHandle != null)
            {
                double dirX = centerX + device.ViewDistance * Math.Sin(directionRad);
                double dirY = centerY - device.ViewDistance * Math.Cos(directionRad);
                Canvas.SetLeft(elements.DirectionHandle, dirX - 5);
                Canvas.SetTop(elements.DirectionHandle, dirY - 5);
            }

            if (elements.LeftAngleHandle != null)
            {
                Canvas.SetLeft(elements.LeftAngleHandle, x1 - 4);
                Canvas.SetTop(elements.LeftAngleHandle, y1 - 4);
            }

            if (elements.RightAngleHandle != null)
            {
                Canvas.SetLeft(elements.RightAngleHandle, x2 - 4);
                Canvas.SetTop(elements.RightAngleHandle, y2 - 4);
            }
        }

        // 顯示/隱藏選擇狀態
        public void ShowSelection(string deviceId)
        {
            if (!fieldOfViews.ContainsKey(deviceId)) return;
            var elements = fieldOfViews[deviceId];

            if (elements.DirectionHandle != null)
            {
                elements.DirectionHandle.Visibility = Visibility.Visible;
                Panel.SetZIndex(elements.DirectionHandle, 1000); // ✅ 確保在最上層
                Debug.WriteLine($"✅ 顯示方向控制點: {deviceId}");
            }
            if (elements.LeftAngleHandle != null)
            {
                elements.LeftAngleHandle.Visibility = Visibility.Visible;
                Panel.SetZIndex(elements.LeftAngleHandle, 1000);
                Debug.WriteLine($"✅ 顯示左角控制點: {deviceId}");
            }
            if (elements.RightAngleHandle != null)
            {
                elements.RightAngleHandle.Visibility = Visibility.Visible;
                Panel.SetZIndex(elements.RightAngleHandle, 1000);
                Debug.WriteLine($"✅ 顯示右角控制點: {deviceId}");
            }
            if (elements.DirectionLine != null)
            {
                elements.DirectionLine.Visibility = Visibility.Visible;
                Debug.WriteLine($"✅ 顯示方向線: {deviceId}");
            }
        }

        public void HideSelection(string deviceId)
        {
            if (!fieldOfViews.ContainsKey(deviceId)) return;
            var elements = fieldOfViews[deviceId];

            if (elements.DirectionHandle != null) elements.DirectionHandle.Visibility = Visibility.Collapsed;
            if (elements.LeftAngleHandle != null) elements.LeftAngleHandle.Visibility = Visibility.Collapsed;
            if (elements.RightAngleHandle != null) elements.RightAngleHandle.Visibility = Visibility.Collapsed;
            if (elements.DirectionLine != null) elements.DirectionLine.Visibility = Visibility.Collapsed;
        }

        // 切換視野顯示
        public void ToggleFieldOfView(string deviceId, bool show)
        {
            if (!fieldOfViews.ContainsKey(deviceId)) return;
            var elements = fieldOfViews[deviceId];

            if (elements.FieldPath != null)
                elements.FieldPath.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            elements.IsVisible = show;
        }

        // 移除視野
        public void RemoveFieldOfView(string deviceId)
        {
            if (!fieldOfViews.ContainsKey(deviceId)) return;
            var elements = fieldOfViews[deviceId];

            if (elements.FieldPath != null) fieldOfViewCanvas.Children.Remove(elements.FieldPath);
            if (elements.DirectionLine != null) handlesCanvas.Children.Remove(elements.DirectionLine);
            if (elements.DirectionHandle != null) handlesCanvas.Children.Remove(elements.DirectionHandle);
            if (elements.LeftAngleHandle != null) handlesCanvas.Children.Remove(elements.LeftAngleHandle);
            if (elements.RightAngleHandle != null) handlesCanvas.Children.Remove(elements.RightAngleHandle);

            fieldOfViews.Remove(deviceId);
        }

        // 檢測點擊的控制點
        public (string? deviceId, string? handleType) GetHandleAt(Point point)
        {
            Debug.WriteLine($"🔍 檢查點擊位置: ({point.X:F0}, {point.Y:F0})");

            foreach (var element in handlesCanvas.Children.OfType<Ellipse>())
            {
                if (element.Visibility != Visibility.Visible) continue;

                var left = Canvas.GetLeft(element);
                var top = Canvas.GetTop(element);
                var bounds = new Rect(left - 5, top - 5, element.Width + 10, element.Height + 10);

                Debug.WriteLine($"   控制點: Tag={element.Tag}, Bounds=({bounds.Left:F0},{bounds.Top:F0},{bounds.Right:F0},{bounds.Bottom:F0})");

                if (bounds.Contains(point) && element.Tag is string tag)
                {
                    var parts = tag.Split('_');
                    if (parts.Length == 2)
                    {
                        Debug.WriteLine($"✅ 找到控制點: {parts[0]} of {parts[1]}");
                        return (parts[1], parts[0]); // (deviceId, handleType)
                    }
                }
            }

            Debug.WriteLine("❌ 未找到控制點");
            return (null, null);
        }
    }


    // DeviceControl - 自訂的設備控件類別
    public class DeviceControl : Grid
    {
        public MapDevice Device { get; set; }
        public Border DeviceBorder { get; private set; }
        public Border SelectionBorder { get; private set; }
        public Canvas HandleContainer { get; private set; } = null!; // 加上 null-forgiving 運算子
        public List<Ellipse> ResizeHandles { get; private set; }

        public bool IsSelected { get; set; }


        public DeviceControl(MapDevice device)
        {
            Device = device;
            ResizeHandles = new List<Ellipse>();
            DeviceBorder = new Border();
            SelectionBorder = new Border();

            CreateControl();
            // ❌ 移除 CreateFieldOfView() 調用
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
        {
            DeviceBorder.Measure(constraint);
            SelectionBorder.Measure(constraint);
            HandleContainer.Measure(constraint);

            foreach (var handle in ResizeHandles)
            {
                handle.Measure(constraint);
            }

            return new System.Windows.Size(Device.Width, Device.Height);
        }

        protected override System.Windows.Size ArrangeOverride(System.Windows.Size arrangeBounds)
        {
            var deviceRect = new Rect(0, 0, Device.Width, Device.Height);

            DeviceBorder.Arrange(deviceRect);
            SelectionBorder.Arrange(new Rect(-5, -5, Device.Width + 10, Device.Height + 10));
            HandleContainer.Arrange(new Rect(-5, -5, Device.Width + 10, Device.Height + 10));

            // ✅ 移除文字排列邏輯

            foreach (var handle in ResizeHandles)
            {
                handle.Arrange(new Rect(Canvas.GetLeft(handle), Canvas.GetTop(handle), handle.Width, handle.Height));
            }

            return new System.Windows.Size(Device.Width, Device.Height);
        }

        private void CreateControl()
        {
            this.ClipToBounds = false;

            DeviceBorder = new Border
            {
                Width = Device.Width,
                Height = Device.Height,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ClipToBounds = false,
                // ✅ 添加 ToolTip
                ToolTip = Device.Name
            };

            if (Device.DeviceType == "Channel")
            {
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(5)
                };

                try
                {
                    var bitmap = new BitmapImage(new Uri("pack://application:,,,/Resources/camera_icon.png"));
                    image.Source = bitmap;
                    grid.Children.Add(image);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"載入圖片失敗: {ex.Message}");
                    var iconText = new TextBlock
                    {
                        Text = "📹",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 20
                    };
                    grid.Children.Add(iconText);
                }

                DeviceBorder.Child = grid;
            }
            else
            {
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

                stackPanel.Children.Add(iconText);
                DeviceBorder.Child = stackPanel;
            }

            this.Children.Add(DeviceBorder);

            // ✅ 不再需要文字元素，移除這部分
            // var nameText = new TextBlock ...

            SelectionBorder = new Border
            {
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(20, 30, 144, 255)),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(-5)
            };
            this.Children.Add(SelectionBorder);

            HandleContainer = new Canvas
            {
                Width = Device.Width + 10,
                Height = Device.Height + 10,
                Margin = new Thickness(-5),
                IsHitTestVisible = true
            };
            this.Children.Add(HandleContainer);

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
                // this.Children.Add(handle);
                HandleContainer.Children.Add(handle);
            }
        }


        public void UpdateDeviceStatus(bool isOnline)
        {
            Device.IsOnline = isOnline;

            // ✅ 只有非視頻通道才更新背景色
            if (Device.DeviceType != "Channel")
            {
                var bgColor = isOnline ? Colors.LightGreen : Colors.LightCoral;
                DeviceBorder.Background = new SolidColorBrush(bgColor);
            }
            // ✅ 視頻通道保持透明，不改變背景
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
                Panel.SetZIndex(handle, 100); // ✅ 確保控制點在最上層
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
            // ✅ 不要在這裡處理視野控制點，由 FieldOfViewManager 處理
        }


        private void PositionResizeHandles()
        {
            double w = DeviceBorder.Width;
            double h = DeviceBorder.Height;
            double handleSize = 8; // 修正：補上 handleSize 定義
            double hw = 4; // 控制點半徑 (handleSize / 2)

            if (ResizeHandles.Count >= 8)
            {
                double fullW = w + 10;
                double fullH = h + 10;

                // 四個角點 (Corners)
                SetCanvasPosition(ResizeHandles[0], 0, 0);                  // 左上
                SetCanvasPosition(ResizeHandles[1], fullW - handleSize, 0);  // 右上
                SetCanvasPosition(ResizeHandles[2], 0, fullH - handleSize);  // 左下
                SetCanvasPosition(ResizeHandles[3], fullW - handleSize, fullH - handleSize); // 右下

                // 四個邊中點 (Middles)
                SetCanvasPosition(ResizeHandles[4], fullW / 2 - hw, 0);                   // 上中
                SetCanvasPosition(ResizeHandles[5], fullW / 2 - hw, fullH - handleSize);  // 下中
                SetCanvasPosition(ResizeHandles[6], 0, fullH / 2 - hw);                   // 左中
                SetCanvasPosition(ResizeHandles[7], fullW - handleSize, fullH / 2 - hw);  // 右中

                HandleContainer.Width = fullW;
                HandleContainer.Height = fullH;
            }
        }

        // ✅ 新增輔助方法，使用 Canvas.Left 和 Canvas.Top
        private void SetCanvasPosition(Ellipse handle, double left, double top)
        {
            Canvas.SetLeft(handle, left);
            Canvas.SetTop(handle, top);
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

                // ✅ 確保 HandleContainer 尺寸同步
                HandleContainer.Width = width + 10;
                HandleContainer.Height = height + 10;

                PositionResizeHandles();
            }
        }

        public ResizeHandle GetHandleAt(Point localPoint)
        {
            // 假設控制點的範圍為一個小的正方形 (例如 10x10 像素)
            double handleSize = 10;

            // 獲取設備的當前尺寸
            double width = this.Device.Width;
            double height = this.Device.Height;

            // 邊界距離 (半徑)
            double halfSize = handleSize / 2;

            // 1. 檢查四個角點 (Corners)

            // 左上 (TopLeft)
            if (localPoint.X >= -halfSize && localPoint.X <= halfSize &&
                localPoint.Y >= -halfSize && localPoint.Y <= halfSize)
            {
                return ResizeHandle.TopLeft;
            }

            // 右上 (TopRight)
            if (localPoint.X >= width - halfSize && localPoint.X <= width + halfSize &&
                localPoint.Y >= -halfSize && localPoint.Y <= halfSize)
            {
                return ResizeHandle.TopRight;
            }

            // 左下 (BottomLeft)
            // ⚠️ 注意：這裡需要判斷 Y 軸的絕對位置，假設 Y=0 是 DeviceControl 的頂部
            if (localPoint.X >= -halfSize && localPoint.X <= halfSize &&
                localPoint.Y >= height - halfSize && localPoint.Y <= height + halfSize)
            {
                return ResizeHandle.BottomLeft;
            }

            // 右下 (BottomRight)
            if (localPoint.X >= width - halfSize && localPoint.X <= width + halfSize &&
                localPoint.Y >= height - halfSize && localPoint.Y <= height + halfSize)
            {
                return ResizeHandle.BottomRight;
            }

            // 2. 檢查四個邊中點 (Middles)

            // 上 (Top) (X 軸在中間，Y 軸在頂部邊緣)
            if (localPoint.X >= width / 2 - halfSize && localPoint.X <= width / 2 + halfSize &&
                localPoint.Y >= -halfSize && localPoint.Y <= halfSize)
            {
                return ResizeHandle.Top;
            }

            // 下 (Bottom)
            if (localPoint.X >= width / 2 - halfSize && localPoint.X <= width / 2 + halfSize &&
                localPoint.Y >= height - halfSize && localPoint.Y <= height + halfSize)
            {
                return ResizeHandle.Bottom;
            }

            // 左 (Left)
            if (localPoint.X >= -halfSize && localPoint.X <= halfSize &&
                localPoint.Y >= height / 2 - halfSize && localPoint.Y <= height / 2 + halfSize)
            {
                return ResizeHandle.Left;
            }

            // 右 (Right)
            if (localPoint.X >= width - halfSize && localPoint.X <= width + halfSize &&
                localPoint.Y >= height / 2 - halfSize && localPoint.Y <= height / 2 + halfSize)
            {
                return ResizeHandle.Right;
            }

            // 如果都沒有命中，返回 None
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

        // ✅ 新增：視野管理器
        private FieldOfViewManager? fieldOfViewManager;

        // 視野調整相關欄位
        private bool isAdjustingFieldOfView = false;
        private string? activeFieldOfViewHandleType = null; // "Direction", "LeftAngle", "RightAngle"
        private string? activeFieldOfViewDeviceId = null;

        private string mapDataFolder = IOPath.Combine(Directory.GetCurrentDirectory(), "MapData");
        private string configFilePath => IOPath.Combine(mapDataFolder, "config.xml");

        private bool isUpdatingProperties = false;

        public MapEditorWindow()
        {
            InitializeComponent();

            // ✅ 初始化視野管理器
            fieldOfViewManager = new FieldOfViewManager(FieldOfViewCanvas, FieldOfViewHandlesCanvas);

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
                                Port = 0,
                                IsOnline = false,
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
                                Port = 0,
                                IsOnline = false,
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
            Panel.SetZIndex(deviceControl, 1); // ✅ 設備在視野下方
            MapCanvas.Children.Add(deviceControl);

            // ✅ 為設備創建視野
            if (device.DeviceId != null)
            {
                fieldOfViewManager?.CreateFieldOfView(device);
            }

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
                    string fileName = IOPath.GetFileName(originalFilePath);
                    string newFilePath = IOPath.Combine(mapDataFolder, fileName);

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
                // ✅ 移除視野
                if (dev.Device?.DeviceId != null)
                    fieldOfViewManager?.RemoveFieldOfView(dev.Device.DeviceId);

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
                // ✅ 移除視野
                if (selectedControl.Device.DeviceId != null)
                    fieldOfViewManager?.RemoveFieldOfView(selectedControl.Device.DeviceId);

                MapCanvas.Children.Remove(selectedControl);
                selectedControl = null;
                UpdateDevicePropertiesPanel();
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
            // 1. 基本檢查和初始化
            if (e.LeftButton != MouseButtonState.Pressed) return; // 只處理左鍵按下

            Point point = e.GetPosition(MapCanvas);
            FrameworkElement? hitElement = e.OriginalSource as FrameworkElement;

            // ==========================================================
            // ✅ 步驟一：處理視野控制點的點擊 (優先處理，解決點擊後失去點擊狀態的問題)
            // ==========================================================
            if (hitElement is Ellipse handle && handle.Tag is string tag)
            {
                // 假設 Tag 格式為 "HandleType_DeviceId..."
                // 例如: "DIR_192.168.31.137:37777_CH1"，split 後會有 3 個 parts
                var parts = tag.Split('_');

                if (parts.Length >= 2)
                {
                    var handleType = parts[0];
                    // 重新組合 DeviceId (將第一個底線後的所有部分都視為 DeviceId)
                    var deviceId = string.Join("_", parts.Skip(1));

                    // 必須在編輯模式下且選中的設備要匹配
                    if (isEditMode && selectedControl != null && selectedControl.Device.DeviceId == deviceId)
                    {
                        Debug.WriteLine($"✅ 找到視野控制點: {handleType} of {deviceId}");

                        isAdjustingFieldOfView = true;
                        activeFieldOfViewHandleType = handleType; // 這裡假設您有 FieldOfViewManager.FieldOfViewHandleType
                        activeFieldOfViewDeviceId = deviceId;
                        dragStartPoint = point; // 確保設置拖曳起始點，用於 MouseMove

                        // 💥 關鍵修正：標記事件為已處理，阻止事件冒泡到 MapCanvas 的取消選中邏輯
                        e.Handled = true;

                        MapCanvas.CaptureMouse();
                        return; // 處理完畢，立即退出方法
                    }
                }
            }

            // ==========================================================
            // 步驟二：查找被點擊的 DeviceControl (用於拖曭、縮放、選中)
            // ==========================================================
            DeviceControl? clickedControl = null;
            FrameworkElement? current = hitElement;

            // 向上遍歷視覺樹查找 DeviceControl
            while (current != null && current != MapCanvas)
            {
                if (current is DeviceControl dc)
                {
                    clickedControl = dc;
                    break;
                }
                // 使用 VisualTreeHelper.GetParent 更安全地查找父元素
                current = VisualTreeHelper.GetParent(current) as FrameworkElement;
            }

            if (clickedControl != null)
            {
                // ------------------------------------------------------
                // 2a. 非編輯模式：點擊設備打開視頻播放器
                // ------------------------------------------------------
                if (!isEditMode)
                {
                    OpenVideoPlayer(clickedControl.Device);
                    e.Handled = true;
                    return;
                }

                // ------------------------------------------------------
                // 2b. 編輯模式
                // ------------------------------------------------------
                if (isEditMode)
                {
                    var localPoint = e.GetPosition(clickedControl);

                    // 檢查是否點擊到設備縮放控制點
                    var resizeHandle = clickedControl.GetHandleAt(localPoint);
                    if (resizeHandle != ResizeHandle.None)
                    {
                        // 縮放操作
                        isResizing = true;
                        activeResizeHandle = resizeHandle;
                        resizeStartPoint = point;
                        initialWidth = clickedControl.Device.Width;
                        initialHeight = clickedControl.Device.Height;

                        MapCanvas.CaptureMouse();
                        e.Handled = true;
                        return;
                    }

                    // 正常的選擇和拖動 (點擊設備圖標本體)
                    if (selectedControl != clickedControl)
                    {
                        // 如果選中了一個新設備，取消舊設備的選中狀態
                        selectedControl?.HideSelection();
                        if (selectedControl?.Device?.DeviceId != null)
                        {
                            fieldOfViewManager?.HideSelection(selectedControl.Device.DeviceId);
                        }

                        // 選中新設備
                        selectedControl = clickedControl;
                        selectedControl.ShowSelection();
                        if (selectedControl.Device.DeviceId != null)
                        {
                            fieldOfViewManager?.ShowSelection(selectedControl.Device.DeviceId);
                        }
                        UpdateDevicePropertiesPanel();
                    }

                    // 開始拖曳
                    isDragging = true;
                    draggedControl = clickedControl;
                    dragStartPoint = point;

                    MapCanvas.CaptureMouse();
                    e.Handled = true; // 標記為已處理，防止冒泡到 MapCanvas 的點擊空白處邏輯
                }
            }
            // ==========================================================
            // 步驟三：點擊空白處 (clickedControl == null)
            // ==========================================================
            else
            {
                // ------------------------------------------------------
                // 3a. 編輯模式下點擊空白區域：取消選中所有設備
                // ------------------------------------------------------
                if (selectedControl != null && isEditMode)
                {
                    selectedControl.HideSelection();
                    if (selectedControl.Device.DeviceId != null)
                    {
                        fieldOfViewManager?.HideSelection(selectedControl.Device.DeviceId);
                    }
                    selectedControl = null;
                    UpdateDevicePropertiesPanel();
                    e.Handled = true;
                }

                // ------------------------------------------------------
                // 3b. 非編輯模式下點擊空白處：拖動地圖
                // ------------------------------------------------------
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

            // ✅ 視野調整邏輯
            if (isAdjustingFieldOfView && selectedControl != null && activeFieldOfViewDeviceId != null)
            {
                var device = selectedControl.Device;
                double centerX = device.X + device.Width / 2;
                double centerY = device.Y + device.Height / 2;

                double dx = currentPoint.X - centerX;
                double dy = currentPoint.Y - centerY;

                if (activeFieldOfViewHandleType == "Direction")
                {
                    double angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
                    if (angle < 0) angle += 360;

                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    distance = Math.Max(20, distance);

                    device.ViewDirection = angle;
                    device.ViewDistance = distance;
                    fieldOfViewManager?.UpdateFieldOfView(device);

                    isUpdatingProperties = true;
                    ViewDirectionTextBox.Text = angle.ToString("F0");
                    ViewDistanceTextBox.Text = distance.ToString("F0");
                    isUpdatingProperties = false;

                    MousePositionText.Text = $"方向: {angle:F0}° 距離: {distance:F0}px";
                }
                else if (activeFieldOfViewHandleType == "LeftAngle")
                {
                    double angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
                    if (angle < 0) angle += 360;

                    double angleDiff = angle - device.ViewDirection;
                    while (angleDiff > 180) angleDiff -= 360;
                    while (angleDiff < -180) angleDiff += 360;

                    if (angleDiff < 0)
                    {
                        double newViewAngle = Math.Abs(angleDiff) * 2;
                        newViewAngle = Math.Max(10, Math.Min(359.9, newViewAngle));

                        device.ViewAngle = newViewAngle;
                        fieldOfViewManager?.UpdateFieldOfView(device);

                        isUpdatingProperties = true;
                        ViewAngleTextBox.Text = newViewAngle.ToString("F0");
                        isUpdatingProperties = false;

                        MousePositionText.Text = $"視野角度: {newViewAngle:F0}°";
                    }
                }
                else if (activeFieldOfViewHandleType == "RightAngle")
                {
                    double angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
                    if (angle < 0) angle += 360;

                    double angleDiff = angle - device.ViewDirection;
                    while (angleDiff > 180) angleDiff -= 360;
                    while (angleDiff < -180) angleDiff += 360;

                    if (angleDiff > 0)
                    {
                        double newViewAngle = Math.Abs(angleDiff) * 2;
                        newViewAngle = Math.Max(10, Math.Min(359.9, newViewAngle));

                        device.ViewAngle = newViewAngle;
                        fieldOfViewManager?.UpdateFieldOfView(device);

                        isUpdatingProperties = true;
                        ViewAngleTextBox.Text = newViewAngle.ToString("F0");
                        isUpdatingProperties = false;

                        MousePositionText.Text = $"視野角度: {newViewAngle:F0}°";
                    }
                }

                e.Handled = true;
                return;
            }

            // 調整大小邏輯
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

                // ✅ 更新視野位置
                if (selectedControl.Device.DeviceId != null)
                    fieldOfViewManager?.UpdateFieldOfView(selectedControl.Device);

                isUpdatingProperties = true;
                DeviceWidthTextBox.Text = newWidth.ToString("F0");
                DeviceHeightTextBox.Text = newHeight.ToString("F0");
                isUpdatingProperties = false;

                MousePositionText.Text = $"大小: {newWidth:0} x {newHeight:0}";
                e.Handled = true;
                return;
            }

            // 拖動設備邏輯
            if (isDragging && draggedControl != null)
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

                    // ✅ 更新視野位置
                    if (draggedControl.Device.DeviceId != null)
                        fieldOfViewManager?.UpdateFieldOfView(draggedControl.Device);

                    isUpdatingProperties = true;
                    DeviceXTextBox.Text = newX.ToString("F0");
                    DeviceYTextBox.Text = newY.ToString("F0");
                    isUpdatingProperties = false;
                }

                MousePositionText.Text = $"座標: {newX:0}, {newY:0}";
                e.Handled = true;
                return;
            }

            // 拖動地圖邏輯
            if (isDraggingMap)
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

                        // ✅ 更新視野位置
                        if (control.Device.DeviceId != null)
                            fieldOfViewManager?.UpdateFieldOfView(control.Device);
                    }
                }

                MousePositionText.Text = $"底圖座標: {newMapLeft:0}, {newMapTop:0}";
                e.Handled = true;
                return;
            }

            MousePositionText.Text = $"座標: {currentPoint.X:0}, {currentPoint.Y:0}";
        }

        private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isAdjustingFieldOfView)
            {
                isAdjustingFieldOfView = false;
                activeFieldOfViewHandleType = null;
                activeFieldOfViewDeviceId = null;
            }

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

                    // 清除現有設備
                    var existingDevices = MapCanvas.Children.OfType<DeviceControl>().ToList();
                    foreach (var dev in existingDevices)
                    {
                        if (dev.Device?.DeviceId != null)
                            fieldOfViewManager?.RemoveFieldOfView(dev.Device.DeviceId);
                        MapCanvas.Children.Remove(dev);
                    }

                    // 載入地圖
                    if (!string.IsNullOrEmpty(config.MapImagePath) && File.Exists(config.MapImagePath))
                    {
                        MapImage.Source = new BitmapImage(new Uri(config.MapImagePath));
                        MapInfoText.Text = $"地圖: {IOPath.GetFileName(config.MapImagePath)}";
                    }
                    else
                    {
                        MapImage.Source = null;
                        MapInfoText.Text = "未載入地圖";
                        MessageBox.Show("載入的圖片路徑無效或檔案不存在。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    // 載入設備
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
                            Panel.SetZIndex(deviceControl, 1);
                            MapCanvas.Children.Add(deviceControl);

                            // ✅ 創建視野
                            if (device.DeviceId != null)
                                fieldOfViewManager?.CreateFieldOfView(device);
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

                // 視野設置
                ShowFieldOfViewCheckBox.IsChecked = selectedControl.Device.ShowFieldOfView;
                ViewAngleTextBox.Text = selectedControl.Device.ViewAngle.ToString("F0");
                ViewDistanceTextBox.Text = selectedControl.Device.ViewDistance.ToString("F0");
                ViewDirectionTextBox.Text = selectedControl.Device.ViewDirection.ToString("F0");

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

                ShowFieldOfViewCheckBox.IsChecked = true;
                ViewAngleTextBox.Text = "90";
                ViewDistanceTextBox.Text = "100";
                ViewDirectionTextBox.Text = "0";
            }
        }

        // 新增視野控制事件
        private void ShowFieldOfViewCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isUpdatingProperties || selectedControl == null) return;

            bool show = ShowFieldOfViewCheckBox.IsChecked == true;
            selectedControl.Device.ShowFieldOfView = show;

            // ✅ 使用視野管理器切換顯示
            if (selectedControl.Device.DeviceId != null)
                fieldOfViewManager?.ToggleFieldOfView(selectedControl.Device.DeviceId, show);
        }

        private void ViewAngleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingProperties || selectedControl == null) return;

            if (double.TryParse(ViewAngleTextBox.Text, out double angle))
            {
                angle = Math.Max(0, Math.Min(359.9, angle));
                selectedControl.Device.ViewAngle = angle;

                // ✅ 更新視野
                if (selectedControl.Device.DeviceId != null)
                    fieldOfViewManager?.UpdateFieldOfView(selectedControl.Device);
            }
        }

        private void ViewDistanceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingProperties || selectedControl == null) return;

            if (double.TryParse(ViewDistanceTextBox.Text, out double distance))
            {
                distance = Math.Max(0, distance);
                selectedControl.Device.ViewDistance = distance;

                // ✅ 更新視野
                if (selectedControl.Device.DeviceId != null)
                    fieldOfViewManager?.UpdateFieldOfView(selectedControl.Device);
            }
        }

        private void ViewDirectionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingProperties || selectedControl == null) return;

            if (double.TryParse(ViewDirectionTextBox.Text, out double direction))
            {
                direction = direction % 360;
                if (direction < 0) direction += 360;

                selectedControl.Device.ViewDirection = direction;

                // ✅ 更新視野
                if (selectedControl.Device.DeviceId != null)
                    fieldOfViewManager?.UpdateFieldOfView(selectedControl.Device);
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

                // ✅ 新增：更新視野位置
                if (selectedControl.Device.DeviceId != null)
                    fieldOfViewManager?.UpdateFieldOfView(selectedControl.Device);

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

                // ✅ 新增：更新視野位置（因為設備尺寸改變會影響中心點）
                if (selectedControl.Device.DeviceId != null)
                    fieldOfViewManager?.UpdateFieldOfView(selectedControl.Device);

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

                // ✅ 注意：這裡不需要再次調用 UpdateFieldOfView
                // 因為 DeviceXTextBox.Text 的改變會觸發 DevicePositionTextBox_TextChanged
                // 那裡已經有更新視野的代碼了
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