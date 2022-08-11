﻿using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BossMod
{
    [ConfigDisplay(Name = "小队职业分配", Order = 2)]
    public class PartyRolesConfig : ConfigNode
    {
        public enum Role { MT, OT, H1, H2, M1, M2, R1, R2, Unassigned }

        public Dictionary<ulong, Role> Assignments = new();

        public Role this[ulong contentID] => Assignments.GetValueOrDefault(contentID, Role.Unassigned);

        // return either array of assigned roles per party slot (if each role is assigned exactly once) or empty array (if assignments are invalid)
        public Role[] AssignmentsPerSlot(PartyState party)
        {
            int[] counts = new int[(int)Role.Unassigned];
            Role[] res = new Role[PartyState.MaxPartySize];
            Array.Fill(res, Role.Unassigned);
            for (int i = 0; i < PartyState.MaxPartySize; ++i)
            {
                var r = this[party.ContentIDs[i]];
                if (r == Role.Unassigned)
                    return new Role[0];
                if (counts[(int)r]++ > 0)
                    return new Role[0];
                res[i] = r;
            }
            return res;
        }

        // return either array of party slots per assigned role (if each role is assigned exactly once) or empty array (if assignments are invalid)
        public int[] SlotsPerAssignment(PartyState party)
        {
            int[] res = new int[(int)Role.Unassigned];
            Array.Fill(res, PartyState.MaxPartySize);
            for (int i = 0; i < PartyState.MaxPartySize; ++i)
            {
                var r = this[party.ContentIDs[i]];
                if (r == Role.Unassigned)
                    return new int[0];
                if (res[(int)r] != PartyState.MaxPartySize)
                    return new int[0];
                res[(int)r] = i;
            }
            return res;
        }

        // return array of effective roles per party slot
        public BossMod.Role[] EffectiveRolePerSlot(PartyState party)
        {
            var res = new BossMod.Role[PartyState.MaxPartySize];
            for (int i = 0; i < PartyState.MaxPartySize; ++i)
            {
                res[i] = this[party.ContentIDs[i]] switch
                {
                    Role.MT or Role.OT => BossMod.Role.Tank,
                    Role.H1 or Role.H2 => BossMod.Role.Healer,
                    Role.M1 or Role.M2 => BossMod.Role.Melee,
                    Role.R1 or Role.R2 => BossMod.Role.Ranged,
                    _ => party[i]?.Role ?? BossMod.Role.None
                };
            }
            return res;
        }

        public override void DrawCustom(UITree tree, WorldState ws)
        {
            if (ImGui.BeginTable("tab2", 10, ImGuiTableFlags.SizingFixedFit))
            {
                int index = 0;
                foreach (var r in typeof(Role).GetEnumValues())
                {
                    ImGui.TableSetupColumn(r.ToString(), ImGuiTableColumnFlags.None, index == 8 ? 70 : 30);
                    index++;
                }
                    

                ImGui.TableSetupColumn("名称");
                ImGui.TableHeadersRow();

                List<(ulong, string, BossMod.Role, Role)> party = new();
                for (int i = 0; i < PartyState.MaxPartySize; ++i)
                {
                    var m = ws.Party.Members[i];
                    if (m != null)
                        party.Add((ws.Party.ContentIDs[i], m.Name, m.Role, this[ws.Party.ContentIDs[i]]));
                }
                party.Sort((l, r) => l.Item3.CompareTo(r.Item3));

                foreach (var (contentID, name, classRole, assignedRole) in party)
                {
                    ImGui.TableNextRow();
                    foreach (var r in typeof(Role).GetEnumValues().Cast<Role>())
                    {
                        ImGui.TableNextColumn();
                        if (ImGui.RadioButton($"###{contentID:X}:{r}", assignedRole == r))
                        {
                            if (r != Role.Unassigned)
                                Assignments[contentID] = r;
                            else
                                Assignments.Remove(contentID);
                            NotifyModified();
                        }
                    }
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"({classRole.ToString()[0]}) {name}");
                }
                ImGui.EndTable();

                if (AssignmentsPerSlot(ws.Party).Length == 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, 0xff00ffff);
                    ImGui.TextUnformatted("无效分配: 每个职业都应该有至少一个玩家");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, 0xff00ff00);
                    ImGui.TextUnformatted("没有问题!");
                    ImGui.PopStyleColor();
                }
            }
        }
    }
}
