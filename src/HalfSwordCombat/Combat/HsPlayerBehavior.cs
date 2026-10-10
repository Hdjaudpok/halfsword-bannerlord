using System;
using System.Collections.Generic;
using System.IO;
using HalfSwordCombat.Lab;
using HalfSwordCombat.Physics;
using HalfSwordCombat.Rules;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace HalfSwordCombat.Combat
{
    /// <summary>
    /// The player fights the Half Sword way (user's design 2026-10-03: every combat control as in Half Sword; walking,
    /// grab, swap hands and drop stay Bannerlord's). While on foot with a melee weapon the agent gets the Half Sword
    /// physics body (pelvis and legs on Bannerlord's walk), native attack and block are off (HsControl), and:
    ///  - left mouse (the attack key) = Guard Right: the weapon arm; right mouse (the block key) = Guard Left: the shield
    ///    or off arm (on a two-handed weapon, the left hand on the grip); both = both arms. A held button lets the mouse
    ///    move Half Sword's aim point (camera nearly frozen); a swing is a fast mouse move, its direction and speed
    ///    picking the real Half Sword swing poses; released, the aim drifts back to the centre (HsAim);
    ///  - double click on a button = thrust (HsHand); Left Alt held = manual thrust, the vertical mouse sets the reach;
    ///  - the arm pose is the real Half Sword driver pose for the held weapon's class and the control state (HsPoseSet),
    ///    retargeted onto the agent and followed by the physics body.
    /// Lab: HSC_FAKE_INPUT=1 replaces mouse and buttons with a script; HSC_AIM_SENS scales the mouse (default 1);
    /// HSC_ATLAS_QUICK=1 uses the coarse check atlases (quick_arm_atlas_*).
    /// </summary>
    internal sealed class HsPlayerBehavior : MissionBehavior
    {
        private const float MouseToAxis = 0.07f * 0.75f; // Unreal mouse axis scale x Half Sword's 0.75
        private const float ModeFade = 0.15f;              // seconds to fade between guard states

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private struct Controls
        {
            public bool Right, Left, Alt;
            public float MouseX, MouseY;
        }

        private readonly bool _fakeInput = Environment.GetEnvironmentVariable("HSC_FAKE_INPUT") == "1";
        private readonly bool _fakeLoop = Environment.GetEnvironmentVariable("HSC_FAKE_LOOP") == "1"; // lab: replay the script
        private readonly bool _quickAtlas = SubModule.LabMode && Environment.GetEnvironmentVariable("HSC_ATLAS_QUICK") == "1";
        private readonly float _sensitivity = ParseOr(Environment.GetEnvironmentVariable("HSC_AIM_SENS"), 1f);
        private readonly HsAim _aim = new HsAim();
        private readonly HsHand _right = new HsHand(), _left = new HsHand();
        private readonly Dictionary<string, HsPoseSet> _sets = new Dictionary<string, HsPoseSet>();
        private readonly float[] _modeWeight = new float[4]; // R, L, RL, N
        private readonly Link.LinkMarker _marker = new Link.LinkMarker(); // passthrough phase 0 (live tuning: marker)
        private string _poseSource = "atlas"; // passthrough phase 1: "link" while the arms take Half Sword's live pose
        private ArmAtlas.Pose _linkPose;      // the smoothed link pose (link_smooth)
        private HalfSwordProfile _profile;
        private HsPoseSet _set;
        private bool _loadTried;
        private Agent _agent;
        private ActiveRagdoll _body;
        private ArmRetarget _retarget;
        private ArmAtlas.Pose _pose;
        private float _weight;
        private float _t0 = -1f;
        private float _nextLog, _nextGeom;
        private bool _probeDone;
        private bool _itemsDescribed;
        private Controls _lastControls;
        private TaleWorlds.Core.EquipmentIndex _mainItem = TaleWorlds.Core.EquipmentIndex.None, _offItem = TaleWorlds.Core.EquipmentIndex.None;

        /// <summary>The running mission's player behaviour, for the hsc.* console commands (HscCommands).</summary>
        internal static HsPlayerBehavior Current { get; private set; }

        // OnCreated, not OnBehaviorInitialize: Mission.AfterStart initializes the behaviours it already has before asking
        // the modules for theirs (SubModule.OnMissionBehaviorInitialize), so this one never gets OnBehaviorInitialize.
        public override void OnCreated()
        {
            base.OnCreated();
            Current = this;
        }

        public override void OnMissionTick(float dt)
        {
            Lab.Watchdog.Enter("HsPlayerBehavior.OnMissionTick");
            try
            {
                if (!LoadData())
                {
                    return;
                }
                Agent main = Mission.MainAgent;
                if (main == null || !main.IsActive() || !Eligibility.IsHalfSwordEligible(main))
                {
                    Stop("not eligible");
                    return;
                }
                if (_agent != main)
                {
                    Stop("agent changed");
                    Start(main);
                }
                float t = Mission.CurrentTime - _t0;
                LiveTuning.Poll(Mission.CurrentTime);
                SelectSet(main);
                TaleWorlds.Core.EquipmentIndex mainItem = main.GetPrimaryWieldedItemIndex(), offItem = main.GetOffhandWieldedItemIndex();
                if (mainItem != _mainItem || offItem != _offItem)
                {
                    _mainItem = mainItem;
                    _offItem = offItem;
                    _retarget.ResetGrip(); // another item in a hand: take the game's hold for it again
                }
                Controls c = ReadControls(t);
                _lastControls = c;
                Link.HsInputLink.Tick(Mission.InputManager);
                Link.HsInputLink.TickEquip(main, Mission.CurrentTime);
                float rawX = c.MouseX * MouseToAxis * _sensitivity, rawY = c.MouseY * MouseToAxis * _sensitivity;
                float axisX = Clamp(rawX, -10f, 10f), axisY = Clamp(rawY, -10f, 10f);
                float now = Mission.CurrentTime;
                _right.Update(now, dt, c.Right, c.Alt, _aim.AimSpeed, rawY, false, _aim.Stamina);
                _left.Update(now, dt, c.Left, c.Alt, _aim.AimSpeed, rawY, false, _aim.Stamina);
                bool guarding = _right.Guarding || _left.Guarding;
                float pitch = (float)(Math.Asin(Clamp(main.LookDirection.z, -1f, 1f)) * 180.0 / Math.PI);
                _aim.Update(dt, guarding, axisX, axisY, pitch);
                HsControl.Guarding = guarding;
                HsControl.ManualThrust = guarding && c.Alt;
                HsControl.CameraScaleX = _aim.CameraScale(axisX, _aim.X);
                HsControl.CameraScaleY = _aim.CameraScale(axisY, _aim.Y);
                // Half Sword never hands the arms back to a canned animation: released, the aim drifts to the centre and
                // the arms hold the matching pose. The weight only fades the pose in when control starts.
                _weight = MoveTowards(_weight, 1f, dt * 4f);
                _pose = ComposePose(dt, c.Alt);
                // Passthrough phase 1 (live tuning link_pose = 1): the arms take Half Sword's live physical pose instead of
                // the recorded atlas whenever a fresh one arrives (docs/PASSTHROUGH.md: Half Sword owns the upper body).
                _poseSource = "atlas";
                if (LiveTuning.Get(null, "link_pose", 0f) >= 0.5f && Link.HsLink.TryReadPose(out ArmAtlas.Pose live))
                {
                    // Live tuning link_smooth (seconds, 0 = off): the drawn pose follows Half Sword's with that time
                    // constant. Half Sword sends ~60 poses a second while Bannerlord draws more frames, so each pose was
                    // held for 2-3 frames ("ça fait même des saccades", 2026-10-05 23:12 with the stiffer driver pose).
                    // The user (23:35) wants an adaptation for Bannerlord: clean and firm, not Half Sword's floppiness.
                    float tau = LiveTuning.Get(null, "link_smooth", 0f);
                    _linkPose = tau > 0.001f && _linkPose != null
                        ? ArmAtlas.Pose.Blend(_linkPose, live, 1f - (float)Math.Exp(-dt / tau))
                        : live;
                    _pose = _linkPose;
                    _poseSource = "link";
                }
                else
                {
                    _linkPose = null;
                }
                // Two-handed weapon in Willie's hands (HSLink GRIP line): the left wrist keeps Half Sword's place relative
                // to the right one (ArmRetarget.KeepHands). Live tuning hands_keep = 0 turns it off for A/B.
                _retarget.KeepHands = _poseSource == "link" && Link.HsLink.TwoHanded && LiveTuning.Get(null, "hands_keep", 1f) >= 0.5f;
                _body.Step(dt, main);
                _marker.Update(main, _body);
                if (SubModule.LabMode)
                {
                    LabLog(main, t, c);
                }
            }
            catch (Exception e)
            {
                ProbeLog.Write("HsPlayerBehavior EXCEPTION: " + e);
                Stop("exception");
            }
            finally
            {
                Lab.Watchdog.Exit();
            }
        }

        protected override void OnEndMission()
        {
            Stop("mission end");
            base.OnEndMission();
        }

        public override void OnRemoveBehavior()
        {
            Stop("behavior removed");
            if (Current == this)
            {
                Current = null;
            }
            base.OnRemoveBehavior();
        }

        /// <summary>Console (hsc.status): control state, weapon class, buttons, thrust, aim, guard fades, held items.</summary>
        internal string Describe()
        {
            if (_profile == null)
            {
                return "HalfSwordCombat: control off (Half Sword data " + (_loadTried ? "missing" : "not loaded yet") + ")";
            }
            if (_agent == null || _body == null || _set == null)
            {
                return "HalfSwordCombat: control off (no eligible player agent: on foot with a melee weapon)";
            }
            return "HalfSwordCombat: control on " + _agent.Name + ", fake input=" + _fakeInput + ", tuning v" + LiveTuning.Version +
                   "\n" + StateLine(_agent, Mission.CurrentTime - _t0, _lastControls) +
                   "\nITEMS " + HeldItems.Describe(_agent, _body);
        }

        /// <summary>Console (hsc.grip): how each item bone is held (rest, game animation, held frame).</summary>
        internal string DescribeGrip() =>
            _retarget == null || _body == null ? "HalfSwordCombat: control off" : "item_grip=" + LiveTuning.Get(_set?.Class, "item_grip", 1f) + "\n" + _retarget.DescribeGrip(_body.BoneIndex);

        /// <summary>Console (hsc.dump): writes the drawn pose to the lab log now.</summary>
        internal string DumpNow()
        {
            if (_body == null || _set == null)
            {
                return "HalfSwordCombat: control off, no pose to dump";
            }
            float id = Mission.CurrentTime;
            DumpPose(id);
            return "POSE " + ProbeLog.F(id) + " written to " + ProbeLog.LogPath;
        }

        private bool LoadData()
        {
            if (_profile != null)
            {
                return true;
            }
            if (_loadTried)
            {
                return false;
            }
            _loadTried = true;
            HsPoseSet first = File.Exists(HalfSwordProfile.DefaultPath) ? GetSet("onehand_shield") : null;
            if (first == null)
            {
                ProbeLog.Write("PLAYER: Half Sword data missing (profile or the sword and shield poses): Half Sword combat stays off");
                return false;
            }
            _profile = HalfSwordProfile.Load(HalfSwordProfile.DefaultPath);
            ProbeLog.Write("PLAYER: Half Sword combat ready, fake input=" + _fakeInput + " quick atlases=" + _quickAtlas);
            return true;
        }

        private HsPoseSet GetSet(string cls)
        {
            if (!_sets.TryGetValue(cls, out HsPoseSet set))
            {
                set = HsPoseSet.Load(cls, _quickAtlas);
                _sets[cls] = set;
                ProbeLog.Write("PLAYER: poses " + cls + (set == null ? " missing" : ": " + set.Modes + " control states"));
            }
            return set;
        }

        // The held weapon picks the Half Sword class; a class not recorded yet falls back to sword and shield.
        private void SelectSet(Agent main)
        {
            string cls = HsPoseSet.ClassOf(main);
            if (_set != null && _set.Class == cls)
            {
                return;
            }
            HsPoseSet set = GetSet(cls) ?? GetSet("onehand_shield");
            if (set != _set)
            {
                _set = set;
                _body.TuningClass = set.Class;
                _retarget.TuningClass = set.Class;
                ProbeLog.Write("PLAYER: weapon class " + cls + " -> poses " + set.Class);
            }
        }

        // Guard states fade into each other (R, L, RL, N), then the thrust goes on top, weighted by the right hand's
        // thrust alpha.
        private ArmAtlas.Pose ComposePose(float dt, bool alt)
        {
            bool r = _right.Guarding, l = _left.Guarding;
            int active = r && l ? 2 : r ? 0 : l ? 1 : 3;
            float step = dt / ModeFade;
            ArmAtlas.Pose pose = null;
            float total = 0f;
            for (int m = 0; m < 4; m++)
            {
                _modeWeight[m] = MoveTowards(_modeWeight[m], m == active ? 1f : 0f, step);
                if (_modeWeight[m] <= 0.001f)
                {
                    continue;
                }
                ArmAtlas.Pose p = _set.Guard(m == 0 || m == 2, m == 1 || m == 2, _aim.X, _aim.Y, _aim.SwingYaw, _aim.SwingSpeed);
                total += _modeWeight[m];
                pose = pose == null ? p : ArmAtlas.Pose.Blend(pose, p, _modeWeight[m] / total);
            }
            if (pose == null)
            {
                pose = _set.Guard(r, l, _aim.X, _aim.Y, _aim.SwingYaw, _aim.SwingSpeed);
            }
            if (_right.ThrustAlpha > 0.001f)
            {
                ArmAtlas.Pose thrust = _set.Thrust(_aim.X, _aim.Y, _right.ThrustPosition, alt);
                if (thrust != null)
                {
                    pose = ArmAtlas.Pose.Blend(pose, thrust, Math.Min(1f, _right.ThrustAlpha));
                }
            }
            return pose;
        }

        private void Start(Agent main)
        {
            _agent = main;
            _t0 = Mission.CurrentTime;
            _body = ActiveRagdoll.Build(main, Mission.Scene, _profile, ProbeLog.Write, true);
            _body.AnimatedLowerBody = true; // walking stays Bannerlord's: physics from the pelvis up
            _retarget = new ArmRetarget(_body.BoneIndex, _body.Parents, _body.RestLocal);
            _body.DriverOverride = frames =>
            {
                if (_pose != null && _weight > 0.001f)
                {
                    _retarget.Apply(frames, _pose, _weight);
                }
            };
            // The engine pose hook alone feeds every mesh: with direct bone writes too, the body (skeleton frames) and the
            // head/face/helmet (animation result) are drawn from two sources, and tear apart as soon as the physics body
            // trails the agent (lab 2026-10-03: 50-70 cm while walking). Lab A/B: HSC_DIRECT=1 brings direct writes back.
            _body.DirectWrite = SubModule.LabMode && Environment.GetEnvironmentVariable("HSC_DIRECT") == "1";
            _weight = 0f;
            Array.Clear(_modeWeight, 0, _modeWeight.Length);
            _modeWeight[3] = 1f;
            HsControl.Agent = main;
            ProbeLog.Write("PLAYER: Half Sword control on " + main.Name + " (direct bone writes=" + _body.DirectWrite + ")");
        }

        private void Stop(string why)
        {
            if (_body == null && HsControl.Agent == null)
            {
                return;
            }
            _marker.Remove();
            _body?.Release();
            _body = null;
            _agent = null;
            _set = null;
            HsControl.Clear();
            ProbeLog.Write("PLAYER: Half Sword control off (" + why + ")");
        }

        // Attack key (left mouse by default) = Guard Right, defend key (right mouse) = Guard Left, as Half Sword binds
        // Guard Right / Guard Left to the left / right mouse buttons; Left Alt = Thrust (manual thrust).
        private Controls ReadControls(float t)
        {
            // With the input passthrough on (link_input = 1) the player's real mouse and buttons drive Half Sword, so they
            // drive this side too (camera freeze while guarding, native attack off), even in the lab profile.
            if (_fakeInput && !Link.HsInputLink.Enabled)
            {
                return FakeControls(_fakeLoop ? t % 21.5f : t);
            }
            IInputContext input = Mission.InputManager;
            if (input == null)
            {
                return default(Controls);
            }
            return new Controls
            {
                Right = input.IsGameKeyDown(9),
                Left = input.IsGameKeyDown(10),
                Alt = input.IsKeyDown(InputKey.LeftAlt),
                MouseX = input.GetMouseMoveX(),
                MouseY = input.GetMouseMoveY(),
            };
        }

        // Lab script (Half Sword aim degrees turned into the mouse moves that produce them):
        //  0-1.5 idle | 1.5-4.5 left mouse, circle | 5.5-8.5 right mouse, circle | 9.5-12 both, diagonal swings |
        //  13.0 click + 13.2 press held to 14.6 = double-click thrust | 15-18 left mouse + Left Alt, mouse up 1 s, still,
        //  mouse down 1 s (manual thrust) | 18.5-20.5 left mouse, diagonal swings.
        private Controls FakeControls(float t)
        {
            // Live tuning session: "lab_pose = 0/1/2/3" (none / left mouse / right mouse / both) and "lab_aim = x y" hold
            // a fixed control state instead of the script, so the pose can be inspected and adjusted while the game runs.
            float held = LiveTuning.Get(null, "lab_pose", -1f);
            if (held >= 0f)
            {
                Vec3 aim = LiveTuning.GetVec(null, "lab_aim");
                Vec2 d = new Vec2(aim.x, aim.y) - _aim.Target;
                float k = 0.75f * MouseToAxis * _sensitivity;
                return new Controls
                {
                    Right = held == 1f || held == 3f,
                    Left = held == 2f || held == 3f,
                    MouseX = d.x / k,
                    MouseY = d.y / k,
                };
            }
            var c = new Controls
            {
                Right = (t >= 1.5f && t < 4.5f) || (t >= 9.5f && t < 12f) || (t >= 13f && t < 13.1f) || (t >= 13.2f && t < 14.6f) ||
                        (t >= 15f && t < 18f) || (t >= 18.5f && t < 20.5f),
                Left = (t >= 5.5f && t < 8.5f) || (t >= 9.5f && t < 12f),
                Alt = t >= 15f && t < 18f,
            };
            Vec2 want = _aim.Target;
            if ((t >= 1.5f && t < 4.5f) || (t >= 5.5f && t < 8.5f))
            {
                float a = ((t >= 5.5f ? t - 5.5f : t - 1.5f) / 3f) * 2f * (float)Math.PI;
                want = new Vec2(60f * (float)Math.Cos(a), 60f * (float)Math.Sin(a));
            }
            else if ((t >= 9.5f && t < 12f) || (t >= 18.5f && t < 20.5f))
            {
                float s = t >= 18.5f ? t - 18.5f : t - 9.5f;
                float u = s % 1f;
                bool forward = ((int)s) % 2 == 0;
                Vec2 from = forward ? new Vec2(-70f, -40f) : new Vec2(70f, 40f), to = forward ? new Vec2(70f, 40f) : new Vec2(-70f, -40f);
                want = u < 0.4f ? from : u < 0.55f ? Vec2.Lerp(from, to, (u - 0.4f) / 0.15f) : to;
            }
            Vec2 delta = want - _aim.Target;
            float perAxis = 0.75f * MouseToAxis * _sensitivity; // aim target change per mouse unit
            c.MouseX = delta.x / perAxis;
            c.MouseY = delta.y / perAxis;
            if (c.Alt)
            {
                // Manual thrust: the vertical mouse alone (axis -0.1 per frame = reach about +0.6 per second).
                float axis = t < 16f ? -0.1f : t >= 17f ? 0.1f : 0f;
                c.MouseX = 0f;
                c.MouseY = axis / (MouseToAxis * _sensitivity);
            }
            return c;
        }

        // Lab: "lab_dump = n" (change n) writes the drawn pose: inputs, every bone's model-space frame (origin, s, f, u)
        // and each held item's final frame, to compare with the Half Sword pose offline.
        private void DumpPose(float id)
        {
            WritePose("POSE " + ProbeLog.F(id) + " X=" + ProbeLog.F(_aim.X) + " Y=" + ProbeLog.F(_aim.Y) +
                      " yaw=" + ProbeLog.F(_aim.SwingYaw) + " speed=" + ProbeLog.F(_aim.SwingSpeed) +
                      " R=" + (_right.Guarding ? 1 : 0) + " L=" + (_left.Guarding ? 1 : 0));
        }

        // Every bone's drawn model-space frame (origin, s, f, u) and each held item's final frame, after `header`.
        private void WritePose(string header)
        {
            var sb = new System.Text.StringBuilder(header);
            foreach (var bone in _body.Bones)
            {
                Append(sb, bone.Key, _body.DrawnEntitial(bone.Value));
            }
            for (int side = 0; side < 2; side++)
            {
                sbyte b = _body.BoneIndex(side == 0 ? "r_finger0" : "l_finger0");
                if (b >= 0)
                {
                    Append(sb, side == 0 ? "ITEM_R" : "ITEM_L", _body.DrawnEntitial(b).TransformToParent(_body.ItemOffset(side)));
                }
            }
            ProbeLog.Write(sb.ToString());
        }

        private static void Append(System.Text.StringBuilder sb, string name, MatrixFrame f)
        {
            sb.Append(" | ").Append(name);
            foreach (Vec3 v in new[] { f.origin, f.rotation.s, f.rotation.f, f.rotation.u })
            {
                sb.Append(' ').Append(ProbeLog.F(v.x)).Append(' ').Append(ProbeLog.F(v.y)).Append(' ').Append(ProbeLog.F(v.z));
            }
        }

        private float _lastDump;
        private float _lastRec, _recUntil = -1f;
        private bool? _calm;

        private void LabLog(Agent main, float t, Controls c)
        {
            // Lab: "lab_calm = 1" keeps a tuning session undisturbed (AI agents stop ticking, nobody dies); 0 = normal.
            bool calm = LiveTuning.Get(null, "lab_calm", 0f) >= 0.5f;
            if (_calm != calm)
            {
                _calm = calm;
                Mission.AllowAiTicking = !calm;
                Mission.DisableDying = calm;
                ProbeLog.Write("LAB calm=" + calm + ": AI ticking " + (calm ? "off" : "on") + ", dying " + (calm ? "off" : "on"));
            }
            float dump = LiveTuning.Get(null, "lab_dump", 0f);
            if (dump != _lastDump)
            {
                _lastDump = dump;
                DumpPose(dump);
            }
            // Lab: "lab_rec = S" (change S) records the drawn skeleton every frame for S seconds (trial B: a Half Sword
            // strike captured on human_skeleton), after one SKEL dump of the skeleton's bones, parents and rest frames.
            float rec = LiveTuning.Get(null, "lab_rec", 0f);
            if (rec != _lastRec)
            {
                _lastRec = rec;
                if (rec > 0f)
                {
                    Lab.SkeletonDump.Write(main);
                    _recUntil = t + rec;
                    ProbeLog.Write("REC start t=" + ProbeLog.F(t) + " seconds=" + ProbeLog.F(rec));
                }
            }
            if (t <= _recUntil)
            {
                WritePose("REC t=" + ProbeLog.F(t) + " X=" + ProbeLog.F(_aim.X) + " Y=" + ProbeLog.F(_aim.Y) +
                          " speed=" + ProbeLog.F(_aim.SwingSpeed) + " R=" + (_right.Guarding ? 1 : 0) +
                          " L=" + (_left.Guarding ? 1 : 0));
            }
            if (t >= _nextLog)
            {
                _nextLog = t + 0.25f;
                ProbeLog.Write("PLAYER " + StateLine(main, t, c));
            }
            if (t >= _nextGeom)
            {
                _nextGeom = t + 0.5f;
                LogGeometry(main, t);
                ProbeLog.Write("HELD t=" + ProbeLog.F(t) + " " + _body.HeldTelemetry(main));
                ProbeLog.Write("MARKER t=" + ProbeLog.F(t) + " pose=" + _poseSource + " " + _marker.Telemetry());
                ProbeLog.Write("HANDS t=" + ProbeLog.F(t) + " twoHanded=" + Link.HsLink.TwoHanded + " attached=" + Link.HsLink.LeftAttached +
                               " " + _retarget.HandsTelemetry);
                ProbeLog.Write("CAM t=" + ProbeLog.F(t) + " " + CameraPatch.Telemetry(main));
            }
            if (!_itemsDescribed && t > 1f)
            {
                _itemsDescribed = true;
                ProbeLog.Write("ITEMS " + HeldItems.Describe(main, _body));
            }
            if (!_probeDone && t > 21f)
            {
                _probeDone = true;
                ProbeLog.Write("PROBE_DONE");
                if (Environment.GetEnvironmentVariable("HSC_AUTOQUIT") == "1")
                {
                    TaleWorlds.Engine.Utilities.QuitGame();
                }
            }
        }

        // Buttons (raw / guarding), thrust state, aim, swing, guard state weights (R/L/RL/N): the lab log line and hsc.status.
        private string StateLine(Agent main, float t, Controls c) =>
            "t=" + ProbeLog.F(t) + " set=" + _set.Class + " R=" + (c.Right ? 1 : 0) + "/" + (_right.Guarding ? 1 : 0) +
            " L=" + (c.Left ? 1 : 0) + "/" + (_left.Guarding ? 1 : 0) + " alt=" + (c.Alt ? 1 : 0) +
            " thrust=" + (_right.Thrusting ? 1 : 0) + (_right.Extend ? "E" : "") + " tA=" + ProbeLog.F(_right.ThrustAlpha) +
            " tP=" + ProbeLog.F(_right.ThrustPosition) + " aim=(" + ProbeLog.F(_aim.X) + "," + ProbeLog.F(_aim.Y) +
            ") swing=" + ProbeLog.F(_aim.SwingSpeed) + " yaw=" + ProbeLog.F(_aim.SwingYaw) + " modes=" +
            ProbeLog.F(_modeWeight[0]) + "/" + ProbeLog.F(_modeWeight[1]) + "/" + ProbeLog.F(_modeWeight[2]) + "/" +
            ProbeLog.F(_modeWeight[3]) + " flags=" + main.MovementFlags;

        // Lab: camera, physics body and the engine's own pose, in the agent's visual frame (x right, y forward, z up, m).
        // Tells "the camera got pulled in" apart from "the head/helmet is drawn somewhere else than the body".
        private void LogGeometry(Agent main, float t)
        {
            MatrixFrame vf = main.AgentVisuals.GetGlobalFrame();
            sbyte head = _body.BoneIndex("head");
            Vec3 skeletonHead = head >= 0 ? vf.TransformToParent(main.AgentVisuals.GetSkeleton().GetBoneEntitialFrame(head).origin) : Vec3.Zero;
            string cam = "";
            if (TaleWorlds.ScreenSystem.ScreenManager.TopScreen is MissionScreen screen && screen.CombatCamera != null)
            {
                cam = " cam=" + Local(vf, screen.CombatCamera.Position) + " camDist=" + ProbeLog.F(screen.CameraResultDistanceToTarget);
            }
            Vec3 p = main.Position, v = main.Velocity;
            ProbeLog.Write("MOVE t=" + ProbeLog.F(t) + " pos=(" + ProbeLog.F(p.x) + "," + ProbeLog.F(p.y) + "," + ProbeLog.F(p.z) + ") vel=(" +
                           ProbeLog.F(v.x) + "," + ProbeLog.F(v.y) + "," + ProbeLog.F(v.z) + ") ai=" + main.IsAIControlled + " input=(" +
                           ProbeLog.F(main.MovementInputVector.x) + "," + ProbeLog.F(main.MovementInputVector.y) + ") groundZ=" + ProbeLog.F(_body.GroundZ) +
                           " act0=" + main.GetCurrentAction(0).GetName());
            ProbeLog.Write("GEOM t=" + ProbeLog.F(t) + cam + " eye=" + Local(vf, main.GetEyeGlobalPosition()) +
                           " pelvis phys=" + Local(vf, _body.BodyBoneWorld("pelvis")) + " drv=" + Local(vf, _body.DriverBoneWorld("pelvis")) +
                           " | head phys=" + Local(vf, _body.BodyBoneWorld("head")) + " drv=" + Local(vf, _body.DriverBoneWorld("head")) +
                           " skel=" + Local(vf, skeletonHead) + " anim=" + Local(vf, _body.AnimatedBoneFrame("head").origin) +
                           " | r_hand phys=" + Local(vf, _body.BodyBoneWorld("r_hand")) + " drv=" + Local(vf, _body.DriverBoneWorld("r_hand")));
            ProbeLog.Write("MUSCLE t=" + ProbeLog.F(t) + " " + _body.MuscleErrors("pelvis", "spine", "spine1", "spine2", "neck", "head",
                           "r_clavicle", "r_upperarm_twist", "r_foretwist", "r_hand", "l_clavicle", "l_upperarm_twist", "l_foretwist", "l_hand"));
            string diag = _body.TakeHookDiagnostics();
            if (diag != null)
            {
                ProbeLog.Write("HOOKDIAG t=" + ProbeLog.F(t) + " " + diag);
            }
            _body.RequestHookDiagnostics();
        }

        private static string Local(MatrixFrame frame, Vec3 world)
        {
            Vec3 v = frame.TransformToLocal(world);
            return "(" + ProbeLog.F(v.x) + "," + ProbeLog.F(v.y) + "," + ProbeLog.F(v.z) + ")";
        }

        private static float MoveTowards(float v, float target, float step) =>
            v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private static float ParseOr(string s, float fallback) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }
}
