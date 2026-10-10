using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Link
{
    /// <summary>
    /// Passthrough: which Half Sword weapon Willie must hold for the player's Bannerlord equipment. Half Sword's arm motion
    /// depends on the weapon in hand: ABP_Willie picks its blend spaces per class (one-handed right hand, shields left
    /// hand, two-handed, pollaxe, spear; NOTES.md, decoded 2026-10-03) and the weapon's mass and length drive the physical
    /// arm. The user, 2026-10-05: "chaque arme doit avoir une animation différente et un comportement dans la main
    /// différent". So the Bannerlord item is translated to a Half Sword weapon blueprint of the same family (paths under
    /// Weapons/Blueprints/Built_Weapons, from the demo's own list, halfsword-decomp\weapon_classes.txt), and HSLink equips
    /// it through Willie_BP's own "Set Up Right/Left Hand Weapon" (as HSPose and HSGrip did).
    /// Within a family, the candidate nearest in size and weight to the Bannerlord item wins, from Half Sword's own
    /// measurements (HSLink lab switch "catalog" -> halfsword-decomp\hs_catalog.txt: real size per axis and mass). Without
    /// the catalogue each family falls back to one fixed weapon.
    /// Shields: every Bannerlord shield is class LargeShield (even 70 cm round ones); its item usage tells the grip:
    /// "hand_shield" = held by a centre grip (round), "shield" = strapped (kite, heater, pavise). Half Sword has both kinds.
    /// </summary>
    internal static class HsWeaponMap
    {
        public struct Loadout
        {
            public string Right, Left; // Half Sword blueprint paths relative to Built_Weapons, "-" for an empty hand
            public bool TwoHanded;     // the left hand on the weapon (Half Sword's two-handed grip)

            public string Line => Right + " " + Left + " " + (TwoHanded ? "1" : "0");
        }

        private sealed class Family
        {
            public readonly string Fallback;
            public readonly Func<string, bool> Member; // blueprint path -> belongs to this family

            public Family(string fallback, Func<string, bool> member)
            {
                Fallback = fallback;
                Member = member;
            }
        }

        private static string Name(string path) => path.Substring(path.LastIndexOf('/') + 1);
        private static bool Has(string path, string part) => path.IndexOf(part, StringComparison.Ordinal) >= 0;
        private static bool Plain(string path) => !Has(path, "_Gold");

        // Half Sword families, by blueprint name (weapon_classes.txt). Fallbacks: the weapons HSPose recorded where it did.
        private static readonly Family OneHandSword = new Family("ModularWeaponBP_ArmingSword_T1",
            p => Plain(p) && (Has(p, "ArmingSword") || Has(p, "Falchion_Short")));
        private static readonly Family TwoHandSword = new Family("ModularWeaponBP_LongSword_T2",
            p => Plain(p) && (Has(p, "LongSword") || Has(p, "GreatSword") || Has(p, "BastardSword") || Has(p, "Falchion_Long")));
        private static readonly Family OneHandAxe = new Family("Reforged/ModularWeaponBP_Axe",
            p => Name(p) == "ModularWeaponBP_Axe" || Has(p, "Tool_Axe"));
        private static readonly Family TwoHandAxe = new Family("Reforged/ModularWeaponBP_Axe2H", p => Has(p, "Axe2H"));
        private static readonly Family OneHandMace = new Family("Tiers/ModularWeaponBP_Mace_Mid_Tier_Avg",
            p => Plain(p) && ((Has(p, "Mace_") && !Has(p, "Giant")) || Has(p, "Tool_Hammer")));
        private static readonly Family TwoHandMace = new Family("Tiers/ModularWeaponBP_Hafted_Mid_Tier_Long",
            p => Has(p, "Mace_High_Tier_Giant") || Has(p, "Hafted_"));
        private static readonly Family Pick = new Family("Tiers/ModularWeaponBP_Hafted_Mid_Tier_Short", p => Has(p, "Hafted_"));
        private static readonly Family Dagger = new Family("ModularWeaponBP_Dagger_T1",
            p => Plain(p) && (Has(p, "Dagger") || Has(p, "Rondel") || Has(p, "Tool_Knife")));
        private static readonly Family Spear = new Family("Reforged/ModularWeaponBP_Spear_A", p => Has(p, "Spear_"));
        private static readonly Family SwingPolearm = new Family("Tiers/ModularWeaponBP_Polearm_Mid_Tier",
            p => Plain(p) && (Has(p, "Polearm_") || Has(p, "Halberd_") || Has(p, "Billhook_") || Has(p, "WarStaff_")));
        private static readonly Family GripShield = new Family("Shield_BossGrip",
            p => Plain(p) && (Has(p, "Shield_BossGrip") || Has(p, "Shield_Buckler")));
        private static readonly Family StrapShield = new Family("Shield_Tagre",
            p => Plain(p) && (Has(p, "Shield_Tagre") || Has(p, "Shield_Pavise")));

        private struct Entry
        {
            public string Path;
            public float Length, Mass; // largest size (cm), mass (kg, 0 = unknown)
        }

        private static List<Entry> _catalog;
        private static DateTime _catalogTime;

        public static string CatalogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "halfsword-decomp", "hs_catalog.txt");

        public static Loadout For(Agent agent)
        {
            EquipmentIndex main = agent.GetPrimaryWieldedItemIndex(), off = agent.GetOffhandWieldedItemIndex();
            MissionWeapon mainWeapon = main == EquipmentIndex.None ? MissionWeapon.Invalid : agent.Equipment[main];
            MissionWeapon offWeapon = off == EquipmentIndex.None ? MissionWeapon.Invalid : agent.Equipment[off];
            WeaponComponentData mainUsage = mainWeapon.IsEmpty ? null : mainWeapon.CurrentUsageItem;
            WeaponComponentData offUsage = offWeapon.IsEmpty ? null : offWeapon.CurrentUsageItem;
            var l = new Loadout
            {
                Right = Nearest(RightFamily(mainUsage), mainUsage, mainWeapon),
                Left = offUsage != null && offUsage.IsShield ? Nearest(ShieldFamily(offUsage), offUsage, offWeapon) : "-",
            };
            // Bannerlord's two-handed weapons, and polearms held without a shield, are held with both hands.
            l.TwoHanded = mainUsage != null && offUsage == null &&
                          (mainUsage.IsTwoHanded || mainUsage.WeaponClass == WeaponClass.TwoHandedPolearm ||
                           mainUsage.WeaponClass == WeaponClass.LowGripPolearm);
            return l;
        }

        private static Family RightFamily(WeaponComponentData w)
        {
            if (w == null)
            {
                return null;
            }
            switch (w.WeaponClass)
            {
                case WeaponClass.Dagger: return Dagger;
                case WeaponClass.OneHandedSword: return OneHandSword;
                case WeaponClass.TwoHandedSword: return TwoHandSword;
                case WeaponClass.OneHandedAxe: return OneHandAxe;
                case WeaponClass.TwoHandedAxe: return TwoHandAxe;
                case WeaponClass.Mace: return OneHandMace;
                case WeaponClass.Pick: return Pick;
                case WeaponClass.TwoHandedMace: return TwoHandMace;
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    return Spear;
                case WeaponClass.TwoHandedPolearm:
                    // Thrust-only polearms (spears, pikes) are Half Sword spears; the others swing like its pollaxes.
                    return w.SwingDamage <= 0 ? Spear : SwingPolearm;
                default:
                    return null; // not a melee weapon: Half Sword control does not run (Rules/Eligibility)
            }
        }

        private static Family ShieldFamily(WeaponComponentData w) => w.ItemUsage == "hand_shield" ? GripShield : StrapShield;

        // The family member nearest to the Bannerlord item: relative length difference, plus half the relative weight
        // difference when Half Sword gave a mass.
        private static string Nearest(Family family, WeaponComponentData usage, MissionWeapon weapon)
        {
            if (family == null)
            {
                return "-";
            }
            List<Entry> catalog = Catalog();
            float length = Math.Max(1f, usage.WeaponLength), weight = Math.Max(0.1f, weapon.Item?.Weight ?? 0f);
            string best = null;
            float bestScore = float.MaxValue;
            foreach (Entry e in catalog)
            {
                if (!family.Member(e.Path))
                {
                    continue;
                }
                float score = Math.Abs(e.Length - length) / length;
                if (e.Mass > 0.01f)
                {
                    score += 0.5f * Math.Abs(e.Mass - weight) / weight;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e.Path;
                }
            }
            return best ?? family.Fallback;
        }

        private static List<Entry> Catalog()
        {
            try
            {
                DateTime t = File.Exists(CatalogPath) ? File.GetLastWriteTimeUtc(CatalogPath) : DateTime.MinValue;
                if (_catalog != null && t == _catalogTime)
                {
                    return _catalog;
                }
                var list = new List<Entry>();
                if (t != DateTime.MinValue)
                {
                    CultureInfo c = CultureInfo.InvariantCulture;
                    foreach (string line in File.ReadAllLines(CatalogPath))
                    {
                        string[] p = line.Split(' ');
                        if (line.StartsWith("#") || p.Length != 5)
                        {
                            continue; // comments and FAILED measurements
                        }
                        float x = float.Parse(p[1], c), y = float.Parse(p[2], c), z = float.Parse(p[3], c);
                        list.Add(new Entry { Path = p[0], Length = Math.Max(x, Math.Max(y, z)), Mass = float.Parse(p[4], c) });
                    }
                }
                _catalog = list;
                _catalogTime = t;
                return list;
            }
            catch (Exception)
            {
                return _catalog ?? new List<Entry>();
            }
        }
    }
}
