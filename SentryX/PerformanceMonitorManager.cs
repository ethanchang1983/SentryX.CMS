using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;

namespace SentryX
{
    public class PerformanceMonitorManager
    {
        private readonly MainWindow _mainWindow;
        private readonly SplitScreenManager _splitScreenManager;
        private DispatcherTimer? _videoInfoTimer;
        private DispatcherTimer? _performanceTimer;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _memoryCounter;

        public PerformanceMonitorManager(MainWindow mainWindow, SplitScreenManager splitScreenManager)
        {
            _mainWindow = mainWindow;
            _splitScreenManager = splitScreenManager;
        }

        public void StartMonitoring()
        {
            if (!_mainWindow.UIManager.IsUIInitialized) return;

            SetupVideoInfoTimer();
            SetupPerformanceMonitoring();
        }

        private void SetupVideoInfoTimer()
        {
            _videoInfoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _videoInfoTimer.Tick += UpdateVideoInfo;
            _videoInfoTimer.Start();
        }

        private void SetupPerformanceMonitoring()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");

                _performanceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _performanceTimer.Tick += UpdatePerformanceInfo;
                _performanceTimer.Start();

                _mainWindow.ShowMessage("🎯 性能監控已啟動");
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 性能監控初始化失敗: {ex.Message}");
            }
        }

        private void UpdateVideoInfo(object? sender, EventArgs e)
        {
            if (!_mainWindow.UIManager.IsUIInitialized) return;

            try
            {
                var playingPlayers = _splitScreenManager.VideoPlayers.Where(p => p.IsPlaying).ToList();

                if (playingPlayers.Count > 0)
                {
                    var firstPlayer = playingPlayers.First();
                    if (firstPlayer.VideoInfo != null)
                    {
                        var info = firstPlayer.VideoInfo;
                        if (_mainWindow.ResolutionTextBlock != null)
                            _mainWindow.ResolutionTextBlock.Text = $"{info.Width}x{info.Height}";
                        if (_mainWindow.FpsTextBlock != null)
                            _mainWindow.FpsTextBlock.Text = $"{info.Fps:F1}";
                        if (_mainWindow.BitrateTextBlock != null)
                            _mainWindow.BitrateTextBlock.Text = $"{info.Bitrate:F1} kbps";
                    }

                    if (_mainWindow.PlayingCountTextBlock != null)
                        _mainWindow.PlayingCountTextBlock.Text = playingPlayers.Count.ToString();

                    double totalBitrate = playingPlayers.Where(p => p.VideoInfo != null)
                                                      .Sum(p => p.VideoInfo!.Bitrate);
                    if (_mainWindow.TotalBitrateTextBlock != null)
                        _mainWindow.TotalBitrateTextBlock.Text = $"{totalBitrate:F1} kbps";
                }
                else
                {
                    ResetVideoInfoDisplay();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateVideoInfo 發生錯誤: {ex.Message}");
            }
        }

        private void UpdatePerformanceInfo(object? sender, EventArgs e)
        {
            if (!_mainWindow.UIManager.IsUIInitialized) return;

            try
            {
                if (_cpuCounter != null && _memoryCounter != null)
                {
                    float cpuUsage = _cpuCounter.NextValue();
                    float availableMemory = _memoryCounter.NextValue();

                    if (_mainWindow.CpuUsageTextBlock != null)
                        _mainWindow.CpuUsageTextBlock.Text = $"{cpuUsage:F1}%";
                    if (_mainWindow.MemoryUsageTextBlock != null)
                        _mainWindow.MemoryUsageTextBlock.Text = $"{availableMemory:F1} MB";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdatePerformanceInfo 發生錯誤: {ex.Message}");
            }
        }

        private void ResetVideoInfoDisplay()
        {
            if (_mainWindow.ResolutionTextBlock != null) _mainWindow.ResolutionTextBlock.Text = "--";
            if (_mainWindow.FpsTextBlock != null) _mainWindow.FpsTextBlock.Text = "--";
            if (_mainWindow.BitrateTextBlock != null) _mainWindow.BitrateTextBlock.Text = "--";
            if (_mainWindow.PlayingCountTextBlock != null) _mainWindow.PlayingCountTextBlock.Text = "0";
            if (_mainWindow.TotalBitrateTextBlock != null) _mainWindow.TotalBitrateTextBlock.Text = "0 kbps";
        }

        public void StopMonitoring()
        {
            _videoInfoTimer?.Stop();
            _performanceTimer?.Stop();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
        }
    }
}