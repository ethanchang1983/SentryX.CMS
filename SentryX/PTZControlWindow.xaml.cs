using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SentryX
{
    public partial class PTZControlWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private PTZManager? _ptzManager;
        private MultiViewPlayer? _currentPlayer;
        private bool _isControlEnabled = false;

        // PTZ控制狀態追蹤 - 改進的狀態管理
        private bool _isMoving = false;
        private string _currentMovementType = "";
        private bool _isMousePressed = false; // 新增：追蹤鼠標按下狀態

        public PTZControlWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            InitializeControls();
            CheckCurrentPlayer();

            // 每秒更新一次狀態
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => CheckCurrentPlayer();
            timer.Start();
        }

        private void InitializeControls()
        {
            // 方向控制按鈕事件 - 改進的事件處理
            SetupPTZButton(TopLeftButton, "TopLeft");
            SetupPTZButton(TopButton, "Up");
            SetupPTZButton(TopRightButton, "TopRight");
            SetupPTZButton(LeftButton, "Left");
            SetupPTZButton(RightButton, "Right");
            SetupPTZButton(BottomLeftButton, "BottomLeft");
            SetupPTZButton(BottomButton, "Down");
            SetupPTZButton(BottomRightButton, "BottomRight");

            // 縮放和焦距控制按鈕事件
            SetupPTZButton(ZoomInButton, "ZoomIn");
            SetupPTZButton(ZoomOutButton, "ZoomOut");
            SetupPTZButton(FocusNearButton, "FocusNear");
            SetupPTZButton(FocusFarButton, "FocusFar");
            SetupPTZButton(IrisOpenButton, "IrisOpen");
            SetupPTZButton(IrisCloseButton, "IrisClose");

            // 中央停止按鈕 - 特殊處理
            CenterButton.Click += (s, e) => StopAllMovement();

            // 預置點控制按鈕事件
            GotoPresetButton.Click += GotoPresetButton_Click;
            SetPresetButton.Click += SetPresetButton_Click;
            DeletePresetButton.Click += DeletePresetButton_Click;
        }

        /// <summary>
        /// 為PTZ控制按鈕設置正確的事件處理 - 新的統一方法
        /// </summary>
        private void SetupPTZButton(System.Windows.Controls.Button button, string controlType)
        {
            // 鼠標按下事件
            button.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!_isMousePressed && !_isMoving)
                {
                    _isMousePressed = true;
                    StartPTZControl(controlType);
                    button.CaptureMouse(); // 捕獲鼠標，確保能收到釋放事件

                    // 視覺反饋：按下時改變顏色
                    button.Background = System.Windows.Media.Brushes.DarkBlue;
                    AddStatusMessage($"開始控制：{GetControlTypeDisplayName(controlType)}");
                }
                e.Handled = true;
            };

            // 鼠標釋放事件
            button.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_isMousePressed && _currentMovementType == controlType)
                {
                    _isMousePressed = false;
                    StopPTZControl(controlType);
                    button.ReleaseMouseCapture(); // 釋放鼠標捕獲

                    // 恢復原始顏色
                    button.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 63, 70));
                    AddStatusMessage($"停止控制：{GetControlTypeDisplayName(controlType)}");
                }
                e.Handled = true;
            };

            // 鼠標離開事件 - 確保在鼠標離開按鈕區域時停止控制
            button.MouseLeave += (s, e) =>
            {
                if (_isMousePressed && _currentMovementType == controlType)
                {
                    _isMousePressed = false;
                    StopPTZControl(controlType);
                    button.ReleaseMouseCapture();

                    // 恢復原始顏色
                    button.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 63, 70));
                    AddStatusMessage($"鼠標離開，停止控制：{GetControlTypeDisplayName(controlType)}");
                }
            };

            // 鼠標進入事件 - 視覺反饋
            button.MouseEnter += (s, e) =>
            {
                if (!_isMoving)
                {
                    button.Background = System.Windows.Media.Brushes.DarkGray;
                }
            };
        }

        private void CheckCurrentPlayer()
        {
            try
            {
                var selectedPlayer = _mainWindow.SplitScreenManager?.SelectedPlayer;

                if (selectedPlayer != _currentPlayer)
                {
                    _currentPlayer = selectedPlayer;
                    UpdatePTZManager();
                }

                UpdateDeviceInfo();
                UpdateControlsState();
            }
            catch (Exception ex)
            {
                AddStatusMessage($"檢查當前播放器時發生錯誤：{ex.Message}");
            }
        }

        private void UpdatePTZManager()
        {
            try
            {
                if (_currentPlayer?.IsPlaying == true)
                {
                    var playbackState = _currentPlayer.GetCurrentPlaybackState();
                    if (playbackState != null && !string.IsNullOrEmpty(playbackState.DeviceId))
                    {
                        var device = DahuaSDK.GetDevice(playbackState.DeviceId);
                        if (device?.IsOnline == true)
                        {
                            _ptzManager = new PTZManager(device.LoginHandle, playbackState.Channel);
                            _isControlEnabled = true;
                            AddStatusMessage($"PTZ控制已連接到設備：{playbackState.DeviceName} 通道 {playbackState.Channel + 1}");
                            return;
                        }
                    }
                }

                _ptzManager = null;
                _isControlEnabled = false;
                AddStatusMessage("PTZ控制已斷開");
            }
            catch (Exception ex)
            {
                AddStatusMessage($"更新PTZ管理器時發生錯誤：{ex.Message}");
                _ptzManager = null;
                _isControlEnabled = false;
            }
        }

        private void UpdateDeviceInfo()
        {
            try
            {
                if (_currentPlayer?.IsPlaying == true)
                {
                    var playbackState = _currentPlayer.GetCurrentPlaybackState();
                    if (playbackState != null)
                    {
                        DeviceInfoTextBlock.Text = $"當前控制：{playbackState.DeviceName} - 通道 {playbackState.Channel + 1}";
                        DeviceInfoTextBlock.Foreground = System.Windows.Media.Brushes.LightGreen;
                        return;
                    }
                }

                DeviceInfoTextBlock.Text = "請先選擇一個正在播放的分割區域";
                DeviceInfoTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
            }
            catch (Exception ex)
            {
                DeviceInfoTextBlock.Text = $"獲取設備資訊時發生錯誤：{ex.Message}";
                DeviceInfoTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void UpdateControlsState()
        {
            // 更新所有控制按鈕的啟用狀態
            var isEnabled = _isControlEnabled;

            // 方向控制
            TopLeftButton.IsEnabled = isEnabled;
            TopButton.IsEnabled = isEnabled;
            TopRightButton.IsEnabled = isEnabled;
            LeftButton.IsEnabled = isEnabled;
            RightButton.IsEnabled = isEnabled;
            BottomLeftButton.IsEnabled = isEnabled;
            BottomButton.IsEnabled = isEnabled;
            BottomRightButton.IsEnabled = isEnabled;

            // 縮放和焦距控制
            ZoomInButton.IsEnabled = isEnabled;
            ZoomOutButton.IsEnabled = isEnabled;
            FocusNearButton.IsEnabled = isEnabled;
            FocusFarButton.IsEnabled = isEnabled;
            IrisOpenButton.IsEnabled = isEnabled;
            IrisCloseButton.IsEnabled = isEnabled;

            // 預置點控制
            GotoPresetButton.IsEnabled = _isControlEnabled;
            SetPresetButton.IsEnabled = _isControlEnabled;
            DeletePresetButton.IsEnabled = _isControlEnabled;

            // 速度選擇
            SpeedComboBox.IsEnabled = _isControlEnabled;

            // 停止按鈕總是可用（如果有PTZ管理器）
            CenterButton.IsEnabled = _ptzManager != null;
        }

        private void StartPTZControl(string controlType)
        {
            if (_ptzManager == null || _isMoving) return;

            try
            {
                int speed = SpeedComboBox.SelectedIndex + 1;

                bool success = controlType switch
                {
                    "Up" => _ptzManager.StartMovement(PTZControlType.Up, speed),
                    "Down" => _ptzManager.StartMovement(PTZControlType.Down, speed),
                    "Left" => _ptzManager.StartMovement(PTZControlType.Left, speed),
                    "Right" => _ptzManager.StartMovement(PTZControlType.Right, speed),
                    "TopLeft" => _ptzManager.StartMovement(PTZControlType.TopLeft, speed),
                    "TopRight" => _ptzManager.StartMovement(PTZControlType.TopRight, speed),
                    "BottomLeft" => _ptzManager.StartMovement(PTZControlType.BottomLeft, speed),
                    "BottomRight" => _ptzManager.StartMovement(PTZControlType.BottomRight, speed),
                    "ZoomIn" => _ptzManager.StartMovement(PTZControlType.ZoomIn, speed),
                    "ZoomOut" => _ptzManager.StartMovement(PTZControlType.ZoomOut, speed),
                    "FocusNear" => _ptzManager.StartMovement(PTZControlType.FocusNear, speed),
                    "FocusFar" => _ptzManager.StartMovement(PTZControlType.FocusFar, speed),
                    "IrisOpen" => _ptzManager.StartMovement(PTZControlType.IrisOpen, speed),
                    "IrisClose" => _ptzManager.StartMovement(PTZControlType.IrisClose, speed),
                    _ => false
                };

                if (success)
                {
                    _isMoving = true;
                    _currentMovementType = controlType;
                    // 狀態訊息在SetupPTZButton中處理
                }
                else
                {
                    AddStatusMessage($"啟動 {GetControlTypeDisplayName(controlType)} 失敗");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"PTZ控制錯誤：{ex.Message}");
            }
        }

        private void StopPTZControl(string controlType)
        {
            if (_ptzManager == null || !_isMoving || _currentMovementType != controlType) return;

            try
            {
                int speed = SpeedComboBox.SelectedIndex + 1;

                bool success = controlType switch
                {
                    "Up" => _ptzManager.StopMovement(PTZControlType.Up, speed),
                    "Down" => _ptzManager.StopMovement(PTZControlType.Down, speed),
                    "Left" => _ptzManager.StopMovement(PTZControlType.Left, speed),
                    "Right" => _ptzManager.StopMovement(PTZControlType.Right, speed),
                    "TopLeft" => _ptzManager.StopMovement(PTZControlType.TopLeft, speed),
                    "TopRight" => _ptzManager.StopMovement(PTZControlType.TopRight, speed),
                    "BottomLeft" => _ptzManager.StopMovement(PTZControlType.BottomLeft, speed),
                    "BottomRight" => _ptzManager.StopMovement(PTZControlType.BottomRight, speed),
                    "ZoomIn" => _ptzManager.StopMovement(PTZControlType.ZoomIn, speed),
                    "ZoomOut" => _ptzManager.StopMovement(PTZControlType.ZoomOut, speed),
                    "FocusNear" => _ptzManager.StopMovement(PTZControlType.FocusNear, speed),
                    "FocusFar" => _ptzManager.StopMovement(PTZControlType.FocusFar, speed),
                    "IrisOpen" => _ptzManager.StopMovement(PTZControlType.IrisOpen, speed),
                    "IrisClose" => _ptzManager.StopMovement(PTZControlType.IrisClose, speed),
                    _ => false
                };

                _isMoving = false;
                _currentMovementType = "";
            }
            catch (Exception ex)
            {
                AddStatusMessage($"停止PTZ控制錯誤：{ex.Message}");
                _isMoving = false;
                _currentMovementType = "";
            }
        }

        private void StopAllMovement()
        {
            if (_ptzManager == null) return;

            try
            {
                // 停止所有可能的移動
                if (_isMoving && !string.IsNullOrEmpty(_currentMovementType))
                {
                    StopPTZControl(_currentMovementType);
                }

                _isMoving = false;
                _isMousePressed = false;
                _currentMovementType = "";

                // 重置所有按鈕顏色
                ResetAllButtonColors();

                AddStatusMessage("🛑 所有PTZ移動已強制停止");
            }
            catch (Exception ex)
            {
                AddStatusMessage($"停止所有移動時發生錯誤：{ex.Message}");
            }
        }

        /// <summary>
        /// 重置所有按鈕的顏色
        /// </summary>
        private void ResetAllButtonColors()
        {
            var normalColor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 63, 70));

            TopLeftButton.Background = normalColor;
            TopButton.Background = normalColor;
            TopRightButton.Background = normalColor;
            LeftButton.Background = normalColor;
            RightButton.Background = normalColor;
            BottomLeftButton.Background = normalColor;
            BottomButton.Background = normalColor;
            BottomRightButton.Background = normalColor;

            ZoomInButton.Background = normalColor;
            ZoomOutButton.Background = normalColor;
            FocusNearButton.Background = normalColor;
            FocusFarButton.Background = normalColor;
            IrisOpenButton.Background = normalColor;
            IrisCloseButton.Background = normalColor;
        }

        private void GotoPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ptzManager == null) return;

            try
            {
                if (int.TryParse(PresetNumberTextBox.Text, out int presetNumber) && presetNumber >= 1 && presetNumber <= 255)
                {
                    if (_ptzManager.GotoPreset(presetNumber))
                    {
                        AddStatusMessage($"轉到預置點 {presetNumber}");
                    }
                    else
                    {
                        AddStatusMessage($"轉到預置點 {presetNumber} 失敗");
                    }
                }
                else
                {
                    AddStatusMessage("請輸入有效的預置點編號 (1-255)");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"轉到預置點時發生錯誤：{ex.Message}");
            }
        }

        private void SetPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ptzManager == null) return;

            try
            {
                if (int.TryParse(PresetNumberTextBox.Text, out int presetNumber) && presetNumber >= 1 && presetNumber <= 255)
                {
                    if (_ptzManager.SetPreset(presetNumber))
                    {
                        AddStatusMessage($"設定預置點 {presetNumber}");
                    }
                    else
                    {
                        AddStatusMessage($"設定預置點 {presetNumber} 失敗");
                    }
                }
                else
                {
                    AddStatusMessage("請輸入有效的預置點編號 (1-255)");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"設定預置點時發生錯誤：{ex.Message}");
            }
        }

        private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ptzManager == null) return;

            try
            {
                if (int.TryParse(PresetNumberTextBox.Text, out int presetNumber) && presetNumber >= 1 && presetNumber <= 255)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"確定要刪除預置點 {presetNumber} 嗎？",
                        "確認刪除",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        if (_ptzManager.DeletePreset(presetNumber))
                        {
                            AddStatusMessage($"刪除預置點 {presetNumber}");
                        }
                        else
                        {
                            AddStatusMessage($"刪除預置點 {presetNumber} 失敗");
                        }
                    }
                }
                else
                {
                    AddStatusMessage("請輸入有效的預置點編號 (1-255)");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"刪除預置點時發生錯誤：{ex.Message}");
            }
        }

        private static string GetControlTypeDisplayName(string controlType)
        {
            return controlType switch
            {
                "Up" => "向上移動",
                "Down" => "向下移動",
                "Left" => "向左移動",
                "Right" => "向右移動",
                "TopLeft" => "向左上移動",
                "TopRight" => "向右上移動",
                "BottomLeft" => "向左下移動",
                "BottomRight" => "向右下移動",
                "ZoomIn" => "放大",
                "ZoomOut" => "縮小",
                "FocusNear" => "近焦",
                "FocusFar" => "遠焦",
                "IrisOpen" => "光圈開",
                "IrisClose" => "光圈關",
                _ => controlType
            };
        }

        private void AddStatusMessage(string message)
        {
            try
            {
                string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
                StatusTextBlock.Text += timestampedMessage + "\n";

                // 保持狀態框在合理長度
                var lines = StatusTextBlock.Text.Split('\n');
                if (lines.Length > 50)
                {
                    StatusTextBlock.Text = string.Join("\n", lines.Skip(lines.Length - 40));
                }

                // 滾動到底部
                if (StatusTextBlock.Parent is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加狀態訊息時發生錯誤：{ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // 停止所有移動
                StopAllMovement();
                _ptzManager = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"關閉PTZ控制視窗時發生錯誤：{ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
    }
}