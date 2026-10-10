using System;
using System.Collections.Generic;
using BEPUphysics;
using BEPUphysics.CollisionRuleManagement;
using BEPUphysics.Constraints.SingleEntity;
using BEPUphysics.Constraints.SolverGroups;
using BEPUphysics.Constraints.TwoEntity.JointLimits;
using BEPUphysics.Constraints.TwoEntity.Joints;
using BEPUphysics.Constraints.TwoEntity.Motors;
using BEPUphysics.Entities;
using BEPUphysics.Entities.Prefabs;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using BMat = BEPUutilities.Matrix3x3;
using BMaterial = BEPUphysics.Materials.Material;
using BQuat = BEPUutilities.Quaternion;
using BVec = BEPUutilities.Vector3;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// Half Sword-style active ragdoll on a Bannerlord agent (architecture validated in lab runs 7-8):
    ///  - driver  = the agent's normal Bannerlord animation, read every frame before we overwrite it;
    ///  - body    = BEPU bodies built from the HalfSwordProfile (masses, shapes, joint limits);
    ///  - muscles = per-body world-orientation servos toward the driver (Half Sword physical animation,
    ///              bIsLocalSimulation=false) + per-joint relative servos (UserConstraint "Motor" drives);
    ///  - balance = linear/angular servos on pelvis and chest toward the driver (PhysicsHandle LowerBody/UpperBody);
    ///  - display = body poses written back onto the skeleton; bones without a body keep the driver's local pose.
    /// Unreal strengths are acceleration-mode values: BEPU stiffness/damping = value x mass (or x inertia).
    /// </summary>
    internal sealed class ActiveRagdoll : IPoseSource
    {
        private sealed class Part
        {
            public HalfSwordProfile.Body Spec;
            public HalfSwordProfile.Joint Joint;
            public sbyte Bone;
            public Entity Body;
            public Part Parent;
            public BQuat Offset;         // bone orientation in the body frame
            public MatrixFrame OffsetFrame; // bone frame in the body frame (keeps the agent's scale for write-back)
            public float Inertia;        // mean principal moment, kg m^2
            public SingleEntityAngularMotor Muscle;
            public AngularMotor Motor;
            public BQuat Target;         // this frame's driver orientation for the body
            public bool Lower;           // pelvis or leg: follows the animation while AnimatedLowerBody is on
            public MatrixFrame OffsetInverse; // body frame in the bone frame
        }

        private static readonly HashSet<string> LowerBones = new HashSet<string> { "pelvis", "l_thigh", "l_calf", "l_foot", "r_thigh", "r_calf", "r_foot" };

        // Segment end of each body on Bannerlord's human_skeleton ("+x" = extend along parent->bone by x metres).
        private static readonly Dictionary<string, string> SegmentEnd = new Dictionary<string, string>
        {
            { "pelvis", "spine" }, { "spine", "spine1" }, { "spine1", "spine2" }, { "spine2", "neck" }, { "neck", "head" }, { "head", "+0.20" },
            { "l_clavicle", "l_upperarm_twist" }, { "l_upperarm_twist", "l_foretwist" }, { "l_foretwist", "l_hand" }, { "l_hand", "+0.09" },
            { "r_clavicle", "r_upperarm_twist" }, { "r_upperarm_twist", "r_foretwist" }, { "r_foretwist", "r_hand" }, { "r_hand", "+0.09" },
            { "l_thigh", "l_calf" }, { "l_calf", "l_foot" }, { "l_foot", "l_toe0" },
            { "r_thigh", "r_calf" }, { "r_calf", "r_foot" }, { "r_foot", "r_toe0" },
        };

        private readonly Space _space = new Space();
        private readonly HalfSwordProfile _profile;
        private readonly Dictionary<sbyte, Part> _parts = new Dictionary<sbyte, Part>();
        private readonly List<Part> _ordered = new List<Part>();
        private readonly Dictionary<string, sbyte> _index = new Dictionary<string, sbyte>();
        private sbyte[] _parent;
        private sbyte _root; // the bone without parent (pelvis on human_skeleton)
        private MatrixFrame[] _driverEntitial;
        private MatrixFrame[] _driverWorld;
        private MatrixFrame[] _animEntitial; // the engine's own animation this frame (before DriverOverride)
        private MatrixFrame[] _animWorld;
        private MatrixFrame[] _world;
        private MatrixFrame _entityFrame;
        private MatrixFrame[] _writtenLocal;    // local frames of our last write (for Rewrite)
        private MatrixFrame[] _writtenEntitial; // entitial frames right after our last write (to detect engine overrides)
        private MatrixFrame _lastWrittenPelvis;
        private bool _wroteOnce;
        private SingleEntityLinearMotor _lowerLinear, _upperLinear;
        private SingleEntityAngularMotor _lowerAngular;
        private Part _pelvis, _chest;
        private Box _ground;
        private Scene _scene;
        private bool _animatedLower;
        private bool _groundPlaced;
        private float _strength = 1f;
        private bool _handles = true;

        // Engine post-integrate hook (see HscPoseHook). The callback reads the animation into _hookDriver and writes the
        // pose of the last physics step (_poseRotation/_poseDisplacement, local frames); _gate guards the exchange.
        private readonly object _gate = new object();
        private readonly HeldItems _items = new HeldItems();
        private Skeleton _sk;
        private HscPoseHook _hook;
        private MatrixFrame[] _hookDriver;
        private MatrixFrame[] _hookDrawn;    // the same callback's result after our writes = what the engine draws
        private MatrixFrame[] _drawnEntitial; // copy of _hookDrawn taken with the driver (one consistent frame)
        private bool _hookDriverFresh;
        private Mat3[] _poseRotation;
        private Vec3[] _poseDisplacement;
        private MatrixFrame[] _poseEntitial; // entitial frames we asked the hook to produce (to check what the engine kept)
        private bool _poseReady;
        private volatile bool _diagRequested;
        private string _diag; // root/hand: animation, wanted, read back inside the callback right after our writes

        public float GroundZ { get; private set; }
        public float TotalMass { get; private set; }
        public int DriverUpdates { get; private set; }
        public int DriverReuses { get; private set; }

        /// <summary>Replaces part of the driver pose each tick (model space frames), e.g. the Half Sword arm atlas.</summary>
        public Action<MatrixFrame[]> DriverOverride { get; set; }

        /// <summary>Bone index on this agent's skeleton, -1 if absent.</summary>
        public sbyte BoneIndex(string name) => _index.TryGetValue(name, out sbyte b) ? b : (sbyte)-1;

        public sbyte[] Parents => _parent;

        /// <summary>Bone names by index (lab dumps).</summary>
        public IEnumerable<KeyValuePair<string, sbyte>> Bones => _index;

        /// <summary>Held item placement in its item bone (Half Sword hold + live tuning), side 0 = main hand.</summary>
        public MatrixFrame ItemOffset(int side) => _items.HandOffset(this, side);

        /// <summary>Lab telemetry: where the held items really are, read back from the engine (HELD log line).</summary>
        public string HeldTelemetry(Agent agent) => _items.Telemetry(agent, this);

        /// <summary>The skeleton's rest (bind) local frame of a bone.</summary>
        public MatrixFrame RestLocal(sbyte bone) => _sk.GetBoneLocalRestFrame(bone);

        /// <summary>True when the pose goes through the engine post-integrate hook instead of direct bone writes.</summary>
        public bool UsesHook => _hook != null;
        public int HookCalls => _hook?.Calls ?? 0;
        public int HookWrites { get; private set; }
        public int HookThreadId => _hook?.LastThreadId ?? 0;
        public string HookError => _hook?.Error;

        /// <summary>Also write bones directly from the mission tick (lowest latency; off to test the hook alone).</summary>
        public bool DirectWrite { get; set; } = true;

        private ActiveRagdoll(HalfSwordProfile profile)
        {
            _profile = profile;
            _space.ForceUpdater.Gravity = new BVec(0f, 0f, -9.81f); // Bannerlord is Z-up
            _space.TimeStepSettings.TimeStepDuration = 1f / 240f;    // stiff drives: small fixed step
            _space.TimeStepSettings.MaximumTimeStepsPerFrame = 12;
            if (SubModule.LabMode)
            {
                // Lab tuning knobs (tracking experiments): solver iterations, muscle and joint motor scales.
                _space.Solver.IterationLimit = (int)LabFloat("HSC_SOLVER_ITER", 10f);
                _muscleScale = LabFloat("HSC_MUSCLE_SCALE", 1f);
                _motorScale = LabFloat("HSC_MOTOR_SCALE", 1f);
                _spineLimitScale = LabFloat("HSC_SPINE_LIMIT_SCALE", 1f);
                _limitScale = LabFloat("HSC_LIMIT_SCALE", 1f);
            }
        }

        private readonly float _muscleScale = 1f, _motorScale = 1f, _spineLimitScale = 1f, _limitScale = 1f;

        private static float LabFloat(string name, float fallback) =>
            float.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

        /// <summary>Half Sword's global StrengthMultiplyer (1 healthy, 0 limp): scales every muscle.</summary>
        public float Strength
        {
            get => _strength;
            set { _strength = Math.Max(0f, value); ApplyStrength(); }
        }

        /// <summary>PhysicsHandle LowerBody/UpperBody on or off (off = no balance, the body can fall).</summary>
        public bool Handles
        {
            get => _handles;
            set { _handles = value; ApplyStrength(); }
        }

        public static ActiveRagdoll Build(Agent agent, Scene scene, HalfSwordProfile profile, Action<string> log, bool useHook)
        {
            var r = new ActiveRagdoll(profile);
            Skeleton sk = agent.AgentVisuals.GetSkeleton();
            int n = sk.GetBoneCount();
            r._sk = sk;
            r._parent = new sbyte[n];
            r._driverEntitial = new MatrixFrame[n];
            r._driverWorld = new MatrixFrame[n];
            r._animEntitial = new MatrixFrame[n];
            r._animWorld = new MatrixFrame[n];
            r._world = new MatrixFrame[n];
            r._writtenLocal = new MatrixFrame[n];
            r._writtenEntitial = new MatrixFrame[n];
            r._hookDriver = new MatrixFrame[n];
            r._hookDrawn = new MatrixFrame[n];
            r._drawnEntitial = new MatrixFrame[n];
            r._poseRotation = new Mat3[n];
            r._poseDisplacement = new Vec3[n];
            r._poseEntitial = new MatrixFrame[n];
            for (sbyte i = 0; i < n; i++)
            {
                r._index[sk.GetBoneName(i)] = i;
                r._parent[i] = sk.GetParentBoneIndex(i);
                if (r._parent[i] < 0)
                {
                    r._root = i;
                }
            }
            r.ReadDriver(agent, true);
            Vec3 forward = agent.LookDirection;

            foreach (HalfSwordProfile.Body spec in profile.Bodies)
            {
                if (!r._index.TryGetValue(spec.Bone, out sbyte bone))
                {
                    log("ACTIVE: no bone " + spec.Bone + " on this skeleton, body skipped");
                    continue;
                }
                MatrixFrame boneWorld = r._driverWorld[bone];
                Vec3 a = boneWorld.origin;
                Vec3 b = r.SegmentEndPoint(spec.Bone, bone);
                Vec3 seg = b - a;
                float length = Math.Max(seg.Length, 0.02f);
                Vec3 dir = seg / length;
                Entity body;
                BQuat orientation;
                if (spec.IsBox)
                {
                    // Feet and hands: box with its long side (BoxY) along the segment, X sideways, Z up from the sole.
                    body = new Box(ToB((a + b) * 0.5f), spec.BoxX, Math.Max(spec.BoxY, length), spec.BoxZ, spec.Mass);
                    Vec3 side = Vec3.CrossProduct(dir, Vec3.Up);
                    if (side.Length < 0.1f)
                    {
                        side = Vec3.CrossProduct(dir, forward);
                    }
                    side.Normalize();
                    Vec3 up = Vec3.CrossProduct(side, dir);
                    orientation = BQuat.CreateFromRotationMatrix(Rows(side, dir, up));
                }
                else
                {
                    body = new Capsule(ToB((a + b) * 0.5f), length, spec.Radius, spec.Mass);
                    BVec yAxis = BVec.Up;
                    BVec target = ToB(dir);
                    BQuat.GetQuaternionBetweenNormalizedVectors(ref yAxis, ref target, out orientation);
                }
                body.Orientation = orientation;
                body.LinearDamping = 1f - (float)Math.Exp(-spec.LinearDamping);   // Unreal 1/s -> BEPU fraction per second
                body.AngularDamping = 1f - (float)Math.Exp(-spec.AngularDamping);
                body.Material = new BMaterial(1.0f, 0.8f, 0f);
                BMat inertia = body.LocalInertiaTensor;
                var part = new Part
                {
                    Spec = spec,
                    Bone = bone,
                    Body = body,
                    Inertia = (inertia.M11 + inertia.M22 + inertia.M33) / 3f,
                    Joint = profile.Joints.TryGetValue(spec.Bone, out HalfSwordProfile.Joint j) ? j : null,
                };
                BQuat boneQ = Q(boneWorld.rotation);
                part.Offset = BQuat.Concatenate(boneQ, BQuat.Conjugate(orientation)); // bone = body o offset
                part.OffsetFrame = FrameOf(body).TransformToLocalNonOrthogonal(boneWorld);
                part.OffsetInverse = part.OffsetFrame.TransformToLocalNonOrthogonal(MatrixFrame.Identity);
                // Lab HSC_KINEMATIC_ALL=1: every body follows the driver exactly (shows the driver pose as drawn).
                part.Lower = LowerBones.Contains(spec.Bone) || (SubModule.LabMode && Environment.GetEnvironmentVariable("HSC_KINEMATIC_ALL") == "1");
                r._parts[bone] = part;
                r._ordered.Add(part);
                r._space.Add(body);
                r.TotalMass += spec.Mass;
            }

            foreach (Part part in r._ordered)
            {
                if (part.Joint == null || !r._index.TryGetValue(part.Joint.Parent, out sbyte pb) || !r._parts.TryGetValue(pb, out Part parent))
                {
                    continue;
                }
                part.Parent = parent;
                BVec anchor = ToB(r._driverWorld[part.Bone].origin);
                r._space.Add(new BallSocketJoint(parent.Body, part.Body, anchor));
                BVec axis = AxisY(part.Body);
                if (part.Joint.Hinge)
                {
                    // Elbows/knees flex well beyond the swing values Unreal stores on its own joint axes: v1 keeps a wide
                    // cone so the drives, not the limit, shape the bend (hyperextension not yet blocked).
                    r._space.Add(new SwingLimit(parent.Body, part.Body, axis, axis, Rad(150f)));
                }
                else
                {
                    float k = (part.Spec.Bone.StartsWith("spine", StringComparison.Ordinal) ? r._spineLimitScale : 1f) * r._limitScale;
                    r._space.Add(new EllipseSwingLimit(parent.Body, part.Body, axis, Rad(Math.Min(170f, part.Joint.Swing1 * k)), Rad(Math.Min(170f, part.Joint.Swing2 * k))));
                }
                float twist = part.Joint.Twist * (part.Spec.Bone.StartsWith("spine", StringComparison.Ordinal) ? r._spineLimitScale : 1f) * r._limitScale;
                r._space.Add(new TwistLimit(parent.Body, part.Body, axis, axis, -Rad(Math.Min(170f, twist)), Rad(Math.Min(170f, twist))));

                var motor = new AngularMotor(parent.Body, part.Body);
                motor.Basis.SetLocalAxes(BMat.Identity);
                motor.Settings.Mode = BEPUphysics.Constraints.TwoEntity.Motors.MotorMode.Servomechanism;
                motor.Settings.MaximumForce = float.MaxValue;
                part.Motor = motor;
                r._space.Add(motor);
            }

            foreach (Part part in r._ordered)
            {
                var muscle = new SingleEntityAngularMotor(part.Body);
                muscle.Basis.SetLocalAxes(BMat.Identity);
                muscle.Settings.Mode = BEPUphysics.Constraints.TwoEntity.Motors.MotorMode.Servomechanism;
                part.Muscle = muscle;
                r._space.Add(muscle);
            }

            // No self-collision for now: joint limits keep limbs apart.
            for (int i = 0; i < r._ordered.Count; i++)
            {
                for (int k = i + 1; k < r._ordered.Count; k++)
                {
                    CollisionRules.AddRule(r._ordered[i].Body, r._ordered[k].Body, CollisionRule.NoBroadPhase);
                }
            }

            // Balance handles (Half Sword PhysicsHandle LowerBody on the pelvis, UpperBody on the chest).
            foreach (HalfSwordProfile.Handle h in profile.Handles)
            {
                if (!r._index.TryGetValue(h.Bone, out sbyte hb) || !r._parts.TryGetValue(hb, out Part hp))
                {
                    continue;
                }
                var linear = new SingleEntityLinearMotor(hp.Body, ToB(r._driverWorld[hb].origin));
                linear.Settings.Mode = BEPUphysics.Constraints.TwoEntity.Motors.MotorMode.Servomechanism;
                r._space.Add(linear);
                if (h.Name == "lower")
                {
                    r._lowerLinear = linear;
                    r._pelvis = hp;
                    var angular = new SingleEntityAngularMotor(hp.Body);
                    angular.Basis.SetLocalAxes(BMat.Identity);
                    angular.Settings.Mode = BEPUphysics.Constraints.TwoEntity.Motors.MotorMode.Servomechanism;
                    r._lowerAngular = angular;
                    r._space.Add(angular);
                }
                else
                {
                    r._upperLinear = linear;
                    r._chest = hp;
                }
            }

            Vec3 pelvisPos = r._driverWorld[0].origin;
            r._scene = scene;
            r.GroundZ = scene.GetGroundHeightAtPosition(pelvisPos);
            r._ground = new Box(new BVec(pelvisPos.x, pelvisPos.y, r.GroundZ - 0.5f), 80f, 80f, 1f);
            r._ground.Material = new BMaterial(1.0f, 0.8f, 0f);
            r._space.Add(r._ground);
            r.PlaceGround();

            r.ApplyStrength();
            r.UpdateTargets();
            log("ACTIVE: built " + r._ordered.Count + " bodies, " + r.TotalMass.ToString("0.0") + " kg, groundZ=" + r.GroundZ.ToString("0.000") +
                " pelvis above ground=" + (pelvisPos.z - r.GroundZ).ToString("0.000") + " | solver iterations " + r._space.Solver.IterationLimit +
                " muscles x" + r._muscleScale + " motors x" + r._motorScale + " limits x" + r._limitScale + " spine limits x" + r._spineLimitScale);
            if (useHook)
            {
                r.AttachHook(agent, log);
            }
            return r;
        }

        /// <summary>
        /// Pelvis and legs follow the engine's animation exactly (kinematic bodies): walking stays Bannerlord's own (user's
        /// design, 2026-10-03: the overhaul is the combat physics, not the locomotion) and the physics works from the
        /// pelvis up. Off = whole-body physics (falls, DED).
        /// </summary>
        public bool AnimatedLowerBody
        {
            get => _animatedLower;
            set
            {
                if (_animatedLower == value)
                {
                    return;
                }
                _animatedLower = value;
                foreach (Part part in _ordered)
                {
                    if (!part.Lower)
                    {
                        continue;
                    }
                    if (value)
                    {
                        part.Body.BecomeKinematic();
                    }
                    else
                    {
                        part.Body.BecomeDynamic(part.Spec.Mass);
                    }
                }
            }
        }

        // Kinematic lower body: each part gets the velocity that brings it where the animation puts its bone by the end of
        // this step (the joints to the dynamic upper body see a smooth motion); SnapLowerBody then removes the residue.
        // A jump of more than a metre (agent teleported) moves the whole body with it instead.
        private void DriveLowerBody(float dt)
        {
            if (!_animatedLower)
            {
                return;
            }
            if (_pelvis != null)
            {
                BVec jump = ToB(LowerTarget(_pelvis).origin) - _pelvis.Body.Position;
                if (jump.LengthSquared() > 1f)
                {
                    foreach (Part part in _ordered)
                    {
                        part.Body.Position += jump;
                    }
                }
            }
            float h = Math.Max(dt, 1e-4f);
            foreach (Part part in _ordered)
            {
                if (!part.Lower)
                {
                    continue;
                }
                MatrixFrame target = LowerTarget(part);
                part.Body.LinearVelocity = (ToB(target.origin) - part.Body.Position) / h;
                BQuat delta = BQuat.Concatenate(BQuat.Conjugate(part.Body.Orientation), Q(target.rotation)); // world delta
                if (delta.W < 0f)
                {
                    delta = new BQuat(-delta.X, -delta.Y, -delta.Z, -delta.W);
                }
                var axis = new BVec(delta.X, delta.Y, delta.Z);
                float sinHalf = axis.Length();
                part.Body.AngularVelocity = sinHalf < 1e-6f ? BVec.Zero : axis * (2f * (float)Math.Atan2(sinHalf, delta.W) / (sinHalf * h));
            }
        }

        private void SnapLowerBody()
        {
            if (!_animatedLower)
            {
                return;
            }
            foreach (Part part in _ordered)
            {
                if (part.Lower)
                {
                    MatrixFrame target = LowerTarget(part);
                    part.Body.Position = ToB(target.origin);
                    part.Body.Orientation = Q(target.rotation);
                }
            }
        }

        // ------------------------------------------------------------------------------------------- held item bodies
        // Half Sword holds its weapons as physics bodies (PhysicsConstraint R/L Hand): here each held item gets a box with
        // its mesh bounds (any item: read at runtime from its own mesh) and its own weight, welded to the hand. It collides
        // with the whole body except the arm holding it, so a shield or blade cannot pass through its bearer.
        private sealed class HeldBody
        {
            public Box Body;
            public WeldJoint Weld;
        }

        private readonly HeldBody[] _held = new HeldBody[2];

        /// <summary>
        /// Gives the item in hand <paramref name="side"/> (0 = main / right, 1 = off / left) a physics body. The box is the
        /// item's mesh bounds in its item bone frame (r_finger0 / l_finger0, what the engine hangs the mesh on).
        /// </summary>
        public bool SetHeldItem(int side, Vec3 boxMin, Vec3 boxMax, float mass, MatrixFrame handOffset)
        {
            ClearHeldItem(side);
            string prefix = side == 0 ? "r_" : "l_";
            if (!_index.TryGetValue(prefix + "hand", out sbyte handBone) || !_parts.TryGetValue(handBone, out Part hand) ||
                !_index.TryGetValue(prefix + "finger0", out sbyte itemBone))
            {
                return false;
            }
            MatrixFrame bone = _world[itemBone].TransformToParent(handOffset);
            float scale = bone.rotation.s.Length;
            Mat3 rotation = bone.rotation;
            rotation.Orthonormalize();
            Vec3 size = (boxMax - boxMin) * scale;
            var box = new Box(ToB(bone.TransformToParent((boxMin + boxMax) * 0.5f)), Math.Max(size.x, 0.02f), Math.Max(size.y, 0.02f),
                              Math.Max(size.z, 0.02f), Math.Max(mass, 0.2f));
            box.Orientation = Q(rotation);
            box.LinearDamping = hand.Body.LinearDamping;
            box.AngularDamping = hand.Body.AngularDamping;
            box.Material = new BMaterial(0.6f, 0.4f, 0f);
            foreach (Part part in _ordered)
            {
                string name = part.Spec.Bone;
                if (name.StartsWith(prefix, StringComparison.Ordinal) && (name.EndsWith("hand", StringComparison.Ordinal) ||
                    name.Contains("foretwist") || name.Contains("upperarm") || name.Contains("clavicle")))
                {
                    CollisionRules.AddRule(box, part.Body, CollisionRule.NoBroadPhase); // the arm holding it
                }
            }
            HeldBody other = _held[1 - side];
            if (other != null)
            {
                CollisionRules.AddRule(box, other.Body, CollisionRule.NoBroadPhase); // own two items: no lock-up for now
            }
            var weld = new WeldJoint(hand.Body, box, hand.Body.Position);
            _space.Add(box);
            _space.Add(weld);
            _held[side] = new HeldBody { Body = box, Weld = weld };
            return true;
        }

        public void ClearHeldItem(int side)
        {
            HeldBody held = _held[side];
            if (held == null)
            {
                return;
            }
            _space.Remove(held.Weld);
            _space.Remove(held.Body);
            _held[side] = null;
        }

        public bool HasHeldItem(int side) => _held[side] != null;

        /// <summary>Half Sword class whose live tuning applies to the held items (null = global values only).</summary>
        public string TuningClass { get; set; }

        // Body frame that puts the part's bone on the driver's bone (body = bone x offset^-1), rotation orthonormal.
        private MatrixFrame LowerTarget(Part part)
        {
            MatrixFrame target = _driverWorld[part.Bone].TransformToParent(part.OffsetInverse);
            target.rotation.Orthonormalize();
            return target;
        }

        /// <summary>
        /// Keeps the ground box under the pelvis on Bannerlord's ground (terrain and scene objects: height and slope there)
        /// while the body stands on animated legs; during a fall it stays where it was (no ground moving under a body).
        /// </summary>
        private void PlaceGround()
        {
            if (!_animatedLower && _groundPlaced)
            {
                return;
            }
            Vec3 p = _driverWorld[_pelvis != null ? _pelvis.Bone : _root].origin;
            float h = _scene.GetGroundHeightAtPosition(p + new Vec3(0f, 0f, 0.3f), out Vec3 normal);
            if (float.IsNaN(h) || float.IsInfinity(h) || h < p.z - 5f || h > p.z + 0.3f)
            {
                return; // no ground found under the pelvis: keep the last placement
            }
            if (normal.z < 0.5f)
            {
                normal = Vec3.Up; // a wall or a steep face: stand on a flat ground at that height
            }
            normal.Normalize();
            BVec n = ToB(normal);
            BVec z = new BVec(0f, 0f, 1f);
            BQuat.GetQuaternionBetweenNormalizedVectors(ref z, ref n, out BQuat tilt);
            _ground.Orientation = tilt;
            _ground.Position = new BVec(p.x, p.y, h) - n * (_ground.Length * 0.5f);
            GroundZ = h;
            _groundPlaced = true;
        }

        private void AttachHook(Agent agent, Action<string> log)
        {
            GameEntity entity = agent.AgentVisuals.GetEntity();
            HscPoseHook hook = entity.GetFirstScriptOfType<HscPoseHook>();
            if (hook == null)
            {
                entity.CreateAndAddScriptComponent(nameof(HscPoseHook), true);
                hook = entity.GetFirstScriptOfType<HscPoseHook>();
            }
            if (hook == null)
            {
                log("ACTIVE: pose hook NOT created (script type unknown to the engine?), direct bone writes only");
                return;
            }
            hook.Error = null;
            hook.Source = this;
            _hook = hook;
            _sk.EnableScriptDrivenPostIntegrateCallback();
            log("ACTIVE: pose hook attached (main thread " + System.Threading.Thread.CurrentThread.ManagedThreadId + ")");
        }

        /// <summary>Stops driving the skeleton: the agent goes back to its normal animation.</summary>
        public void Release()
        {
            if (_hook != null)
            {
                _hook.Source = null;
            }
            _items.Restore();
        }

        // Engine thread, right after the animation is evaluated: keep the animation as driver, put the physics pose in.
        bool IPoseSource.OnPostIntegrate(AnimResult animResult)
        {
            lock (_gate)
            {
                for (sbyte i = 0; i < _hookDriver.Length; i++)
                {
                    _hookDriver[i] = animResult.GetEntitialOutTransform(i, _sk).AsMatrixFrame;
                }
                _hookDriverFresh = true;
                if (!_poseReady)
                {
                    Array.Copy(_hookDriver, _hookDrawn, _hookDrawn.Length);
                    return false;
                }
                // The engine keeps the animated root position whatever we set (lab run 17), so the root's children are
                // placed relative to that position: every bone but the root lands where the physics put it. The root bone
                // itself is right only on the skeleton (direct write), which is what the body mesh uses.
                Vec3 rootAnim = _hookDriver[_root].origin;
                Mat3 rootRotation = _poseRotation[_root];
                for (sbyte i = 0; i < _poseRotation.Length; i++)
                {
                    animResult.SetOutQuat(i, _poseRotation[i], _sk);
                    Vec3 displacement = _parent[i] == _root ? rootRotation.TransformToLocal(_poseEntitial[i].origin - rootAnim) : _poseDisplacement[i];
                    animResult.SetOutBoneDisplacement(i, displacement, _sk);
                }
                for (sbyte i = 0; i < _hookDrawn.Length; i++)
                {
                    _hookDrawn[i] = animResult.GetEntitialOutTransform(i, _sk).AsMatrixFrame;
                }
                if (_diagRequested)
                {
                    _diagRequested = false;
                    _diag = "root anim=" + V(rootAnim) + " want=" + V(_poseEntitial[_root].origin) +
                            " read=" + V(animResult.GetEntitialOutTransform(_root, _sk).Origin) +
                            DiagBone(animResult, "spine") + DiagBone(animResult, "head") + DiagBone(animResult, "r_hand");
                }
            }
            HookWrites++;
            return true;
        }

        private string DiagBone(AnimResult animResult, string boneName)
        {
            if (!_index.TryGetValue(boneName, out sbyte b))
            {
                return "";
            }
            Vec3 read = animResult.GetEntitialOutTransform(b, _sk).Origin;
            return " | " + boneName + " want=" + V(_poseEntitial[b].origin) + " read=" + V(read) +
                   " err_cm=" + (read.Distance(_poseEntitial[b].origin) * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Asks the next callback to report root/hand positions (animation, wanted, read back after our write).</summary>
        public void RequestHookDiagnostics() => _diagRequested = true;

        /// <summary>Last callback report, plus where the skeleton's root and hand are now; null when none is pending.</summary>
        public string TakeHookDiagnostics()
        {
            string d = _diag;
            if (d == null)
            {
                return null;
            }
            _diag = null;
            sbyte hand = _index.TryGetValue("r_hand", out sbyte hb) ? hb : (sbyte)0;
            return d + " | skeleton now root=" + V(_sk.GetBoneEntitialFrame(0).origin) + " r_hand=" + V(_sk.GetBoneEntitialFrame(hand).origin);
        }

        private static string V(Vec3 v) =>
            "(" + v.x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "," +
            v.y.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "," +
            v.z.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ")";

        /// <summary>Distance (m) between a bone's current entitial position and the one we last asked the hook for.</summary>
        /// <summary>Lab: angle (deg) between each listed body's orientation and its muscle target, "name=deg ...".</summary>
        public string MuscleErrors(params string[] boneNames)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string name in boneNames)
            {
                if (!_index.TryGetValue(name, out sbyte b) || !_parts.TryGetValue(b, out Part part))
                {
                    continue;
                }
                BQuat q = part.Body.Orientation, t = part.Target;
                float dot = Math.Abs(q.X * t.X + q.Y * t.Y + q.Z * t.Z + q.W * t.W);
                float deg = (float)(2.0 * Math.Acos(Math.Min(1.0, dot)) * 180.0 / Math.PI);
                sb.Append(name).Append('=').Append(deg.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            }
            return sb.ToString();
        }

        public float HookPoseError(string boneName)
        {
            if (_hook == null || !_poseReady || !_index.TryGetValue(boneName, out sbyte b))
            {
                return -1f;
            }
            MatrixFrame wanted;
            lock (_gate)
            {
                wanted = _poseEntitial[b];
            }
            return _sk.GetBoneEntitialFrame(b).origin.Distance(wanted.origin);
        }

        /// <summary>Driver in, physics step, display out. Call once per mission tick.</summary>
        public void Step(float dt, Agent agent)
        {
            ReadDriver(agent, false);
            UpdateTargets();
            PlaceGround();
            DriveLowerBody(Math.Min(dt, 0.05f));
            _space.Update(Math.Min(dt, 0.05f));
            SnapLowerBody();
            WriteBack(agent);
            _items.Update(agent, this);
        }

        public void Push(string boneName, Vec3 impulse)
        {
            if (_index.TryGetValue(boneName, out sbyte bone) && _parts.TryGetValue(bone, out Part part))
            {
                part.Body.ApplyImpulse(part.Body.Position, ToB(impulse));
            }
        }

        public Vec3 BodyBoneWorld(string boneName) => _index.TryGetValue(boneName, out sbyte b) ? _world[b].origin : Vec3.Zero;

        public Vec3 DriverBoneWorld(string boneName) => _index.TryGetValue(boneName, out sbyte b) ? _driverWorld[b].origin : Vec3.Zero;

        /// <summary>World frame of a bone as displayed (physics), identity if unknown.</summary>
        public MatrixFrame BodyBoneFrame(string boneName) => _index.TryGetValue(boneName, out sbyte b) ? _world[b] : MatrixFrame.Identity;

        /// <summary>World frame of a bone in this frame's driver pose (after DriverOverride), identity if unknown.</summary>
        public MatrixFrame DriverBoneFrame(string boneName) => _index.TryGetValue(boneName, out sbyte b) ? _driverWorld[b] : MatrixFrame.Identity;

        /// <summary>World frame of a bone in the engine's own animation this frame (before DriverOverride).</summary>
        public MatrixFrame AnimatedBoneFrame(string boneName) => _index.TryGetValue(boneName, out sbyte b) ? _animWorld[b] : MatrixFrame.Identity;

        /// <summary>Model-space frame of a bone in the engine's own animation this frame (what held items hang on).</summary>
        public MatrixFrame AnimatedEntitial(sbyte bone) => _animEntitial[bone];

        /// <summary>
        /// Model-space frame of a bone as the engine draws it this frame: our direct write, or with the hook alone the
        /// callback's result (last physics pose on this frame's animated root), which can trail the physics by a frame.
        /// </summary>
        public MatrixFrame DrawnEntitial(sbyte bone) => DirectWrite || _hook == null ? _writtenEntitial[bone] : _drawnEntitial[bone];

        // ------------------------------------------------------------------------------------------------- internals
        private void ReadDriver(Agent agent, bool force)
        {
            Skeleton sk = agent.AgentVisuals.GetSkeleton();
            _entityFrame = agent.AgentVisuals.GetGlobalFrame();
            if (!force && _hook != null)
            {
                // Hook mode: the skeleton holds our pose; the animation was captured in the post-integrate callback.
                lock (_gate)
                {
                    if (_hookDriverFresh)
                    {
                        Array.Copy(_hookDriver, _animEntitial, _animEntitial.Length);
                        Array.Copy(_hookDrawn, _drawnEntitial, _drawnEntitial.Length);
                        _hookDriverFresh = false;
                        DriverUpdates++;
                    }
                    else
                    {
                        DriverReuses++;
                    }
                }
            }
            else
            {
                MatrixFrame pelvis = sk.GetBoneEntitialFrame(0);
                // AI agents are not re-animated every frame: if the skeleton still holds our last write, keep the last driver.
                if (!force && _wroteOnce && pelvis.origin.Distance(_lastWrittenPelvis.origin) < 1e-5f &&
                    pelvis.rotation.f.Distance(_lastWrittenPelvis.rotation.f) < 1e-5f)
                {
                    DriverReuses++;
                }
                else
                {
                    for (sbyte i = 0; i < _animEntitial.Length; i++)
                    {
                        _animEntitial[i] = sk.GetBoneEntitialFrame(i);
                    }
                    DriverUpdates++;
                }
            }
            // Driver = the engine's animation, optionally replaced in part (Half Sword arm atlas); _anim* keeps the
            // original, which is what the engine itself uses for held items.
            Array.Copy(_animEntitial, _driverEntitial, _driverEntitial.Length);
            DriverOverride?.Invoke(_driverEntitial);
            for (int i = 0; i < _driverEntitial.Length; i++)
            {
                _driverWorld[i] = _entityFrame.TransformToParent(_driverEntitial[i]);
                _animWorld[i] = _entityFrame.TransformToParent(_animEntitial[i]);
            }
        }

        private void UpdateTargets()
        {
            foreach (Part part in _ordered)
            {
                BQuat boneQ = Q(_driverWorld[part.Bone].rotation);
                part.Target = BQuat.Concatenate(BQuat.Conjugate(part.Offset), boneQ); // body = bone o offset^-1
                part.Muscle.Settings.Servo.Goal = part.Target;
            }
            foreach (Part part in _ordered)
            {
                if (part.Motor != null)
                {
                    part.Motor.Settings.Servo.Goal = BQuat.Concatenate(part.Target, BQuat.Conjugate(part.Parent.Target));
                }
            }
            if (_lowerLinear != null)
            {
                _lowerLinear.Settings.Servo.Goal = ToB(_driverWorld[_pelvis.Bone].origin);
                _lowerAngular.Settings.Servo.Goal = _pelvis.Target;
            }
            if (_upperLinear != null)
            {
                _upperLinear.Settings.Servo.Goal = ToB(_driverWorld[_chest.Bone].origin);
            }
        }

        private void ApplyStrength()
        {
            float s = _strength;
            foreach (Part part in _ordered)
            {
                float i = part.Inertia, m = s * _muscleScale, j = s * _motorScale;
                Spring(part.Muscle.Settings.Servo.SpringSettings, _profile.OrientationStrength * i * m, _profile.AngularVelocityStrength * i * m);
                part.Muscle.Settings.MaximumForce = Math.Max(1e-4f, _profile.MaxAngularForce * i * m);
                if (part.Motor != null)
                {
                    Spring(part.Motor.Settings.Servo.SpringSettings, part.Joint.MotorStiffness * i * j, Math.Max(part.Joint.MotorDamping * i * j, 1e-6f));
                }
            }
            foreach (HalfSwordProfile.Handle h in _profile.Handles)
            {
                float on = _handles ? 1f : 0f;
                if (h.Name == "lower" && _lowerLinear != null)
                {
                    float m = _pelvis.Spec.Mass;
                    Spring(_lowerLinear.Settings.Servo.SpringSettings, h.LinearStiffness * m * on, Math.Max(h.LinearDamping * m * on, 1e-6f));
                    _lowerLinear.Settings.MaximumForce = _handles ? float.MaxValue : 0f;
                    Spring(_lowerAngular.Settings.Servo.SpringSettings, h.AngularStiffness * _pelvis.Inertia * on, Math.Max(h.AngularDamping * _pelvis.Inertia * on, 1e-6f));
                    _lowerAngular.Settings.MaximumForce = _handles ? float.MaxValue : 0f;
                }
                else if (h.Name == "upper" && _upperLinear != null)
                {
                    float m = _chest.Spec.Mass;
                    Spring(_upperLinear.Settings.Servo.SpringSettings, h.LinearStiffness * m * on, Math.Max(h.LinearDamping * m * on, 1e-6f));
                    _upperLinear.Settings.MaximumForce = _handles ? float.MaxValue : 0f;
                }
            }
        }

        private static void Spring(BEPUphysics.Constraints.SpringSettings spring, float stiffness, float damping)
        {
            spring.Stiffness = Math.Max(stiffness, 1e-6f);
            spring.Damping = Math.Max(damping, 1e-6f);
        }

        private void WriteBack(Agent agent)
        {
            Skeleton sk = agent.AgentVisuals.GetSkeleton();
            int n = _parent.Length;
            for (int i = 0; i < n; i++)
            {
                sbyte p = _parent[i];
                if (_parts.TryGetValue((sbyte)i, out Part part))
                {
                    _world[i] = FrameOf(part.Body).TransformToParent(part.OffsetFrame);
                }
                else
                {
                    // Bones without a body (twists, toes, fingers) keep the driver's local pose on top of their parent.
                    MatrixFrame local = p < 0 ? _driverEntitial[i] : _driverEntitial[p].TransformToLocalNonOrthogonal(_driverEntitial[i]);
                    _world[i] = p < 0 ? _entityFrame.TransformToParent(local) : _world[p].TransformToParent(local);
                }
            }
            for (int i = 0; i < n; i++)
            {
                sbyte p = _parent[i];
                _writtenLocal[i] = p < 0 ? _entityFrame.TransformToLocalNonOrthogonal(_world[i]) : _world[p].TransformToLocalNonOrthogonal(_world[i]);
            }
            if (_hook != null)
            {
                // Picked up by the next post-integrate callback (local rotation + displacement per bone).
                lock (_gate)
                {
                    for (int i = 0; i < n; i++)
                    {
                        Mat3 rotation = _writtenLocal[i].rotation;
                        rotation.Orthonormalize();
                        _poseRotation[i] = rotation;
                        _poseDisplacement[i] = _writtenLocal[i].origin;
                        _poseEntitial[i] = _entityFrame.TransformToLocalNonOrthogonal(_world[i]);
                    }
                    _poseReady = true;
                }
            }
            if (!DirectWrite && _hook != null)
            {
                return;
            }
            for (int i = 0; i < n; i++)
            {
                sk.SetBoneLocalFrame((sbyte)i, _writtenLocal[i]);
            }
            sk.UpdateEntitialFramesFromLocalFrames();
            for (sbyte i = 0; i < n; i++)
            {
                _writtenEntitial[i] = sk.GetBoneEntitialFrame(i);
            }
            _lastWrittenPelvis = _writtenEntitial[0];
            _wroteOnce = true;
        }


        private Vec3 SegmentEndPoint(string boneName, sbyte bone)
        {
            string end = SegmentEnd.TryGetValue(boneName, out string e) ? e : "+0.10";
            if (!end.StartsWith("+") && _index.TryGetValue(end, out sbyte eb))
            {
                return _driverWorld[eb].origin;
            }
            float ext = end.StartsWith("+") ? float.Parse(end.Substring(1), System.Globalization.CultureInfo.InvariantCulture) : 0.1f;
            Vec3 along = _driverWorld[bone].origin - _driverWorld[_parent[bone]].origin;
            along.Normalize();
            return _driverWorld[bone].origin + along * ext;
        }

        private static BMat Rows(Vec3 x, Vec3 y, Vec3 z) =>
            new BMat(x.x, x.y, x.z, y.x, y.y, y.z, z.x, z.y, z.z);

        private static BQuat Q(Mat3 m)
        {
            Vec3 s = m.s, f = m.f, u = m.u;
            s.Normalize();
            f.Normalize();
            u.Normalize();
            return BQuat.CreateFromRotationMatrix(Rows(s, f, u));
        }

        private static BVec AxisY(Entity body)
        {
            BMat m = body.OrientationMatrix;
            return new BVec(m.M21, m.M22, m.M23);
        }

        // BEPU and Bannerlord both store a rotation as rows = local axes in parent space.
        private static MatrixFrame FrameOf(Entity body)
        {
            BMat m = body.OrientationMatrix;
            var rot = new Mat3(new Vec3(m.M11, m.M12, m.M13), new Vec3(m.M21, m.M22, m.M23), new Vec3(m.M31, m.M32, m.M33));
            return new MatrixFrame(rot, ToTw(body.Position));
        }

        private static float Rad(float deg) => deg * (float)Math.PI / 180f;

        private static BVec ToB(Vec3 v) => new BVec(v.x, v.y, v.z);

        private static Vec3 ToTw(BVec v) => new Vec3(v.X, v.Y, v.Z);
    }
}
