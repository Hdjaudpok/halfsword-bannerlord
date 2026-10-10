using System;

namespace HalfSwordCombat.Combat
{
    /// <summary>
    /// One arm's button logic, as Half Sword's Willie_BP runs it (decoded 2026-10-03; left mouse = Guard Right, right
    /// mouse = Guard Left in the demo's own input settings):
    ///  - press: Down and Guarding on; the press counter restarts its 0.5 s reset; a second press within that window
    ///    (and not just after a thrust) starts a thrust: Thrusting on, Extend 0.1 s later, End 0.25 s after that;
    ///  - release: Down, Thrusting, Extend, End off at once; Guarding stays 0.2 s more (room for the double press);
    ///  - every tick: Thrust Alpha (how much of the thrust pose) eases toward 1 while thrusting with a still aim
    ///    (towards 0 above an aim speed of 15 deg/frame, the thrust is cancelled by swinging), at 30 (one-hand grip)
    ///    or 12.5; Thrust Position (0.2 = drawn back, 1 = extended) eases toward Extend ? Thrust Alpha : 0.2 at
    ///    20..10 by stamina (x2 one-handed); with Left Alt held (manual thrust) it eases at 10 toward the position the
    ///    mouse sets (AltThrust), and the thrust alpha target stays 1 whatever the aim speed.
    /// </summary>
    internal sealed class HsHand
    {
        public bool Down { get; private set; }
        public bool Guarding { get; private set; }
        public bool Thrusting { get; private set; }
        public bool Extend { get; private set; }
        public bool ThrustEnd { get; private set; }
        public float ThrustAlpha { get; private set; }
        public float ThrustPosition { get; private set; }
        public float AltThrust { get; private set; }

        private int _presses;
        private float _pressReset = -1f, _guardOff = -1f, _extendAt = -1f, _endAt = -1f, _thrustAgainAt;

        /// <param name="now">Mission time, seconds.</param>
        /// <param name="down">This arm's button is held.</param>
        /// <param name="alt">Left Alt held: manual thrust.</param>
        /// <param name="aimSpeed">Aim velocity magnitude, degrees per 60 Hz frame (HsAim).</param>
        /// <param name="axisY">This frame's vertical mouse axis value (Half Sword units, mouse up negative).</param>
        /// <param name="oneHandGrip">Half Sword grip types 0/5: faster thrust.</param>
        public void Update(float now, float dt, bool down, bool alt, float aimSpeed, float axisY, bool oneHandGrip, float stamina)
        {
            if (down && !Down)
            {
                Press(now, alt);
            }
            else if (!down && Down)
            {
                Release(now);
            }
            if (_pressReset >= 0f && now >= _pressReset)
            {
                _pressReset = -1f;
                _presses = 0;
            }
            if (_guardOff >= 0f && now >= _guardOff)
            {
                _guardOff = -1f;
                Guarding = Down;
            }
            if (_extendAt >= 0f && now >= _extendAt)
            {
                _extendAt = -1f;
                if (Thrusting)
                {
                    Extend = true;
                    _endAt = now + 0.25f;
                }
            }
            if (_endAt >= 0f && now >= _endAt)
            {
                _endAt = -1f;
                ThrustEnd = Extend;
            }

            bool thrustOn = Thrusting || (alt && Guarding);
            float alphaTarget = thrustOn ? MapRange(aimSpeed, 15f, 0f, alt ? 1f : 0f, 1f) : 0f;
            ThrustAlpha = FInterpTo(ThrustAlpha, alphaTarget, dt, oneHandGrip ? 30f : 12.5f);
            if (alt)
            {
                AltThrust = Clamp(ThrustPosition - axisY, 0f, 1f);
                ThrustPosition = FInterpTo(ThrustPosition, Guarding ? AltThrust : 0f, dt, 10f);
            }
            else
            {
                AltThrust = 0f;
                if (Thrusting)
                {
                    float speed = MapRange(stamina, 100f, 0f, 20f, 10f) * (oneHandGrip ? 2f : 1f);
                    ThrustPosition = FInterpTo(ThrustPosition, Extend ? ThrustAlpha : 0.2f, dt, speed);
                }
            }
        }

        private void Press(float now, bool alt)
        {
            Down = true;
            Guarding = true;
            _guardOff = -1f;
            _presses++;
            _pressReset = now + 0.5f;
            if (_presses >= 2 && !alt && now >= _thrustAgainAt)
            {
                Thrusting = true;
                Extend = false;
                ThrustEnd = false;
                _extendAt = now + 0.1f;
                _endAt = -1f;
                _thrustAgainAt = now + 0.25f;
            }
        }

        private void Release(float now)
        {
            Down = false;
            Thrusting = false;
            Extend = false;
            ThrustEnd = false;
            _extendAt = _endAt = -1f;
            _guardOff = now + 0.2f;
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
            float d = target - current;
            return d * d < 1e-8f ? target : current + d * Clamp(dt * speed, 0f, 1f);
        }
    }
}
