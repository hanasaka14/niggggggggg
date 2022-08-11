﻿using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ImGuiNET;
using System;
using System.Linq;

namespace BossMod.AI
{
    class AIManager : IDisposable
    {
        private Autorotation _autorot;
        private AIController _controller;
        private AIConfig _config;
        private int _masterSlot = PartyState.PlayerSlot; // non-zero means corresponding player is master
        private AIBehaviour? _beh;
        private WindowManager.Window? _ui;

        public AIManager(InputOverride inputOverride, Autorotation autorot)
        {
            _autorot = autorot;
            _controller = new(inputOverride, autorot);
            _config = Service.Config.Get<AIConfig>();
            Service.ChatGui.ChatMessage += OnChatMessage;
        }

        public void Dispose()
        {
            SwitchToIdle();
            Service.ChatGui.ChatMessage -= OnChatMessage;
        }

        public void Update()
        {
            if (_autorot.WorldState.Party.ContentIDs[_masterSlot] == 0)
                SwitchToIdle();

            if (!_config.Enabled && _beh != null)
                SwitchToIdle();

            var player = _autorot.WorldState.Party.Player();
            var master = _autorot.WorldState.Party[_masterSlot];
            if (_beh != null && player != null && master != null)
            {
                _beh.Execute(player, master);
            }
            else
            {
                _controller.Clear();
            }
            _controller.Update(player);

            bool showUI = _config.Enabled && player != null;
            if (showUI && _ui == null)
            {
                _ui = WindowManager.CreateWindow("AI", DrawOverlay, () => { }, () => true);
                _ui.SizeHint = new(100, 100);
                _ui.MinSize = new(100, 100);
                _ui.Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            }
            else if (!showUI && _ui != null)
            {
                WindowManager.CloseWindow(_ui);
                _ui = null;
            }
        }

        private void DrawOverlay()
        {
            ImGui.TextUnformatted($"AI: {(_beh != null ? "开" : "关")}, master={_autorot.WorldState.Party[_masterSlot]?.Name}");
            ImGui.TextUnformatted($"navi={_controller.NaviTargetPos}");
            _beh?.DrawDebug();
            if (ImGui.Button("重设"))
                SwitchToIdle();
            ImGui.SameLine();
            if (ImGui.Button("跟随队长"))
            {
                var leader = Service.PartyList[(int)Service.PartyList.PartyLeaderIndex];
                int leaderSlot = leader != null ? _autorot.WorldState.Party.ContentIDs.IndexOf((ulong)leader.ContentId) : -1;
                SwitchToFollow(leaderSlot >= 0 ? leaderSlot : PartyState.PlayerSlot);
            }
        }

        private void SwitchToIdle()
        {
            _beh?.Dispose();
            _beh = null;

            _masterSlot = PartyState.PlayerSlot;
            _controller.Clear();
        }

        private void SwitchToFollow(int masterSlot)
        {
            SwitchToIdle();
            _masterSlot = masterSlot;
            _beh = new AIBehaviour(_controller, _autorot);
        }

        private int FindPartyMemberSlotFromSender(SeString sender)
        {
            var source = sender.Payloads.FirstOrDefault() as PlayerPayload;
            if (source == null)
                return -1;
            var pm = Service.PartyList.FirstOrDefault(pm => pm.Name.TextValue == source.PlayerName && pm.World.Id == source.World.RowId);
            if (pm == null)
                return -1;
            return _autorot.WorldState.Party.ContentIDs.IndexOf((ulong)pm.ContentId);
        }

        private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            if (!_config.Enabled || type != XivChatType.Party)
                return;

            var messagePrefix = message.Payloads.FirstOrDefault() as TextPayload;
            if (messagePrefix?.Text == null || !messagePrefix.Text.StartsWith("vbmai "))
                return;

            var messageData = messagePrefix.Text.Split(' ');
            if (messageData.Length < 2)
                return;

            switch (messageData[1])
            {
                case "follow":
                    var master = FindPartyMemberSlotFromSender(sender);
                    if (master >= 0)
                        SwitchToFollow(master);
                    break;
                case "cancel":
                    SwitchToIdle();
                    break;
                default:
                    Service.Log($"[AI] 未知指令: {messageData[1]}");
                    break;
            }
        }
    }
}
