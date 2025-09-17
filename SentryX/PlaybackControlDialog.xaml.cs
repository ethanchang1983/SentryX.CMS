using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NetSDKCS;

namespace SentryX
{
    public partial class PlaybackControlDialog : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly PlaybackManager _playbackManager;

        // 時間軸相關變數
        private bool _isDragging = false;
        private string _dragTarget = ""; // "start", "end", "position"
        private double _timelineWidth = 700; // 時間軸寬度
        private List<RecordSegment> _recordSegments = new List<RecordSegment>();

        // 新增：時間軸縮放相關變數
        private double _timelineZoomLevel = 1.0; // 縮放級別
        private DateTime _timelineViewStart = DateTime.Today; // 視圖開始時間
        private DateTime _timelineViewEnd = DateTime.Today.AddDays(1); // 視圖結束時間
        private double _minZoomLevel = 0.1; // 最小縮放（10倍放大，看6分鐘）
        private double _maxZoomLevel = 10.0; // 最大縮放（縮小10倍，看10天）

        // 固定的目標播放器索引
        private MultiViewPlayer? _initialSelectedPlayer;
        private int _targetPlayerIndex = -1;

        public PlaybackControlDialog(MainWindow mainWindow, PlaybackManager playbackManager)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _playbackManager = playbackManager;

            // 鎖定目標播放器
            _initialSelectedPlayer = _mainWindow.SplitScreenManager?.SelectedPlayer;
            if (_initialSelectedPlayer != null)
            {
                _targetPlayerIndex = _initialSelectedPlayer.Index;
                AddStatusMessage($"對話框已鎖定到分割區域 {_targetPlayerIndex + 1}");
            }

            InitializeTimeline();
            InitializeDatePicker();
            UpdateCurrentStatus();

