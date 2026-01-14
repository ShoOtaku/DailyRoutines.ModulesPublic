using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace DailyRoutines.ModulesPublic;

public class AutoRecordSubTimeLeft : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = "自动记录剩余游戏时间",
        Description = "登录时, 自动记录保存当前账号剩余的游戏时间, 并显示在服务器信息栏",
        Category    = ModuleCategories.General,
        Author      = ["Due"]
    };

    public override ModulePermission Permission { get; } = new() { CNOnly = true, CNDefaultEnabled = true };

    private static readonly CompSig AgentLobbyOnLoginSig = new("E8 ?? ?? ?? ?? 41 C6 45 ?? ?? E9 ?? ?? ?? ?? 83 FB 03");

    private unsafe delegate nint AgentLobbyOnLoginDelegate(AgentLobby* agent);

    private static Hook<AgentLobbyOnLoginDelegate>? AgentLobbyOnLoginHook;

    private static Config           ModuleConfig = null!;
    private static IDtrBarEntry?    Entry;
    private static PlaytimeManager? Manager;

    protected override unsafe void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new();

        var path = Path.Join(ConfigDirectoryPath, "PlatimeData.log");
        Manager = new PlaytimeManager(path);
        Manager.Start();

        Entry         ??= DService.Instance().DTRBar.Get("DailyRoutines-GameTimeLeft");
        Entry.OnClick =   OnDTREntryClick;

        // 初次更新
        UpdateEntryAndTimeInfo();

        AgentLobbyOnLoginHook ??= AgentLobbyOnLoginSig.GetHook<AgentLobbyOnLoginDelegate>(AgentLobbyOnLoginDetour);
        AgentLobbyOnLoginHook.Enable();

        DService.Instance().ClientState.Login  += OnLogin;
        DService.Instance().ClientState.Logout += OnLogout;

        FrameworkManager.Instance().Reg(OnUpdate, 5_000);

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "CharaSelect",        OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostDraw,            "CharaSelect",        OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_CharaSelectRemain", OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostDraw,            "_CharaSelectRemain", OnAddon);
    }

    protected override void ConfigUI()
    {
        var contentID = LocalPlayerState.ContentID;
        if (contentID == 0) return;

        if (!ModuleConfig.Infos.TryGetValue(contentID, out var info) ||
            info.Record == DateTime.MinValue                         ||
            info.LeftMonth == TimeSpan.MinValue && info.LeftTime == TimeSpan.MinValue)
        {
            ImGui.TextColored(KnownColor.Orange.ToVector4(), "当前角色暂无数据, 请重新登录游戏以记录");
            return;
        }

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), "上次记录:");
        ImGui.SameLine();
        ImGui.TextUnformatted($"{info.Record}");

        ImGui.NewLine();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), "月卡剩余时间:");
        ImGui.SameLine();
        ImGui.TextUnformatted(FormatTimeSpan(info.LeftMonth == TimeSpan.MinValue ? TimeSpan.Zero : info.LeftMonth));

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), "点卡剩余时间:");
        ImGui.SameLine();
        ImGui.TextUnformatted(FormatTimeSpan(info.LeftTime));
    }

    protected override void Uninit()
    {
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);
        FrameworkManager.Instance().Unreg(OnUpdate);

        Entry?.Remove();
        Entry = null;

        Manager?.Dispose();
        Manager = null;

        DService.Instance().ClientState.Login  -= OnLogin;
        DService.Instance().ClientState.Logout -= OnLogout;
    }

    private void OnLogin()
    {
        Manager?.OnLogin();
        TaskHelper.Enqueue
        (() =>
            {
                var contentID = LocalPlayerState.ContentID;
                if (contentID == 0) return false;
                UpdateEntryAndTimeInfo(contentID);
                return true;
            }
        );
    }

    private void OnLogout(int code, int type)
    {
        Manager?.OnLogout();
        TaskHelper?.Abort();
    }

    private static void OnUpdate(IFramework _) => UpdateEntryAndTimeInfo();

    private unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (CharaSelect == null) return;

        if (type == AddonEvent.PostDraw)
        {
            if (!Throttler.Throttle("AutoRecordSubTimeLeft-OnAddonDraw"))
                return;
        }

        var agent = AgentLobby.Instance();
        if (agent == null) return;

        var info = agent->LobbyData.LobbyUIClient.SubscriptionInfo;
        if (info == null) return;

        var contentID = agent->HoveredCharacterContentId;
        if (contentID == 0) return;

        if (agent->WorldIndex == -1) return;

        var timeInfo = GetLeftTimeSecond(*info);
        ModuleConfig.Infos[contentID] = new
        (
            StandardTimeManager.Instance().Now,
            timeInfo.MonthTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.MonthTime),
            timeInfo.PointTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.PointTime)
        );
        ModuleConfig.Save(this);

        if (CharaSelectRemain != null)
        {
            var textNode = CharaSelectRemain->GetTextNodeById(7);

            if (textNode != null)
            {
                textNode->SetPositionFloat(-20, 40);
                textNode->SetText
                (
                    $"剩余天数: {FormatTimeSpan(TimeSpan.FromSeconds(timeInfo.MonthTime))}\n" +
                    $"剩余时长: {FormatTimeSpan(TimeSpan.FromSeconds(timeInfo.PointTime))}"
                );
            }
        }

        UpdateEntryAndTimeInfo(contentID);
    }

    private unsafe nint AgentLobbyOnLoginDetour(AgentLobby* agent)
    {
        var ret = AgentLobbyOnLoginHook.Original(agent);
        UpdateSubInfo(agent);
        return ret;
    }

    private unsafe void UpdateSubInfo(AgentLobby* agent)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue
        (
            () =>
            {
                try
                {
                    var info = agent->LobbyData.LobbyUIClient.SubscriptionInfo;
                    if (info == null) return false;

                    var contentID = agent->HoveredCharacterContentId;
                    if (contentID == 0) return false;

                    if (agent->WorldIndex == -1) return false;

                    var timeInfo = GetLeftTimeSecond(*info);
                    ModuleConfig.Infos[contentID] = new
                    (
                        StandardTimeManager.Instance().Now,
                        timeInfo.MonthTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.MonthTime),
                        timeInfo.PointTime == 0 ? TimeSpan.MinValue : TimeSpan.FromSeconds(timeInfo.PointTime)
                    );
                    ModuleConfig.Save(this);

                    UpdateEntryAndTimeInfo(contentID);
                }
                catch (Exception ex)
                {
                    Warning("更新游戏点月卡订阅信息失败", ex);
                    NotificationWarning(ex.Message, "更新游戏点月卡订阅信息失败");
                }

                return true;
            },
            "更新订阅信息"
        );
    }

    private static (int MonthTime, int PointTime) GetLeftTimeSecond(LobbySubscriptionInfo info)
    {
        var size = Marshal.SizeOf(info);
        var arr  = new byte[size];
        var ptr  = nint.Zero;

        try
        {
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(info, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        var month = string.Join(string.Empty, arr.Skip(16).Take(3).Reverse().Select(x => x.ToString("X2")));
        var point = string.Join(string.Empty, arr.Skip(24).Take(3).Reverse().Select(x => x.ToString("X2")));
        return (Convert.ToInt32(month, 16), Convert.ToInt32(point, 16));
    }

    private static void UpdateEntryAndTimeInfo(ulong contentID = 0)
    {
        if (Entry == null || Manager == null) return;

        if (contentID == 0)
            contentID = LocalPlayerState.ContentID;

        if (contentID == 0                                           ||
            DService.Instance().Condition[ConditionFlag.InCombat]    ||
            !ModuleConfig.Infos.TryGetValue(contentID, out var info) ||
            info.Record == DateTime.MinValue                         ||
            info.LeftMonth == TimeSpan.MinValue && info.LeftTime == TimeSpan.MinValue)
        {
            Entry.Shown = false;
            return;
        }

        var isMonth    = info.LeftMonth != TimeSpan.MinValue;
        var expireTime = info.Record + (isMonth ? info.LeftMonth : info.LeftTime);

        var textBuilder = new SeStringBuilder();
        textBuilder.AddUiForeground($"[{(isMonth ? "月卡" : "点卡")}] ", 25)
                   .AddText($"{expireTime:MM/dd HH:mm}");
        Entry.Text = textBuilder.Build();

        var stats = Manager.CachedStats;

        var tooltipBuilder = new SeStringBuilder();
        tooltipBuilder.AddUiForeground("[过期时间]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{expireTime}")
                      .Add(NewLinePayload.Payload)
                      .AddUiForeground("[剩余时长]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{FormatTimeSpan(expireTime - StandardTimeManager.Instance().Now)}")
                      .Add(NewLinePayload.Payload)
                      .Add(NewLinePayload.Payload)
                      .AddUiForeground("[本日游玩时长]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{FormatTimeSpan(stats.Today)}")
                      .Add(NewLinePayload.Payload)
                      .AddUiForeground("[昨日游玩时长]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{FormatTimeSpan(stats.Yesterday)}")
                      .Add(NewLinePayload.Payload)
                      .AddUiForeground("[七日游玩时长]", 28)
                      .Add(NewLinePayload.Payload)
                      .AddText($"{FormatTimeSpan(stats.Last7Days)}")
                      .Add(NewLinePayload.Payload)
                      .Add(NewLinePayload.Payload)
                      .AddText("(左键: ")
                      .AddUiForeground("模块配置界面", 34)
                      .AddText(")")
                      .Add(NewLinePayload.Payload)
                      .AddText("(右键: ")
                      .AddUiForeground("时长充值页面", 34)
                      .AddText(")");
        Entry.Tooltip = tooltipBuilder.Build();

        Entry.Shown = true;
    }

    private static void OnDTREntryClick(DtrInteractionEvent eventData)
    {
        switch (eventData.ClickType)
        {
            case MouseClickType.Left:
                ChatManager.Instance().SendMessage($"/pdr search {nameof(AutoRecordSubTimeLeft)}");
                break;
            case MouseClickType.Right:
                Util.OpenLink("https://pay.sdo.com/item/GWPAY-100001900");
                break;
        }
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        var parts = new List<string>(4);

        if (timeSpan.Days > 0)
            parts.Add($"{timeSpan.Days} 天");
        if (timeSpan.Hours > 0)
            parts.Add($"{timeSpan.Hours} 小时");
        if (timeSpan.Minutes > 0)
            parts.Add($"{timeSpan.Minutes} 分");
        if (timeSpan.Seconds > 0)
            parts.Add($"{timeSpan.Seconds} 秒");

        return parts.Count > 0 ? string.Join(" ", parts) : "0 秒";
    }

    private class Config : ModuleConfiguration
    {
        public Dictionary<ulong, (DateTime Record, TimeSpan LeftMonth, TimeSpan LeftTime)> Infos = [];
    }

    private sealed class PlaytimeManager : IDisposable
    {
        private readonly string   logPath;
        private readonly string   sessionID  = Guid.NewGuid().ToString("N");
        private readonly int      processID  = Environment.ProcessId;
        private readonly TimeSpan staleGrace = TimeSpan.FromSeconds(90);
        private readonly Mutex    fileMutex;

        private CancellationTokenSource? cancelSource;
        private Task?                    backgroundTask;

        public PlaytimeStats CachedStats;

        private readonly Dictionary<string, SessionState> activeSessions   = new(StringComparer.Ordinal);
        private readonly List<TimeInterval>               historyIntervals = [];

        private long lastFilePosition;
        private bool isRunning;
        private int  compactCounter;

        public record struct PlaytimeStats
        (
            TimeSpan Today,
            TimeSpan Yesterday,
            TimeSpan Last7Days
        );

        public PlaytimeManager(string path)
        {
            logPath = path;

            // 初始化系统互斥锁
            var mutexName = $@"Global\DailyRoutines-PlaytimeTracker-{GetStableHashCode(Path.GetFullPath(logPath).ToUpperInvariant())}";
            fileMutex = new Mutex(false, mutexName);

            try
            {
                var dir = Path.GetDirectoryName(logPath);
                if (dir != null) Directory.CreateDirectory(dir);
                // 启动时检查是否有残留的 stale session 并关闭
                CheckAndCloseStaleSessions();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoRecordSubTimeLeft] Error initializing: {ex.Message}");
            }
        }

        private static uint GetStableHashCode(string value)
        {
            var hash = 2166136261;
            foreach (var character in value)
                hash = hash * 16777619 ^ character;
            return hash;
        }

        public void Start()
        {
            if (isRunning) return;
            isRunning    = true;
            cancelSource = new CancellationTokenSource();

            // 启动后台任务：负责心跳写入、日志读取、数据统计
            backgroundTask = Task.Factory.StartNew(BackgroundTaskLoop, cancelSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void OnLogin() =>
            WriteEvent("start");

        public void OnLogout() =>
            WriteEvent("stop");

        public void Dispose()
        {
            isRunning = false;
            cancelSource?.Cancel();

            try
            {
                backgroundTask?.Wait(1000);
            }
            catch
            {
                /* ignored */
            }

            cancelSource?.Dispose();

            // 尝试写入停止事件 (如果还在运行)
            try
            {
                WriteEvent("stop");
            }
            catch
            {
                /* ignored */
            }

            fileMutex.Dispose();
        }

        private async Task BackgroundTaskLoop()
        {
            var token = cancelSource!.Token;

            // 初始加载
            UpdateData();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (GameState.IsLoggedIn)
                        WriteEvent("heartbeat");

                    UpdateData();

                    if (++compactCounter >= 12)
                    {
                        compactCounter = 0;
                        TryCompactLog();
                    }

                    await Task.Delay(5000, token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoRecordSubTimeLeft] Background loop error: {ex}");
            }
        }

        private void WriteEvent(string eventType)
        {
            var line = $"{sessionID}\t{processID}\t{eventType}\t{StandardTimeManager.Instance().UTCNow:O}{Environment.NewLine}";

            try
            {
                if (fileMutex.WaitOne(2000)) // 等待获取锁，最多2秒
                {
                    try
                    {
                        File.AppendAllText(logPath, line);
                    }
                    finally
                    {
                        fileMutex.ReleaseMutex();
                    }
                }
            }
            catch
            {
                // 忽略写入错误
            }
        }

        private void CheckAndCloseStaleSessions()
        {
            UpdateData();

            var now        = StandardTimeManager.Instance().UTCNow;
            var linesToAdd = new List<string>();

            foreach (var kvp in activeSessions)
            {
                // 如果不是当前进程的 session 且超时
                if (kvp.Key == sessionID) continue; // 忽略自己

                if (now - kvp.Value.LastEventTime > staleGrace)
                {
                    var closeTime = kvp.Value.LastEventTime + staleGrace;
                    linesToAdd.Add($"{kvp.Key}\t-1\tautoClose\t{closeTime:O}");
                }
            }

            if (linesToAdd.Count > 0)
            {
                try
                {
                    if (fileMutex.WaitOne(2000))
                    {
                        try
                        {
                            File.AppendAllLines(logPath, linesToAdd);
                        }
                        finally
                        {
                            fileMutex.ReleaseMutex();
                        }
                    }
                }
                catch
                {
                    /* ignored */
                }

                // 再次更新以应用 autoClose
                UpdateData();
            }
        }

        private void TryCompactLog()
        {
            const long COMPACT_THRESHOLD = 500 * 1024;

            try
            {
                var fileInfo = new FileInfo(logPath);
                if (!fileInfo.Exists || fileInfo.Length < COMPACT_THRESHOLD) return;

                if (fileMutex.WaitOne(0))
                {
                    try
                    {
                        fileInfo.Refresh();
                        if (fileInfo.Length < COMPACT_THRESHOLD) return;

                        var lines         = File.ReadAllLines(logPath);
                        var now           = StandardTimeManager.Instance().UTCNow;
                        var retentionTime = now.AddDays(-7);

                        var sessionInfo = new Dictionary<string, (DateTime LastTime, bool HasStop)>(StringComparer.Ordinal);

                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var span = line.AsSpan();

                            var t1 = span.IndexOf('\t');
                            if (t1 == -1) continue;
                            var t2 = span[(t1 + 1)..].IndexOf('\t');
                            if (t2 == -1) continue;
                            t2 += t1 + 1;
                            var t3 = span[(t2 + 1)..].IndexOf('\t');
                            if (t3 == -1) continue;
                            t3 += t2 + 1;

                            var id            = span[..t1].ToString();
                            var eventTypeSpan = span.Slice(t2 + 1, t3 - t2 - 1);
                            var timeSpan      = span[(t3 + 1)..];

                            if (!DateTime.TryParseExact(timeSpan, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                                continue;

                            if (!sessionInfo.TryGetValue(id, out var info))
                                info = (ts, false);

                            if (ts > info.LastTime) info.LastTime = ts;

                            switch (eventTypeSpan)
                            {
                                case "stop":
                                case "autoClose":
                                    info.HasStop = true;
                                    break;
                                case "start":
                                    info.HasStop = false;
                                    break;
                            }

                            sessionInfo[id] = info;
                        }

                        // 2. 过滤和压缩
                        var compactedLines = new List<string>(lines.Length);

                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var span = line.AsSpan();

                            var t1 = span.IndexOf('\t');
                            if (t1 == -1) continue;

                            var id = span[..t1].ToString();

                            if (!sessionInfo.TryGetValue(id, out var info)) continue;

                            // 规则1: 删除一周前的数据 (如果整个 Session 都在一周前)
                            if (info.LastTime < retentionTime)
                                continue;

                            // 规则2: 压缩非活跃会话
                            // 活跃定义：LastTime 在 Grace 范围内，且没有 Stop
                            var isActive = !info.HasStop && now - info.LastTime < staleGrace;

                            if (!isActive)
                            {
                                // 非活跃：过滤 Heartbeat
                                var t2 = span[(t1 + 1)..].IndexOf('\t');
                                if (t2 == -1) continue;
                                t2 += t1 + 1;
                                var t3 = span[(t2 + 1)..].IndexOf('\t');
                                if (t3 == -1) continue;
                                t3 += t2 + 1;
                                var eventTypeSpan = span.Slice(t2 + 1, t3 - t2 - 1);

                                if (eventTypeSpan is "heartbeat") 
                                    continue;
                            }

                            compactedLines.Add(line);
                        }

                        File.WriteAllLines(logPath, compactedLines);

                        lastFilePosition = 0;
                        activeSessions.Clear();
                        historyIntervals.Clear();
                    }
                    finally
                    {
                        fileMutex.ReleaseMutex();
                    }

                    UpdateData();
                }
            }
            catch
            {
                // ignored
            }
        }

        private void UpdateData()
        {
            // 1. 读取增量日志
            ParseLogFile();

            // 2. 计算统计数据
            var now        = StandardTimeManager.Instance().UTCNow; // UTC
            var todayLocal = StandardTimeManager.Instance().Today;  // Local Midnight

            var todayStartUTC = todayLocal.ToUniversalTime();
            var todayEndUTC   = todayStartUTC.AddDays(1);

            var yesterdayStartUTC = todayStartUTC.AddDays(-1);

            var weekStartUTC = todayStartUTC.AddDays(-6); // 7天包括今天

            // 获取当前活跃的 intervals (临时)
            var activeIntervals = GetActiveIntervals(now);

            // 计算
            var today     = CalculateDuration(todayStartUTC,     todayEndUTC,   activeIntervals);
            var yesterday = CalculateDuration(yesterdayStartUTC, todayStartUTC, activeIntervals);
            var last7     = CalculateDuration(weekStartUTC,      todayEndUTC,   activeIntervals);

            // 更新缓存
            CachedStats = new PlaytimeStats(today, yesterday, last7);
        }

        private void ParseLogFile()
        {
            if (!File.Exists(logPath)) return;

            try
            {
                using var fs  = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var       len = fs.Length;

                if (len < lastFilePosition)
                {
                    lastFilePosition = 0;
                    activeSessions.Clear();
                    historyIntervals.Clear();
                }

                if (len == lastFilePosition) return;

                fs.Seek(lastFilePosition, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                var newClosedIntervals = new List<TimeInterval>();

                while (reader.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var span = line.AsSpan();

                    var t1 = span.IndexOf('\t');
                    if (t1 == -1) continue;
                    var t2 = span[(t1 + 1)..].IndexOf('\t');
                    if (t2 == -1) continue;
                    t2 += t1 + 1;
                    var t3 = span[(t2 + 1)..].IndexOf('\t');
                    if (t3 == -1) continue;
                    t3 += t2 + 1;

                    var id            = span[..t1].ToString();
                    var eventTypeSpan = span.Slice(t2 + 1, t3 - t2 - 1);
                    var timeSpan      = span[(t3 + 1)..];

                    if (!DateTime.TryParseExact(timeSpan, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                        continue;

                    if (!activeSessions.TryGetValue(id, out var state))
                    {
                        state              = new SessionState();
                        activeSessions[id] = state;
                    }

                    switch (eventTypeSpan)
                    {
                        case "start":
                        {
                            // 如果之前已经是 Start 状态且时间更晚，说明之前的没闭合（异常），先闭合它
                            if (state.StartTime.HasValue && ts > state.StartTime.Value)
                                newClosedIntervals.Add(new TimeInterval(state.StartTime.Value, ts));
                            state.StartTime = ts;
                            break;
                        }
                        case "stop":
                        case "autoClose":
                        {
                            if (state.StartTime.HasValue)
                            {
                                // 闭合区间
                                var end                              = ts;
                                if (end < state.StartTime.Value) end = state.StartTime.Value; // 防御性
                                newClosedIntervals.Add(new TimeInterval(state.StartTime.Value, end));
                                state.StartTime = null;
                            }

                            break;
                        }
                        case "heartbeat":
                        {
                            state.StartTime ??= ts;

                            break;
                        }
                    }

                    state.LastEventTime = ts;
                }

                lastFilePosition = fs.Position;

                // 合并新的闭合区间到历史记录中
                if (newClosedIntervals.Count > 0)
                    MergeIntervals(newClosedIntervals);
            }
            catch (Exception ex)
            {
                // 日志读取错误不应崩溃
                Console.WriteLine($"[AutoRecordSubTimeLeft] Parse error: {ex.Message}");
            }
        }

        private void MergeIntervals(List<TimeInterval> newIntervals)
        {
            historyIntervals.AddRange(newIntervals);
            if (historyIntervals.Count == 0) return;

            historyIntervals.Sort((a, b) => a.Start.CompareTo(b.Start));

            var merged  = new List<TimeInterval>(historyIntervals.Count);
            var current = historyIntervals[0];

            for (var i = 1; i < historyIntervals.Count; i++)
            {
                var next = historyIntervals[i];

                if (next.Start <= current.End) // 重叠或相接
                {
                    if (next.End > current.End) current = new TimeInterval(current.Start, next.End);
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);

            historyIntervals.Clear();
            historyIntervals.AddRange(merged);
        }

        private List<TimeInterval> GetActiveIntervals(DateTime now)
        {
            var list = new List<TimeInterval>();

            foreach (var kvp in activeSessions)
            {
                if (kvp.Value.StartTime.HasValue)
                {
                    var start = kvp.Value.StartTime.Value;
                    var end   = kvp.Value.LastEventTime;

                    // 如果最后一次心跳距今很近，认为活跃到现在
                    if (now - end < staleGrace)
                        end = now;
                    else
                    {
                        // 否则只算到最后一次心跳 + Grace
                        end += staleGrace;
                    }

                    if (end > start)
                        list.Add(new TimeInterval(start, end));
                }
            }

            return list;
        }

        private TimeSpan CalculateDuration(DateTime rangeStart, DateTime rangeEnd, List<TimeInterval> activeIntervals)
        {
            long ticks = 0;

            var idx          = historyIntervals.BinarySearch(new TimeInterval(rangeStart, rangeStart), IntervalStartComparer.Instance);
            if (idx < 0) idx = ~idx;

            if (idx > 0 && historyIntervals[idx - 1].End > rangeStart)
                idx--;

            for (var i = idx; i < historyIntervals.Count; i++)
            {
                var interval = historyIntervals[i];
                if (interval.Start >= rangeEnd) break; // 超出范围

                var start = interval.Start > rangeStart ? interval.Start : rangeStart;
                var end   = interval.End   < rangeEnd ? interval.End : rangeEnd;

                if (end > start)
                    ticks += (end - start).Ticks;
            }

            foreach (var active in activeIntervals)
            {
                // 裁剪到查询窗口
                if (active.End <= rangeStart || active.Start >= rangeEnd) continue;

                var start = active.Start > rangeStart ? active.Start : rangeStart;
                var end   = active.End   < rangeEnd ? active.End : rangeEnd;

                if (end > start)
                    ticks += (end - start).Ticks;
            }

            return new TimeSpan(ticks);
        }

        private class SessionState
        {
            public DateTime? StartTime;
            public DateTime  LastEventTime;
        }

        private readonly record struct TimeInterval
        (
            DateTime Start,
            DateTime End
        );

        private class IntervalStartComparer : IComparer<TimeInterval>
        {
            public static readonly IntervalStartComparer Instance = new();

            public int Compare(TimeInterval x, TimeInterval y) => x.Start.CompareTo(y.Start);
        }
    }
}
