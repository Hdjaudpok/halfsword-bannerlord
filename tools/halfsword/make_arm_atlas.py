"""Turn a Half Sword driver-pose atlas (recorded by HSPose on the player's own install) into skeleton-independent
features for the Bannerlord mod.

    python make_arm_atlas.py <set> [--quick]     e.g. onehand_shield, onehand_shield_L, twohand_T
    reads   %USERPROFILE%\\halfsword-decomp\\pose\\[quick_]atlas_<set>.csv
    writes  %USERPROFILE%\\halfsword-decomp\\port\\[quick_]arm_atlas_<set>.txt   (personal use, never in a repository)
    A thrust set (_T) is split by its extend state into _T0 (drawn back) and _T1 (extended); a manual thrust set (_A)
    by its thrust position into _A0, _A05 and _A1.

Half Sword's component space is Unreal's left-handed frame with the character facing +Y and its right side at -X.
Features are written in a right-handed character frame like Bannerlord's model space: right = +X (= -x_ue),
forward = +Y, up = +Z, metres, positions relative to the pelvis. A rotation moves to that frame by the X reflection
S = diag(-1, 1, 1): R' = S R S, i.e. quaternion (x, y, z, w) -> (x, -y, -z, w).

Per sample: the inputs (aim X/Y, swing direction and speed actually used by the game), then for each arm the shoulder
(upperarm), elbow (lowerarm) and wrist (hand) positions and the hand, clavicle, upper-arm and forearm rotations,
plus the pelvis and spine_02..05 rotations, all in the character frame.
"""
import csv
import os
import sys

import numpy as np
from scipy.spatial.transform import Rotation as R

CHAIN = ["pelvis", "spine_01", "spine_02", "spine_03", "spine_04", "spine_05"]
ARMS = {"r": ["clavicle_r", "upperarm_r", "lowerarm_r", "hand_r"], "l": ["clavicle_l", "upperarm_l", "lowerarm_l", "hand_l"]}
REFLECT = np.diag([-1.0, 1.0, 1.0])


def local_q(row, bone):
    return R.from_quat([float(row[bone + "_" + c]) for c in ("qx", "qy", "qz", "qw")])


def pos(row, bone):
    return np.array([float(row[bone + "_" + c]) for c in ("px", "py", "pz")])


def to_char_pos(p_ue, pelvis_ue):
    return REFLECT @ ((p_ue - pelvis_ue) / 100.0)


def to_char_rot(rot_ue):
    x, y, z, w = rot_ue.as_quat()
    return np.array([x, -y, -z, w])


def component_rotations(row):
    comp, rot = {}, R.identity()
    for b in CHAIN:
        rot = rot * local_q(row, b)
        comp[b] = rot
    for side, bones in ARMS.items():
        rot = comp["spine_05"]
        for b in bones:
            rot = rot * local_q(row, b)
            comp[b] = rot
    return comp


def convert(rows, name, root, prefix):
    out = ["# HalfSwordCombat arm atlas (%s), from the local Half Sword install by tools/halfsword/make_arm_atlas.py." % name,
           "# Personal use: do not commit or share. Character frame: right +X, forward +Y, up +Z, metres, pelvis-relative.",
           "# columns: x y dir proxy X Y swingYaw swingSpeed | per arm r/l: sh el wr (xyz) hq cq uq fq (xyzw) | pq t2q t3q t4q t5q (xyzw)"]
    lengths = []
    for row in rows:
        comp = component_rotations(row)
        pelvis = pos(row, "pelvis")
        vals = [row[k] for k in ("x", "y", "dir", "proxy", "X", "Y", "swingYaw", "swingSpeed")]
        for side, bones in ARMS.items():
            sh, el, wr = (to_char_pos(pos(row, b), pelvis) for b in bones[1:])
            lengths.append((np.linalg.norm(el - sh), np.linalg.norm(wr - el)))
            for p in (sh, el, wr):
                vals += ["%.4f" % v for v in p]
            for b in (bones[3], bones[0], bones[1], bones[2]):
                vals += ["%.5f" % v for v in to_char_rot(comp[b])]
        for b in ("pelvis", "spine_02", "spine_03", "spine_04", "spine_05"):
            vals += ["%.5f" % v for v in to_char_rot(comp[b])]
        out.append(" ".join(vals))
    lengths = np.array(lengths)
    out.append("# samples %d, upper arm %.4f +- %.4f m, forearm %.4f +- %.4f m" % (
        len(rows), lengths[:, 0].mean(), lengths[:, 0].std(), lengths[:, 1].mean(), lengths[:, 1].std()))
    os.makedirs(os.path.join(root, "port"), exist_ok=True)
    path = os.path.join(root, "port", "%sarm_atlas_%s.txt" % (prefix, name))
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    print("wrote", path, "samples", len(rows))
    print(out[-1])


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    name = args[0] if args else "onehand_shield"
    prefix = "quick_" if "--quick" in sys.argv else ""
    root = os.path.join(os.path.expanduser("~"), "halfsword-decomp")
    rows = [r for r in csv.DictReader(open(os.path.join(root, "pose", "%satlas_%s.csv" % (prefix, name)), encoding="utf-8"))
            if r.get("hand_l_pz")]
    if name.endswith("_T"):
        for state, suffix in (("false", "0"), ("true", "1")):
            convert([r for r in rows if r["extend"] == state], name + suffix, root, prefix)
    elif name.endswith("_A"):
        for alt, suffix in (("0", "0"), ("0.5", "05"), ("1", "1")):
            convert([r for r in rows if r["alt"] == alt], name + suffix, root, prefix)
    else:
        convert(rows, name, root, prefix)


if __name__ == "__main__":
    main()
