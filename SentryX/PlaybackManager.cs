using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NetSDKCS;

namespace SentryX
{
    /// <summary>
    /// 回放管理器 - 處理實況與回放的無縫切換
    /// </summary>
    public class PlaybackManager
    {
        private readonly MainWindow _mainWindow;
        private readonly SplitScreenManager _splitScreenManager;

        // 回放相關變數
        private readonly Dictionary<int, PlaybackSession> _activePlaybacks = new();
        private readonly object _playbackLock = new object();

        public PlaybackManager(MainWindow mainWindow, SplitScreenManager splitScreenManager)
        {
            _mainWindow = mainWindow;
            _splitScreenManager = splitScreenManager;
        }

        /// <summary>
        /// 切換選中的播放器到回放模式
        /// </summary>
        /// <param name="startTime">開始時間</param>
        /// <param name="endTime">結束時間（可選，預設為開始時間+1小時）</param>
        /// <returns>是否成功切換到回放模式</returns>
        public bool SwitchToPlayback(DateTime startTime, DateTime? endTime = null)
        {
            var selectedPlayer = _splitScreenManager.SelectedPlayer;
            if (selectedPlayer == null)
            {
                _mainWindow.ShowMessage("請先選擇一個分割區域");
                return false;
            }

            return SwitchToPlaybackByIndex(selectedPlayer.Index, startTime, endTime);
        }

