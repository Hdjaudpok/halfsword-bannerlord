using System.Collections.Generic;
using System.Text;
using HalfSwordCombat.Link;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Player test of the passthrough weapon by weapon (the user, 2026-10-05: one report per weapon and per combination,
    /// "je te dirais épée une main fait et tu lance le prochain"). hsc.loadout equips the player with one of these sets,
    /// using the game's own equip and instant wield; the passthrough then sends the matching Half Sword weapon (HsWeaponMap).
    /// Items are the campaign's own (SandBoxCore items: weapons.xml, shields.xml), one per Bannerlord weapon class and
    /// shield grip.
    /// </summary>
    internal static class LabLoadouts
    {
        private struct Set
        {
            public string Name, Main, Off;

            public Set(string name, string main, string off = null)
            {
                Name = name;
                Main = main;
                Off = off;
            }
        }

        private static readonly Set[] Sets =
        {
            new Set("épée une main", "vlandia_sword_1_t2"),
            new Set("épée + écu (kite, sanglé)", "vlandia_sword_1_t2", "reinforced_kite_shield"),
            new Set("épée + bouclier rond (poignée)", "vlandia_sword_1_t2", "heavy_round_shield"),
            new Set("hache une main", "sturgia_axe_2_t2"),
            new Set("hache + bouclier rond", "sturgia_axe_2_t2", "heavy_round_shield"),
            new Set("masse + écu (heater)", "vlandia_mace_1_t2", "tall_heater_shield"),
            new Set("dague", "seax"),
            new Set("épée à deux mains", "western_2hsword_t3"),
            new Set("hache à deux mains", "battania_2haxe_1_t2"),
            new Set("masse à deux mains", "sturgia_mace_1_t3"),
            new Set("lance + bouclier rond", "western_spear_1_t2", "heavy_round_shield"),
            new Set("lance à deux mains", "western_spear_1_t2"),
            new Set("arme d'hast (taille)", "vlandia_polearm_1_t5"),
        };

        private static int _current = -1;

        public static string Run(List<string> args)
        {
            if (args.Count == 0 || args[0] == "list")
            {
                return List();
            }
            int i;
            if (args[0] == "next")
            {
                i = _current + 1;
            }
            else if (args[0] == "prev")
            {
                i = _current - 1;
            }
            else if (int.TryParse(args[0], out int n))
            {
                i = n - 1;
            }
            else
            {
                return "usage: hsc.loadout [list | next | prev | 1-" + Sets.Length + "]";
            }
            if (i < 0 || i >= Sets.Length)
            {
                return "no loadout " + (i + 1) + " (1-" + Sets.Length + ")";
            }
            Agent agent = Mission.Current?.MainAgent;
            if (agent == null || !agent.IsActive())
            {
                return "HalfSwordCombat: no player agent";
            }
            string error = Equip(agent, Sets[i]);
            if (error != null)
            {
                return error;
            }
            _current = i;
            ProbeLog.Write("LOADOUT " + (i + 1) + " " + Sets[i].Name + " -> Half Sword " + HsWeaponMap.For(agent).Line);
            return (i + 1) + "/" + Sets.Length + " " + Sets[i].Name + " | Half Sword: " + HsWeaponMap.For(agent).Line;
        }

        private static string List()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Sets.Length; i++)
            {
                sb.Append(i == _current ? "> " : "  ").Append(i + 1).Append(". ").Append(Sets[i].Name).Append(" (")
                  .Append(Sets[i].Main).Append(Sets[i].Off != null ? " + " + Sets[i].Off : "").Append(")\n");
            }
            return sb.ToString();
        }

        private static string Equip(Agent agent, Set s)
        {
            ItemObject main = MBObjectManager.Instance.GetObject<ItemObject>(s.Main);
            ItemObject off = s.Off == null ? null : MBObjectManager.Instance.GetObject<ItemObject>(s.Off);
            if (main == null || (s.Off != null && off == null))
            {
                return "unknown item: " + (main == null ? s.Main : s.Off);
            }
            for (EquipmentIndex e = EquipmentIndex.WeaponItemBeginSlot; e < EquipmentIndex.NumAllWeaponSlots; e++)
            {
                if (!agent.Equipment[e].IsEmpty)
                {
                    agent.RemoveEquippedWeapon(e);
                }
            }
            var weapon = new MissionWeapon(main, null, null);
            agent.EquipWeaponWithNewEntity(EquipmentIndex.Weapon0, ref weapon);
            if (off != null)
            {
                var shield = new MissionWeapon(off, null, null);
                agent.EquipWeaponWithNewEntity(EquipmentIndex.Weapon1, ref shield);
            }
            agent.TryToWieldWeaponInSlot(EquipmentIndex.Weapon0, Agent.WeaponWieldActionType.Instant, false);
            if (off != null)
            {
                agent.TryToWieldWeaponInSlot(EquipmentIndex.Weapon1, Agent.WeaponWieldActionType.Instant, false);
            }
            // The engine made new item entities: the passthrough copies and hidden natives refer to the old ones.
            LinkMarker.NativeReplaced();
            return null;
        }
    }
}
