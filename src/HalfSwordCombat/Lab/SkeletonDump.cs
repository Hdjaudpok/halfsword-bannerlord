using System.Globalization;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Logs every bone of an agent's skeleton: parent, rest frame and current animated frame in model (entitial) space
    /// (origin + side/forward/up axes). Reference for retargeting Half Sword's recorded arm poses onto human_skeleton.
    /// </summary>
    internal static class SkeletonDump
    {
        public static void Write(Agent agent)
        {
            Skeleton sk = agent.AgentVisuals.GetSkeleton();
            MatrixFrame visual = agent.AgentVisuals.GetGlobalFrame();
            ProbeLog.Write("SKEL: " + sk.GetName() + " bones=" + sk.GetBoneCount() + " visualScale=" + F(visual.rotation.s.Length) +
                           " visualFwd=" + V(visual.rotation.f) + " lookDir=" + V(agent.LookDirection));
            for (sbyte i = 0; i < sk.GetBoneCount(); i++)
            {
                MatrixFrame rest = sk.GetBoneEntitialRestFrame(i);
                MatrixFrame now = sk.GetBoneEntitialFrame(i);
                var sb = new StringBuilder("SKEL ");
                sb.Append(i).Append(' ').Append(sk.GetBoneName(i)).Append(" parent=").Append(sk.GetParentBoneIndex(i))
                  .Append(" | rest o=").Append(V(rest.origin)).Append(" s=").Append(V(rest.rotation.s)).Append(" f=").Append(V(rest.rotation.f))
                  .Append(" u=").Append(V(rest.rotation.u))
                  .Append(" | now o=").Append(V(now.origin)).Append(" s=").Append(V(now.rotation.s)).Append(" f=").Append(V(now.rotation.f))
                  .Append(" u=").Append(V(now.rotation.u));
                ProbeLog.Write(sb.ToString());
            }
        }

        private static string F(float v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

        private static string V(Vec3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";
    }
}
