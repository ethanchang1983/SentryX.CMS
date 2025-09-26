using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;

namespace SentryX
{
    public class SplitScreenManager
    {
        private readonly MainWindow _mainWindow;
        private readonly List<MultiViewPlayer> _videoPlayers = new();
        private MultiViewPlayer? _selectedPlayer = null;
        private int _currentSplitCount = 1;

        // 流暢切換相關變數
        private bool _isFullScreenMode = false;
        private MultiViewPlayer? _fullScreenPlayer = null;
        private int _previousSplitCount = 1;
        private List<FrameworkElement> _hiddenPlayers = new();


        public List<MultiViewPlayer> VideoPlayers => _videoPlayers;
        public MultiViewPlayer? SelectedPlayer => _selectedPlayer;
        public int CurrentSplitCount => _currentSplitCount;
        public bool IsFullScreenMode => _isFullScreenMode;

        public event Action<MultiViewPlayer>? PlayerSelected;
        public event Action<bool>? FullScreenModeChanged;

        public SplitScreenManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        // === 原有方法保持不變，但修改建構 MultiViewPlayer ===
        public void CreateSplitScreenLayout(int splitCount)
        {
            if (!_mainWindow.UIManager.IsUIInitialized)
            {
                Console.WriteLine("警告：UI 尚未初始化，無法建立分割畫面");
                return;
            }

            try
            {
                if (_mainWindow.VideoDisplayGrid == null)
                {
                    Console.WriteLine("嚴重錯誤：VideoDisplayGrid 仍然為 null，無法建立分割畫面");
                    _mainWindow.ShowMessage("無法建立分割畫面：視頻顯示區域未初始化");
                    return;
                }

                _currentSplitCount = splitCount;
                StopAllVideoPlayers();

                _mainWindow.VideoDisplayGrid.Children.Clear();
                _mainWindow.VideoDisplayGrid.RowDefinitions.Clear();
                _mainWindow.VideoDisplayGrid.ColumnDefinitions.Clear();

                int gridSize = (int)Math.Ceiling(Math.Sqrt(splitCount));

                for (int i = 0; i < gridSize; i++)
                {
                    _mainWindow.VideoDisplayGrid.RowDefinitions.Add(new RowDefinition());
                    _mainWindow.VideoDisplayGrid.ColumnDefinitions.Add(new ColumnDefinition());
                }

                _videoPlayers.Clear();
                _selectedPlayer = null;
                int panelIndex = 0;

                for (int row = 0; row < gridSize && panelIndex < splitCount; row++)
                {
                    for (int col = 0; col < gridSize && panelIndex < splitCount; col++)
                    {
                        // 🔥 修改：傳遞 _mainWindow 到 MultiViewPlayer 建構子
                        var player = new MultiViewPlayer(panelIndex, _mainWindow);
                        player.Selected += OnPlayerSelected;
                        player.DoubleClicked += OnPlayerDoubleClicked;
                        _videoPlayers.Add(player);

                        Grid.SetRow(player.HostControl, row);
                        Grid.SetColumn(player.HostControl, col);
                        _mainWindow.VideoDisplayGrid.Children.Add(player.HostControl);

                        panelIndex++;
                    }
                }

                if (_videoPlayers.Count > 0)
                {
                    SelectPlayer(_videoPlayers[0]);
                }

                _mainWindow.VideoDisplayGrid.UpdateLayout();
                _mainWindow.VideoDisplayGrid.InvalidateVisual();
                _mainWindow.ShowMessage($"建立了 {splitCount} 個視頻顯示區域");

                ForceUpdateBorders();
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"建立分割畫面佈局失敗: {ex.Message}");
                Console.WriteLine($"CreateSplitScreenLayout 異常：{ex}");
            }
        }

