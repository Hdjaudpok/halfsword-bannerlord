using System;
using TaleWorlds.Library;

namespace HalfSwordCombat.Combat
{
    /// <summary>
    /// Half Sword's aim model, ported from the decoded Willie_BP (Update Aim + the per-tick aim velocity and swing blocks):
    ///  - guarding (aim button held): the mouse moves a target point (degrees, disc of radius 90, +0.75 x axis value per
    ///    event); the aim point chases it at <= 175 deg/s (less when out of stamina); X = aim x, Y = aim y - camera pitch;
    ///  - not guarding: the aim point drifts back to the centre (interp speed 1);
    ///  - swing speed: an accumulator fed by the aim velocity (more when moving towards the centre, more near the centre),
    ///    decaying, 0..75 after remapping; swing direction = yaw of the aim velocity, frozen above swing speed 35.
    /// Half Sword runs these per frame at its own rate: velocities here are per 60 Hz frame so the feel does not depend
    /// on Bannerlord's frame rate.
    /// </summary>
    internal sealed class HsAim
    {
        private const float RefFrame = 1f / 60f;

        public Vec2 Target;      // Aim Vector_0
        public Vec2 Aim;         // Aim Vector
        private Vec2 _delayed;   // Aim Vector Delayed
        private float _pitchRef; // camera pitch the arm height is measured from (frozen while not guarding)

        public float X { get; private set; }
        public float Y { get; private set; }
        public float SwingProxy { get; private set; }
        public float SwingSpeed { get; private set; }
        public float SwingYaw { get; private set; }

        /// <summary>Aim velocity magnitude, degrees per 60 Hz frame (0 while not guarding).</summary>
        public float AimSpeed { get; private set; }

        public float Stamina = 100f;
        public float Exhaustion;
        public bool Player = true;

        /// <param name="axisX">Half Sword axis value (mouse delta x sensitivity x 0.75, clamped to +-10), right positive.</param>
        /// <param name="axisY">Same for the vertical axis, mouse up negative (arm up).</param>
        /// <param name="pitchDeg">Camera pitch, degrees, looking up positive.</param>
        public void Update(float dt, bool guarding, float axisX, float axisY, float pitchDeg)
        {
            if (dt <= 0f)
            {
                return;
            }
            if (guarding)
            {
                Target = ClampSize(Target + new Vec2(axisX, axisY) * 0.75f, 90f);
                float maxSpeed = MapRange(Stamina, 100f, 0f, 175f, MapRange(Exhaustion, 0f, 1f, 100f, 50f)) * (Player ? 1f : 4f);
                // Half Sword runs this chase in every Update Aim call: both mouse axis events and the tick, 3 per frame
                // while the mouse moves (the only way its swing direction threshold of 5 deg/frame can be reached).
                int steps = axisX != 0f || axisY != 0f ? 3 : 1;
                for (int i = 0; i < steps; i++)
                {
                    Aim += ClampSize(Target - Aim, maxSpeed * dt);
                }
                // The camera pitch only counts while guarding (user's choice 2026-10-03: without a click the arms do not
                // follow the look); it eases in so pressing a button does not snap the arm.
                _pitchRef = _pitchRef + (pitchDeg - _pitchRef) * Clamp(dt * 10f, 0f, 1f);
                X = Aim.x;
                Y = Clamp(Aim.y - _pitchRef, -90f, 90f);
            }
            else
            {
                Aim = Aim - Aim * Clamp(dt * 1f, 0f, 1f); // VInterpTo(Aim, 0, dt, 1)
                Target = Aim;
                X = Aim.x;
                Y = Clamp(Aim.y - _pitchRef, -90f, 90f);
            }

            var cur = new Vec2(X, Y);
            Vec2 vel = guarding ? (cur - _delayed) * (RefFrame / dt) : Vec2.Zero;
            _delayed = cur;
            float speed = vel.Length;
            AimSpeed = speed;
            Vec2 velDir = speed > 1e-4f ? vel * (1f / speed) : Vec2.Zero;
            float curLen = cur.Length;
            Vec2 toCentre = curLen > 1e-4f ? cur * (-1f / curLen) : Vec2.Zero;
            float dot = velDir.x * toCentre.x + velDir.y * toCentre.y;
            float aimLen = Aim.Length;
            float factor = MapRange(aimLen, 0f, 90f, 2f, MapRange(dot, 0f, 1f, 0.1f, 1.5f));
            float proxy = SwingProxy + speed * 0.3f * factor * (dt / RefFrame);
            proxy = FInterpTo(proxy, 0f, dt, MapRange(aimLen, 0f, 90f, 1.25f, 1.5f));
            SwingProxy = Clamp(proxy, 0f, 75f);
            SwingSpeed = MapRange(SwingProxy, 25f, 75f, 0f, 75f);

            float yawTarget = (float)(Math.Atan2(-vel.x, vel.y) * 180.0 / Math.PI);
            float chosen = speed >= 5f ? yawTarget : SwingYaw;
            float interp = SwingSpeed <= 35f ? 0f : MapRange(speed, 0f, 70f, 50f, 5f);
            SwingYaw = interp <= 0f ? chosen : YawInterpTo(SwingYaw, chosen, dt, interp);
        }

        /// <summary>Half Sword's camera turn factor while guarding (x Look Sensitivity 0.25): slow, faster at the disc edge.</summary>
        public float CameraScale(float axis, float aimCoord) =>
            0.25f * 0.005f * Clamp(axis * aimCoord >= 0f ? Math.Abs(aimCoord) : 0f, 10f, 90f);

        private static Vec2 ClampSize(Vec2 v, float max)
        {
            float len = v.Length;
            return len > max && len > 0f ? v * (max / len) : v;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        // Unreal MapRangeClamped.
        private static float MapRange(float v, float inA, float inB, float outA, float outB)
        {
            float t = Math.Abs(inB - inA) < 1e-6f ? 0f : Clamp((v - inA) / (inB - inA), 0f, 1f);
            return outA + t * (outB - outA);
        }

        // Unreal FInterpTo.
        private static float FInterpTo(float current, float target, float dt, float speed)
        {
            if (speed <= 0f)
            {
                return target;
            }
            float dist = target - current;
            return dist * dist < 1e-8f ? target : current + dist * Clamp(dt * speed, 0f, 1f);
        }

        private static float YawInterpTo(float current, float target, float dt, float speed)
        {
            float d = target - current;
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            return current + d * Clamp(dt * speed, 0f, 1f);
        }
    }
}
