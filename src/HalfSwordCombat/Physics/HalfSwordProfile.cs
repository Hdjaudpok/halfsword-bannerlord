using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// Physics profile generated on the player's machine by tools/halfsword/make_profile.py from their own Half Sword
    /// install. The mod ships no Half Sword values: without this file, Half Sword physics stays off.
    /// Units: metres, kilograms, degrees. Strengths are Unreal acceleration-mode values (multiplied by mass/inertia here).
    /// </summary>
    internal sealed class HalfSwordProfile
    {
        internal sealed class Body
        {
            public string Bone;
            public float Mass;
            public float Radius;
            public bool IsBox;
            public float BoxX, BoxY, BoxZ;
            public float LinearDamping, AngularDamping;
        }

        internal sealed class Joint
        {
            public string Bone, Parent;
            public float Swing1, Swing2, Twist;
            public float MotorStiffness, MotorDamping;
            public float LimitStiffness, LimitDamping;
            public bool Hinge;
        }

        internal sealed class Handle
        {
            public string Name, Bone;
            public float LinearStiffness, LinearDamping, AngularStiffness, AngularDamping;
        }

        public readonly List<Body> Bodies = new List<Body>();
        public readonly Dictionary<string, Joint> Joints = new Dictionary<string, Joint>();
        public readonly List<Handle> Handles = new List<Handle>();
        public float OrientationStrength, AngularVelocityStrength, MaxAngularForce, MaxLinearForce;

        public static string DefaultPath =>
            Environment.GetEnvironmentVariable("HSC_HS_PROFILE") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "halfsword-decomp", "port", "hs_profile.txt");

        public static HalfSwordProfile Load(string path)
        {
            var p = new HalfSwordProfile();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                string[] tok = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var kv = new Dictionary<string, string>();
                for (int i = 2; i < tok.Length; i++)
                {
                    int eq = tok[i].IndexOf('=');
                    if (eq > 0)
                    {
                        kv[tok[i].Substring(0, eq)] = tok[i].Substring(eq + 1);
                    }
                }
                switch (tok[0])
                {
                    case "body":
                        var b = new Body { Bone = tok[1], Mass = F(kv, "mass"), LinearDamping = F(kv, "lin_damp"), AngularDamping = F(kv, "ang_damp") };
                        if (kv.TryGetValue("box", out string box))
                        {
                            string[] d = box.Split(',');
                            b.IsBox = true;
                            b.BoxX = P(d[0]);
                            b.BoxY = P(d[1]);
                            b.BoxZ = P(d[2]);
                        }
                        else
                        {
                            b.Radius = F(kv, "radius");
                        }
                        p.Bodies.Add(b);
                        break;
                    case "joint":
                        p.Joints[tok[1]] = new Joint
                        {
                            Bone = tok[1], Parent = kv["parent"], Swing1 = F(kv, "swing1"), Swing2 = F(kv, "swing2"), Twist = F(kv, "twist"),
                            MotorStiffness = F(kv, "motor_k"), MotorDamping = F(kv, "motor_d"),
                            LimitStiffness = F(kv, "limit_k"), LimitDamping = F(kv, "limit_d"), Hinge = kv.ContainsKey("hinge"),
                        };
                        break;
                    case "muscles":
                        // "muscles orientation=.." has no name token: tok[1] is the first key=value pair.
                        AddPair(kv, tok[1]);
                        p.OrientationStrength = F(kv, "orientation");
                        p.AngularVelocityStrength = F(kv, "angular_velocity");
                        p.MaxAngularForce = F(kv, "max_angular");
                        p.MaxLinearForce = F(kv, "max_linear");
                        break;
                    case "handle":
                        AddPair(kv, tok[1]);
                        p.Handles.Add(new Handle
                        {
                            Name = kv["name"], Bone = kv["bone"], LinearStiffness = F(kv, "linear_k"), LinearDamping = F(kv, "linear_d"),
                            AngularStiffness = F(kv, "angular_k"), AngularDamping = F(kv, "angular_d"),
                        });
                        break;
                }
            }
            return p;
        }

        private static void AddPair(Dictionary<string, string> kv, string token)
        {
            int eq = token.IndexOf('=');
            if (eq > 0)
            {
                kv[token.Substring(0, eq)] = token.Substring(eq + 1);
            }
        }

        private static float F(Dictionary<string, string> kv, string key) => kv.TryGetValue(key, out string v) ? P(v) : 0f;

        private static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
