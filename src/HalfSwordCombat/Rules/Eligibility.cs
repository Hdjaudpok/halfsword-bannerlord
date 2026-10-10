using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace HalfSwordCombat.Rules
{
    /// <summary>
    /// Which agents get Half Sword physics combat. Everyone else keeps vanilla Bannerlord combat.
    /// Called when an agent enters the Half Sword radius around the player, and again when its situation
    /// changes (mounts/dismounts, switches weapon), so it must be cheap and side-effect free.
    /// </summary>
    internal static class Eligibility
    {
        /// <summary>
        /// Useful facts available on an agent:
        ///   agent.IsHuman, agent.HasMount (riding right now), agent.IsMainAgent (the player)
        ///   agent.Character.DefaultFormationClass  -> Infantry, Ranged, Cavalry, HorseArcher, ...
        ///   agent.Formation?.PhysicalClass          -> what its current formation is acting as
        ///   agent.WieldedWeapon.CurrentUsageItem    -> null when unarmed; .IsRangedWeapon, .IsMeleeWeapon
        ///   agent.IsRangedCached                    -> carries a usable ranged weapon
        /// </summary>
        public static bool IsHalfSwordEligible(Agent agent)
        {
            // User's rules (2026-10-02): anyone fighting on foot with a melee weapon, whatever their troop class.
            // An archer who draws a sword, dismounted cavalry and the player on foot all qualify.
            if (agent == null || !agent.IsHuman || !agent.IsActive())
            {
                return false;
            }
            if (agent.HasMount)
            {
                return false; // riders keep vanilla combat
            }
            WeaponComponentData weapon = agent.WieldedWeapon.CurrentUsageItem;
            if (weapon != null && weapon.IsRangedWeapon)
            {
                return false; // bow, crossbow or throwing weapon in hand: vanilla until a melee weapon is drawn
            }
            return true; // melee weapon, shield or bare hands, on foot
        }
    }
}
