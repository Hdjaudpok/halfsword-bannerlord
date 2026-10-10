using System;
using System.Collections.Generic;
using BEPUphysics;
using BEPUphysics.CollisionRuleManagement;
using BEPUphysics.Constraints.TwoEntity.JointLimits;
using BEPUphysics.Constraints.TwoEntity.Joints;
using BEPUphysics.Entities;
using BEPUphysics.Entities.Prefabs;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using BMat = BEPUutilities.Matrix3x3;
using BQuat = BEPUutilities.Quaternion;
using BVec = BEPUutilities.Vector3;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// A ragdoll simulated by BEPUphysics v1 and written back onto a frozen Bannerlord agent skeleton
    /// (Skeleton.Freeze + SetBoneLocalFrame + UpdateEntitialFramesFromLocalFrames, proven in lab run 4).
    /// Bodies are built from the agent's current pose. Masses, radii and joint limits are placeholders
    /// until Half Sword's real physics asset is read.
    /// </summary>
    internal sealed class Ragdoll
    {
        private sealed class PartSpec
        {
            public string Bone;
            public string End;      // segment end bone; null = extend along parent->bone by Extend metres
            public float Extend;
            public float Radius;
            public float Mass;
            public float Swing;     // max swing from the start pose, radians
            public float Twist;     // twist range +/-, radians
        }

        // The same split as an Unreal physics asset, so Half Sword's bodies can be mapped one to one later.
        private static readonly PartSpec[] Layout =
        {
            new PartSpec { Bone = "pelvis", End = "spine1", Radius = 0.14f, Mass = 11f, Swing = 0f, Twist = 0f },
            new PartSpec { Bone = "spine1", End = "spine2", Radius = 0.13f, Mass = 10f, Swing = 0.45f, Twist = 0.35f },
            new PartSpec { Bone = "spine2", End = "neck", Radius = 0.15f, Mass = 14f, Swing = 0.45f, Twist = 0.35f },
            new PartSpec { Bone = "neck", End = null, Extend = 0.30f, Radius = 0.10f, Mass = 5.5f, Swing = 0.7f, Twist = 0.8f },
            new PartSpec { Bone = "l_upperarm_twist", End = "l_foretwist", Radius = 0.055f, Mass = 2.2f, Swing = 1.6f, Twist = 1.0f },
            new PartSpec { Bone = "l_foretwist", End = "l_hand", Radius = 0.045f, Mass = 1.4f, Swing = 1.4f, Twist = 1.2f },
            new PartSpec { Bone = "l_hand", End = null, Extend = 0.09f, Radius = 0.04f, Mass = 0.5f, Swing = 1.0f, Twist = 0.6f },
            new PartSpec { Bone = "r_upperarm_twist", End = "r_foretwist", Radius = 0.055f, Mass = 2.2f, Swing = 1.6f, Twist = 1.0f },
            new PartSpec { Bone = "r_foretwist", End = "r_hand", Radius = 0.045f, Mass = 1.4f, Swing = 1.4f, Twist = 1.2f },
            new PartSpec { Bone = "r_hand", End = null, Extend = 0.09f, Radius = 0.04f, Mass = 0.5f, Swing = 1.0f, Twist = 0.6f },
            new PartSpec { Bone = "l_thigh", End = "l_calf", Radius = 0.075f, Mass = 8f, Swing = 1.3f, Twist = 0.5f },
            new PartSpec { Bone = "l_calf", End = "l_foot", Radius = 0.055f, Mass = 3.7f, Swing = 1.3f, Twist = 0.2f },
            new PartSpec { Bone = "l_foot", End = "l_toe0", Radius = 0.045f, Mass = 1.1f, Swing = 0.6f, Twist = 0.3f },
            new PartSpec { Bone = "r_thigh", End = "r_calf", Radius = 0.075f, Mass = 8f, Swing = 1.3f, Twist = 0.5f },
            new PartSpec { Bone = "r_calf", End = "r_foot", Radius = 0.055f, Mass = 3.7f, Swing = 1.3f, Twist = 0.2f },
            new PartSpec { Bone = "r_foot", End = "r_toe0", Radius = 0.045f, Mass = 1.1f, Swing = 0.6f, Twist = 0.3f },
        };

        private sealed class Part
        {
            public PartSpec Spec;
            public sbyte Bone;
            public Entity Body;
            public MatrixFrame Offset; // the bone's frame expressed in the body's frame
        }

        private readonly Space _space = new Space();
        private readonly Dictionary<sbyte, Part> _parts = new Dictionary<sbyte, Part>();
        private readonly MatrixFrame _entityFrame;
        private readonly sbyte[] _parent;
        private readonly MatrixFrame[] _fixedLocal;
        private readonly MatrixFrame[] _world;

        public int BodyCount => _parts.Count;

        private Ragdoll(Skeleton sk, MatrixFrame entityFrame)
        {
            _entityFrame = entityFrame;
            int n = sk.GetBoneCount();
            _parent = new sbyte[n];
            _fixedLocal = new MatrixFrame[n];
            _world = new MatrixFrame[n];
            _space.ForceUpdater.Gravity = new BVec(0f, 0f, -9.81f); // Bannerlord is Z-up
            _space.TimeStepSettings.TimeStepDuration = 1f / 120f;
            _space.TimeStepSettings.MaximumTimeStepsPerFrame = 8;
        }

        public static Ragdoll Build(Agent agent, Scene scene, Action<string> log)
        {
            Skeleton sk = agent.AgentVisuals.GetSkeleton();
            Ragdoll r = new Ragdoll(sk, agent.AgentVisuals.GetGlobalFrame());
            int n = sk.GetBoneCount();
            var index = new Dictionary<string, sbyte>();
            var entitial = new MatrixFrame[n];
            for (sbyte i = 0; i < n; i++)
            {
                index[sk.GetBoneName(i)] = i;
                r._parent[i] = sk.GetParentBoneIndex(i);
                entitial[i] = sk.GetBoneEntitialFrame(i);
            }
            for (int i = 0; i < n; i++)
            {
                sbyte p = r._parent[i];
                r._fixedLocal[i] = p < 0 ? entitial[i] : entitial[p].TransformToLocalNonOrthogonal(entitial[i]);
                r._world[i] = r._entityFrame.TransformToParent(entitial[i]);
            }

            foreach (PartSpec spec in Layout)
            {
                if (!index.TryGetValue(spec.Bone, out sbyte bone))
                {
                    log("RAGDOLL: missing bone " + spec.Bone);
                    continue;
                }
                Vec3 a = r._world[bone].origin;
                Vec3 b;
                if (spec.End != null && index.TryGetValue(spec.End, out sbyte end))
                {
                    b = r._world[end].origin;
                }
                else
                {
                    Vec3 along = a - r._world[r._parent[bone]].origin;
                    along.Normalize();
                    b = a + along * spec.Extend;
                }
                Vec3 seg = b - a;
                float length = seg.Length;
                Vec3 dir = seg / Math.Max(length, 1e-4f);
                var body = new Capsule(ToB((a + b) * 0.5f), Math.Max(0.02f, length), spec.Radius, spec.Mass);
                BVec up = BVec.Up; // BEPU capsules lie along their local Y axis
                BVec target = ToB(dir);
                BQuat.GetQuaternionBetweenNormalizedVectors(ref up, ref target, out BQuat orientation);
                body.Orientation = orientation;
                body.LinearDamping = 0.05f;
                body.AngularDamping = 0.15f;
                var part = new Part { Spec = spec, Bone = bone, Body = body };
                part.Offset = FrameOf(body).TransformToLocalNonOrthogonal(r._world[bone]);
                r._parts[bone] = part;
                r._space.Add(body);
            }

            // Joints: each part hangs from its nearest simulated ancestor, at its own bone origin.
            var all = new List<Part>(r._parts.Values);
            foreach (Part part in all)
            {
                Part parent = r.SimulatedAncestor(part.Bone);
                if (parent == null)
                {
                    continue;
                }
                BVec anchor = ToB(r._world[part.Bone].origin);
                r._space.Add(new BallSocketJoint(parent.Body, part.Body, anchor));
                BVec axis = Axis(part.Body);
                if (part.Spec.Swing > 0f)
                {
                    r._space.Add(new SwingLimit(parent.Body, part.Body, axis, axis, part.Spec.Swing));
                }
                if (part.Spec.Twist > 0f)
                {
                    r._space.Add(new TwistLimit(parent.Body, part.Body, axis, axis, -part.Spec.Twist, part.Spec.Twist));
                }
            }
            // No self-collision inside one ragdoll for now: limits keep limbs apart.
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    CollisionRules.AddRule(all[i].Body, all[j].Body, CollisionRule.NoBroadPhase);
                }
            }

            // Ground: a static slab at the terrain height under the pelvis.
            Vec3 pelvis = r._world[0].origin;
            float groundZ = scene.GetGroundHeightAtPosition(pelvis);
            r._space.Add(new Box(new BVec(pelvis.x, pelvis.y, groundZ - 0.5f), 60f, 60f, 1f));
            log("RAGDOLL: built " + r._parts.Count + " bodies, groundZ=" + groundZ.ToString("0.000") +
                " pelvisZ=" + pelvis.z.ToString("0.000"));
            return r;
        }

        public void Push(string boneName, Vec3 impulse, Skeleton sk)
        {
            for (sbyte i = 0; i < _parent.Length; i++)
            {
                if (sk.GetBoneName(i) == boneName && _parts.TryGetValue(i, out Part part))
                {
                    BVec at = part.Body.Position;
                    BVec j = ToB(impulse);
                    part.Body.ApplyImpulse(at, j);
                    return;
                }
            }
        }

        /// <summary>Advance the simulation and pose the frozen skeleton from the bodies.</summary>
        public void Step(float dt, Skeleton sk)
        {
            Lab.Watchdog.Enter("Ragdoll.Step: BEPU Space.Update");
            _space.Update(Math.Min(dt, 0.1f));
            Lab.Watchdog.Enter("Ragdoll.Step: bone write-back");
            int n = _parent.Length;
            for (int i = 0; i < n; i++)
            {
                if (_parts.TryGetValue((sbyte)i, out Part part))
                {
                    _world[i] = FrameOf(part.Body).TransformToParent(part.Offset);
                }
                else
                {
                    sbyte p = _parent[i];
                    _world[i] = p < 0 ? _entityFrame.TransformToParent(_fixedLocal[i]) : _world[p].TransformToParent(_fixedLocal[i]);
                }
            }
            for (int i = 0; i < n; i++)
            {
                sbyte p = _parent[i];
                MatrixFrame local = p < 0
                    ? _entityFrame.TransformToLocalNonOrthogonal(_world[i])
                    : _world[p].TransformToLocalNonOrthogonal(_world[i]);
                sk.SetBoneLocalFrame((sbyte)i, local);
            }
            sk.UpdateEntitialFramesFromLocalFrames();
        }

        public Vec3 WorldOf(sbyte bone) => _world[bone].origin;

        private Part SimulatedAncestor(sbyte bone)
        {
            sbyte p = _parent[bone];
            while (p >= 0)
            {
                if (_parts.TryGetValue(p, out Part part))
                {
                    return part;
                }
                p = _parent[p];
            }
            return null;
        }

        private static BVec Axis(Entity body)
        {
            BMat m = body.OrientationMatrix;
            return new BVec(m.M21, m.M22, m.M23); // local Y (the capsule axis) in world space
        }

        // BEPU and Bannerlord both store a rotation as rows = local axes in parent space.
        private static MatrixFrame FrameOf(Entity body)
        {
            BMat m = body.OrientationMatrix;
            var rot = new Mat3(new Vec3(m.M11, m.M12, m.M13), new Vec3(m.M21, m.M22, m.M23), new Vec3(m.M31, m.M32, m.M33));
            return new MatrixFrame(rot, ToTw(body.Position));
        }

        private static BVec ToB(Vec3 v) => new BVec(v.x, v.y, v.z);

        private static Vec3 ToTw(BVec v) => new Vec3(v.X, v.Y, v.Z);
    }
}
