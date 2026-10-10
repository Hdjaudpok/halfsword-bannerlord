#!/usr/bin/env python3
"""Changes one constant of Half Sword's Blueprint bytecode in the running demo's memory (nothing on disk).

Each preset is a byte pattern that occurs once in the whole game's bytecode (checked in the UE4SS .jmap of 2026-10-02):
EX_DoubleConst (0x37) arguments of one MapRangeClamped call, then EX_EndFunctionParms (0x16). The script finds it in the
process's committed read-write memory (also when already patched), writes the new double and reads it back.

  side   Willie_BP `Set Arm Aim Rotation`: vertical aim floor = MapRangeClamped(|X|, 0, 90, -88, SIDE). Half Sword: -30.
         Tried 2026-10-05 22:50 at -88 for "arme basse sur les côtés": the whole guard went lower (rest 0.22 -> -0.05 m)
         and the side rise stayed, so it was reverted (MODLOG 23:00).
  swing  Willie_BP ubergraph: Aim Swing Speed = MapRangeClamped(Swing Velocity proxy, 25, 75, 0, SWING). Half Sword: 75.
         Aim Swing Speed doses every swing pose of the animation blueprint (22 blends) and the arm's reach in
         `Set Arm Aim Location`; the aim itself still follows the mouse. The user (2026-10-05 23:05): big swings lift the
         weapon without the mouse going up.

  hs_bp_patch.py PID side -30        hs_bp_patch.py PID swing 37.5        hs_bp_patch.py PID swing 75   (restore)
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import struct
import sys


def D(v):
    return b"\x37" + struct.pack("<d", v)


# name: (bytes before the value, bytes after it, Half Sword's value, allowed range)
PRESETS = {
    "side": (D(-88.0) + b"\x37", b"\x16", -30.0, (-88.0, -30.0)),
    "swing": (D(25.0) + D(75.0) + D(0.0) + b"\x37", b"\x16", 75.0, (0.0, 75.0)),
}

PROCESS_VM_READ, PROCESS_VM_WRITE, PROCESS_VM_OPERATION, PROCESS_QUERY_INFORMATION = 0x10, 0x20, 0x08, 0x400
MEM_COMMIT, PAGE_READWRITE = 0x1000, 0x04


class MBI(ctypes.Structure):
    _fields_ = [("BaseAddress", ctypes.c_void_p), ("AllocationBase", ctypes.c_void_p), ("AllocationProtect", wt.DWORD),
                ("PartitionId", wt.WORD), ("RegionSize", ctypes.c_size_t), ("State", wt.DWORD), ("Protect", wt.DWORD),
                ("Type", wt.DWORD)]


k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = wt.HANDLE
k32.VirtualQueryEx.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.POINTER(MBI), ctypes.c_size_t]
k32.VirtualQueryEx.restype = ctypes.c_size_t
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.WriteProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


def read(h, addr, size):
    buf = ctypes.create_string_buffer(size)
    got = ctypes.c_size_t()
    if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, size, ctypes.byref(got)):
        return b""
    return buf.raw[:got.value]


def find(h, before, after, lo, hi):
    """Addresses of the value (8 bytes) wherever before + value + after occurs and the value is within [lo, hi]."""
    hits, addr, mbi, span = [], 0, MBI(), len(before) + 8 + len(after)
    while k32.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi)):
        base, size = mbi.BaseAddress or 0, mbi.RegionSize
        if mbi.State == MEM_COMMIT and mbi.Protect == PAGE_READWRITE:
            chunk = 16 << 20
            for off in range(0, size, chunk):
                data = read(h, base + off, min(chunk + span, size - off))
                i = data.find(before)
                while i >= 0:
                    j = i + len(before)
                    if j + 8 + len(after) <= len(data) and data[j + 8:j + 8 + len(after)] == after:
                        value = struct.unpack_from("<d", data, j)[0]
                        if lo <= value <= hi:
                            hits.append((base + off + j, value))
                    i = data.find(before, i + 1)
        addr = base + size
        if addr >= 0x7FFFFFFFFFFF:
            break
    return sorted(set(hits))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pid", type=int)
    ap.add_argument("preset", choices=sorted(PRESETS))
    ap.add_argument("value", type=float)
    a = ap.parse_args()
    before, after, own, (lo, hi) = PRESETS[a.preset]
    if not lo <= a.value <= hi:
        sys.exit("%s must be within %g..%g (Half Sword's own value: %g)" % (a.preset, lo, hi, own))
    h = k32.OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, False, a.pid)
    if not h:
        sys.exit("OpenProcess(%d) failed: %d" % (a.pid, ctypes.get_last_error()))
    hits = find(h, before, after, lo, hi)
    if not hits:
        sys.exit("pattern not found (Willie_BP not loaded yet, or another game version)")
    if len(hits) > 2:
        sys.exit("pattern found %d times, refusing to write: %s" % (len(hits), hits))
    new = struct.pack("<d", a.value)
    for addr, old in hits:
        wrote = ctypes.c_size_t()
        ok = k32.WriteProcessMemory(h, ctypes.c_void_p(addr), new, 8, ctypes.byref(wrote))
        back = struct.unpack("<d", read(h, addr, 8))[0]
        print("0x%x: %s %g -> %g (%s, read back %g; Half Sword's own %g)" % (
            addr, a.preset, old, a.value, "ok" if ok else "WRITE FAILED", back, own))
    k32.CloseHandle(h)


if __name__ == "__main__":
    main()