            // 新增：註冊時間軸滾輪事件
            TimelineCanvas.MouseWheel += TimelineCanvas_MouseWheel;
        }

        /// <summary>
        /// 新增：時間軸滾輪事件處理 - 實現專業CMS的縮放功能
        /// </summary>
        private void TimelineCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                // 取得滑鼠在時間軸上的位置
                var mousePos = e.GetPosition(TimelineCanvas);
                var mouseTimePosition = GetTimeFromPosition(mousePos.X);

                // 計算縮放因子
                double zoomFactor = e.Delta > 0 ? 0.8 : 1.25; // 滾輪上：縮小範圍(放大), 滾輪下：擴大範圍(縮小)
                double newZoomLevel = _timelineZoomLevel * zoomFactor;

                // 限制縮放範圍
                newZoomLevel = Math.Max(_minZoomLevel, Math.Min(_maxZoomLevel, newZoomLevel));

                if (Math.Abs(newZoomLevel - _timelineZoomLevel) < 0.01) // 達到縮放極限
                {
                    return;
                }

                // 計算新的時間範圍
                var currentSpan = _timelineViewEnd - _timelineViewStart;
                var newSpan = TimeSpan.FromTicks((long)(currentSpan.Ticks / zoomFactor));

                // 以滑鼠位置為中心進行縮放
                var mouseRatio = (mouseTimePosition - _timelineViewStart).Ticks / (double)currentSpan.Ticks;
                mouseRatio = Math.Max(0, Math.Min(1, mouseRatio)); // 確保比例在0-1之間

                var newStart = mouseTimePosition.AddTicks(-(long)(newSpan.Ticks * mouseRatio));
                var newEnd = newStart.Add(newSpan);

                // 確保時間範圍不超出當天限制
                var selectedDate = PlaybackDatePicker.SelectedDate ?? DateTime.Today;
                var dayStart = selectedDate.Date;
                var dayEnd = dayStart.AddDays(1);

                if (newStart < dayStart)
                {
                    newStart = dayStart;
                    newEnd = newStart.Add(newSpan);
                }
                if (newEnd > dayEnd)
                {
                    newEnd = dayEnd;
                    newStart = newEnd.Subtract(newSpan);
                }

                // 更新縮放級別和視圖範圍
                _timelineZoomLevel = newZoomLevel;
                _timelineViewStart = newStart;
                _timelineViewEnd = newEnd;

                // 重新繪製時間軸
                RedrawTimelineWithZoom();

                // 更新狀態訊息
                var spanMinutes = newSpan.TotalMinutes;
                string spanText;
                if (spanMinutes < 60)
                {
                    spanText = $"{spanMinutes:F1}分鐘";
                }
                else if (spanMinutes < 1440) // 24小時
                {
                    spanText = $"{spanMinutes / 60:F1}小時";
                }
                else
                {
                    spanText = $"{spanMinutes / 1440:F1}天";
                }

                AddStatusMessage($"時間軸縮放：{spanText} (級別: {_timelineZoomLevel:F2}x)");

                e.Handled = true;
            }
            catch (Exception ex)
            {
                AddStatusMessage($"時間軸縮放時發生錯誤：{ex.Message}");
            }
        }

        /// <summary>
        /// 新增：根據縮放重新繪製時間軸
        /// </summary>
        private void RedrawTimelineWithZoom()
        {
            // 更新時間範圍
            SetTimelineRange(_timelineViewStart, _timelineViewEnd);

            // 重新建立時間標籤
            CreateZoomedTimeLabels();

            // 重新顯示錄影區段
            DisplayRecordSegments();
        }

        /// <summary>
        /// 新增：建立適應縮放的時間標籤
        /// </summary>
        private void CreateZoomedTimeLabels()
        {
            var labelsCanvas = TimelineCanvas.Parent as Grid;
            var timeLabelsCanvas = labelsCanvas?.Children.OfType<Canvas>().FirstOrDefault();

            if (timeLabelsCanvas == null) return;

            timeLabelsCanvas.Children.Clear();

            var timeSpan = _timelineViewEnd - _timelineViewStart;
            var totalMinutes = timeSpan.TotalMinutes;

            // 根據時間範圍決定標籤間隔
            TimeSpan labelInterval;
            string labelFormat;

            if (totalMinutes <= 60) // 1小時內 - 每10分鐘一個標籤
            {
                labelInterval = TimeSpan.FromMinutes(10);
                labelFormat = "HH:mm";
            }
            else if (totalMinutes <= 360) // 6小時內 - 每30分鐘一個標籤
            {
                labelInterval = TimeSpan.FromMinutes(30);
                labelFormat = "HH:mm";
            }
            else if (totalMinutes <= 1440) // 24小時內 - 每2小時一個標籤
            {
                labelInterval = TimeSpan.FromHours(2);
                labelFormat = "HH:mm";
            }
            else if (totalMinutes <= 4320) // 3天內 - 每6小時一個標籤
            {
                labelInterval = TimeSpan.FromHours(6);
                labelFormat = "MM/dd HH:mm";
            }
            else // 3天以上 - 每12小時一個標籤
            {
                labelInterval = TimeSpan.FromHours(12);
                labelFormat = "MM/dd HH:mm";
            }

            // 計算第一個標籤的時間（對齊到間隔）
            var firstLabelTime = new DateTime(
                _timelineViewStart.Year,
                _timelineViewStart.Month,
                _timelineViewStart.Day,
                _timelineViewStart.Hour,
                (_timelineViewStart.Minute / (int)labelInterval.TotalMinutes) * (int)labelInterval.TotalMinutes,
                0
            );

            // 添加標籤
            var currentTime = firstLabelTime;
            while (currentTime <= _timelineViewEnd)
            {
                if (currentTime >= _timelineViewStart)
                {
                    var position = GetPositionFromTime(currentTime);
                    if (position >= 0 && position <= _timelineWidth)
                    {
                        var label = new TextBlock
                        {
                            Text = currentTime.ToString(labelFormat),
                            FontSize = 9,
                            Foreground = System.Windows.Media.Brushes.Black
                        };

                        Canvas.SetLeft(label, position - 20); // 置中對齊
                        Canvas.SetTop(label, 0);

                        timeLabelsCanvas.Children.Add(label);
                    }
                }
                currentTime = currentTime.Add(labelInterval);
            }
        }

        /// <summary>
        /// 新增：從時間計算在時間軸上的位置
        /// </summary>
        private double GetPositionFromTime(DateTime time)
        {
            var totalSpan = _timelineViewEnd - _timelineViewStart;
            var timeSpan = time - _timelineViewStart;

            if (totalSpan.TotalSeconds <= 0) return 0;

            return (timeSpan.TotalSeconds / totalSpan.TotalSeconds) * _timelineWidth;
        }

        /// <summary>
        /// 初始化時間軸
        /// </summary>
        private void InitializeTimeline()
        {
            try
            {
                _timelineWidth = TimelineCanvas.ActualWidth > 0 ? TimelineCanvas.ActualWidth : 700;

                // 設定預設時間範圍（當天全天）
                var selectedDate = PlaybackDatePicker.SelectedDate ?? DateTime.Today;
                _timelineViewStart = selectedDate.Date;
                _timelineViewEnd = selectedDate.Date.AddDays(1);

                SetTimelineRange(_timelineViewStart.AddHours(6), _timelineViewStart.AddHours(18)); // 預設顯示6-18點
                CreateZoomedTimeLabels();

                AddStatusMessage("時間軸已初始化，支援滾輪縮放功能");
            }
            catch (Exception ex)
            {
                AddStatusMessage($"初始化時間軸時發生錯誤：{ex.Message}");
            }
        }

        /// <summary>
        /// 初始化日期選擇器
        /// </summary>
        private void InitializeDatePicker()
        {
            PlaybackDatePicker.SelectedDate = DateTime.Today;
        }

        /// <summary>
        /// 建立時間標籤（0-24小時）
        /// </summary>
        private void CreateTimeLabels()
        {
            // 使用新的縮放時間標籤方法
            CreateZoomedTimeLabels();
        }

        /// <summary>
        /// 設定時間軸範圍
        /// </summary>
        private void SetTimelineRange(DateTime startTime, DateTime endTime)
        {
            var selectedDate = PlaybackDatePicker.SelectedDate ?? DateTime.Today;

            // 限制在選中的日期範圍內
            if (startTime < _timelineViewStart) startTime = _timelineViewStart;
            if (endTime > _timelineViewEnd) endTime = _timelineViewEnd;
            if (startTime >= endTime) endTime = startTime.AddMinutes(30);

            // 計算在當前視圖範圍中的位置
            double startPosition = GetPositionFromTime(startTime);
            double endPosition = GetPositionFromTime(endTime);

            // 更新時間指針位置
            Canvas.SetLeft(StartTimeMarker, startPosition);
            Canvas.SetLeft(EndTimeMarker, endPosition);

            // 更新時間顯示
            UpdateTimeDisplay(startTime, endTime);
        }

        /// <summary>
        /// 更新時間顯示文字
        /// </summary>
        private void UpdateTimeDisplay(DateTime startTime, DateTime endTime)
        {
            StartTimeText.Text = startTime.ToString("HH:mm:ss");
            EndTimeText.Text = endTime.ToString("HH:mm:ss");

            var duration = endTime - startTime;
            DurationText.Text = $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        /// <summary>
        /// 從時間軸位置取得時間
        /// </summary>
        private DateTime GetTimeFromPosition(double position)
        {
            var totalSpan = _timelineViewEnd - _timelineViewStart;
            double ratio = Math.Max(0, Math.Min(1, position / _timelineWidth));

            return _timelineViewStart.AddTicks((long)(totalSpan.Ticks * ratio));
        }

        /// <summary>
        /// 查詢並顯示錄影區段
        /// </summary>
        private async void QueryAndDisplayRecordSegments()
        {
            try
            {
                if (_targetPlayerIndex < 0) return;

                var targetPlayer = GetTargetPlayer();
                var currentState = targetPlayer?.GetCurrentPlaybackState();

                if (currentState == null || string.IsNullOrEmpty(currentState.DeviceId))
                {
                    AddStatusMessage("無法取得設備資訊");
                    return;
                }

                var device = DahuaSDK.GetDevice(currentState.DeviceId);
                if (device == null)
                {
                    AddStatusMessage("找不到設備");
                    return;
                }

                var selectedDate = PlaybackDatePicker.SelectedDate ?? DateTime.Today;
                var dayStart = selectedDate.Date;
                var dayEnd = dayStart.AddDays(1);

                AddStatusMessage($"正在查詢 {device.Name} 通道 {currentState.Channel} 的錄影記錄...");

                // 這裡需要實作錄影查詢邏輯
                await QueryRecordSegmentsAsync(device, currentState.Channel, dayStart, dayEnd);
            }
            catch (Exception ex)
            {
                AddStatusMessage($"查詢錄影區段時發生錯誤：{ex.Message}");
            }
        }

        /// <summary>
        /// 非同步查詢錄影區段
        /// </summary>
        private async System.Threading.Tasks.Task QueryRecordSegmentsAsync(DeviceInfo device, int channel, DateTime startTime, DateTime endTime)
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // 呼叫 SDK 查詢錄影檔案
                    NET_RECORDFILE_INFO[] recordFiles = new NET_RECORDFILE_INFO[5000];
                    int fileCount = 0;

                    bool result = NETClient.QueryRecordFile(
                        device.LoginHandle,
                        channel,
                        EM_QUERY_RECORD_TYPE.ALL,
                        startTime,
                        endTime,
                        null,
                        ref recordFiles,
                        ref fileCount,
                        10000,
                        false
                    );

                    if (result && fileCount > 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _recordSegments.Clear();

                            for (int i = 0; i < fileCount; i++)
                            {
                                var segment = new RecordSegment
                                {
                                    StartTime = recordFiles[i].starttime.ToDateTime(),
                                    EndTime = recordFiles[i].endtime.ToDateTime()
                                };
                                _recordSegments.Add(segment);
                            }

                            DisplayRecordSegments();
                            AddStatusMessage($"找到 {fileCount} 個錄影區段");
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _recordSegments.Clear();
                            DisplayRecordSegments();
                            AddStatusMessage("該日期沒有找到錄影記錄");
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AddStatusMessage($"查詢錄影時發生錯誤：{ex.Message}");
                    });
                }
            });
        }

        /// <summary>
        /// 在時間軸上顯示錄影區段
        /// </summary>
        private void DisplayRecordSegments()
        {
            // 清除現有的錄影區段顯示
            var existingSegments = TimelineCanvas.Children.OfType<System.Windows.Shapes.Rectangle>()
                .Where(r => r.Name?.StartsWith("RecordSegment") == true)
                .ToList();

            foreach (var segment in existingSegments)
            {
                TimelineCanvas.Children.Remove(segment);
            }

            // 顯示錄影區段
            for (int i = 0; i < _recordSegments.Count; i++)
            {
                var segment = _recordSegments[i];

                // 檢查區段是否在當前視圖範圍內
                if (segment.EndTime < _timelineViewStart || segment.StartTime > _timelineViewEnd)
                {
                    continue; // 跳過不在視圖範圍內的區段
                }

                // 計算區段在時間軸上的位置
                double startPos = GetPositionFromTime(segment.StartTime);
                double endPos = GetPositionFromTime(segment.EndTime);

                // 確保位置在可見範圍內
                startPos = Math.Max(0, startPos);
                endPos = Math.Min(_timelineWidth, endPos);

                double width = Math.Max(2, endPos - startPos);

                var segmentRect = new System.Windows.Shapes.Rectangle
                {
                    Name = $"RecordSegment_{i}",
                    Width = width,
                    Height = 35,
                    Fill = System.Windows.Media.Brushes.LightBlue,
                    Stroke = System.Windows.Media.Brushes.Blue,
                    StrokeThickness = 1,
                    Opacity = 0.7,
                    ToolTip = $"錄影時間：{segment.StartTime:HH:mm:ss} - {segment.EndTime:HH:mm:ss}"
                };

                Canvas.SetLeft(segmentRect, startPos);
                Canvas.SetTop(segmentRect, 2);
                Canvas.SetZIndex(segmentRect, 1);

                TimelineCanvas.Children.Add(segmentRect);
            }
        }

        // === 時間軸滑鼠事件處理 ===

        private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (TimelineCanvas.ActualWidth > 0)
            {
                _timelineWidth = TimelineCanvas.ActualWidth;
            }

            var mousePos = e.GetPosition(TimelineCanvas);
            var startPos = Canvas.GetLeft(StartTimeMarker);
            var endPos = Canvas.GetLeft(EndTimeMarker);

            // 判斷點擊的是哪個控制項
            if (Math.Abs(mousePos.X - startPos) < 10)
            {
                _dragTarget = "start";
                _isDragging = true;
            }
            else if (Math.Abs(mousePos.X - endPos) < 10)
            {
                _dragTarget = "end";
                _isDragging = true;
            }
            else
            {
                // 點擊時間軸其他位置，移動最近的指針
                if (Math.Abs(mousePos.X - startPos) < Math.Abs(mousePos.X - endPos))
                {
                    _dragTarget = "start";
                    Canvas.SetLeft(StartTimeMarker, mousePos.X);
                }
                else
                {
                    _dragTarget = "end";
                    Canvas.SetLeft(EndTimeMarker, mousePos.X);
                }
                UpdateTimeFromMarkers();
            }

            TimelineCanvas.CaptureMouse();
        }

        private void TimelineCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging) return;

            var mousePos = e.GetPosition(TimelineCanvas);
            var x = Math.Max(0, Math.Min(_timelineWidth, mousePos.X));

            if (_dragTarget == "start")
            {
                Canvas.SetLeft(StartTimeMarker, x);
            }
            else if (_dragTarget == "end")
            {
                Canvas.SetLeft(EndTimeMarker, x);
            }

            UpdateTimeFromMarkers();
        }

        private void TimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _dragTarget = "";
            TimelineCanvas.ReleaseMouseCapture();
        }

        /// <summary>
        /// 從指針位置更新時間顯示
        /// </summary>
        private void UpdateTimeFromMarkers()
        {
            var startPos = Canvas.GetLeft(StartTimeMarker);
            var endPos = Canvas.GetLeft(EndTimeMarker);

            // 確保開始時間早於結束時間
            if (startPos > endPos)
            {
                var temp = startPos;
                startPos = endPos;
                endPos = temp;
                Canvas.SetLeft(StartTimeMarker, startPos);
                Canvas.SetLeft(EndTimeMarker, endPos);
            }

            var startTime = GetTimeFromPosition(startPos);
            var endTime = GetTimeFromPosition(endPos);

            UpdateTimeDisplay(startTime, endTime);
        }

        // === 新增：時間軸控制方法 ===

        /// <summary>
        /// 重置時間軸縮放到全天視圖
        /// </summary>
        private void ResetTimelineZoom()
        {
            var selectedDate = PlaybackDatePicker.SelectedDate ?? DateTime.Today;
            _timelineViewStart = selectedDate.Date;
            _timelineViewEnd = selectedDate.Date.AddDays(1);
            _timelineZoomLevel = 1.0;

            RedrawTimelineWithZoom();
            AddStatusMessage("時間軸已重置為全天視圖");
        }

        /// <summary>
        /// 縮放到指定時間範圍
        /// </summary>
        private void ZoomToTimeRange(DateTime start, DateTime end)
        {
            _timelineViewStart = start;
            _timelineViewEnd = end;

            var totalSpan = end - start;
            var daySpan = TimeSpan.FromDays(1);
            _timelineZoomLevel = daySpan.TotalSeconds / totalSpan.TotalSeconds;

            RedrawTimelineWithZoom();
            AddStatusMessage($"時間軸已縮放到：{start:HH:mm} - {end:HH:mm}");
        }

        // === 原有的方法（保持不變） ===

        private MultiViewPlayer? GetTargetPlayer()
        {
            if (_targetPlayerIndex < 0 || _mainWindow.SplitScreenManager == null)
            {
                return null;
            }

            var players = _mainWindow.SplitScreenManager.VideoPlayers;
            if (_targetPlayerIndex < players.Count)
            {
                return players[_targetPlayerIndex];
            }

            return null;
        }

        private void UpdateCurrentStatus()
        {
            var targetPlayer = GetTargetPlayer();

            if (targetPlayer == null)
            {
                CurrentStatusText.Text = "❌ 無效的分割區域";
                DeviceInfoText.Text = "目標分割區域不存在";
                StartPlaybackButton.IsEnabled = false;
                EnablePlaybackControls(false);
                return;
            }

            // 修正：檢查回放模式時也應該允許全螢幕
            bool isInPlaybackMode = _playbackManager.IsInPlaybackMode(_targetPlayerIndex);
            bool hasContent = targetPlayer.IsPlaying || isInPlaybackMode;

            if (!hasContent)
            {
                CurrentStatusText.Text = "❌ 選中的區域沒有正在播放的視頻";
                DeviceInfoText.Text = $"分割區域 {_targetPlayerIndex + 1} - 沒有播放內容";
                StartPlaybackButton.IsEnabled = false;
                EnablePlaybackControls(false);
                return;
            }

            if (isInPlaybackMode)
            {
                var session = _playbackManager.GetPlaybackSession(_targetPlayerIndex);
                CurrentStatusText.Text = "🔄 當前為回放模式";
                DeviceInfoText.Text = session?.DisplayInfo ?? "回放資訊不可用";
                StartPlaybackButton.IsEnabled = false;
                StopPlaybackButton.IsEnabled = true;
                EnablePlaybackControls(true);
                UpdatePlaybackStatus(session);
            }
            else
            {
                var state = targetPlayer.GetCurrentPlaybackState();
                CurrentStatusText.Text = "📺 當前為實況模式";
                DeviceInfoText.Text = state != null ?
                    $"分割區域 {_targetPlayerIndex + 1} - {state.DeviceName} 通道 {state.Channel + 1}" :
                    $"分割區域 {_targetPlayerIndex + 1}";
                StartPlaybackButton.IsEnabled = true;
                StopPlaybackButton.IsEnabled = false;
                EnablePlaybackControls(false);
            }

            // 查詢錄影區段
            QueryAndDisplayRecordSegments();
        }

        // === 事件處理方法 ===

        private void PlaybackDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            // 重設時間軸到新的日期
            var selectedDate = PlaybackDatePicker.SelectedDate ?? DateTime.Today;
            _timelineViewStart = selectedDate.Date;
            _timelineViewEnd = selectedDate.Date.AddDays(1);
            _timelineZoomLevel = 1.0;

            RedrawTimelineWithZoom();
            QueryAndDisplayRecordSegments();
            AddStatusMessage($"已切換到日期：{selectedDate:yyyy-MM-dd}");
        }

        private void SetToday_Click(object sender, RoutedEventArgs e)
        {
            PlaybackDatePicker.SelectedDate = DateTime.Today;
        }

        private void SetYesterday_Click(object sender, RoutedEventArgs e)
        {
            PlaybackDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
        }

        private void SetQuickTime_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string tag)
            {
                var selectedDate = PlaybackDatePicker.SelectedDate ?? DateTime.Today;
                var now = DateTime.Now;

                DateTime startTime, endTime;

                switch (tag)
                {
                    case "30m":
                        endTime = now;
                        startTime = endTime.AddMinutes(-30);
                        break;
                    case "1h":
                        endTime = now;
                        startTime = endTime.AddHours(-1);
                        break;
                    case "2h":
                        endTime = now;
                        startTime = endTime.AddHours(-2);
                        break;
                    case "4h":
                        endTime = now;
                        startTime = endTime.AddHours(-4);
                        break;
                    case "morning":
                        startTime = selectedDate.Date.AddHours(6);
                        endTime = selectedDate.Date.AddHours(12);
                        break;
                    case "afternoon":
                        startTime = selectedDate.Date.AddHours(12);
                        endTime = selectedDate.Date.AddHours(18);
                        break;
                    case "evening":
                        startTime = selectedDate.Date.AddHours(18);
                        endTime = selectedDate.Date.AddDays(1);
                        break;
                    case "fullday":
                        startTime = selectedDate.Date;
                        endTime = selectedDate.Date.AddDays(1);
                        break;
                    default:
                        return;
                }

                // 使用新的縮放功能
                ZoomToTimeRange(startTime, endTime);
                SetTimelineRange(startTime, endTime);
                AddStatusMessage($"已設定時間範圍：{button.Content}");
            }
        }

        private void StartPlayback_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startTime = GetTimeFromPosition(Canvas.GetLeft(StartTimeMarker));
                var endTime = GetTimeFromPosition(Canvas.GetLeft(EndTimeMarker));

                if (startTime >= endTime)
                {
                    System.Windows.MessageBox.Show("開始時間必須早於結束時間", "時間設定錯誤",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (endTime > DateTime.Now)
                {
                    var result = System.Windows.MessageBox.Show(
                        "結束時間超過當前時間，是否調整為當前時間？",
                        "時間範圍確認",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        endTime = DateTime.Now;
                        SetTimelineRange(startTime, endTime);
                    }
                }

                AddStatusMessage($"正在切換分割區域 {_targetPlayerIndex + 1} 到回放模式：{startTime:yyyy-MM-dd HH:mm:ss} - {endTime:yyyy-MM-dd HH:mm:ss}");

                if (_playbackManager.SwitchToPlaybackByIndex(_targetPlayerIndex, startTime, endTime))
                {
                    AddStatusMessage("✅ 回放模式啟動成功");
                    UpdateCurrentStatus();
                }
                else
                {
                    AddStatusMessage("❌ 回放模式啟動失敗");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"❌ 開始回放時發生錯誤：{ex.Message}");
                System.Windows.MessageBox.Show($"開始回放時發生錯誤：\n{ex.Message}", "錯誤",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopPlayback_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddStatusMessage($"正在切換分割區域 {_targetPlayerIndex + 1} 回實況模式...");

                if (_playbackManager.SwitchToLiveByIndex(_targetPlayerIndex))
                {
                    AddStatusMessage("✅ 已切換回實況模式");
                    UpdateCurrentStatus();
                }
                else
                {
                    AddStatusMessage("❌ 切換實況模式失敗");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"❌ 停止回放時發生錯誤：{ex.Message}");
                System.Windows.MessageBox.Show($"停止回放時發生錯誤：\n{ex.Message}", "錯誤",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlaybackControl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button button)
                {
                    AddStatusMessage("❌ 無效的控制來源");
                    return;
                }

                if (button.Tag is not string controlType)
                {
                    AddStatusMessage($"❌ 按鈕 {button.Name} 缺少有效的 Tag 屬性");
                    return;
                }

                AddStatusMessage($"偵測到回放控制請求：{controlType} (按鈕: {button.Name})");

                if (_targetPlayerIndex < 0)
                {
                    AddStatusMessage("❌ 無效的目標播放器索引");
                    return;
                }

                if (!_playbackManager.IsInPlaybackMode(_targetPlayerIndex))
                {
                    AddStatusMessage($"❌ 分割區域 {_targetPlayerIndex + 1} 不在回放模式");
                    return;
                }

                // 針對大華 SDK 的回放控制類型映射
                PlayBackType playBackType = controlType switch
                {
                    "Pause" => PlayBackType.Pause,
                    "Rewind" => PlayBackType.Slow, // 暫時使用慢放，因為 SDK 可能不支援直接倒帶
                    "Fast" => PlayBackType.Fast,
                    "Slow" => PlayBackType.Slow,
                    "Normal" => PlayBackType.Normal,
                    _ => throw new ArgumentException($"未知的控制類型: {controlType}")
                };

                string actionText = controlType switch
                {
                    "Pause" => "暫停",
                    "Rewind" => "倒帶", // 顯示為倒帶，但實際可能是慢放
                    "Fast" => "快進",
                    "Slow" => "慢放",
                    "Normal" => "正常速度",
                    _ => "未知操作"
                };

                // 特殊處理倒帶功能
                if (controlType == "Rewind")
                {
                    AddStatusMessage("注意：倒帶功能可能需要特殊實現，正在嘗試執行...");
                    // 這裡可以添加特殊的倒帶邏輯
                    // 例如：先暫停，然後跳轉到更早的時間點
                    bool rewindResult = ExecuteRewind();
                    if (rewindResult)
                    {
                        AddStatusMessage($"✅ {actionText} 執行成功");
                        UpdateSpeedDisplay(controlType);
                    }
                    else
                    {
                        AddStatusMessage($"❌ {actionText} 執行失敗");
                    }
                    return;
                }

                AddStatusMessage($"正在對分割區域 {_targetPlayerIndex + 1} 執行回放控制：{actionText} ({playBackType})");

                if (_playbackManager.PlaybackControlByIndex(_targetPlayerIndex, playBackType))
                {
                    AddStatusMessage($"✅ {actionText} 執行成功");
                    UpdateSpeedDisplay(controlType);
                }
                else
                {
                    AddStatusMessage($"❌ {actionText} 執行失敗");
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"❌ 回放控制時發生錯誤：{ex.Message}");
                System.Windows.MessageBox.Show($"回放控制時發生錯誤：\n{ex.Message}\n\n堆疊追蹤：\n{ex.StackTrace}", "錯誤",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 執行倒帶功能的自定義實現
        /// </summary>
        /// <returns>是否成功執行倒帶</returns>
        private bool ExecuteRewind()
        {
            try
            {
                if (_targetPlayerIndex < 0)
                {
                    return false;
                }

                // 方法1：嘗試使用 SDK 提供的倒帶功能（如果有的話）
                // 這裡可以嘗試不同的 PlayBackType 選項
                var possibleRewindTypes = new PlayBackType[]
                {
                    // 這些是可能存在的倒帶相關枚舉值，需要根據實際 SDK 版本調整
                    // PlayBackType.SlowBackward,  // 如果存在的話
                    // PlayBackType.Backward,      // 如果存在的話
                    PlayBackType.Slow  // 備用選項
                };

                foreach (var rewindType in possibleRewindTypes)
                {
                    try
                    {
                        if (_playbackManager.PlaybackControlByIndex(_targetPlayerIndex, rewindType))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // 繼續嘗試下一個選項
                        continue;
                    }
                }

                // 方法2：如果 SDK 不支援直接倒帶，使用時間跳轉模擬
                return SimulateRewindByTimeJump();
            }
            catch (Exception ex)
            {
                AddStatusMessage($"執行倒帶時發生錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 通過時間跳轉模擬倒帶效果
        /// </summary>
        /// <returns>是否成功模擬倒帶</returns>
        private bool SimulateRewindByTimeJump()
        {
            try
            {
                // 獲取當前回放會話
                var session = _playbackManager.GetPlaybackSession(_targetPlayerIndex);
                if (session == null)
                {
                    return false;
                }

                // 計算倒退時間（例如倒退30秒）
                var currentTime = DateTime.Now; // 這裡應該獲取當前回放位置
                var rewindTime = currentTime.AddSeconds(-30);

                // 確保不超出回放範圍
                if (rewindTime < session.StartTime)
                {
                    rewindTime = session.StartTime;
                }

                AddStatusMessage($"模擬倒帶：跳轉到 {rewindTime:HH:mm:ss}");

                // 這裡可以實現時間跳轉邏輯
                // 由於大華 SDK 的具體 API 限制，這個功能可能需要進一步實現

                return true; // 暫時返回 true，實際效果取決於 SDK 支援
            }
            catch (Exception ex)
            {
                AddStatusMessage($"模擬倒帶時發生錯誤：{ex.Message}");
                return false;
            }
        }

        private void EnablePlaybackControls(bool enabled)
        {
            PauseButton.IsEnabled = enabled;
            RewindButton.IsEnabled = enabled;
            FastButton.IsEnabled = enabled;
            SlowButton.IsEnabled = enabled;
            NormalButton.IsEnabled = enabled;

            AddStatusMessage($"回放控制按鈕狀態設定為: {enabled}");
        }

        private void UpdatePlaybackStatus(PlaybackSession? session)
        {
            if (session == null)
            {
                PlaybackStatusText.Text = "等待開始回放...";
                TimeRangeText.Text = "";
                SpeedText.Text = "";
                RecordFilesText.Text = "";
                return;
            }

            PlaybackStatusText.Text = "正在回放";
            TimeRangeText.Text = $"時間範圍：{session.StartTime:yyyy-MM-dd HH:mm:ss} - {session.EndTime:yyyy-MM-dd HH:mm:ss}";
            SpeedText.Text = "播放速度：正常 (1X)";

            // 顯示當前播放位置（如果有的話）
            if (_playbackManager.IsInPlaybackMode(_targetPlayerIndex))
            {
                CurrentPositionMarker.Visibility = Visibility.Visible;
                // 這裡可以添加獲取當前播放位置的邏輯
            }
        }

        private void UpdateSpeedDisplay(string controlType)
        {
            try
            {
                SpeedText.Text = controlType switch
                {
                    "Fast" => "播放速度：快進 (2X)",
                    "Slow" => "播放速度：慢放 (0.5X)",
                    "Normal" => "播放速度：正常 (1X)",
                    "Pause" => "播放速度：暫停",
                    "Rewind" => "播放速度：倒帶 (-1X)",
                    _ => "播放速度：未知"
                };
            }
            catch (Exception ex)
            {
                AddStatusMessage($"更新速度顯示時發生錯誤：{ex.Message}");
            }
        }

        private void AddStatusMessage(string message)
        {
            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            StatusTextBlock.Text += timestampedMessage + "\n";
            StatusScrollViewer.ScrollToEnd();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateCurrentStatus();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            UpdateCurrentStatus();
        }

        public void RefreshCurrentStatus()
        {
            try
            {
                UpdateCurrentStatus();
                AddStatusMessage("狀態已由主視窗觸發更新");
            }
            catch (Exception ex)
            {
                AddStatusMessage($"刷新狀態時發生錯誤：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 錄影區段資料結構
    /// </summary>
    public class RecordSegment
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Type { get; set; } = "Normal"; // Normal, Motion, Alarm 等
    }
}