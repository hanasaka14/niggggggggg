﻿using ImGuiNET;
using System;
using System.Linq;

namespace BossMod.AI
{
    // constantly follow master
    class AIBehaviour : IDisposable
    {
        private Autorotation _autorot;
        private AIController _ctrl;
        private AvoidAOE _avoidAOE;
        private bool _passive;
        private bool _followMaster;
        private bool _instantCastsOnly;
        private bool _afkMode;
        private WPos _masterPrevPos;
        private WPos _masterMovementStart;
        private DateTime _masterLastMoved;

        public AIBehaviour(AIController ctrl, Autorotation autorot)
        {
            _autorot = autorot;
            _ctrl = ctrl;
            _avoidAOE = new(autorot.Bossmods);
        }

        public void Dispose()
        {
            _avoidAOE.Dispose();
        }

        public void Execute(Actor player, Actor master)
        {
            var targeting = UpdateTargeting(player, master);
            if (targeting.Target != null)
            {
                _autorot.PrimaryTarget = targeting.Target;
                _ctrl.SetPrimaryTarget(targeting.Target);
            }

            UpdateState(player, master);
            UpdateControl(player, master, targeting);
        }

        // returns null if we're to be idle, otherwise target to attack
        private CommonActions.Targeting UpdateTargeting(Actor player, Actor master)
        {
            if (_autorot.PotentialTargets.Valid.Count == 0 || !master.InCombat)
                return new(); // there are no valid targets to attack, or we're not fighting - remain idle

            // we prefer not to switch targets unnecessarily, so start with current target - it could've been selected manually or by AI on previous frames
            var target = _autorot.PrimaryTarget;

            // if current target is not among valid targets, clear it - this opens way for future target selection heuristics
            if (target != null && !_autorot.PotentialTargets.Valid.Contains(target))
                target = null;

            // if we don't have a valid target yet, use some heuristics to select some 'ok' target to attack
            // try assisting master, otherwise (if player is own master, or if master has no valid target) just select closest valid target
            target ??= master != player ? _autorot.PotentialTargets.Valid.Find(t => master.TargetID == t.InstanceID) : null;
            target ??= _autorot.PotentialTargets.Valid.Closest(player.Position);

            // now give class module a chance to improve targeting
            // typically it would switch targets for multidotting, or to hit more targets with AOE
            // in case of ties, it should prefer to return original target - this would prevent useless switches
            return _autorot.ClassActions?.SelectBetterTarget(target!) ?? new(target!);
        }

        private void UpdateState(Actor player, Actor master)
        {
            // keep master in focus
            bool masterChanged = Service.TargetManager.FocusTarget?.ObjectId != master.InstanceID;
            if (masterChanged)
            {
                _ctrl.SetFocusTarget(master);
                _masterPrevPos = _masterMovementStart = master.Position;
                _masterLastMoved = _autorot.WorldState.CurrentTime.AddSeconds(-1);
            }

            // keep track of master movement
            // idea is that if master is moving forward (e.g. running in outdoor or pulling trashpacks in dungeon), we want to closely follow and not stop to cast
            bool masterIsMoving = true;
            if (master.Position != _masterPrevPos)
            {
                _masterLastMoved = _autorot.WorldState.CurrentTime;
                _masterPrevPos = master.Position;
            }
            else if ((_autorot.WorldState.CurrentTime - _masterLastMoved).TotalSeconds > 0.5f)
            {
                // master has stopped, consider previous movement finished
                _masterMovementStart = _masterPrevPos;
                masterIsMoving = false;
            }
            // else: don't consider master to have stopped moving unless he's standing still for some small time

            _followMaster = master != player && _autorot.Bossmods.ActiveModule?.StateMachine.ActiveState == null && (_masterPrevPos - _masterMovementStart).LengthSq() > 100;
            bool moveWithMaster = masterIsMoving && (master == player || _followMaster);
            _instantCastsOnly = moveWithMaster || _ctrl.ForceFacing || _ctrl.NaviTargetPos != null && (_ctrl.NaviTargetPos.Value - player.Position).LengthSq() > 1;
            _afkMode = !masterIsMoving && !master.InCombat && (_autorot.WorldState.CurrentTime - _masterLastMoved).TotalSeconds > 10;
        }

