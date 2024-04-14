using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using ImGuiNET;
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace vfallguy;

public class MainWindow : Window, IDisposable
{
    private GameEvents _gameEvents = new();
    private DebugDrawer _drawer = new();
    private AutoJoinLeave _automation = new();
    private Map? _map;
    private DateTime _now;
    private Vector3 _prevPos;
    private Vector3 _movementDirection;
    private float _movementSpeed;
    private bool _autoJoin;
    private bool _autoLeaveIfNotSolo;
    private bool _showAOEs;
    private bool _showAOEText;
    private bool _showPathfind;
    private DateTime _autoJoinAt = DateTime.MaxValue;
    private DateTime _autoLeaveAt = DateTime.MaxValue;
    private int _numPlayersInDuty;
    private float _autoJoinDelay = 0.5f;
    private float _autoLeaveDelay = 3;
    private int _autoLeaveLimit = 1;
    private bool _autoFarmingMode;
    private string _webSocketUrl = "";
    private int _webSocketPort;
    private string _qqPrivateChatNumber = "";
    private string _qqBotNumber = "";
    private string _gameName = "";
    private HttpClient _httpClient = new HttpClient();
    private int _battlePlayerCount = 1;
    private bool _enableQQBotConfig = false;




    public MainWindow() : base("vfailguy改 by:Cindy-Master 闲鱼司马")
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Apifox/1.0.0 (https://apifox.com)");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        Service.ChatGui.ChatMessage += OnChatMessage;
    }

private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
{
    if (!_autoFarmingMode) return;

    // 检测"获得了金碟声誉"的消息并执行自动刷币
    var match = Regex.Match(message.TextValue, @"获得了(\d+)个金碟声誉。");
    if (match.Success)
    {
        PerformAutoFarming();
    }

    // 新增检测“节目马上就要开始了”这句话
    if (Regex.IsMatch(message.TextValue, @"节目马上就要开始了"))
    {
        // 检查人数是否满足条件
        if (_numPlayersInDuty <= _battlePlayerCount)
        {
            // 发送QQ消息
            SendMessageToQQ($"当前为{_numPlayersInDuty}人对局");
        }
    }
}

    public void Dispose()
    {
        Service.ChatGui.ChatMessage -= OnChatMessage;
        _map?.Dispose();
        _gameEvents.Dispose();
        _automation.Dispose();

    }



    public unsafe override void PreOpenCheck()
    {
        _automation.Update();
        _drawer.Update();

        _now = DateTime.Now;
        var playerPos = Service.ClientState.LocalPlayer?.Position ?? new();
        _movementDirection = playerPos - _prevPos;
        _prevPos = playerPos;
        _movementSpeed = _movementDirection.Length() / Framework.Instance()->FrameDeltaTime;
        _movementDirection = _movementDirection.NormalizedXZ();

        

        IsOpen = Service.ClientState.TerritoryType is 1165 or 1197;

        UpdateMap();
        UpdateAutoJoin();
        UpdateAutoLeave();
        DrawOverlays();

        _drawer.DrawWorldPrimitives();


    }

