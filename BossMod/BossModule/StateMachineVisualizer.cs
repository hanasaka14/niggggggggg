using ImGuiNET;

namespace BossMod
{
    public class StateMachineVisualizer
    {
        private Timeline _timeline = new();
        private StateMachineTreeColumn _col;

        public StateMachineVisualizer(StateMachine sm)
        {
            _col = _timeline.AddColumn(new StateMachineTreeColumn(_timeline, new(sm), sm));
            _timeline.MaxTime = _col.Tree.TotalMaxTime;
        }

        public void Draw()
        {
            if (ImGui.CollapsingHeader("设置"))
            {
                ImGui.Checkbox("显示未命名节点", ref _col.DrawUnnamedNodes);
                ImGui.Checkbox("只显示坦克死刑节点", ref _col.DrawTankbusterNodesOnly);
                ImGui.Checkbox("只显示全屏AOE节点", ref _col.DrawRaidwideNodesOnly);
            }

            _timeline.CurrentTime = null;
            if (_col.ControlledSM?.ActiveState != null)
            {
                var dt = _col.ControlledSM.ActiveState.Duration - _col.ControlledSM.TimeSinceTransitionClamped;
                var activeNode = _col.Tree.Nodes[_col.ControlledSM.ActiveState.ID];
                _timeline.CurrentTime = activeNode.Time - dt;
            }

            _timeline.Draw();
        }
    }
}
