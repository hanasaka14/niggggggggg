﻿using BossMod;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace UIDev
{
    class ReplayVisualizer : IDisposable
    {
        private ReplayPlayer _player;
        private BossModuleManager _mgr;
        private DateTime _first;
        private DateTime _last;
        private DateTime _curTime; // note that is could fall between frames
        private DateTime _prevFrame;
        private float _playSpeed = 0;
        private float _azimuth;
        private int _povSlot = PartyState.PlayerSlot;
        private ConfigUI _config;
        private bool _showConfig;
        private EventList _events;
        private AnalysisManager _analysis;

        public ReplayVisualizer(Replay data)
        {
            _player = new(data);
            _mgr = new(_player.WorldState);
            _curTime = _first = data.Ops.First().Timestamp;
            _last = data.Ops.Last().Timestamp;
            _player.AdvanceTo(_first, _mgr.Update);
            _config = new(Service.Config, _player.WorldState);
            _events = new(data, MoveTo);
            _analysis = new(data);
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            var curFrame = DateTime.Now;
            if (_playSpeed > 0)
                MoveTo(_curTime + (curFrame - _prevFrame) * _playSpeed);
            _prevFrame = curFrame;

            DrawControlRow();
            DrawTimelineRow();
            ImGui.TextUnformatted($"Num loaded modules: {_mgr.LoadedModules.Count}, num active modules: {_mgr.LoadedModules.Count(m => m.StateMachine.ActiveState != null)}, active module: {_mgr.ActiveModule?.GetType()}");
            ImGui.DragFloat("Camera azimuth", ref _azimuth, 1, -180, 180);
            if (_mgr.ActiveModule != null)
            {
                var drawTimerPre = DateTime.Now;
                _mgr.ActiveModule.Draw(_azimuth / 180 * MathF.PI, _povSlot, null);
                var drawTimerPost = DateTime.Now;

                ImGui.TextUnformatted($"Draw time: {(drawTimerPost - drawTimerPre).TotalMilliseconds:f3}ms, Downtime in: {_mgr.ActiveModule.PlanExecution?.EstimateTimeToNextDowntime(_mgr.ActiveModule.StateMachine):f2}, Positioning in: {_mgr.ActiveModule.PlanExecution?.EstimateTimeToNextPositioning(_mgr.ActiveModule.StateMachine):f2}, Vuln in: {_mgr.ActiveModule.PlanExecution?.EstimateTimeToNextVulnerable(_mgr.ActiveModule.StateMachine):f2}, Components:");
                foreach (var comp in _mgr.ActiveModule.Components)
                {
                    ImGui.SameLine();
                    ImGui.TextUnformatted(comp.GetType().Name);
                }

                if (ImGui.CollapsingHeader("Plan execution"))
                {
                    if (ImGui.Button("Show timeline"))
                    {
                        var timeline = new StateMachineVisualizer(_mgr.ActiveModule.StateMachine);
                        var w = WindowManager.CreateWindow($"{_mgr.ActiveModule.GetType().Name} timeline", timeline.Draw, () => { }, () => true);
                        w.SizeHint = new(600, 600);
                        w.MinSize = new(100, 100);
                    }
                    ImGui.SameLine();
                    _mgr.ActiveModule.PlanConfig?.DrawSelectionUI(_mgr.ActiveModule.Raid[_povSlot]?.Class ?? Class.None, _mgr.ActiveModule.StateMachine);

                    _mgr.ActiveModule.PlanExecution?.Draw(_mgr.ActiveModule.StateMachine);
                }
            }

            DrawPartyTable();
            DrawEnemyTables();
            DrawAllActorsTable();

            if (ImGui.CollapsingHeader("Events"))
                _events.Draw();
            if (ImGui.CollapsingHeader("Analysis"))
                _analysis.Draw();
        }

        private void DrawControlRow()
        {
            ImGui.TextUnformatted($"{_curTime:O}");
            ImGui.SameLine();
            if (ImGui.Button("<<<"))
                Rewind(20);
            ImGui.SameLine();
            if (ImGui.Button("<<"))
                Rewind(5);
            ImGui.SameLine();
            if (ImGui.Button("<"))
                Rewind(1);
            ImGui.SameLine();
            if (ImGui.Button("||"))
                _playSpeed = _playSpeed == 0 ? 1 : 0;
            ImGui.SameLine();
            if (ImGui.Button(">"))
                _playSpeed = 0.2f;
            ImGui.SameLine();
            if (ImGui.Button(">>"))
                _playSpeed = 1;
            ImGui.SameLine();
            if (ImGui.Button(">>>"))
                _playSpeed = 10;

            ImGui.SameLine();
            ImGui.Checkbox("Show config", ref _showConfig);
            if (_showConfig)
            {
                _config.Draw();
            }
        }

        private void DrawTimelineRow()
        {
            var dl = ImGui.GetWindowDrawList();
            var cursor = ImGui.GetCursorScreenPos();
            var w = ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - 15;
            cursor.Y += 4;
            dl.AddLine(cursor, cursor + new Vector2(w, 0), 0xff00ffff);

            var curp = cursor + new Vector2(w * (float)((_curTime - _first) / (_last - _first)), 0);
            dl.AddTriangleFilled(curp, curp + new Vector2(3, 5), curp + new Vector2(-3, 5), 0xff00ffff);
            foreach (var e in _player.Replay.Encounters)
            {
                DrawCheckpoint(e.Time.Start, 0xff00ff00, cursor, w);
                DrawCheckpoint(e.Time.End, 0xff0000ff, cursor, w);
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                var pos = ImGui.GetMousePos();
                if (Math.Abs(pos.Y - cursor.Y) <= 3)
                {
                    var t = _first + (pos.X - cursor.X) / w * (_last - _first);
                    var margin = (_last - _first).TotalSeconds * 3 / w;
                    var enc = _player.Replay.Encounters.Find(e => Math.Abs((t - e.Time.Start).TotalSeconds) <= margin);
                    MoveTo(enc?.Time.Start ?? t);
                }
            }
            ImGui.Dummy(new(w, 8));
        }

        private void DrawCheckpoint(DateTime timestamp, uint color, Vector2 cursor, float w)
        {
            var off = (float)((timestamp - _first) / (_last - _first));
            var center = cursor + new Vector2(w * off, 0);
            ImGui.GetWindowDrawList().AddCircleFilled(center, 3, color);
        }

        // x, z, rot, hp, name, cast, statuses
        private void DrawCommonColumns(Actor actor)
        {
            var pos = actor.Position;
            var rot = actor.Rotation.Deg;
            bool modified = false;
            ImGui.TableNextColumn(); modified |= ImGui.DragFloat("###X", ref pos.X, 0.25f, 80, 120);
            ImGui.TableNextColumn(); modified |= ImGui.DragFloat("###Z", ref pos.Z, 0.25f, 80, 120);
            ImGui.TableNextColumn(); modified |= ImGui.DragFloat("###Rot", ref rot, 1, -180, 180);
            if (modified)
                actor.PosRot = new(pos.X, actor.PosRot.Y, pos.Z, rot.Degrees().Rad);

            ImGui.TableNextColumn();
            if (actor.HP.Max > 0)
            {
                float frac = Math.Min((float)(actor.HP.Cur + actor.HP.Shield) / actor.HP.Max, 1);
                ImGui.ProgressBar(frac, new(ImGui.GetColumnWidth(), 0), $"{frac * 100:f1}% ({actor.HP.Cur} + {actor.HP.Shield} / {actor.HP.Max})");
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{(actor.IsDead ? "(Dead) " : "")}{actor.Name} (r={actor.HitboxRadius:f2})");

            ImGui.TableNextColumn();
            if (actor.CastInfo != null)
                ImGui.TextUnformatted($"{actor.CastInfo.Action}: {Utils.CastTimeString(actor.CastInfo, _player.WorldState.CurrentTime)}");

            ImGui.TableNextColumn();
            foreach (var s in actor.Statuses.Where(s => s.ID != 0))
            {
                var src = _player.WorldState.Actors.Find(s.SourceID);
                if (src?.Type == ActorType.Player || src?.Type == ActorType.Pet)
                    continue;
                if (s.ID is 360 or 362 or 364 or 365 or 413 or 902)
                    continue; // skip FC buff
                ImGui.TextUnformatted($"{Utils.StatusString(s.ID)} ({s.Extra}): {Utils.StatusTimeString(s.ExpireAt, _player.WorldState.CurrentTime)}");
                ImGui.SameLine();
            }
        }

        private void DrawPartyTable()
        {
            if (!ImGui.CollapsingHeader("Party"))
                return;

            ImGui.BeginTable("party", 10, ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("POV", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 25);
            ImGui.TableSetupColumn("Class", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 30);
            ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("Z", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("Rot", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 100);
            ImGui.TableSetupColumn("Cast", ImGuiTableColumnFlags.None, 100);
            ImGui.TableSetupColumn("Statuses", ImGuiTableColumnFlags.None, 100);
            ImGui.TableSetupColumn("Hints", ImGuiTableColumnFlags.None, 250);
            ImGui.TableHeadersRow();
            foreach ((int slot, var player) in _player.WorldState.Party.WithSlot(true))
            {
                ImGui.PushID((int)player.InstanceID);
                ImGui.TableNextRow();

                bool isPOV = _povSlot == slot;
                ImGui.TableNextColumn();
                ImGui.Checkbox("###POV", ref isPOV);
                if (isPOV)
                    _povSlot = slot;

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(player.Class.ToString());

                DrawCommonColumns(player);

                ImGui.TableNextColumn();
                if (_mgr.ActiveModule != null)
                {
                    var hints = _mgr.ActiveModule.CalculateHintsForRaidMember(slot, player);
                    foreach ((var hint, bool risk) in hints)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, risk ? 0xff00ffff : 0xff00ff00);
                        ImGui.TextUnformatted(hint);
                        ImGui.PopStyleColor();
                        ImGui.SameLine();
                    }
                }

                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        private void DrawEnemyTables()
        {
            if (_mgr.ActiveModule == null)
                return;

            DrawEnemyTable(_mgr.ActiveModule.PrimaryActor.OID, new Actor[] { _mgr.ActiveModule.PrimaryActor });
            foreach ((var oid, var list) in _mgr.ActiveModule.RelevantEnemies)
            {
                if (oid != _mgr.ActiveModule.PrimaryActor.OID)
                {
                    DrawEnemyTable(oid, list);
                }
            }
        }

        private void DrawEnemyTable(uint oid, ICollection<Actor> actors)
        {
            var moduleInfo = _mgr.ActiveModule != null ? ModuleRegistry.FindByOID(_mgr.ActiveModule.PrimaryActor.OID) : null;
            var oidName = moduleInfo?.ObjectIDType?.GetEnumName(oid);
            if (!ImGui.CollapsingHeader($"Enemy {oid:X} {oidName ?? ""}") || actors.Count == 0)
                return;

            ImGui.BeginTable($"enemy_{oid}", 7, ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("Z", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("Rot", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Cast");
            ImGui.TableSetupColumn("Statuses");
            ImGui.TableHeadersRow();
            foreach (var enemy in actors)
            {
                ImGui.PushID((int)enemy.InstanceID);
                ImGui.TableNextRow();
                DrawCommonColumns(enemy);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        private void DrawAllActorsTable()
        {
            if (!ImGui.CollapsingHeader("All actors"))
                return;

            ImGui.BeginTable($"actors", 7, ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("Z", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("Rot", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90);
            ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Cast");
            ImGui.TableSetupColumn("Statuses");
            ImGui.TableHeadersRow();
            foreach (var actor in _player.WorldState.Actors)
            {
                ImGui.PushID((int)actor.InstanceID);
                ImGui.TableNextRow();
                DrawCommonColumns(actor);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        private void MoveTo(DateTime t)
        {
            if (t < _player.WorldState.CurrentTime)
            {
                _player.Reset();
                _mgr = new(_player.WorldState);
            }
            _player.AdvanceTo(t, _mgr.Update);
            _curTime = t;
        }

        private void Rewind(float seconds)
        {
            _playSpeed = 0;
            MoveTo(_curTime.AddSeconds(-seconds));
        }
    }
}
