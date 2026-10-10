using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// Puts the wielded items (weapon, shield) in the hands as they are drawn.
    /// The engine draws an item with meshes attached to an item bone (ItemR / ItemL: r_finger0 / l_finger0) of its own
    /// animation, by a native path (AddMultiMeshToSkeletonBone; the forearm is its secondary bone). Two days of
    /// compensating that from outside (item_anchor / item_order) kept the weapon beside the fist while the frame we
    /// computed matched Half Sword within 8 degrees (lab 2026-10-04). So by default (item_ghost 1) the engine's item
    /// entity is hidden and a copy of its meshes, in an entity of ours under the agent's visual entity (no skeleton
    /// attachment), is put every tick exactly on the drawn item bone x the hold: what we compute is what is drawn.
    /// Being a child of the visual entity, the copy follows it when the agents tick moves it after the mission tick.
    /// item_ghost 0 = the old compensation, local = drawnBone x hold x animatedBone^-1, for A/B.
    /// </summary>
    internal sealed class HeldItems
    {
        private const string MainBone = "r_finger0";
        private const string OffBone = "l_finger0";
        private const int RemoveReason = 91;

        private WeakGameEntity _main, _off;
        private readonly GameEntity[] _ghost = new GameEntity[2];
        private readonly WeakGameEntity[] _ghostOf = new WeakGameEntity[2];
        private int _tuning = -1;
        private readonly MatrixFrame[] _grip = { MatrixFrame.Identity, MatrixFrame.Identity }; // Half Sword's hold, per hand

        private Agent _agent;

        /// <summary>Call after the physics pose is written (bone frames for this tick known).</summary>
        public void Update(Agent agent, ActiveRagdoll body)
        {
            // The free-standing copy (Link.LinkMarker, live tuning marker = 1) replaces this whole placement and hides the
            // engine's weapon with MBAgentVisuals.ClearWeaponMeshes, which destroys the native item entity; its re-equip
            // makes a new one. A WeakGameEntity is a raw native pointer, so no handle of ours may outlive that: lab
            // 2026-10-05 12:11:15, Reset() on the freed entity -> AccessViolationException, then again in Restore() during
            // Stop(), and the game closed. This runs before LinkMarker.Update in the same tick, with the same tuning, so
            // the handles are dropped while they are still valid.
            if (LiveTuning.Get(null, "marker", 0f) >= 0.5f)
            {
                if (_main.IsValid || _off.IsValid || _ghost[0] != null || _ghost[1] != null)
                {
                    Restore();
                    body.ClearHeldItem(0);
                    body.ClearHeldItem(1);
                }
                _agent = agent;
                return;
            }
            _agent = agent;
            bool retune = _tuning != LiveTuning.Version; // new hand offsets: rebuild the item bodies
            _tuning = LiveTuning.Version;
            _main = Place(agent, agent.GetPrimaryWieldedItemIndex(), _main, body.BoneIndex(MainBone), body, 0, retune);
            _off = Place(agent, agent.GetOffhandWieldedItemIndex(), _off, body.BoneIndex(OffBone), body, 1, retune);
        }

        /// <summary>
        /// The item's place in its item bone: Half Sword's hold (HsGrip), then the live tuning main_item_* / off_item_*.
        /// </summary>
        public MatrixFrame HandOffset(ActiveRagdoll body, int side) =>
            _grip[side].TransformToParent(LiveTuning.GetFrame(body.TuningClass, side == 0 ? "main_item" : "off_item"));

        // A new item in a hand gets its physics body (mesh bounds in the item bone frame, item weight); an empty hand
        // loses it. Works for any item: nothing is tuned per weapon.
        private void UpdateBody(Agent agent, EquipmentIndex slot, WeakGameEntity entity, ActiveRagdoll body, int side, sbyte bone)
        {
            if (!entity.IsValid)
            {
                body.ClearHeldItem(side);
                return;
            }
            bool any = false;
            Vec3 min = Vec3.Zero, max = Vec3.Zero;
            for (int i = 0; i < entity.MultiMeshComponentCount; i++)
            {
                BoundingBox mesh = entity.GetMetaMesh(i).GetBoundingBox();
                min = any ? Vec3.Vec3Min(min, mesh.min) : mesh.min;
                max = any ? Vec3.Vec3Max(max, mesh.max) : mesh.max;
                any = true;
            }
            if (!any)
            {
                body.ClearHeldItem(side);
                return;
            }
            float weight = agent.Equipment[slot].Item?.Weight ?? 1f;
            bool shield = agent.Equipment[slot].CurrentUsageItem?.IsShield == true;
            // Bannerlord's own hold by default (user's choice 2026-10-03: one hold per weapon is what the base game
            // defines; no per-weapon positions). Live tuning hs_grip = 1 tries Half Sword's measured hold instead.
            bool hsGrip = LiveTuning.Get(null, "hs_grip", 0f) >= 0.5f;
            _grip[side] = (hsGrip && bone >= 0 ? HsGrip.Offset(side == 0, body.RestLocal(bone), min, max, shield) : null) ?? MatrixFrame.Identity;
            body.SetHeldItem(side, min, max, weight, HandOffset(body, side));
        }

        /// <summary>Items back on the animation (identity local frame under the agent's visual entity, visible again).</summary>
        public void Restore()
        {
            RemoveGhost(0);
            RemoveGhost(1);
            Reset(_main);
            Reset(_off);
            _main = _off = default(WeakGameEntity);
        }

        // Our drawn copy of a held item: built from the item entity's meshes the first time, the original then hidden.
        private GameEntity Ghost(Agent agent, WeakGameEntity item, int side)
        {
            if (_ghost[side] != null && _ghostOf[side] == item)
            {
                return _ghost[side];
            }
            RemoveGhost(side);
            GameEntity parent = agent.AgentVisuals?.GetEntity();
            if (parent == null || item.MultiMeshComponentCount == 0 || agent.Mission?.Scene == null)
            {
                return null;
            }
            GameEntity copy = GameEntity.CreateEmpty(agent.Mission.Scene, false, false, false);
            // Moved every tick: Dynamic, as the game does for the entities it moves at runtime (ArrangementOrder,
            // SpawnerEntityMissionHelper). A Stationary entity's frame changes in the data but is not redrawn there (lab
            // 2026-10-04: the readback moved with Half Sword's hand while the user saw the axe stand still).
            copy.SetMobility(GameEntity.Mobility.Dynamic);
            for (int i = 0; i < item.MultiMeshComponentCount; i++)
            {
                copy.AddMultiMesh(item.GetMetaMesh(i).CreateCopy(), true);
            }
            parent.AddChild(copy, false);
            item.SetVisibilityExcludeParents(false);
            _ghost[side] = copy;
            _ghostOf[side] = item;
            return copy;
        }

        private void RemoveGhost(int side)
        {
            if (_ghost[side] != null)
            {
                _ghost[side].Remove(RemoveReason);
                _ghost[side] = null;
            }
            if (OwnedBy(_agent, _ghostOf[side]))
            {
                _ghostOf[side].SetVisibilityExcludeParents(true);
            }
            _ghostOf[side] = default(WeakGameEntity);
        }

        private WeakGameEntity Place(Agent agent, EquipmentIndex slot, WeakGameEntity previous, sbyte bone, ActiveRagdoll body, int side, bool retune)
        {
            WeakGameEntity entity = slot == EquipmentIndex.None ? default(WeakGameEntity) : agent.GetWeaponEntityFromEquipmentSlot(slot);
            if (previous.IsValid && previous != entity && OwnedBy(agent, previous))
            {
                Reset(previous); // the agent switched items: the old one goes back to its normal place
            }
            if (previous != entity || retune || (entity.IsValid && !body.HasHeldItem(side)))
            {
                UpdateBody(agent, slot, entity, body, side, bone);
            }
            bool ghost = LiveTuning.Get(null, "item_ghost", 1f) >= 0.5f;
            if (!entity.IsValid || bone < 0 || !ghost)
            {
                RemoveGhost(side);
            }
            if (!entity.IsValid || bone < 0)
            {
                return entity;
            }
            if (ghost)
            {
                GameEntity copy = Ghost(agent, entity, side);
                if (copy != null)
                {
                    MatrixFrame held = body.DrawnEntitial(bone).TransformToParent(HandOffset(body, side));
                    // Passthrough phase 0 marker (mashup-mods "a cube from A drawn in B"): with link_marker 1 the main
                    // item copy's origin goes where Half Sword's right hand is, relative to our drawn pelvis.
                    sbyte pelvis = body.BoneIndex("pelvis");
                    if (side == 0 && pelvis >= 0 && LiveTuning.Get(null, "link_marker", 0f) >= 0.5f &&
                        Link.HsLink.TryHandFromPelvis(out Vec3 fromPelvis))
                    {
                        held.origin = body.DrawnEntitial(pelvis).origin + fromPelvis;
                    }
                    // Lab diagnostics (2026-10-04, the user saw the axe stand still while the readback moved):
                    // copy_offset = x y z moves the copy in model space, copy_hide 1 hides it. They show whether the axe
                    // on screen is this copy and whether it is redrawn where its frame says.
                    held.origin += LiveTuning.GetVec(null, "copy_offset");
                    copy.SetVisibilityExcludeParents(LiveTuning.Get(null, "copy_hide", 0f) < 0.5f);
                    copy.SetLocalFrame(ref held, false);
                    return entity;
                }
            }
            // Put the item on the drawn bone, undoing the bone the engine hangs it on. Lab A/B 2026-10-04 (raised fist,
            // GABS screenshots): the engine hangs items on the ORIGINAL animation's bone even with the pose hook alone
            // (item_anchor 0, now the default), and applies the entity's local frame in model space after it (item =
            // local x bone, item_order 0); the three other combinations put the weapon behind the head, at the feet
            // or out of sight.
            MatrixFrame drawn = body.DrawnEntitial(bone);
            MatrixFrame hung = LiveTuning.Get(null, "item_anchor", 0f) >= 0.5f ? drawn : body.AnimatedEntitial(bone);
            MatrixFrame target = drawn.TransformToParent(HandOffset(body, side));
            // Which side of the bone the engine applies the entity's local frame on is not visible from C# (native
            // attachment): item_order 0 assumes item = local x bone, 1 assumes item = bone x local (lab A/B, 2026-10-04).
            MatrixFrame local = LiveTuning.Get(null, "item_order", 0f) >= 0.5f
                ? hung.TransformToLocalNonOrthogonal(target)
                : target.TransformToParent(hung.TransformToLocalNonOrthogonal(MatrixFrame.Identity));
            entity.SetLocalFrame(ref local, false);
            return entity;
        }

        /// <summary>
        /// Lab telemetry (measurement only, changes nothing): per hand, the intended item frame (world = visual entity x
        /// drawn item bone x hold), our drawn copy's frame read back from the engine, the error between them, and
        /// whether the engine's own item entity is still visible.
        /// </summary>
        public string Telemetry(Agent agent, ActiveRagdoll body)
        {
            var sb = new System.Text.StringBuilder();
            MatrixFrame visual = agent.AgentVisuals.GetGlobalFrame();
            for (int side = 0; side < 2; side++)
            {
                WeakGameEntity item = side == 0 ? _main : _off;
                sbyte bone = body.BoneIndex(side == 0 ? MainBone : OffBone);
                sb.Append(side == 0 ? "R:" : " | L:");
                if (!item.IsValid || bone < 0)
                {
                    sb.Append(" none");
                    continue;
                }
                MatrixFrame intended = visual.TransformToParent(body.DrawnEntitial(bone).TransformToParent(HandOffset(body, side)));
                sb.Append(" origVisible=").Append(item.GetVisibilityExcludeParents())
                  .Append(" intended o=").Append(V(intended.origin)).Append(" z=").Append(V(intended.rotation.u));
                GameEntity copy = _ghost[side];
                if (copy == null)
                {
                    MatrixFrame orig = item.GetGlobalFrame();
                    sb.Append(" copy=none origEntity o=").Append(V(orig.origin));
                    continue;
                }
                MatrixFrame actual = copy.GetGlobalFrame();
                sbyte pelvisBone = body.BoneIndex("pelvis");
                if (side == 0 && pelvisBone >= 0 && Link.HsLink.TryHandFromPelvis(out Vec3 linkOff))
                {
                    // Where the copy sits relative to our pelvis (model axes) against Half Sword's hand-from-pelvis.
                    Vec3 rel = visual.TransformToLocal(actual.origin) - body.DrawnEntitial(pelvisBone).origin;
                    sb.Append(" linkHS=").Append(V(linkOff)).Append(" copyFromPelvis=").Append(V(rel));
                }
                Vec3 za = actual.rotation.u, zi = intended.rotation.u;
                za.Normalize();
                zi.Normalize();
                float deg = (float)(System.Math.Acos(System.Math.Max(-1f, System.Math.Min(1f, Vec3.DotProduct(za, zi)))) * 180.0 / System.Math.PI);
                sb.Append(" copy o=").Append(V(actual.origin)).Append(" z=").Append(V(actual.rotation.u))
                  .Append(" err=").Append(Lab.ProbeLog.F((actual.origin - intended.origin).Length * 100f)).Append("cm/")
                  .Append(Lab.ProbeLog.F(deg)).Append("deg copyVisible=").Append(copy.GetVisibilityExcludeParents())
                  .Append(" meshes=").Append(copy.MultiMeshComponentCount);
            }
            return sb.ToString();
        }

        /// <summary>Lab: what the engine knows of each held item's size and where it hangs (to build its physics shape).</summary>
        public static string Describe(Agent agent, ActiveRagdoll body) =>
            "main:" + DescribeOne(agent, agent.GetPrimaryWieldedItemIndex(), MainBone, body) +
            " || off:" + DescribeOne(agent, agent.GetOffhandWieldedItemIndex(), OffBone, body);

        private static string DescribeOne(Agent agent, EquipmentIndex slot, string boneName, ActiveRagdoll body)
        {
            if (slot == EquipmentIndex.None)
            {
                return " none";
            }
            MissionWeapon weapon = agent.Equipment[slot];
            string s = " " + weapon.Item?.StringId + " type=" + weapon.Item?.Type + " len=" + (weapon.CurrentUsageItem?.WeaponLength ?? 0);
            WeakGameEntity e = agent.GetWeaponEntityFromEquipmentSlot(slot);
            if (!e.IsValid)
            {
                return s + " (no entity)";
            }
            BoundingBox local = e.GetLocalBoundingBox(), global = e.GetGlobalBoundingBox();
            s += " local=" + V(local.min) + ".." + V(local.max) + " global=" + V(global.min) + ".." + V(global.max);
            for (int i = 0; i < e.MultiMeshComponentCount; i++)
            {
                BoundingBox mesh = e.GetMetaMesh(i).GetBoundingBox();
                s += " mesh" + i + "=" + V(mesh.min) + ".." + V(mesh.max);
            }
            sbyte b = body.BoneIndex(boneName);
            if (b >= 0)
            {
                MatrixFrame bone = agent.AgentVisuals.GetGlobalFrame().TransformToParent(body.DrawnEntitial(b));
                s += " bone o=" + V(bone.origin) + " s=" + V(bone.rotation.s) + " f=" + V(bone.rotation.f) + " u=" + V(bone.rotation.u);
            }
            return s + " entity o=" + V(e.GetGlobalFrame().origin) + " children=" + e.ChildCount;
        }

        private static string V(Vec3 v) => "(" + Lab.ProbeLog.F(v.x) + "," + Lab.ProbeLog.F(v.y) + "," + Lab.ProbeLog.F(v.z) + ")";

        /// <summary>
        /// True while the entity is still one of the agent's weapon entities. A remembered WeakGameEntity can point to
        /// freed native memory once the engine replaces the item (re-equip, ClearWeaponMeshes); IsValid only tests for a
        /// null pointer, so a remembered handle is used only after this check.
        /// </summary>
        private static bool OwnedBy(Agent agent, WeakGameEntity entity)
        {
            if (!entity.IsValid || agent == null || !agent.IsActive())
            {
                return false;
            }
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                if (agent.GetWeaponEntityFromEquipmentSlot(i) == entity)
                {
                    return true;
                }
            }
            return false;
        }

        private void Reset(WeakGameEntity entity)
        {
            if (OwnedBy(_agent, entity))
            {
                MatrixFrame identity = MatrixFrame.Identity;
                entity.SetLocalFrame(ref identity, false);
            }
        }
    }
}
