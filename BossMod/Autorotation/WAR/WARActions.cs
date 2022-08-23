﻿using Dalamud.Game.ClientState.JobGauge.Types;
using System;
using System.Linq;

namespace BossMod.WAR
{
    class Actions : CommonActions
    {
        public const int AutoActionST = AutoActionFirstCustom + 0;
        public const int AutoActionAOE = AutoActionFirstCustom + 1;

        private WARConfig _config;
        private bool _aoe;
        private Rotation.State _state;
        private Rotation.Strategy _strategy;

        public Actions(Autorotation autorot, Actor player)
            : base(autorot, player, Definitions.UnlockQuests, Definitions.SupportedActions)
        {
            _config = Service.Config.Get<WARConfig>();
            _state = new(autorot.Cooldowns);
            _strategy = new()
            {
                FirstChargeIn = 0.01f, // by default, always preserve 1 onslaught charge
                SecondChargeIn = 10000, // ... but don't preserve second
            };

            // upgrades
            SupportedSpell(AID.InnerBeast).TransformAction = SupportedSpell(AID.FellCleave).TransformAction = SupportedSpell(AID.InnerChaos).TransformAction = () => ActionID.MakeSpell(_state.BestFellCleave);
            SupportedSpell(AID.SteelCyclone).TransformAction = SupportedSpell(AID.Decimate).TransformAction = SupportedSpell(AID.ChaoticCyclone).TransformAction = () => ActionID.MakeSpell(_state.BestDecimate);
            SupportedSpell(AID.Berserk).TransformAction = SupportedSpell(AID.InnerRelease).TransformAction = () => ActionID.MakeSpell(_state.BestInnerRelease);
            SupportedSpell(AID.RawIntuition).TransformAction = SupportedSpell(AID.Bloodwhetting).TransformAction = () => ActionID.MakeSpell(_state.BestBloodwhetting);

            SupportedSpell(AID.Equilibrium).Condition = _ => Player.HP.Cur < Player.HP.Max;
            SupportedSpell(AID.Reprisal).Condition = _ => Autorot.PotentialTargetsInRangeFromPlayer(5).Any(); // TODO: consider checking only target?..
            SupportedSpell(AID.Interject).Condition = target => target?.CastInfo?.Interruptible ?? false;
            // TODO: SIO - check that raid is in range?..
            // TODO: Provoke - check that not already MT?
            // TODO: Shirk - check that hate is close to MT?..

            _config.Modified += OnConfigModified;
            OnConfigModified(null, EventArgs.Empty);
        }

        public override void Dispose()
        {
            _config.Modified -= OnConfigModified;
        }

        protected override void UpdateInternalState(int autoAction)
        {
            _aoe = autoAction switch
            {
                AutoActionST => false,
                AutoActionAOE => true, // TODO: consider making AI-like check
                AutoActionAIFight or AutoActionAIFightMove => Autorot.PotentialTargetsInRangeFromPlayer(5).Count() >= 3,
                _ => false, // irrelevant...
            };
            UpdatePlayerState();
            FillCommonStrategy(_strategy, CommonDefinitions.IDPotionStr);
        }

        protected override void QueueAIActions()
        {
        }

        protected override NextAction CalculateAutomaticGCD()
        {
            if (Autorot.PrimaryTarget == null || AutoAction < AutoActionFirstFight)
                return new();
            var aid = Rotation.GetNextBestGCD(_state, _strategy, _aoe);
            return MakeResult(aid, Autorot.PrimaryTarget);
        }

        protected override NextAction CalculateAutomaticOGCD(float deadline)
        {
            if (Autorot.PrimaryTarget == null || AutoAction < AutoActionFirstFight)
                return new();

            ActionID res = new();
            if (_state.CanWeave(deadline - _state.OGCDSlotLength)) // first ogcd slot
                res = Rotation.GetNextBestOGCD(_state, _strategy, deadline - _state.OGCDSlotLength, _aoe);
            if (!res && _state.CanWeave(deadline)) // second/only ogcd slot
                res = Rotation.GetNextBestOGCD(_state, _strategy, deadline, _aoe);
            return MakeResult(res, Autorot.PrimaryTarget);
        }

        protected override void OnActionExecuted(ActionID action, Actor? target)
        {
            Log($"Executed {action} @ {target} [{_state}]");
        }

        protected override void OnActionSucceeded(ActorCastEvent ev)
        {
            Log($"Succeeded {ev.Action} @ {ev.MainTargetID:X} [{_state}]");
        }

        private void UpdatePlayerState()
        {
            FillCommonPlayerState(_state);

            _state.Gauge = Service.JobGauges.Get<WARGauge>().BeastGauge;

            _state.SurgingTempestLeft = _state.NascentChaosLeft = _state.PrimalRendLeft = _state.InnerReleaseLeft = 0;
            _state.InnerReleaseStacks = 0;
            foreach (var status in Player.Statuses)
            {
                switch ((SID)status.ID)
                {
                    case SID.SurgingTempest:
                        _state.SurgingTempestLeft = StatusDuration(status.ExpireAt);
                        break;
                    case SID.NascentChaos:
                        _state.NascentChaosLeft = StatusDuration(status.ExpireAt);
                        break;
                    case SID.Berserk:
                    case SID.InnerRelease:
                        _state.InnerReleaseLeft = StatusDuration(status.ExpireAt);
                        _state.InnerReleaseStacks = status.Extra & 0xFF;
                        break;
                    case SID.PrimalRend:
                        _state.PrimalRendLeft = StatusDuration(status.ExpireAt);
                        break;
                }
            }
        }

        private void OnConfigModified(object? sender, EventArgs args)
        {
            // placeholders
            SupportedSpell(AID.HeavySwing).PlaceholderForAuto = _config.FullRotation ? AutoActionST : AutoActionNone;
            SupportedSpell(AID.Overpower).PlaceholderForAuto = _config.FullRotation ? AutoActionAOE : AutoActionNone;

            // combo replacement
            SupportedSpell(AID.Maim).TransformAction = _config.STCombos ? () => ActionID.MakeSpell(Rotation.GetNextMaimComboAction(ComboLastMove)) : null;
            SupportedSpell(AID.StormEye).TransformAction = _config.STCombos ? () => ActionID.MakeSpell(Rotation.GetNextSTComboAction(ComboLastMove, AID.StormEye)) : null;
            SupportedSpell(AID.StormPath).TransformAction = _config.STCombos ? () => ActionID.MakeSpell(Rotation.GetNextSTComboAction(ComboLastMove, AID.StormPath)) : null;
            SupportedSpell(AID.MythrilTempest).TransformAction = _config.AOECombos ? () => ActionID.MakeSpell(Rotation.GetNextAOEComboAction(ComboLastMove)) : null;

            // smart targets
            SupportedSpell(AID.NascentFlash).TransformTarget = _config.SmartNascentFlashShirkTarget ? SmartTargetCoTank : null;
            SupportedSpell(AID.Shirk).TransformTarget = _config.SmartNascentFlashShirkTarget ? SmartTargetCoTank : null;
            SupportedSpell(AID.Provoke).TransformTarget = _config.ProvokeMouseover ? SmartTargetHostile : null; // TODO: also interject/low-blow
            SupportedSpell(AID.Holmgang).TransformTarget = _config.HolmgangSelf ? _ => Player : null; // TODO: otherwise smarttarget hostile or self...
        }

        private AID ComboLastMove => (AID)ActionManagerEx.Instance!.ComboLastMove;
    }
}