        private void UpdateControl(Actor player, Actor master, CommonActions.Targeting target)
        {
            int strategy = CommonActions.AutoActionNone;
            if (_autorot.ClassActions != null && !_passive && !_ctrl.InCutscene && !_ctrl.IsMounted)
            {
                if (target.Target != null)
                {
                    // note: that there is a 1-frame delay if target and/or strategy changes - we don't really care?..
                    // note: if target-of-target is player, don't try flanking, it's probably impossible... - unless target is currently casting
                    strategy = _instantCastsOnly ? CommonActions.AutoActionAIFightMove : CommonActions.AutoActionAIFight;
                    var positional = target.PreferredPosition;
                    if (target.Target.TargetID == player.InstanceID && target.Target.CastInfo == null)
                        positional = CommonActions.Positional.Any;
                    _avoidAOE.SetDesired(target.Target.Position, target.Target.Rotation, target.PreferredRange + player.HitboxRadius + target.Target.HitboxRadius, positional);
                }
                else
                {
                    if (!_afkMode)
                        strategy = _instantCastsOnly ? CommonActions.AutoActionAIIdleMove : CommonActions.AutoActionAIIdle;
                    _avoidAOE.ClearDesired();
                }
            }
            else
            {
                _avoidAOE.ClearDesired();
            }

            _autorot.ClassActions?.UpdateAutoAction(strategy);
            var destData = _avoidAOE.Update(player);
            if (destData.DestPos == null && (target.Target == null || _followMaster) && master != player)
            {
                // if there is no planned action and no aoe avoidance, just follow master...
                var targetPos = master.Position;
                var playerPos = player.Position;
                var toTarget = targetPos - playerPos;
                if (toTarget.LengthSq() > 1)
                {
                    destData.DestPos = targetPos;
                }

                // sprint
                if (toTarget.LengthSq() > 400 && !player.InCombat)
                {
                    _autorot.ClassActions?.HandleUserActionRequest(CommonDefinitions.IDSprint, player);
                }

                //var cameraFacing = _ctrl.CameraFacing;
                //var dot = cameraFacing.Dot(_ctrl.TargetRot.Value);
                //if (dot < -0.707107f)
                //    _ctrl.TargetRot = -_ctrl.TargetRot.Value;
                //else if (dot < 0.707107f)
                //    _ctrl.TargetRot = cameraFacing.OrthoL().Dot(_ctrl.TargetRot.Value) > 0 ? _ctrl.TargetRot.Value.OrthoR() : _ctrl.TargetRot.Value.OrthoL();
            }

            if (destData.DestRot != null && (destData.RotDeadline - _autorot.WorldState.CurrentTime).TotalSeconds < 0.5f)
            {
                // rotation check imminent, drop any movement - we should have moved to safe zone already...
                _ctrl.NaviTargetPos = null;
                _ctrl.NaviTargetRot = destData.DestRot;
                _ctrl.ForceFacing = true;
            }
            else
            {
                var toDest = destData.DestPos != null ? destData.DestPos.Value - player.Position : new();
                _ctrl.NaviTargetPos = destData.DestPos;
                _ctrl.NaviTargetRot = toDest.LengthSq() >= 0.04f ? toDest.Normalized() : null;
                _ctrl.ForceFacing = false;
            }
        }

        public void DrawDebug()
        {
            ImGui.Checkbox("Passively follow", ref _passive);
            ImGui.TextUnformatted($"Only-instant={_instantCastsOnly}, afk={_afkMode}, master standing for {(_autorot.WorldState.CurrentTime - _masterLastMoved).TotalSeconds:f1}");
        }
    }
}
