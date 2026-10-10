"""Summarise an ActiveProbe lab log by phase (stand / push 1 / push 2 / DED), next to the Half Sword fight oracle.

    python tools/active_summary.py logs/lab-YYYYMMDD-HHMMSS.log

Phases follow ActiveProbe's timeline: build 2 s, push 60 N.s at 7 s, push 220 N.s at 11 s, DED at 15 s, release 19 s.
For each phase: median and max of every body-driver gap (cm), min/median of pelvis and head height (m), and, when the
log has them, the largest engine moves of our written bones (pre-display / late) split by late re-write on/off.
"""
import re
import statistics
import sys

ORACLE = {"pelvis": 0, "spine2": 4, "head": 6, "r_hand": 22, "l_hand": 13, "r_foot": 6, "l_foot": 6}
PHASES = [("stand", 3, 7), ("push1", 7, 11), ("push2", 11, 15), ("ded", 15.5, 19)]
LINE = re.compile(r"ACTIVE t=([\d.]+) pelvisH=(-?[\d.]+) headH=(-?[\d.]+) gaps_cm (.*?)(?: \| moved_cm (.*))?$")


def parse(path):
    rows = []
    for line in open(path, encoding="utf-8-sig"):
        m = LINE.search(line.strip())
        if not m:
            continue
        row = {"t": float(m.group(1)), "pelvisH": float(m.group(2)), "headH": float(m.group(3))}
        row["gaps"] = {k: float(v) for k, v in (kv.split("=") for kv in m.group(4).split())}
        if m.group(5):
            moved = m.group(5)
            row["lateWrite"] = "lateWrite=on" in moved
            nums = re.findall(r"(pre|late)? ?(head|neck|hand)=([\d.]+)", moved)
            stage = None
            for st, bone, val in nums:
                stage = st or stage
                row["%s_%s" % (stage, bone)] = float(val)
        rows.append(row)
    return rows


def main():
    rows = parse(sys.argv[1])
    if not rows:
        sys.exit("no ACTIVE lines in " + sys.argv[1])
    bones = list(rows[0]["gaps"])
    print("%-7s %5s %6s %6s  %s" % ("phase", "n", "pelvH", "headH", "  ".join("%-13s" % b for b in bones)))
    print("%-7s %5s %6s %6s  %s" % ("oracle", "", "", "", "  ".join("%-13s" % ("med %d" % ORACLE.get(b, 0)) for b in bones)))
    for name, a, b in PHASES:
        sel = [r for r in rows if a <= r["t"] < b]
        if not sel:
            continue
        cells = []
        for bone in bones:
            vals = [r["gaps"][bone] for r in sel]
            cells.append("%-13s" % ("%.1f/%.1f" % (statistics.median(vals), max(vals))))
        print("%-7s %5d %6.3f %6.3f  %s" % (name, len(sel), statistics.median(r["pelvisH"] for r in sel),
                                            statistics.median(r["headH"] for r in sel), "  ".join(cells)))
    print("(gap cells: median/max cm)")
    if "pre_head" in rows[0]:
        print("\nengine moves of our written bones (max cm per window), by late re-write state:")
        for name, a, b in [("stand", 2, 15), ("ded", 15.5, 19)]:
            for state in (False, True):
                sel = [r for r in rows if a <= r["t"] < b and r.get("lateWrite") == state]
                if not sel:
                    continue
                keys = ["pre_head", "pre_hand", "late_head", "late_neck", "late_hand"]
                print("  %-6s lateWrite=%-3s n=%3d  %s" % (name, "on" if state else "off", len(sel),
                      "  ".join("%s=%.1f" % (k, max(r.get(k, 0) for r in sel)) for k in keys)))


if __name__ == "__main__":
    main()
