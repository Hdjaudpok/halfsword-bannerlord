"""Fake Half Sword sender for passthrough phase 0 (the GTA passthrough note's "fake host" idea): writes the HSLink line
format at 60 Hz with KNOWN values, so the Bannerlord reader and the axis mapping can be checked without Half Sword.

The actor sits at (1000, 2000, 50) with yaw 90 (facing +Y). The right hand circles in the actor's own frame:
forward = 30 + 10 cos(t), right = 20 + 10 sin(t), up = 140 cm, one turn every 4 s. So Bannerlord's LINK line must show
hand_r local fwd/right/up on that circle.

    python fake_hslink.py [seconds]
"""
import math
import os
import sys
import time

OUT = os.path.join(os.environ["USERPROFILE"], "halfsword-decomp", "hslink_state.txt")
ACTOR, YAW = (1000.0, 2000.0, 50.0), 90.0


def main(seconds):
    yaw = math.radians(YAW)
    fwd_axis = (math.cos(yaw), math.sin(yaw))     # UE: X forward, Y right
    right_axis = (-math.sin(yaw), math.cos(yaw))
    seq, start = 0, time.perf_counter()
    while time.perf_counter() - start < seconds:
        t = time.perf_counter() - start
        fwd, right, up = 30 + 10 * math.cos(t * math.pi / 2), 20 + 10 * math.sin(t * math.pi / 2), 140.0
        hx = ACTOR[0] + fwd * fwd_axis[0] + right * right_axis[0]
        hy = ACTOR[1] + fwd * fwd_axis[1] + right * right_axis[1]
        hz = ACTOR[2] + up
        seq += 1
        vals = [*ACTOR, YAW, ACTOR[0], ACTOR[1], ACTOR[2] + 95, hx, hy, hz]
        with open(OUT, "w") as f:
            f.write(f"{seq} " + " ".join(f"{v:.3f}" for v in vals) + " END\n")
        time.sleep(1 / 60)
    print(f"sent {seq} samples in {seconds} s")


if __name__ == "__main__":
    main(float(sys.argv[1]) if len(sys.argv) > 1 else 20)
