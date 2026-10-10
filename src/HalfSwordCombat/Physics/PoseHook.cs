using System;
using System.Threading;
using TaleWorlds.Engine;

namespace HalfSwordCombat.Physics
{
    /// <summary>
    /// Engine skeleton post-integrate callback (Skeleton.EnableScriptDrivenPostIntegrateCallback): runs right after the
    /// animation is evaluated and before anything uses it (rendering, head and face meshes, attachments). The owner reads
    /// the fresh animation there (Half Sword's driver pose) and replaces it with the physics pose.
    /// Lab runs 9-11: bones written from mission ticks moved the body but the head meshes stayed on the animation, and
    /// writing after the agents tick froze the game.
    /// </summary>
    public sealed class HscPoseHook : ScriptComponentBehavior
    {
        internal IPoseSource Source;
        internal int Calls;
        internal int LastThreadId;
        internal string Error;

        protected override bool SkeletonPostIntegrateCallback(AnimResult animResult)
        {
            Calls++;
            LastThreadId = Thread.CurrentThread.ManagedThreadId;
            try
            {
                IPoseSource source = Source;
                return source != null && source.OnPostIntegrate(animResult);
            }
            catch (Exception e)
            {
                // Never let an exception cross back into native code.
                Error = e.ToString();
                Source = null;
                return false;
            }
        }
    }

    internal interface IPoseSource
    {
        bool OnPostIntegrate(AnimResult animResult);
    }
}
