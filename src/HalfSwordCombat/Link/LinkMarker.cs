using System;
using HalfSwordCombat.Physics;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Link
{
    /// <summary>
    /// The held items as the passthrough draws them, one per hand (main weapon: right, r_finger0; off-hand item, the
    /// shield: left, l_finger0). Each is a free-standing copy of the wielded item, built the way the game builds a dropped
    /// weapon (GameEntityExtensions.Instantiate, the native CreateFromWeapon, as in Mission.SpawnWeaponWithNewEntityAux), so
    /// the engine draws it through its ordinary scene path. Test 0 (2026-10-05, tools\gabs\visual_ab.py) measured that the
    /// held-item copy under the agent (HeldItems, item_ghost) is never drawn. A copy is a top-level scene entity with no
    /// physics and no pick-up script, moved every tick:
    ///  - by default onto its drawn item bone ("hand"). The user, 2026-10-05: "la position de la hache est parfaite"
    ///    there, the weapon sits in the fist, which two days of compensating the engine's own item never got. With phase 1
    ///    (link_pose = 1) the drawn arms are Half Sword's, so each item follows its Half Sword hand this way;
    ///  - main hand only, with marker_link = 1 and a fresh HSLink sample: to Half Sword's right hand relative to our drawn
    ///    pelvis ("link"), the phase-0 test of the link alone.
    /// Live tuning: marker = 1 shows them; marker_offset = x y z (model axes, metres) moves the main one, for A/B tests.
    /// While a copy is shown, the engine's own drawing of that item is removed (marker_hide = 1, default): the native item
    /// hangs on the original animation's hand, a second item away from the fist. Entity visibility flags don't hide it
    /// (test 0); the agent visuals' own MBAgentVisuals.ClearWeaponMeshes does, and the game's own re-equip
    /// (EquipWeaponWithNewEntity) plus an instant wield draws it again (lab 2026-10-05 12:11:13). ClearWeaponMeshes
    /// destroys the native item entity: HeldItems keeps no handle while marker is on.
    /// </summary>
    internal sealed class LinkMarker
    {
        private const int RemoveReason = 92;

        private sealed class Hand
        {
            public readonly string Bone;
            public readonly bool Main;
            public GameEntity Entity;
            public EquipmentIndex Slot = EquipmentIndex.None, Hidden = EquipmentIndex.None;
            public ItemObject Item; // the item the copy was made from: a new item in the same slot needs a new copy
            public string Source = "off";
            public Vec3 Target;
            public float BladeErr = -1f; // main hand: degrees between our blade (+Z) and Willie's, before any alignment
            public bool Aligned;
            public Vec3 SmoothDir;      // Willie's blade direction, smoothed (marker_hsdir_smooth)
            public bool HasSmoothDir;
            public float AlignWeight;   // 0..1, fades in and out (marker_hsdir_fade)
            public double LastAlignAt = -1;

            public Hand(string bone, bool main)
            {
                Bone = bone;
                Main = main;
            }
        }

        private readonly Hand[] _hands = { new Hand("r_finger0", true), new Hand("l_finger0", false) };
        private Agent _agent;
        private static int _replaced;
        private int _seen;

        /// <summary>
        /// The player's items were equipped again (hsc.loadout): the engine made new native entities, so the copies are
        /// rebuilt and the new items hidden, without re-equipping the old ones.
        /// </summary>
        public static void NativeReplaced() => _replaced++;

        /// <summary>Call after the body step (the drawn pose for this tick is known).</summary>
        public void Update(Agent agent, ActiveRagdoll body)
        {
            _agent = agent;
            if (_seen != _replaced)
            {
                _seen = _replaced;
                foreach (Hand h in _hands)
                {
                    h.Hidden = EquipmentIndex.None;
                    Remove(h);
                }
            }
            bool on = LiveTuning.Get(null, "marker", 0f) >= 0.5f && agent.Mission?.Scene != null;
            foreach (Hand h in _hands)
            {
                EquipmentIndex slot = h.Main ? agent.GetPrimaryWieldedItemIndex() : agent.GetOffhandWieldedItemIndex();
                sbyte bone = body.BoneIndex(h.Bone);
                if (!on || slot == EquipmentIndex.None || bone < 0)
                {
                    Remove(h);
                    continue;
                }
                Place(agent, body, h, slot, bone);
            }
        }

        private void Place(Agent agent, ActiveRagdoll body, Hand h, EquipmentIndex slot, sbyte bone)
        {
            ItemObject item = agent.Equipment[slot].Item;
            if (h.Entity != null && slot == h.Slot && item != h.Item)
            {
                // Another item put into the same slot (the game's re-equip, hsc.loadout): it already made a new native
                // entity, so the old one is not shown again; only our copy is replaced and the new item hidden.
                h.Hidden = EquipmentIndex.None;
                h.Entity.Remove(RemoveReason);
                h.Entity = null;
            }
            if (h.Entity == null || slot != h.Slot)
            {
                Remove(h);
                h.Item = item;
                // needBatchedVersion true, as the game does for every weapon it spawns into the scene.
                h.Entity = GameEntityExtensions.Instantiate(agent.Mission.Scene, agent.Equipment[slot], false, true);
                // Moved every tick: Dynamic, as the game does for the entities it moves at runtime.
                h.Entity.SetMobility(GameEntity.Mobility.Dynamic);
                h.Slot = slot;
            }
            bool hide = LiveTuning.Get(null, "marker_hide", 1f) >= 0.5f;
            if (hide && h.Hidden != slot)
            {
                ShowNative(h);
                agent.AgentVisuals.ClearWeaponMeshes(slot);
                h.Hidden = slot;
            }
            else if (!hide)
            {
                ShowNative(h);
            }
            MatrixFrame local = body.DrawnEntitial(bone);
            h.Source = "hand";
            sbyte pelvis = body.BoneIndex("pelvis");
            if (h.Main && pelvis >= 0 && LiveTuning.Get(null, "marker_link", 0f) >= 0.5f && HsLink.TryHandFromPelvis(out Vec3 fromPelvis))
            {
                local.origin = body.DrawnEntitial(pelvis).origin + fromPelvis;
                h.Source = "link";
            }
            if (h.Main)
            {
                local.origin += LiveTuning.GetVec(null, "marker_offset");
                AlignBlade(h, ref local);
            }
            MatrixFrame global = agent.AgentVisuals.GetGlobalFrame().TransformToParent(local);
            h.Entity.SetGlobalFrame(in global, false);
            h.Target = global.origin;
        }

        // Willie holds his weapon by a physics constraint, so his blade can turn in his hand, while our copy is fixed to
        // the hand bone. BladeErr measures it: our blade (+Z, the copy's long axis: its box runs -0.22..+0.98 m on Z)
        // against Willie's (HSLink WEAPON line, same axes as the model frame; +Z is his blade: 151 deg from his
        // right-to-left wrist vector on the two-handed grip). Live tuning marker_hsdir turns the copy about its grip:
        //  0 = measure only;
        //  1 = always like Willie's blade. User 2026-10-05 23:44: the right click (half-sword grip) was fixed, but normal
        //      strikes lost their cleanness: the physical blade's wobble came back;
        //  2 = only while Willie half-swords (GRIP line R Alt Grip), fading in and out over marker_hsdir_fade s.
        // Willie's direction is smoothed over marker_hsdir_smooth s.
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        private static void AlignBlade(Hand h, ref MatrixFrame local)
        {
            double now = Clock.Elapsed.TotalSeconds;
            float dt = h.LastAlignAt < 0 ? 0f : (float)Math.Min(0.1, now - h.LastAlignAt);
            h.LastAlignAt = now;
            h.BladeErr = -1f;
            h.Aligned = false;
            if (!HsLink.HasWeapon || HsLink.Age > 0.5)
            {
                h.HasSmoothDir = false;
                h.AlignWeight = 0f;
                return;
            }
            Vec3 ours = local.rotation.u;
            ours.Normalize();
            Vec3 willie = HsLink.WeaponZ;
            if (willie.Normalize() < 1e-4f)
            {
                return;
            }
            h.BladeErr = Angle(ours, willie);
            float tauDir = LiveTuning.Get(null, "marker_hsdir_smooth", 0.06f);
            h.SmoothDir = !h.HasSmoothDir || tauDir <= 0.001f ? willie : Vec3.Lerp(h.SmoothDir, willie, 1f - (float)Math.Exp(-dt / tauDir));
            h.SmoothDir.Normalize();
            h.HasSmoothDir = true;
            float mode = LiveTuning.Get(null, "marker_hsdir", 0f);
            float target = mode >= 1.5f ? (HsLink.TwoHanded && HsLink.AltGrip ? 1f : 0f) : Math.Abs(mode) >= 0.5f ? 1f : 0f;
            float tauW = LiveTuning.Get(null, "marker_hsdir_fade", 0.12f);
            h.AlignWeight = tauW <= 0.001f ? target : h.AlignWeight + (target - h.AlignWeight) * (1f - (float)Math.Exp(-dt / tauW));
            if (h.AlignWeight < 0.001f)
            {
                return;
            }
            Vec3 want = mode < -0.5f ? -h.SmoothDir : h.SmoothDir;
            Vec3 axis = Vec3.CrossProduct(ours, want);
            float sin = axis.Length, cos = Vec3.DotProduct(ours, want);
            if (sin < 1e-6f)
            {
                h.Aligned = cos > 0f;
                return;
            }
            axis *= 1f / sin;
            float angle = (float)Math.Atan2(sin, cos) * h.AlignWeight;
            Mat3 r = local.rotation;
            local.rotation = new Mat3(Turn(axis, angle, r.s), Turn(axis, angle, r.f), Turn(axis, angle, r.u));
            h.Aligned = true;
        }

        private static Vec3 Turn(Vec3 axis, float angle, Vec3 v)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            return v * c + Vec3.CrossProduct(axis, v) * s + axis * (Vec3.DotProduct(axis, v) * (1f - c));
        }

        private static float Angle(Vec3 a, Vec3 b)
        {
            float la = a.Length, lb = b.Length;
            if (la < 1e-5f || lb < 1e-5f)
            {
                return -1f;
            }
            float c = Math.Min(1f, Math.Max(-1f, Vec3.DotProduct(a, b) / (la * lb)));
            return (float)(Math.Acos(c) * 180.0 / Math.PI);
        }

        public void Remove()
        {
            foreach (Hand h in _hands)
            {
                Remove(h);
            }
        }

        private void Remove(Hand h)
        {
            ShowNative(h);
            if (h.Entity != null)
            {
                h.Entity.Remove(RemoveReason);
                h.Entity = null;
            }
            h.Slot = EquipmentIndex.None;
            h.Source = "off";
        }

        // The engine draws the hidden item again (the game's own re-equip of the same item in the same slot, then wield).
        private void ShowNative(Hand h)
        {
            if (h.Hidden == EquipmentIndex.None)
            {
                return;
            }
            if (_agent != null && _agent.IsActive())
            {
                MissionWeapon weapon = _agent.Equipment[h.Hidden];
                if (!weapon.IsEmpty)
                {
                    // Only an item this hand still holds goes back in the hand: after the player switches weapons, the
                    // old one is redrawn (holstered) without being wielded again, or the switch would be undone.
                    EquipmentIndex held = h.Main ? _agent.GetPrimaryWieldedItemIndex() : _agent.GetOffhandWieldedItemIndex();
                    // Re-equip alone gave no weapon entity back (lab 2026-10-05: GetWeaponEntityFromEquipmentSlot stayed
                    // invalid); the game's own instant wield puts it in the hand again.
                    _agent.EquipWeaponWithNewEntity(h.Hidden, ref weapon);
                    if (held == h.Hidden)
                    {
                        _agent.TryToWieldWeaponInSlot(h.Hidden, Agent.WeaponWieldActionType.Instant, false);
                    }
                }
            }
            h.Hidden = EquipmentIndex.None;
        }

        /// <summary>Lab log: where each copy was sent and where the engine says it is (not proof that it is drawn).</summary>
        public string Telemetry()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Hand h in _hands)
            {
                sb.Append(h.Main ? "R:" : " | L:");
                if (h.Entity == null)
                {
                    sb.Append(" off");
                    continue;
                }
                Vec3 readback = h.Entity.GetGlobalFrame().origin;
                // The item's extent in its own frame (= the hand item bone's frame): the longest axis is the blade or
                // shaft, min/max say where the grip (the origin) sits along it.
                BoundingBox box = h.Entity.GetLocalBoundingBox();
                sb.Append(" source=").Append(h.Source).Append(" nativeHidden=").Append(h.Hidden != EquipmentIndex.None)
                  .Append(" target=").Append(V(h.Target)).Append(" err=")
                  .Append(Lab.ProbeLog.F((readback - h.Target).Length * 100f)).Append("cm")
                  .Append(" box min=").Append(V(box.min)).Append(" max=").Append(V(box.max));
                if (h.Main)
                {
                    sb.Append(" bladeVsWillie=").Append(Lab.ProbeLog.F(h.BladeErr)).Append("deg aligned=").Append(h.Aligned)
                      .Append(" alignWeight=").Append(Lab.ProbeLog.F(h.AlignWeight)).Append(" altGrip=").Append(HsLink.AltGrip);
                    // Check of Willie's axis: on a two-handed grip his hands lie along the blade (about 0 or 180 deg).
                    Physics.ArmAtlas.Pose p = HsLink.LastPose;
                    if (p != null && HsLink.HasWeapon)
                    {
                        sb.Append(" willieBladeVsHands=").Append(Lab.ProbeLog.F(Angle(p.Wrist[1] - p.Wrist[0], HsLink.WeaponZ))).Append("deg");
                    }
                }
            }
            return sb.ToString();
        }

        private static string V(Vec3 v) => "(" + Lab.ProbeLog.F(v.x) + "," + Lab.ProbeLog.F(v.y) + "," + Lab.ProbeLog.F(v.z) + ")";
    }
}
