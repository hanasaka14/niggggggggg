﻿using Dalamud.Game.ClientState.JobGauge.Types;
using System;
using System.Linq;

namespace BossMod.MNK
{
    class Actions : CommonActions
    {
        public const int AutoActionST = AutoActionFirstCustom + 0;
        public const int AutoActionAOE = AutoActionFirstCustom + 1;

        private MNKConfig _config;
        private Rotation.State _state;
        private Rotation.Strategy _strategy;

        public Actions(Autorotation autorot, Actor player)
            : base(autorot, player, Definitions.UnlockQuests, Definitions.SupportedActions)
        {
            _config = Service.Config.Get<MNKConfig>();
            _state = new(autorot.Cooldowns);
            _strategy = new();

            // upgrades
            SupportedSpell(AID.SteelPeak).TransformAction = SupportedSpell(AID.ForbiddenChakra).TransformAction = () => ActionID.MakeSpell(_state.BestForbiddenChakra);
            SupportedSpell(AID.HowlingFist).TransformAction = SupportedSpell(AID.Enlightenment).TransformAction = () => ActionID.MakeSpell(_state.BestEnlightenment);
            SupportedSpell(AID.ArmOfTheDestroyer).TransformAction = SupportedSpell(AID.ShadowOfTheDestroyer).TransformAction = () => ActionID.MakeSpell(_state.BestShadowOfTheDestroyer);
            SupportedSpell(AID.FlintStrike).TransformAction = SupportedSpell(AID.RisingPhoenix).TransformAction = () => ActionID.MakeSpell(_state.BestRisingPhoenix);
            SupportedSpell(AID.TornadoKick).TransformAction = SupportedSpell(AID.PhantomRush).TransformAction = () => ActionID.MakeSpell(_state.BestPhantomRush);

            _config.Modified += OnConfigModified;
            OnConfigModified(null, EventArgs.Empty);
        }

        public override void Dispose()
        {
            _config.Modified -= OnConfigModified;
        }

        public override Targeting SelectBetterTarget(Actor initial)
        {
            // TODO: multidotting support...
            var pos = (_state.Form == Rotation.Form.Coeurl ? Rotation.GetCoeurlFormAction(_state, _strategy.NumPointBlankAOETargets) : AID.None) switch
            {
                AID.SnapPunch => Positional.Flank,
                AID.Demolish => Positional.Rear,
                _ => Positional.Any
            };
            return new(initial, 3, pos);
        }

        protected override void UpdateInternalState(int autoAction)
        {
            UpdatePlayerState();
            FillCommonStrategy(_strategy, CommonDefinitions.IDPotionStr);
            _strategy.NumPointBlankAOETargets = autoAction == AutoActionST ? 0 : Autorot.PotentialTargetsInRangeFromPlayer(5).Count();
            _strategy.NumEnlightenmentTargets = 0;
            if (Autorot.PrimaryTarget != null && autoAction != AutoActionST && _state.Unlocked(AID.HowlingFist))
            {
                var toTarget = (Autorot.PrimaryTarget.Position - Player.Position).Normalized();
                _strategy.NumEnlightenmentTargets = Autorot.PotentialTargets.Valid.Where(a => a.Position.InRect(Player.Position, toTarget, 10, 0, _state.Unlocked(AID.Enlightenment) ? 2 : 1)).Count();
            }
        }

        protected override void QueueAIActions()
        {
            if (_state.Unlocked(AID.SteelPeak))
                SimulateManualActionForAI(ActionID.MakeSpell(AID.Meditation), Player, _strategy.Prepull && _state.Chakra < 5);
            if (_state.Unlocked(AID.SecondWind))
                SimulateManualActionForAI(ActionID.MakeSpell(AID.SecondWind), Player, Player.InCombat && Player.HP.Cur < Player.HP.Max * 0.5f);
            if (_state.Unlocked(AID.Bloodbath))
                SimulateManualActionForAI(ActionID.MakeSpell(AID.Bloodbath), Player, Player.InCombat && Player.HP.Cur < Player.HP.Max * 0.8f);
        }

        protected override NextAction CalculateAutomaticGCD()
        {
            if (Autorot.PrimaryTarget == null || AutoAction < AutoActionFirstFight)
                return new();
            var aid = Rotation.GetNextBestGCD(_state, _strategy);
            return MakeResult(aid, Autorot.PrimaryTarget);
        }

        protected override NextAction CalculateAutomaticOGCD(float deadline)
        {
            if (Autorot.PrimaryTarget == null || AutoAction < AutoActionFirstFight)
                return new();

            ActionID res = new();
            if (_state.CanWeave(deadline - _state.OGCDSlotLength)) // first ogcd slot
                res = Rotation.GetNextBestOGCD(_state, _strategy, deadline - _state.OGCDSlotLength);
            if (!res && _state.CanWeave(deadline)) // second/only ogcd slot
                res = Rotation.GetNextBestOGCD(_state, _strategy, deadline);
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

            _state.Chakra = Service.JobGauges.Get<MNKGauge>().Chakra;

            (_state.Form, _state.FormLeft) = DetermineForm();
            _state.DisciplinedFistLeft = StatusDetails(Player, SID.DisciplinedFist, Player.InstanceID).Left;
            _state.LeadenFistLeft = StatusDetails(Player, SID.LeadenFist, Player.InstanceID).Left;

            _state.TargetDemolishLeft = StatusDetails(Autorot.PrimaryTarget, SID.Demolish, Player.InstanceID).Left;
        }

        private (Rotation.Form, float) DetermineForm()
        {
            var s = StatusDetails(Player, SID.OpoOpoForm, Player.InstanceID).Left;
            if (s > 0)
                return (Rotation.Form.OpoOpo, s);
            s = StatusDetails(Player, SID.RaptorForm, Player.InstanceID).Left;
            if (s > 0)
                return (Rotation.Form.Raptor, s);
            s = StatusDetails(Player, SID.CoeurlForm, Player.InstanceID).Left;
            if (s > 0)
                return (Rotation.Form.Coeurl, s);
            return (Rotation.Form.None, 0);
        }

        private void OnConfigModified(object? sender, EventArgs args)
        {
            // placeholders
            SupportedSpell(AID.Bootshine).PlaceholderForAuto = _config.FullRotation ? AutoActionST : AutoActionNone;
            SupportedSpell(AID.ArmOfTheDestroyer).PlaceholderForAuto = _config.FullRotation ? AutoActionAOE : AutoActionNone;

            // combo replacement
            SupportedSpell(AID.FourPointFury).TransformAction = _config.AOECombos ? () => ActionID.MakeSpell(Rotation.GetNextComboAction(_state, 100)) : null;

            // smart targets
        }
    }
}
