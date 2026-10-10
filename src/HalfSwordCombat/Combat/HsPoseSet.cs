using System;
using System.IO;
using HalfSwordCombat.Physics;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Combat
{
    /// <summary>
    /// One weapon class's real Half Sword driver poses for every control state, recorded from the player's own demo by
    /// HSPose and converted by make_arm_atlas.py (port\arm_atlas_&lt;class&gt;_&lt;mode&gt;.txt):
    ///   R  left mouse held (weapon arm guard)    L  right mouse held (shield / off arm, or left hand on a 2H weapon)
    ///   RL both held                             N  none held (aim drifting back to the centre)
    ///   T0 / T1 double-click thrust, drawn back (position 0.2) / extended (1)
    ///   A0 / A05 / A1 Left Alt manual thrust at position 0 / 0.5 / 1
    /// Half Sword classes: onehand_shield (arming sword + tagre shield), twohand (longsword, left hand on the grip),
    /// polearm (pollaxe), spear.
    /// </summary>
    internal sealed class HsPoseSet
    {
        public readonly string Class;
        private ArmAtlas _r, _l, _rl, _n, _t0, _t1, _a0, _a05, _a1;

        private HsPoseSet(string cls)
        {
            Class = cls;
        }

        public int Modes => Count(_r, _l, _rl, _n, _t0, _t1, _a0, _a05, _a1);

        /// <summary>Loads what exists; null when not even the weapon-arm guard (R) is there.</summary>
        public static HsPoseSet Load(string cls, bool quick)
        {
            string dir = Path.GetDirectoryName(ArmAtlas.DefaultPath(cls));
            // Full recordings (15 deg grid) first; the coarse check recordings (45 deg) stand in until they exist.
            // quick = lab switch: coarse only.
            ArmAtlas Get(string name)
            {
                string full = Path.Combine(dir, "arm_atlas_" + name + ".txt"), coarse = Path.Combine(dir, "quick_arm_atlas_" + name + ".txt");
                if (!quick && File.Exists(full))
                {
                    return ArmAtlas.Load(full, 15f);
                }
                return File.Exists(coarse) ? ArmAtlas.Load(coarse, 45f) : null;
            }
            var set = new HsPoseSet(cls)
            {
                _r = Get(cls + "_R") ?? Get(cls) ?? (quick ? Load(cls, false)?._r : null),
                _l = Get(cls + "_L"),
                _rl = Get(cls + "_RL"),
                _n = Get(cls + "_N"),
                _t0 = Get(cls + "_T0"),
                _t1 = Get(cls + "_T1"),
                _a0 = Get(cls + "_A0"),
                _a05 = Get(cls + "_A05"),
                _a1 = Get(cls + "_A1"),
            };
            return set._r == null ? null : set;
        }

        /// <summary>Guard pose for the held buttons (swing inputs only matter while an arm guards).</summary>
        public ArmAtlas.Pose Guard(bool guardR, bool guardL, float x, float y, float swingYaw, float swingSpeed)
        {
            ArmAtlas atlas = guardR && guardL ? _rl ?? _r : guardR ? _r : guardL ? _l ?? _r : _n ?? _r;
            bool guarding = guardR || guardL;
            return atlas.Query(x, y, guarding ? swingYaw : 0f, guarding ? swingSpeed : 0f);
        }

        /// <summary>Thrust pose at a thrust position (0..1), null when this class has no thrust recordings.</summary>
        public ArmAtlas.Pose Thrust(float x, float y, float position, bool manual)
        {
            if (manual && _a0 != null && _a05 != null && _a1 != null)
            {
                return position < 0.5f
                    ? ArmAtlas.Pose.Blend(_a0.Query(x, y, 0f, 0f), _a05.Query(x, y, 0f, 0f), position / 0.5f)
                    : ArmAtlas.Pose.Blend(_a05.Query(x, y, 0f, 0f), _a1.Query(x, y, 0f, 0f), Math.Min(1f, (position - 0.5f) / 0.5f));
            }
            if (_t0 == null || _t1 == null)
            {
                return null;
            }
            float t = Math.Max(0f, Math.Min(1f, (position - 0.2f) / 0.8f));
            return ArmAtlas.Pose.Blend(_t0.Query(x, y, 0f, 0f), _t1.Query(x, y, 0f, 0f), t);
        }

        /// <summary>
        /// Half Sword class for what the agent wields: any one-handed weapon with a shield (or alone) = sword and shield;
        /// two-handed sword/axe/mace = longsword; a two-handed polearm that can swing = pollaxe; spears and pikes = spear.
        /// </summary>
        public static string ClassOf(Agent agent)
        {
            EquipmentIndex main = agent.GetPrimaryWieldedItemIndex(), off = agent.GetOffhandWieldedItemIndex();
            WeaponComponentData weapon = main == EquipmentIndex.None ? null : agent.Equipment[main].CurrentUsageItem;
            bool shield = off != EquipmentIndex.None && agent.Equipment[off].CurrentUsageItem?.IsShield == true;
            if (shield || weapon == null)
            {
                return "onehand_shield";
            }
            switch (weapon.WeaponClass)
            {
                case WeaponClass.TwoHandedSword:
                case WeaponClass.TwoHandedAxe:
                case WeaponClass.TwoHandedMace:
                    return "twohand";
                case WeaponClass.TwoHandedPolearm:
                    return weapon.SwingDamage > 0 ? "polearm" : "spear";
                case WeaponClass.LowGripPolearm:
                case WeaponClass.OneHandedPolearm:
                    return "spear";
                default:
                    return "onehand_shield";
            }
        }

        private static int Count(params ArmAtlas[] atlases)
        {
            int n = 0;
            foreach (ArmAtlas a in atlases)
            {
                if (a != null)
                {
                    n++;
                }
            }
            return n;
        }
    }
}