    public unsafe override void Draw()
    {
        if (ImGui.Button("进本"))
            _automation.RegisterForDuty();
        ImGui.SameLine();
        if (ImGui.Button("退本"))
            _automation.LeaveDuty();
        ImGui.SameLine();
        ImGui.TextUnformatted($"Num players in duty: {_numPlayersInDuty} (autoleave: {(_autoLeaveAt == DateTime.MaxValue ? "never" : $"in {(_autoLeaveAt - _now).TotalSeconds:f1}s")})");
        ImGui.Checkbox("挂机刷币模式(请同时勾选自动排本)", ref _autoFarmingMode);
        ImGui.Checkbox("自动排本(需要在NPC旁边)", ref _autoJoin);
        if (_autoJoin)
        {
            using (ImRaii.PushIndent())
            {
                ImGui.SliderFloat("Delay###j", ref _autoJoinDelay, 0, 10);
            }
        }
        ImGui.Checkbox("如果非单人自动退本", ref _autoLeaveIfNotSolo);
        if (_autoLeaveIfNotSolo)
        {
            using (ImRaii.PushIndent())
            {
                ImGui.SliderFloat("Delay###l", ref _autoLeaveDelay, 0, 10);
                ImGui.SliderInt("Limit", ref _autoLeaveLimit, 1, 23);
            }
        }
        ImGui.Checkbox("AOE范围", ref _showAOEs);
        ImGui.Checkbox("AOE时间", ref _showAOEText);
        ImGui.Checkbox("推荐路线", ref _showPathfind);


        ImGui.Checkbox("启用QQ机器人配置", ref _enableQQBotConfig);


        if (_enableQQBotConfig)
        {
            ImGui.Text("QQ机器人配置:");
            ImGui.InputText("本账号名称", ref _gameName, 255);
            ImGui.InputText("私聊的QQ号", ref _qqPrivateChatNumber, 255);
            ImGui.InputText("BotQQ号", ref _qqBotNumber, 255);
            ImGui.InputText("WebSocket URL", ref _webSocketUrl, 255);
            ImGui.InputInt("端口号", ref _webSocketPort);
            ImGui.SliderInt("战局人数通知(小于等于这个值都会提醒)", ref _battlePlayerCount, 1, 24, "%d人战局");

            if (ImGui.Button("应用"))
            {
                SendMessageToQQ("机器人已成功连接\n" +
                                $"本账号名称: {_gameName}\n" +
                                $"通知QQ私聊号码: {_qqPrivateChatNumber}\n");
                
            }
        }



        if (_map != null)
        {
            var strats = _map.Strats();
            if (strats.Length > 0)
                ImGui.TextUnformatted(strats);
            ImGui.TextUnformatted($"Pos: {_map.PlayerPos}");
            ImGui.TextUnformatted($"Path: {_map.PathSkip}-{_map.Path.Count}");
            ImGui.TextUnformatted($"Speed: {_movementSpeed}");

            /*foreach (var aoe in _map.AOEs.Where(aoe => aoe.NextActivation != default))
            {
                var nextActivation = (aoe.NextActivation - _now).TotalSeconds;
                using (ImRaii.PushColor(ImGuiCol.Text, nextActivation < 0 ? 0xff0000ff : 0xffffffff))
                    ImGui.TextUnformatted($"{aoe.Type} R{aoe.R1} @ {aoe.Origin}: activate in {nextActivation:f3}, repeat={aoe.Repeat}, seqd={aoe.SeqDelay}");
            }*/
        }
    }
    private async void SendMessageToQQ(string message)
    {
        long qqPrivateChatNumberLong = Convert.ToInt64(_qqPrivateChatNumber);
        long qqBotNumberLong = Convert.ToInt64(_qqBotNumber);
        string requestUrl = $"{_webSocketUrl}:{_webSocketPort}/v1/LuaApiCaller?funcname=MagicCgiCmd&timeout=35&qq={qqBotNumberLong}";
        var postData = new
        {
            CgiCmd = "MessageSvc.PbSendMsg",
            CgiRequest = new
            {
                ToUin = qqPrivateChatNumberLong,
                ToType = 1,
                Content = message
            }
        };

        var content = new StringContent(JsonConvert.SerializeObject(postData), Encoding.UTF8, "application/json");
        try
        {
            var response = await _httpClient.PostAsync(requestUrl, content);
            var responseString = await response.Content.ReadAsStringAsync();

        }
        catch (HttpRequestException e)
        {
        }
    }


    private void UpdateMap()
    {
        if (Service.Condition[ConditionFlag.BetweenAreas])
            return;

        Type? mapType = null;
        if (IsOpen)
        {
            if (Service.ClientState.TerritoryType == 1197)
            {
                //mapType = typeof(MapTest);
            }
            else
            {
                var pos = Service.ClientState.LocalPlayer!.Position;
                mapType = pos switch
                {
                    //{ X: >= -20 and <= 20, Z: >= -400 and <= -100 } => typeof(Map1A),
                    { X: >= -40 and <= 40, Z: >= 100 and <= 350 } => typeof(Map3),
                    _ => null
                };
            }
        }

        if (_map?.GetType() != mapType)
        {
            _map?.Dispose();
            _map = null;
            if (mapType != null)
                _map = (Map?)Activator.CreateInstance(mapType, _gameEvents);
        }

        _map?.Update();
    }
    private void PerformAutoFarming()