        /// <summary>
        /// 🔥 優化版：停止所有視頻播放器 - 使用並行處理，同時清除所有畫面
        /// </summary>
        public void StopAllVideoPlayers()
        {
            try
            {
                if (_videoPlayers.Count == 0) return;

                var stopwatch = Stopwatch.StartNew();
                Console.WriteLine($"開始並行停止 {_videoPlayers.Count} 個播放器");

                // 第一步：並行清除所有選中狀態和停止播放
                var tasks = new List<Task>();
                var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2); // 限制並行數量

                foreach (var player in _videoPlayers)
                {
                    var task = Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            // 立即清除選中狀態
                            player.ForceClearSelectedState();

                            // 停止播放
                            player.StopPlay();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"停止播放器 {player.Index} 時發生錯誤: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });
                    tasks.Add(task);
                }

                // 等待所有停止操作完成
                Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(3));

                // 第二步：清理事件訂閱和釋放資源 - 確保在 UI 執行緒上執行
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    foreach (var player in _videoPlayers.ToList())  // ToList 避免迭代中修改
                    {
                        try
                        {
                            player.Selected -= OnPlayerSelected;
                            player.DoubleClicked -= OnPlayerDoubleClicked;
                            player.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"清理播放器 {player.Index} 時發生錯誤: {ex.Message}");
                        }
                    }

                    // 清空集合
                    _videoPlayers.Clear();
                    _selectedPlayer = null;

                    // 重置全螢幕相關狀態
                    _isFullScreenMode = false;
                    _fullScreenPlayer = null;
                    _hiddenPlayers.Clear();
                });

                stopwatch.Stop();
                Console.WriteLine($"所有播放器已停止，耗時: {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"並行停止所有播放器時發生異常: {ex.Message}");
                // 如果並行處理失敗，回退到逐個處理
                StopAllVideoPlayersFallback();
            }
        }

        /// <summary>
        /// 後備方案：如果並行處理失敗，使用傳統方式
        /// </summary>
        private void StopAllVideoPlayersFallback()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                foreach (var player in _videoPlayers.ToList())
                {
                    try
                    {
                        player.ForceClearSelectedState();
                        player.StopPlay();
                        player.Selected -= OnPlayerSelected;
                        player.DoubleClicked -= OnPlayerDoubleClicked;
                        player.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Fallback 清理播放器 {player.Index} 時發生錯誤: {ex.Message}");
                    }
                }

                _videoPlayers.Clear();
                _selectedPlayer = null;
                _isFullScreenMode = false;
                _fullScreenPlayer = null;
                _hiddenPlayers.Clear();
            });
        }

        /// <summary>
        /// 🔥 批次停止播放（只停止播放，不釋放播放器） - 優化版
        /// </summary>
        public async Task StopAllPlaybackAsync()
        {
            try
            {
                if (!HasAnyPlayerPlaying())
                {
                    _mainWindow.Dispatcher.Invoke(() => _mainWindow.ShowMessage("沒有正在播放的視頻"));
                    return;
                }

                var stopwatch = Stopwatch.StartNew();
                var playingPlayers = _videoPlayers.Where(p => p.IsPlaying).ToList();
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.ShowMessage($"正在停止 {playingPlayers.Count} 個視頻..."));

                // 先黑屏 - await 確保視覺統一
                var blackScreenTasks = playingPlayers.Select(player => Task.Run(() => player.QuickBlackScreen())).ToArray();
                await Task.WhenAll(blackScreenTasks); // 等待黑屏完成

                // 並行停止 - 移除額外 RefreshDisplay()
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount / 2 }; // 限制並行，防 Dispatcher 過載
                var stopTasks = playingPlayers.Select(player => Task.Run(() =>
                {
                    try
                    {
                        player.StopPlay(); // 已包含 RefreshDisplay()
                        player.SetPlaybackMode(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"停止播放器 {player.Index} 時發生錯誤: {ex.Message}");
                    }
                })).ToArray();

                await Task.WhenAll(stopTasks);

                stopwatch.Stop();
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.ShowMessage($"✅ 已停止所有視頻播放 (耗時 {stopwatch.ElapsedMilliseconds}ms)"));

                if (_isFullScreenMode)
                {
                    ExitFullScreenModeSmooth();
                }
            }
            catch (Exception ex)
            {
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.ShowMessage($"批次停止播放時發生錯誤: {ex.Message}"));
                Console.WriteLine($"StopAllPlaybackAsync 異常：{ex}");
            }
        }

        /// <summary>
        /// 🔥 同步版本的批次停止播放（供按鈕直接調用） - 優化版
        /// </summary>
        public void StopAllPlayback()
        {
            try
            {
                var playingPlayers = _videoPlayers.Where(p => p.IsPlaying).ToList();
                if (playingPlayers.Count == 0)
                {
                    _mainWindow.Dispatcher.Invoke(() => _mainWindow.ShowMessage("沒有正在播放的視頻"));
                    return;
                }

                _mainWindow.Dispatcher.Invoke(() => _mainWindow.ShowMessage($"正在停止 {playingPlayers.Count} 個視頻..."));

                // 先黑屏 - 使用 Parallel，但限制度
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount / 2 };
                Parallel.ForEach(playingPlayers, parallelOptions, player => player.QuickBlackScreen());

                // 並行停止
                var stopTasks = playingPlayers.Select(player => Task.Run(() =>
                {
                    try
                    {
                        player.StopPlay(); // 已包含 RefreshDisplay()
                        player.SetPlaybackMode(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"停止播放器 {player.Index} 時發生錯誤: {ex.Message}");
                    }
                })).ToArray();

                Task.WhenAll(stopTasks).Wait(TimeSpan.FromSeconds(3)); // 同步等待

                _mainWindow.Dispatcher.Invoke(() => _mainWindow.ShowMessage($"✅ 已並行停止所有播放"));
            }
            catch (Exception ex)
            {
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.ShowMessage($"批次停止播放時發生錯誤: {ex.Message}"));
            }
        }

        // === 以下為原有的其他方法，保持不變 ===
        private void OnPlayerSelected(MultiViewPlayer selectedPlayer)
        {
            try
            {
                SelectPlayer(selectedPlayer);
                _mainWindow.ShowMessage($"已選中分割區域 {selectedPlayer.Index + 1}");
                PlayerSelected?.Invoke(selectedPlayer);
                _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _mainWindow.UpdateButtonStates();
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"選擇分割區域時發生錯誤: {ex.Message}");
            }
        }

        private void OnPlayerDoubleClicked(MultiViewPlayer doubleClickedPlayer)
        {
            try
            {
                if (_isFullScreenMode)
                {
                    ExitFullScreenModeSmooth();
                }
                else
                {
                    EnterFullScreenModeSmooth(doubleClickedPlayer);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"切換顯示模式時發生錯誤: {ex.Message}");
                Console.WriteLine($"OnPlayerDoubleClicked 異常：{ex}");
            }
        }

        private void EnterFullScreenModeSmooth(MultiViewPlayer targetPlayer)
        {
            try
            {
                Console.WriteLine($"流暢進入全螢幕模式：播放器 {targetPlayer.Index}");
                if (!targetPlayer.IsPlaying)
                {
                    _mainWindow.ShowMessage("該分割區域沒有播放視頻，無法進入全螢幕模式");
                    return;
                }

                _isFullScreenMode = true;
                _fullScreenPlayer = targetPlayer;
                _previousSplitCount = _currentSplitCount;
                _hiddenPlayers.Clear();

                foreach (var player in _videoPlayers)
                {
                    if (player != targetPlayer)
                    {
                        _mainWindow.VideoDisplayGrid.Children.Remove(player.HostControl);
                        _hiddenPlayers.Add(player.HostControl);
                    }
                }

                _mainWindow.VideoDisplayGrid.RowDefinitions.Clear();
                _mainWindow.VideoDisplayGrid.ColumnDefinitions.Clear();
                _mainWindow.VideoDisplayGrid.RowDefinitions.Add(new RowDefinition());
                _mainWindow.VideoDisplayGrid.ColumnDefinitions.Add(new ColumnDefinition());

                Grid.SetRow(targetPlayer.HostControl, 0);
                Grid.SetColumn(targetPlayer.HostControl, 0);

                _mainWindow.VideoDisplayGrid.UpdateLayout();
                _mainWindow.VideoDisplayGrid.InvalidateVisual();

                SelectPlayer(targetPlayer);
                _mainWindow.ShowMessage($"已進入全螢幕模式：分割區域 {targetPlayer.Index + 1}");
                FullScreenModeChanged?.Invoke(true);
                Console.WriteLine("流暢全螢幕模式設定完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"流暢進入全螢幕模式時發生異常：{ex}");
                _mainWindow.ShowMessage($"進入全螢幕模式失敗: {ex.Message}");
            }
        }

        private void ExitFullScreenModeSmooth()
        {
            try
            {
                Console.WriteLine("流暢退出全螢幕模式，恢復多分割畫面");
                if (!_isFullScreenMode || _fullScreenPlayer == null)
                {
                    Console.WriteLine("當前不在全螢幕模式");
                    return;
                }

                RestoreGridLayout(_previousSplitCount);
                RestoreAllPlayersToGrid();

                _isFullScreenMode = false;
                _fullScreenPlayer = null;
                _hiddenPlayers.Clear();

                _mainWindow.VideoDisplayGrid.UpdateLayout();
                _mainWindow.VideoDisplayGrid.InvalidateVisual();

                _mainWindow.ShowMessage($"已退出全螢幕模式，恢復 {_previousSplitCount} 分割畫面");
                FullScreenModeChanged?.Invoke(false);
                Console.WriteLine("流暢退出全螢幕模式完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"流暢退出全螢幕模式時發生異常：{ex}");
                _mainWindow.ShowMessage($"退出全螢幕模式失敗: {ex.Message}");
            }
        }

        private void RestoreGridLayout(int splitCount)
        {
            _currentSplitCount = splitCount;
            _mainWindow.VideoDisplayGrid.RowDefinitions.Clear();
            _mainWindow.VideoDisplayGrid.ColumnDefinitions.Clear();
            int gridSize = (int)Math.Ceiling(Math.Sqrt(splitCount));
            for (int i = 0; i < gridSize; i++)
            {
                _mainWindow.VideoDisplayGrid.RowDefinitions.Add(new RowDefinition());
                _mainWindow.VideoDisplayGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }
        }

        private void RestoreAllPlayersToGrid()
        {
            _mainWindow.VideoDisplayGrid.Children.Clear();
            int gridSize = (int)Math.Ceiling(Math.Sqrt(_previousSplitCount));
            int panelIndex = 0;
            for (int row = 0; row < gridSize && panelIndex < _videoPlayers.Count; row++)
            {
                for (int col = 0; col < gridSize && panelIndex < _videoPlayers.Count; col++)
                {
                    var player = _videoPlayers[panelIndex];
                    Grid.SetRow(player.HostControl, row);
                    Grid.SetColumn(player.HostControl, col);
                    _mainWindow.VideoDisplayGrid.Children.Add(player.HostControl);
                    panelIndex++;
                }
            }
        }

        public void ForceExitFullScreenMode()
        {
            if (_isFullScreenMode)
            {
                ExitFullScreenModeSmooth();
            }
        }

        public bool IsFullScreenPlayer(MultiViewPlayer player)
        {
            return _isFullScreenMode && _fullScreenPlayer == player;
        }

        public void SelectPlayer(MultiViewPlayer player)
        {
            if (_selectedPlayer != null)
            {
                _selectedPlayer.IsSelected = false;
            }

            _selectedPlayer = player;
            _selectedPlayer.IsSelected = true;

            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                _mainWindow.UpdateButtonStates();
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        public void SelectNextAvailablePlayer()
        {
            try
            {
                var nextPlayer = _videoPlayers.FirstOrDefault(p => !p.IsPlaying && !p.HasActiveContent);

                if (nextPlayer == null)
                {
                    nextPlayer = _videoPlayers.FirstOrDefault(p => !p.IsPlaying);
                }

                if (nextPlayer != null)
                {
                    SelectPlayer(nextPlayer);
                    string status = nextPlayer.HasActiveContent ? "（回放模式）" : "";
                    _mainWindow.ShowMessage($"自動選中下一個可用區域：分割區域 {nextPlayer.Index + 1} {status}");
                }
                else
                {
                    _mainWindow.ShowMessage("⚠️ 沒有可用的分割區域");
                }
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"自動選擇下一個區域時發生錯誤: {ex.Message}");
            }
        }

        public void ForceClearAllSelectedStates()
        {
            try
            {
                // 並行清除所有選中狀態
                Parallel.ForEach(_videoPlayers, player =>
                {
                    player.ForceClearSelectedState();
                });

                _mainWindow.ShowMessage("已強制清除所有分割區域的選中狀態");
                Debug.WriteLine("所有播放器的選中狀態已強制清除");
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"清除選中狀態時發生錯誤: {ex.Message}");
            }
        }

        public int GetAvailablePlayerCount()
        {
            return _videoPlayers.Count(p => !p.IsPlaying);
        }

        public int GetPlayingPlayerCount()
        {
            return _videoPlayers.Count(p => p.IsPlaying);
        }

        public bool StopPlayer(int index)
        {
            if (index >= 0 && index < _videoPlayers.Count)
            {
                try
                {
                    var player = _videoPlayers[index];

                    if (_isFullScreenMode && _fullScreenPlayer == player)
                    {
                        ExitFullScreenModeSmooth();
                    }

                    player.StopPlay();
                    _mainWindow.ShowMessage($"已停止分割區域 {index + 1} 的播放");
                    return true;
                }
                catch (Exception ex)
                {
                    _mainWindow.ShowMessage($"停止分割區域 {index + 1} 播放失敗: {ex.Message}");
                    return false;
                }
            }
            return false;
        }

        public bool StopSelectedPlayer()
        {
            if (_selectedPlayer != null)
            {
                try
                {
                    if (_isFullScreenMode && _fullScreenPlayer == _selectedPlayer)
                    {
                        ExitFullScreenModeSmooth();
                    }

                    _selectedPlayer.StopPlay();
                    _selectedPlayer.SetPlaybackMode(false);

                    _mainWindow.ShowMessage($"已停止分割區域 {_selectedPlayer.Index + 1} 的播放");
                    return true;
                }
                catch (Exception ex)
                {
                    _mainWindow.ShowMessage($"停止分割區域 {_selectedPlayer.Index + 1} 播放失敗: {ex.Message}");
                    return false;
                }
            }
            else
            {
                _mainWindow.ShowMessage("❌ 沒有選中的分割區域");
                return false;
            }
        }

        public string GetPlayerStatusSummary()
        {
            var playing = GetPlayingPlayerCount();
            var total = _videoPlayers.Count;
            var fullScreenStatus = _isFullScreenMode ? " (全螢幕模式)" : "";
            return $"播放中: {playing}/{total}{fullScreenStatus}";
        }

        public bool HasAnyPlayerPlaying()
        {
            return _videoPlayers.Any(p => p.IsPlaying);
        }

        public bool AreAllPlayersPlaying()
        {
            return _videoPlayers.Count > 0 && _videoPlayers.All(p => p.IsPlaying);
        }

        public bool SelectPlayerByIndex(int index)
        {
            if (index >= 0 && index < _videoPlayers.Count)
            {
                SelectPlayer(_videoPlayers[index]);
                return true;
            }
            return false;
        }

        public void SelectNextPlayer()
        {
            if (_videoPlayers.Count == 0) return;

            int currentIndex = _selectedPlayer != null ? _videoPlayers.IndexOf(_selectedPlayer) : -1;
            int nextIndex = (currentIndex + 1) % _videoPlayers.Count;
            SelectPlayer(_videoPlayers[nextIndex]);
        }

        public void SelectPreviousPlayer()
        {
            if (_videoPlayers.Count == 0) return;

            int currentIndex = _selectedPlayer != null ? _videoPlayers.IndexOf(_selectedPlayer) : 0;
            int previousIndex = currentIndex == 0 ? _videoPlayers.Count - 1 : currentIndex - 1;
            SelectPlayer(_videoPlayers[previousIndex]);
        }

        private void ForceUpdateBorders()
        {
            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (var player in _videoPlayers)
                    {
                        var currentSelected = player.IsSelected;
                        player.IsSelected = !currentSelected;
                        player.IsSelected = currentSelected;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"強制邊框更新失敗: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}