        /// <summary>
        /// 按索引切換指定播放器到回放模式
        /// </summary>
        /// <param name="playerIndex">播放器索引</param>
        /// <param name="startTime">開始時間</param>
        /// <param name="endTime">結束時間</param>
        /// <returns>是否成功切換到回放模式</returns>
        public bool SwitchToPlaybackByIndex(int playerIndex, DateTime startTime, DateTime? endTime = null)
        {
            if (playerIndex < 0 || playerIndex >= _splitScreenManager.VideoPlayers.Count)
            {
                _mainWindow.ShowMessage($"無效的播放器索引：{playerIndex}");
                return false;
            }

            var targetPlayer = _splitScreenManager.VideoPlayers[playerIndex];

            if (!targetPlayer.IsPlaying && !IsInPlaybackMode(playerIndex))
            {
                _mainWindow.ShowMessage($"分割區域 {playerIndex + 1} 沒有正在播放的視頻");
                return false;
            }

            // 取得當前播放狀態
            var currentState = targetPlayer.GetCurrentPlaybackState();
            if (currentState == null)
            {
                _mainWindow.ShowMessage($"無法取得分割區域 {playerIndex + 1} 的播放狀態");
                return false;
            }

            // 設定結束時間
            var actualEndTime = endTime ?? startTime.AddHours(1);

            try
            {
                _mainWindow.ShowMessage($"正在切換分割區域 {playerIndex + 1} 到回放模式...");

                // 先檢查是否已經在回放模式，如果是則先停止
                if (IsInPlaybackMode(playerIndex))
                {
                    _mainWindow.ShowMessage("檢測到現有回放會話，正在停止...");
                    StopPlayback(playerIndex);
                    // 等待清理完成
                    System.Threading.Thread.Sleep(200);
                }

                // 停止實況播放但保留狀態
                targetPlayer.StopPlay(keepPlaybackState: true);

                // 等待實況播放完全停止
                System.Threading.Thread.Sleep(100);

                // 開始回放
                if (StartPlayback(targetPlayer, currentState, startTime, actualEndTime))
                {
                    // 設定播放器為回放模式
                    targetPlayer.SetPlaybackMode(true);
                    
                    _mainWindow.ShowMessage($"✅ 已切換到回放模式：{startTime:yyyy-MM-dd HH:mm:ss} - {actualEndTime:yyyy-MM-dd HH:mm:ss}");
                    return true;
                }
                else
                {
                    // 回放失敗，嘗試恢復實況播放
                    _mainWindow.ShowMessage("回放啟動失敗，正在恢復實況播放...");
                    System.Threading.Thread.Sleep(100);
                    targetPlayer.RestorePlay();
                    return false;
                }
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"切換回放模式時發生錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切換選中的播放器回到實況模式
        /// </summary>
        /// <returns>是否成功切換到實況模式</returns>
        public bool SwitchToLive()
        {
            var selectedPlayer = _splitScreenManager.SelectedPlayer;
            if (selectedPlayer == null)
            {
                _mainWindow.ShowMessage("請先選擇一個分割區域");
                return false;
            }

            return SwitchToLiveByIndex(selectedPlayer.Index);
        }

        /// <summary>
        /// 按索引切換指定播放器回到實況模式
        /// </summary>
        /// <param name="playerIndex">播放器索引</param>
        /// <returns>是否成功切換到實況模式</returns>
        public bool SwitchToLiveByIndex(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= _splitScreenManager.VideoPlayers.Count)
            {
                _mainWindow.ShowMessage($"無效的播放器索引：{playerIndex}");
                return false;
            }

            var targetPlayer = _splitScreenManager.VideoPlayers[playerIndex];

            lock (_playbackLock)
            {
                // 檢查是否在回放模式
                if (!_activePlaybacks.ContainsKey(playerIndex))
                {
                    _mainWindow.ShowMessage($"分割區域 {playerIndex + 1} 不在回放模式");
                    return false;
                }

                try
                {
                    _mainWindow.ShowMessage($"正在切換分割區域 {playerIndex + 1} 回到實況模式...");

                    // 停止回放 - 強制清理
                    StopPlayback(playerIndex, forceCleanup: true);

                    // 設定播放器退出回放模式
                    targetPlayer.SetPlaybackMode(false);

                    // 等待回放完全停止
                    System.Threading.Thread.Sleep(200);

                    // 恢復實況播放
                    if (targetPlayer.RestorePlay())
                    {
                        _mainWindow.ShowMessage($"✅ 分割區域 {playerIndex + 1} 已切換回實況模式");
                        return true;
                    }
                    else
                    {
                        _mainWindow.ShowMessage($"❌ 分割區域 {playerIndex + 1} 恢復實況播放失敗");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _mainWindow.ShowMessage($"切換實況模式時發生錯誤：{ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 開始指定播放器的回放 - 增強除錯版本
        /// </summary>
        private bool StartPlayback(MultiViewPlayer player, MultiViewPlayer.PlaybackState liveState, DateTime startTime, DateTime endTime)
        {
            try
            {
                // 詳細記錄播放狀態資訊
                _mainWindow.ShowMessage($"📋 檢查播放狀態：DeviceId={liveState.DeviceId}, Channel={liveState.Channel}, DeviceName={liveState.DeviceName}");

                var device = DahuaSDK.GetDevice(liveState.DeviceId);
                if (device == null)
                {
                    _mainWindow.ShowMessage($"❌ 找不到設備：DeviceId={liveState.DeviceId}");

                    // 嘗試用設備名稱查找
                    if (!string.IsNullOrEmpty(liveState.DeviceName))
                    {
                        _mainWindow.ShowMessage($"🔄 嘗試用設備名稱查找：{liveState.DeviceName}");
                        var devices = DahuaSDK.GetAllDevices();
                        device = devices?.FirstOrDefault(d => d.Name == liveState.DeviceName);

                        if (device != null)
                        {
                            _mainWindow.ShowMessage($"✅ 透過設備名稱找到設備：{device.Name} (ID: {device.Id})");
                            // 更新播放狀態中的設備ID
                            liveState.DeviceId = device.Id;
                        }
                    }
                }

                if (device == null)
                {
                    _mainWindow.ShowMessage($"❌ 完全找不到設備，無法開始回放");
                    return false;
                }

                if (!device.IsOnline)
                {
                    _mainWindow.ShowMessage($"❌ 設備 {device.Name} 不在線，狀態：{(device.IsOnline ? "在線" : "離線")}");
                    return false;
                }

                _mainWindow.ShowMessage($"✅ 設備驗證通過：{device.Name} (ID: {device.Id}) - 在線");
                _mainWindow.ShowMessage($"準備回放參數：設備={device.Name}, 通道={liveState.Channel}, 開始={startTime:yyyy-MM-dd HH:mm:ss}, 結束={endTime:yyyy-MM-dd HH:mm:ss}");

                // 查詢錄影檔案
                var recordFiles = QueryRecordFiles(device.LoginHandle, liveState.Channel, startTime, endTime, liveState.StreamType);
                if (recordFiles?.Length == 0)
                {
                    _mainWindow.ShowMessage($"在指定時間範圍內沒有找到錄影檔案：{startTime:yyyy-MM-dd HH:mm:ss} - {endTime:yyyy-MM-dd HH:mm:ss}");
                    return false;
                }

                // 建立回放 - 傳遞正確的時間參數
                var playbackHandle = CreatePlayback(device.LoginHandle, liveState.Channel, startTime, endTime, player.VideoPanel.Handle, liveState.StreamType);
                if (playbackHandle == IntPtr.Zero)
                {
                    var errorCode = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"回放建立失敗，錯誤碼：{errorCode}");
                    return false;
                }

                // 建立回放會話 - 保存實際的時間和更新後的設備ID
                var session = new PlaybackSession
                {
                    PlaybackHandle = playbackHandle,
                    PlayerIndex = player.Index,
                    DeviceId = device.Id,  // 使用找到的設備ID
                    Channel = liveState.Channel,
                    StartTime = startTime,  // 保存實際的開始時間
                    EndTime = endTime,      // 保存實際的結束時間
                    StreamType = liveState.StreamType,
                    OriginalState = liveState
                };

                lock (_playbackLock)
                {
                    _activePlaybacks[player.Index] = session;
                }

                _mainWindow.ShowMessage($"找到 {recordFiles?.Length ?? 0} 個錄影檔案，回放已開始 (句柄: {playbackHandle.ToInt64()})");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"開始回放時發生錯誤：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 查詢錄影檔案
        /// </summary>
        private NET_RECORDFILE_INFO[]? QueryRecordFiles(IntPtr loginHandle, int channel, DateTime startTime, DateTime endTime, VideoStreamType streamType)
        {
            try
            {
                // 設定碼流類型
                SetStreamType(loginHandle, streamType);

                _mainWindow.ShowMessage($"查詢錄影檔案：通道{channel}, 時間範圍 {startTime:yyyy-MM-dd HH:mm:ss} - {endTime:yyyy-MM-dd HH:mm:ss}");

                // 查詢錄影檔案
                NET_RECORDFILE_INFO[] recordFiles = new NET_RECORDFILE_INFO[5000];
                int fileCount = 0;

                bool result = NETClient.QueryRecordFile(
                    loginHandle,
                    channel,
                    EM_QUERY_RECORD_TYPE.ALL,
                    startTime,
                    endTime,
                    null,
                    ref recordFiles,
                    ref fileCount,
                    10000, // 增加等待時間到 10 秒
                    false
                );

                if (!result)
                {
                    var errorCode = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"查詢錄影檔案失敗，錯誤碼：{errorCode}");
                    return null;
                }

                _mainWindow.ShowMessage($"查詢到 {fileCount} 個錄影檔案");

                // 只返回實際的檔案數量
                var actualFiles = new NET_RECORDFILE_INFO[fileCount];
                Array.Copy(recordFiles, actualFiles, fileCount);
                return actualFiles;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"查詢錄影檔案時發生錯誤：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 設定碼流類型 - 根據文檔修正
        /// </summary>
        private void SetStreamType(IntPtr loginHandle, VideoStreamType streamType)
        {
            try
            {
                // 根據文檔：0=主輔碼流, 1=主碼流, 2=輔碼流
                int streamTypeValue = streamType switch
                {
                    VideoStreamType.Main => 1,    // 主碼流
                    VideoStreamType.Sub => 2,     // 輔碼流
                    _ => 1                         // 預設主碼流
                };

                IntPtr pValue = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    Marshal.WriteInt32(pValue, streamTypeValue);
                    bool result = NETClient.SetDeviceMode(loginHandle, EM_USEDEV_MODE.RECORD_STREAM_TYPE, pValue);
                    _mainWindow.ShowMessage($"設定碼流類型為 {streamType} (值: {streamTypeValue}), 結果: {result}");
                }
                finally
                {
                    Marshal.FreeHGlobal(pValue);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"設定碼流類型時發生錯誤：{ex.Message}");
            }
        }

        /// <summary>
        /// 建立回放 - 根據文檔完整實現
        /// </summary>
        private IntPtr CreatePlayback(IntPtr loginHandle, int channel, DateTime startTime, DateTime endTime, IntPtr windowHandle, VideoStreamType streamType)
        {
            try
            {
                // 使用 NetTimeExtensions.cs 檔案中定義的方法
                var netStartTime = NetTimeExtensions.FromDateTime(startTime);
                var netEndTime = NetTimeExtensions.FromDateTime(endTime);

                _mainWindow.ShowMessage($"建立回放：通道{channel}, 視窗句柄={windowHandle.ToInt64()}, " +
                    $"開始時間={netStartTime.dwYear}-{netStartTime.dwMonth:D2}-{netStartTime.dwDay:D2} {netStartTime.dwHour:D2}:{netStartTime.dwMinute:D2}:{netStartTime.dwSecond:D2}, " +
                    $"結束時間={netEndTime.dwYear}-{netEndTime.dwMonth:D2}-{netEndTime.dwDay:D2} {netEndTime.dwHour:D2}:{netEndTime.dwMinute:D2}:{netEndTime.dwSecond:D2}");

                // 先設定碼流類型
                SetStreamType(loginHandle, streamType);

                // 建立輸入參數結構
                var pstNetIn = new NET_IN_PLAY_BACK_BY_TIME_INFO
                {
                    stStartTime = netStartTime,     // 使用正確轉換的時間
                    stStopTime = netEndTime,        // 使用正確轉換的時間
                    hWnd = windowHandle,
                    cbDownLoadPos = null,           // 不需要下載進度回調
                    dwPosUser = IntPtr.Zero,
                    fDownLoadDataCallBack = null,   // 不需要數據回調
                    dwDataUser = IntPtr.Zero,
                    nPlayDirection = 0,             // 正向播放
                    nWaittime = 15000              // 增加等待時間到 15 秒
                };

                // 建立輸出參數結構
                var pstNetOut = new NET_OUT_PLAY_BACK_BY_TIME_INFO();

                // 呼叫回放API
                var playbackHandle = NETClient.PlayBackByTime(loginHandle, channel, pstNetIn, ref pstNetOut);

                if (playbackHandle == IntPtr.Zero)
                {
                    var error = NETClient.GetLastError();
                    _mainWindow.ShowMessage($"建立回放失敗，錯誤碼：{error}");
                }
                else
                {
                    _mainWindow.ShowMessage($"回放句柄建立成功：{playbackHandle.ToInt64()}, 輸出資訊已取得");
                }

                return playbackHandle;
            }
            catch (Exception ex)
            {
                _mainWindow.ShowMessage($"建立回放時發生錯誤：{ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 停止指定播放器的回放 - 修正版本，確保清除畫面
        /// </summary>
        public bool StopPlayback(int playerIndex, bool forceCleanup = false)
        {
            lock (_playbackLock)
            {
                if (!_activePlaybacks.TryGetValue(playerIndex, out var session))
                {
                    return false;
                }

                try
                {
                    // 停止回放句柄
                    if (session.PlaybackHandle != IntPtr.Zero)
                    {
                        _mainWindow.ShowMessage($"正在停止回放句柄：{session.PlaybackHandle.ToInt64()}");

                        // 先嘗試停止回放
                        try
                        {
                            NETClient.PlayBackControl(session.PlaybackHandle, PlayBackType.Stop);
                            System.Threading.Thread.Sleep(50);
                        }
                        catch (Exception ex)
                        {
                            _mainWindow.ShowMessage($"回放控制停止失敗：{ex.Message}");
                        }

                        // 直接呼叫 DLL API 停止回放
                        try
                        {
                            bool result = CLIENT_StopPlayBack(session.PlaybackHandle);
                            _mainWindow.ShowMessage($"回放停止結果：{result}");
                        }
                        catch (Exception ex)
                        {
                            _mainWindow.ShowMessage($"停止回放API調用失敗：{ex.Message}");
                        }
                    }

                    // 🔥 關鍵修正：清除播放器顯示內容
                    if (playerIndex >= 0 && playerIndex < _splitScreenManager.VideoPlayers.Count)
                    {
                        var player = _splitScreenManager.VideoPlayers[playerIndex];

                        // 設定退出回放模式
                        player.SetPlaybackMode(false);

                        // 🔥 重要：停止播放並清除畫面
                        player.StopPlay(keepPlaybackState: false);

                        // 🔥 強制刷新顯示，確保沒有殘留畫面
                        player.RefreshDisplay();

                        // 🔥 額外保險：再次確認清除
                        _mainWindow.Dispatcher.Invoke(() =>
                        {
                            player.VideoPanel.Invalidate();
                            player.VideoPanel.Update();
                            player.VideoPanel.Refresh();
                        }, System.Windows.Threading.DispatcherPriority.Send);

                        _mainWindow.ShowMessage($"分割區域 {playerIndex + 1} 顯示已清除");
                    }

                    // 移除會話記錄
                    _activePlaybacks.Remove(playerIndex);
                    _mainWindow.ShowMessage($"回放會話 {playerIndex} 已完全清理");
                    return true;
                }
                catch (Exception ex)
                {
                    _mainWindow.ShowMessage($"停止回放時發生錯誤：{ex.Message}");

                    // 強制清理模式
                    if (forceCleanup)
                    {
                        try
                        {
                            // 🔥 即使發生錯誤也要嘗試清除畫面
                            if (playerIndex >= 0 && playerIndex < _splitScreenManager.VideoPlayers.Count)
                            {
                                var player = _splitScreenManager.VideoPlayers[playerIndex];
                                player.SetPlaybackMode(false);
                                player.StopPlay(keepPlaybackState: false);
                                player.RefreshDisplay();
                            }
                        }
                        catch
                        {
                            // 忽略清理時的錯誤
                        }

                        _activePlaybacks.Remove(playerIndex);
                        _mainWindow.ShowMessage($"強制清理回放會話 {playerIndex}");
                    }
                    return false;
                }
            }
        }

        /// <summary>
        /// 回放控制（暫停、播放、快進、慢放等）- 修正版本
        /// </summary>
        public bool PlaybackControl(PlayBackType controlType)
        {
            var selectedPlayer = _splitScreenManager.SelectedPlayer;
            if (selectedPlayer == null)
            {
                _mainWindow.ShowMessage("請先選擇一個分割區域");
                return false;
            }

            return PlaybackControlByIndex(selectedPlayer.Index, controlType);
        }

        /// <summary>
        /// 按索引執行回放控制
        /// </summary>
        /// <param name="playerIndex">播放器索引</param>
        /// <param name="controlType">控制類型</param>
        /// <returns>是否成功執行控制</returns>
        public bool PlaybackControlByIndex(int playerIndex, PlayBackType controlType)
        {
            lock (_playbackLock)
            {
                if (!_activePlaybacks.TryGetValue(playerIndex, out var session))
                {
                    _mainWindow.ShowMessage($"分割區域 {playerIndex + 1} 不在回放模式");
                    return false;
                }

                try
                {
                    _mainWindow.ShowMessage($"正在對分割區域 {playerIndex + 1} 執行回放控制：{controlType}，句柄：{session.PlaybackHandle.ToInt64()}");

                    bool result = NETClient.PlayBackControl(session.PlaybackHandle, controlType);

                    if (result)
                    {
                        string action = controlType switch
                        {
                            PlayBackType.Play => "播放",
                            PlayBackType.Pause => "暫停",
                            PlayBackType.Stop => "停止",
                            PlayBackType.Fast => "快進",
                            PlayBackType.Slow => "慢放",
                            PlayBackType.Normal => "正常速度",
                            _ => "未知操作"
                        };
                        _mainWindow.ShowMessage($"✅ 分割區域 {playerIndex + 1} 回放控制成功：{action}");
                    }
                    else
                    {
                        var errorCode = NETClient.GetLastError();
                        _mainWindow.ShowMessage($"❌ 分割區域 {playerIndex + 1} 回放控制失敗，錯誤碼：{errorCode}");
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    _mainWindow.ShowMessage($"回放控制時發生錯誤：{ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 取得回放 OSD 時間資訊
        /// </summary>
        public bool GetPlaybackTime(out NET_TIME osdTime, out NET_TIME startTime, out NET_TIME endTime)
        {
            return GetPlaybackTimeByIndex(-1, out osdTime, out startTime, out endTime);
        }

        /// <summary>
        /// 按索引取得回放 OSD 時間資訊
        /// </summary>
        public bool GetPlaybackTimeByIndex(int playerIndex, out NET_TIME osdTime, out NET_TIME startTime, out NET_TIME endTime)
        {
            osdTime = new NET_TIME();
            startTime = new NET_TIME();
            endTime = new NET_TIME();

            if (playerIndex < 0)
            {
                var selectedPlayer = _splitScreenManager.SelectedPlayer;
                if (selectedPlayer == null)
                {
                    return false;
                }
                playerIndex = selectedPlayer.Index;
            }

            lock (_playbackLock)
            {
                if (!_activePlaybacks.TryGetValue(playerIndex, out var session))
                {
                    return false;
                }

                try
                {
                    return NETClient.GetPlayBackOsdTime(session.PlaybackHandle, ref osdTime, ref startTime, ref endTime);
                }
                catch (Exception ex)
                {
                    _mainWindow.ShowMessage($"取得回放時間時發生錯誤：{ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 檢查指定播放器是否在回放模式
        /// </summary>
        public bool IsInPlaybackMode(int playerIndex)
        {
            lock (_playbackLock)
            {
                return _activePlaybacks.ContainsKey(playerIndex);
            }
        }

        /// <summary>
        /// 取得回放會話資訊
        /// </summary>
        public PlaybackSession? GetPlaybackSession(int playerIndex)
        {
            lock (_playbackLock)
            {
                return _activePlaybacks.TryGetValue(playerIndex, out var session) ? session : null;
            }
        }

        /// <summary>
        /// 停止所有回放 - 修正版本，確保清除所有畫面
        /// </summary>
        public void StopAllPlayback()
        {
            List<int> playerIndicesToStop;

            // 先取得要停止的索引列表，然後釋放鎖
            lock (_playbackLock)
            {
                playerIndicesToStop = _activePlaybacks.Keys.ToList();
            }

            // 在鎖外面執行並行停止
            if (playerIndicesToStop.Count > 0)
            {
                Parallel.ForEach(playerIndicesToStop, playerIndex =>
                {
                    try
                    {
                        StopPlayback(playerIndex, forceCleanup: true);
                    }
                    catch (Exception ex)
                    {
                        _mainWindow.ShowMessage($"停止回放 {playerIndex} 時發生錯誤：{ex.Message}");
                    }
                });

                // 最後清除所有會話記錄
                lock (_playbackLock)
                {
                    _activePlaybacks.Clear();
                }

                _mainWindow.ShowMessage($"已停止並清除 {playerIndicesToStop.Count} 個回放會話");
            }
        }

        /// <summary>
        /// 清理資源
        /// </summary>
        public void Cleanup()
        {
            StopAllPlayback();
        }

        // Win32 API 聲明 - 直接呼叫 DLL
        [DllImport("dhnetsdk.dll")]
        private static extern bool CLIENT_StopPlayBack(IntPtr lPlayHandle);
    }

    /// <summary>
    /// 回放會話資訊
    /// </summary>
    public class PlaybackSession
    {
        public IntPtr PlaybackHandle { get; set; }
        public int PlayerIndex { get; set; }
        public string DeviceId { get; set; } = "";
        public int Channel { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public VideoStreamType StreamType { get; set; }
        public required MultiViewPlayer.PlaybackState OriginalState { get; set; }

        public string DisplayInfo => $"設備: {DeviceId}, 通道: {Channel + 1}, 時間: {StartTime:HH:mm:ss}-{EndTime:HH:mm:ss}";
    }
}