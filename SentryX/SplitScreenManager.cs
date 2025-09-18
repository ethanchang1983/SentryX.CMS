using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace SentryX
{
    public class SplitScreenManager
    {
        private readonly MainWindow _mainWindow;
        private readonly List<MultiViewPlayer> _videoPlayers = new();
        private MultiViewPlayer? _selectedPlayer = null;
        private int _currentSplitCount = 1;

        // 流暢切換相關變數 - 不停止播放，只切換顯示
        private bool _isFullScreenMode = false;
        private MultiViewPlayer? _fullScreenPlayer = null;
        private int _previousSplitCount = 1;
        private List<FrameworkElement> _hiddenPlayers = new(); // 隱藏的播放器

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
                        var player = new MultiViewPlayer(panelIndex);
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

        private void OnPlayerSelected(MultiViewPlayer selectedPlayer)
        {
            try
            {
                SelectPlayer(selectedPlayer);
                _mainWindow.ShowMessage($"已選中分割區域 {selectedPlayer.Index + 1}");
                PlayerSelected?.Invoke(selectedPlayer);
                
                // 新增：通知 MainWindow 更新按鈕狀態
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

        /// <summary>
        /// 處理播放器雙擊事件 - 流暢切換模式
        /// </summary>
        private void OnPlayerDoubleClicked(MultiViewPlayer doubleClickedPlayer)
        {
            try
            {
                if (_isFullScreenMode)
                {
                    // 當前在全螢幕模式，切換回多分割畫面
                    ExitFullScreenModeSmooth();
                }
                else
                {
                    // 當前在多分割畫面，切換到全螢幕模式
                    EnterFullScreenModeSmooth(doubleClickedPlayer);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"切換顯示模式時發生錯誤: {ex.Message}");
                Console.WriteLine($"OnPlayerDoubleClicked 異常：{ex}");
            }
        }

        /// <summary>
        /// 流暢進入全螢幕模式 - 不停止播放，只隱藏其他播放器
        /// </summary>
        private void EnterFullScreenModeSmooth(MultiViewPlayer targetPlayer)
        {
            try
            {
                Console.WriteLine($"流暢進入全螢幕模式：播放器 {targetPlayer.Index}");

                // 檢查目標播放器是否正在播放
                if (!targetPlayer.IsPlaying)
                {
                    _mainWindow.ShowMessage("該分割區域沒有播放視頻，無法進入全螢幕模式");
                    return;
                }

                // 標記狀態
                _isFullScreenMode = true;
                _fullScreenPlayer = targetPlayer;
                _previousSplitCount = _currentSplitCount;

                // 隱藏其他播放器（不停止播放）
                _hiddenPlayers.Clear();
                foreach (var player in _videoPlayers)
                {
                    if (player != targetPlayer)
                    {
                        // 從網格中移除但不銷毀
                        _mainWindow.VideoDisplayGrid.Children.Remove(player.HostControl);
                        _hiddenPlayers.Add(player.HostControl);
                    }
                }

                // 重新配置網格為單一格子
                _mainWindow.VideoDisplayGrid.RowDefinitions.Clear();
                _mainWindow.VideoDisplayGrid.ColumnDefinitions.Clear();
                _mainWindow.VideoDisplayGrid.RowDefinitions.Add(new RowDefinition());
                _mainWindow.VideoDisplayGrid.ColumnDefinitions.Add(new ColumnDefinition());

                // 將目標播放器設置為全螢幕
                Grid.SetRow(targetPlayer.HostControl, 0);
                Grid.SetColumn(targetPlayer.HostControl, 0);

                // 立即更新佈局
                _mainWindow.VideoDisplayGrid.UpdateLayout();
                _mainWindow.VideoDisplayGrid.InvalidateVisual();

                // 選中目標播放器
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

        /// <summary>
        /// 流暢退出全螢幕模式 - 恢復所有播放器顯示
        /// </summary>
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

                // 重新建立原本的網格佈局
                RestoreGridLayout(_previousSplitCount);

                // 恢復所有播放器到網格中
                RestoreAllPlayersToGrid();

                // 重置狀態
                _isFullScreenMode = false;
                _fullScreenPlayer = null;
                _hiddenPlayers.Clear();

                // 立即更新佈局
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

        /// <summary>
        /// 恢復網格佈局
        /// </summary>
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

        /// <summary>
        /// 恢復所有播放器到網格中
        /// </summary>
        private void RestoreAllPlayersToGrid()
        {
            // 確保網格中只有全螢幕播放器
            _mainWindow.VideoDisplayGrid.Children.Clear();

            int gridSize = (int)Math.Ceiling(Math.Sqrt(_previousSplitCount));
            int panelIndex = 0;

            for (int row = 0; row < gridSize && panelIndex < _videoPlayers.Count; row++)
            {
                for (int col = 0; col < gridSize && panelIndex < _videoPlayers.Count; col++)
                {
                    var player = _videoPlayers[panelIndex];

                    // 設置網格位置
                    Grid.SetRow(player.HostControl, row);
                    Grid.SetColumn(player.HostControl, col);

                    // 添加到網格
                    _mainWindow.VideoDisplayGrid.Children.Add(player.HostControl);

                    panelIndex++;
                }
            }
        }

        /// <summary>
        /// 強制退出全螢幕模式（供外部調用）
        /// </summary>
        public void ForceExitFullScreenMode()
        {
            if (_isFullScreenMode)
            {
                ExitFullScreenModeSmooth();
            }
        }

        /// <summary>
        /// 檢查指定播放器是否為當前全螢幕播放器
        /// </summary>
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
            
            // 新增：觸發按鈕狀態更新
            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                _mainWindow.UpdateButtonStates();
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        public void SelectNextAvailablePlayer()
        {
            try
            {
                // 首先嘗試找到不在播放且不在回放模式的播放器
                var nextPlayer = _videoPlayers.FirstOrDefault(p => !p.IsPlaying && !p.HasActiveContent);
                
                if (nextPlayer == null)
                {
                    // 如果沒有完全空閒的播放器，則找不在播放的播放器（可能在回放模式）
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

        /// <summary>
        /// 🔥 修正：停止所有視頻播放器 - 確保清除選中狀態
        /// </summary>
        public void StopAllVideoPlayers()
        {
            foreach (var player in _videoPlayers)
            {
                try
                {
                    // 🔥 先強制清除選中狀態，防止 IVS 規則殘留
                    player.ForceClearSelectedState();
                    
                    player.Selected -= OnPlayerSelected;
                    player.DoubleClicked -= OnPlayerDoubleClicked;
                    player.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"清理播放器時發生錯誤: {ex.Message}");
                }
            }
            _videoPlayers.Clear();
            _selectedPlayer = null;

            // 重置全螢幕相關狀態
            _isFullScreenMode = false;
            _fullScreenPlayer = null;
            _hiddenPlayers.Clear();
        }

        /// <summary>
        /// 🔥 新增：強制清除所有播放器的選中狀態 - 專門解決 IVS 問題
        /// </summary>
        public void ForceClearAllSelectedStates()
        {
            try
            {
                foreach (var player in _videoPlayers)
                {
                    player.ForceClearSelectedState();
                }
                
                _mainWindow.ShowMessage("已強制清除所有分割區域的選中狀態");
                Debug.WriteLine("所有播放器的選中狀態已強制清除");
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"清除選中狀態時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 獲取可用的播放器數量
        /// </summary>
        public int GetAvailablePlayerCount()
        {
            return _videoPlayers.Count(p => !p.IsPlaying);
        }

        /// <summary>
        /// 獲取正在播放的播放器數量
        /// </summary>
        public int GetPlayingPlayerCount()
        {
            return _videoPlayers.Count(p => p.IsPlaying);
        }

        /// <summary>
        /// 停止指定索引的播放器
        /// </summary>
        public bool StopPlayer(int index)
        {
            if (index >= 0 && index < _videoPlayers.Count)
            {
                try
                {
                    var player = _videoPlayers[index];

                    // 如果正在全螢幕顯示這個播放器，先退出全螢幕
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

        /// <summary>
        /// 停止選中播放器的播放
        /// </summary>
        public bool StopSelectedPlayer()
        {
            if (_selectedPlayer != null)
            {
                try
                {
                    // 如果正在全螢幕顯示選中的播放器，先退出全螢幕
                    if (_isFullScreenMode && _fullScreenPlayer == _selectedPlayer)
                    {
                        ExitFullScreenModeSmooth();
                    }

                    // 停止播放（包括實況和可能的回放狀態）
                    _selectedPlayer.StopPlay();
                    
                    // 確保清除回放模式狀態
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

        /// <summary>
        /// 獲取播放器狀態摘要
        /// </summary>
        public string GetPlayerStatusSummary()
        {
            var playing = GetPlayingPlayerCount();
            var total = _videoPlayers.Count;
            var fullScreenStatus = _isFullScreenMode ? " (全螢幕模式)" : "";
            return $"播放中: {playing}/{total}{fullScreenStatus}";
        }

        /// <summary>
        /// 檢查是否有任何播放器正在播放
        /// </summary>
        public bool HasAnyPlayerPlaying()
        {
            return _videoPlayers.Any(p => p.IsPlaying);
        }

        /// <summary>
        /// 檢查是否所有播放器都在播放
        /// </summary>
        public bool AreAllPlayersPlaying()
        {
            return _videoPlayers.Count > 0 && _videoPlayers.All(p => p.IsPlaying);
        }

        /// <summary>
        /// 根據索引選擇播放器
        /// </summary>
        public bool SelectPlayerByIndex(int index)
        {
            if (index >= 0 && index < _videoPlayers.Count)
            {
                SelectPlayer(_videoPlayers[index]);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 切換到下一個播放器
        /// </summary>
        public void SelectNextPlayer()
        {
            if (_videoPlayers.Count == 0) return;

            int currentIndex = _selectedPlayer != null ? _videoPlayers.IndexOf(_selectedPlayer) : -1;
            int nextIndex = (currentIndex + 1) % _videoPlayers.Count;
            SelectPlayer(_videoPlayers[nextIndex]);
        }

        /// <summary>
        /// 切換到上一個播放器
        /// </summary>
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