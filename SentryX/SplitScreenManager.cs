using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SentryX
{
    public class SplitScreenManager
    {
        private readonly MainWindow _mainWindow;
        private readonly List<MultiViewPlayer> _videoPlayers = new();
        private MultiViewPlayer? _selectedPlayer = null;
        private int _currentSplitCount = 1;

        public List<MultiViewPlayer> VideoPlayers => _videoPlayers;
        public MultiViewPlayer? SelectedPlayer => _selectedPlayer;
        public int CurrentSplitCount => _currentSplitCount;

        public event Action<MultiViewPlayer>? PlayerSelected;

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
                    Console.WriteLine("❌ 嚴重錯誤：VideoDisplayGrid 仍然為 null，無法建立分割畫面");
                    _mainWindow.ShowMessage("❌ 無法建立分割畫面：視頻顯示區域未初始化");
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
                _mainWindow.ShowMessage($"📐 建立了 {splitCount} 個視頻顯示區域");

                ForceUpdateBorders();
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 建立分割畫面佈局失敗: {ex.Message}");
                Console.WriteLine($"CreateSplitScreenLayout 異常：{ex}");
            }
        }

        private void OnPlayerSelected(MultiViewPlayer selectedPlayer)
        {
            try
            {
                SelectPlayer(selectedPlayer);
                _mainWindow.ShowMessage($"🎯 已選中分割區域 {selectedPlayer.Index + 1}");
                PlayerSelected?.Invoke(selectedPlayer);
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 選擇分割區域時發生錯誤: {ex.Message}");
            }
        }

        public void SelectPlayer(MultiViewPlayer player)
        {
            if (_selectedPlayer != null)
            {
                _selectedPlayer.IsSelected = false;
            }

            _selectedPlayer = player;
            _selectedPlayer.IsSelected = true;
        }

        public void SelectNextAvailablePlayer()
        {
            try
            {
                var nextPlayer = _videoPlayers.FirstOrDefault(p => !p.IsPlaying);
                if (nextPlayer != null)
                {
                    SelectPlayer(nextPlayer);
                    _mainWindow.ShowMessage($"🎯 自動選中下一個可用區域：分割區域 {nextPlayer.Index + 1}");
                }
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"❌ 自動選擇下一個區域時發生錯誤: {ex.Message}");
            }
        }

        public void StopAllVideoPlayers()
        {
            foreach (var player in _videoPlayers)
            {
                try
                {
                    player.Selected -= OnPlayerSelected;
                    player.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"清理播放器時發生錯誤: {ex.Message}");
                }
            }
            _videoPlayers.Clear();
            _selectedPlayer = null;
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