    {

        _automation.LeaveDuty();

    }

    private void UpdateAutoJoin()
    {
        bool wantAutoJoin = _autoJoin && _automation.Idle && IsOpen && Service.ClientState.TerritoryType == 1197 && !Service.Condition[ConditionFlag.WaitingForDutyFinder] && !Service.Condition[ConditionFlag.BetweenAreas];
        if (!wantAutoJoin)
        {
            _autoJoinAt = DateTime.MaxValue;
        }
        else if (_autoJoinAt == DateTime.MaxValue)
        {
            Service.Log.Debug($"Auto-joining in {_autoJoinDelay:f2}s...");
            _autoJoinAt = _now.AddSeconds(_autoJoinDelay);
        }
        else if (_now >= _autoJoinAt)
        {
            Service.Log.Debug($"Auto-joining");
            _automation.RegisterForDuty();
            _autoJoinAt = DateTime.MaxValue;
        }
    }

    private void UpdateAutoLeave()
    {
        _numPlayersInDuty = Service.ClientState.TerritoryType == 1165 && Service.Condition[ConditionFlag.BoundByDuty] && !Service.Condition[ConditionFlag.BetweenAreas]
            ? Service.ObjectTable.Count(o => o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
            : 0;
        bool wantAutoLeave = _autoLeaveIfNotSolo && _numPlayersInDuty > _autoLeaveLimit && _automation.Idle;
        if (!wantAutoLeave)
        {
            _autoLeaveAt = DateTime.MaxValue;
        }
        else if (_autoLeaveAt == DateTime.MaxValue)
        {
            Service.Log.Debug($"Auto-leaving in {_autoLeaveDelay:f2}s...");
            _autoLeaveAt = _now.AddSeconds(_autoLeaveDelay);
        }
        else if (_now >= _autoLeaveAt)
        {
            Service.Log.Debug($"Auto-leaving: {_numPlayersInDuty} players");
            _automation.LeaveDuty();
            _autoLeaveAt = DateTime.MaxValue;
        }
    }

    private void DrawOverlays()
    {
        if (_map == null || Service.Condition[ConditionFlag.BetweenAreas])
            return;

        if (_showPathfind)
        {
            var from = _map.PlayerPos;
            for (int i = _map.PathSkip; i < _map.Path.Count; ++i)
            {
                var wp = _map.Path[i];
                var delay = (wp.StartMoveAt - _now).TotalSeconds;
                _drawer.DrawWorldLine(from, wp.Dest, i > 0 ? 0xff00ffff : delay <= 0 ? 0xff00ff00 : 0xff0000ff);
                if (delay > 0)
                    _drawer.DrawWorldText(from, 0xff0000ff, $"{delay:f3}");
                from = wp.Dest;
            }
        }

        foreach (var aoe in _map.AOEs.Where(aoe => aoe.NextActivation != default))
        {
            var nextActivation = (aoe.NextActivation - _now).TotalSeconds;
            if (nextActivation < 2.5f)
            {
                var (aoeEnter, aoeExit) = _movementSpeed > 0 ? aoe.Intersect(_map.PlayerPos, _movementDirection) : aoe.Contains(_map.PlayerPos) ? (0, float.PositiveInfinity) : (float.NaN, float.NaN);
                var delay = !float.IsNaN(aoeEnter) ? aoe.ActivatesBetween(_now, aoeEnter * Map.InvSpeed - 0.1f, aoeExit * Map.InvSpeed + 0.1f) : 0;
                var color = delay > 0 ? 0xff0000ff : 0xff00ffff;
                if (_showAOEs)
                {
                    aoe.Draw(_drawer, color);
                }
                if (_showAOEText)
                {
                    var text = $"{nextActivation:f3} [{aoeEnter * Map.InvSpeed:f2}-{aoeExit * Map.InvSpeed:f2}, {delay:f2}]";
                    var dir = (aoe.Origin - _map.PlayerPos).NormalizedXZ();
                    var (enter, exit) = aoe.Intersect(_map.PlayerPos, dir);
                    var textPos = _map.PlayerPos + dir * MathF.Max(enter, 0);
                    _drawer.DrawWorldText(textPos, color, text);
                }
            }
        }
    }
}
