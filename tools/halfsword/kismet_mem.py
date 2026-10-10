"""Disassemble in-memory Kismet (Blueprint) bytecode from a UE4SS .jmap dump into readable pseudo-code.

The .jmap stores each UFunction's Script array as it sits in memory (UE 5.4 shipping): object, function and
property references are raw 8-byte pointers, names are 8-byte FScriptName (no case-preserving index).
Pointers are resolved through the addresses the .jmap records for every UObject and FProperty.

    python kismet_mem.py <dump.jmap> "<Class path>" ["<function name>" ...]   -> prints pseudo-code
    python kismet_mem.py <dump.jmap> "<Class path>" --all > out.txt          -> every function of the class

Personal reverse-engineering aid for the HalfSwordCombat Bannerlord mod. Output (game logic) stays outside repos.
"""
import base64
import os
import json
import struct
import sys

# ------------------------------------------------------------------ address -> name tables
ADDR = {}
NAMES = {}  # FName ComparisonIndex -> string, optional (names.json resolved in game by UE4SS)


def load(path):
    data = json.load(open(path, "rb"))
    objs = data["objects"]
    for name, o in objs.items():
        if not isinstance(o, dict):
            continue
        a = o.get("address")
        if a:
            ADDR[int(a, 16)] = name.rsplit(".", 1)[-1].split(":")[-1] if o.get("type") != "Function" else name.split(":")[-1]
        for p in o.get("properties") or []:
            pa = p.get("address")
            if pa:
                ADDR[int(pa, 16)] = p.get("name")
    return objs


def nm(ptr):
    if ptr == 0:
        return "None"
    return ADDR.get(ptr, "<0x%x>" % ptr)


# ------------------------------------------------------------------ reader
class R:
    def __init__(self, b):
        self.b, self.i = b, 0

    def u8(self):
        v = self.b[self.i]; self.i += 1; return v

    def u16(self):
        v = struct.unpack_from("<H", self.b, self.i)[0]; self.i += 2; return v

    def u32(self):
        v = struct.unpack_from("<I", self.b, self.i)[0]; self.i += 4; return v

    def i32(self):
        v = struct.unpack_from("<i", self.b, self.i)[0]; self.i += 4; return v

    def i64(self):
        v = struct.unpack_from("<q", self.b, self.i)[0]; self.i += 8; return v

    def u64(self):
        v = struct.unpack_from("<Q", self.b, self.i)[0]; self.i += 8; return v

    def f32(self):
        v = struct.unpack_from("<f", self.b, self.i)[0]; self.i += 4; return v

    def f64(self):
        v = struct.unpack_from("<d", self.b, self.i)[0]; self.i += 8; return v

    def ptr(self):
        return self.u64()

    def name(self):
        # FScriptName is 12 bytes in this build: ComparisonIndex, DisplayIndex, Number (verified on Muscle Motor:
        # with 8 bytes the stream desyncs by 4 at the first EX_NameConst). The .jmap has no FName pool, so names
        # print as their index unless NAMES (index -> string, resolved in game) was loaded.
        idx, _display, num = self.u32(), self.u32(), self.u32()
        base = NAMES.get(idx, "fname#%d" % idx)
        return base + ("_%d" % (num - 1) if num else "")

    def cstr(self):
        j = self.b.index(0, self.i)
        s = self.b[self.i:j].decode("latin-1"); self.i = j + 1; return s

    def wstr(self):
        out = []
        while True:
            c = self.u16()
            if c == 0:
                break
            out.append(chr(c))
        return "".join(out)


END = object()  # sentinel for EX_EndFunctionParms / EX_EndArray / ...


def F(x):
    return ("%.6g" % x) if isinstance(x, float) else str(x)


def expr(r):
    """Read one expression; return its text (or END for terminators)."""
    op = r.u8()
    t = OPS.get(op)
    if t is None:
        raise ValueError("unknown opcode 0x%02x at %d" % (op, r.i - 1))
    return t(r)


def args(r):
    out = []
    while True:
        e = expr(r)
        if e is END:
            return out
        out.append(e)


def call(fname, a, ctx=None):
    s = "%s(%s)" % (fname, ", ".join(a))
    return s if ctx is None else "%s.%s" % (ctx, s)


OPS = {}


def op(code):
    def deco(fn):
        OPS[code] = fn
        return fn
    return deco


@op(0x00)
def _local(r): return nm(r.ptr())
@op(0x01)
def _inst(r): return "this." + nm(r.ptr())
@op(0x02)
def _default(r): return "default." + nm(r.ptr())
@op(0x04)
def _ret(r): return "return " + expr(r)
@op(0x06)
def _jump(r): return "goto L%d" % r.u32()
@op(0x07)
def _jumpifnot(r):
    off = r.u32()
    return "if not (%s) goto L%d" % (expr(r), off)
@op(0x09)
def _assert(r):
    r.u16(); r.u8(); return "assert(%s)" % expr(r)
@op(0x0B)
def _nothing(r): return "nop"
@op(0x0C)
def _nothinginitdefault(r): r.ptr(); return "nop"
@op(0x0F)
def _let(r):
    r.ptr()
    var = expr(r)
    return "%s = %s" % (var, expr(r))
@op(0x11)
def _bitfield(r): r.ptr(); return "bitfield(%d)" % r.u8()
@op(0x12)
def _classctx(r):
    obj = expr(r); r.u32(); r.ptr(); return "%s::%s" % (obj, expr(r))
@op(0x13)
def _metacast(r):
    cls = nm(r.ptr()); return "MetaCast<%s>(%s)" % (cls, expr(r))
@op(0x14)
def _letbool(r):
    var = expr(r); return "%s = %s" % (var, expr(r))
@op(0x15)
def _endparmvalue(r): return END
@op(0x16)
def _endfuncparms(r): return END
@op(0x17)
def _self(r): return "this"
@op(0x18)
def _skip(r): r.u32(); return expr(r)
@op(0x19)
def _context(r):
    obj = expr(r); r.u32(); r.ptr(); return "%s.%s" % (obj, expr(r))
@op(0x1A)
def _context_fs(r):
    obj = expr(r); r.u32(); r.ptr(); return "%s?.%s" % (obj, expr(r))
@op(0x1B)
def _virtual(r):
    n = r.name(); return call(n, args(r))
@op(0x1C)
def _final(r):
    f = nm(r.ptr()); return call(f, args(r))
@op(0x1D)
def _int(r): return str(r.i32())
@op(0x1E)
def _float(r): return F(r.f32())
@op(0x1F)
def _string(r): return repr(r.cstr())
@op(0x20)
def _objconst(r): return "obj:" + nm(r.ptr())
@op(0x21)
def _nameconst(r): return "Name(%s)" % r.name()
@op(0x22)
def _rot(r): return "Rot(%s,%s,%s)" % (F(r.f64()), F(r.f64()), F(r.f64()))
@op(0x23)
def _vec(r): return "Vec(%s,%s,%s)" % (F(r.f64()), F(r.f64()), F(r.f64()))
@op(0x24)
def _byte(r): return str(r.u8())
@op(0x25)
def _int0(r): return "0"
@op(0x26)
def _int1(r): return "1"
@op(0x27)
def _true(r): return "true"
@op(0x28)
def _false(r): return "false"
@op(0x29)
def _text(r):
    kind = r.u8()
    if kind == 0:   # Empty
        return 'Text("")'
    if kind == 1:   # LocalizedText: source, key, namespace
        s = expr(r); expr(r); expr(r); return "Text(%s)" % s
    if kind == 2:   # InvariantText
        return "Text(%s)" % expr(r)
    if kind == 3:   # LiteralString
        return "Text(%s)" % expr(r)
    if kind == 4:   # StringTableEntry
        r.ptr(); expr(r); expr(r); return "Text(table)"
    return "Text(?)"
@op(0x2A)
def _noobj(r): return "null"
@op(0x2B)
def _transform(r):
    q = [r.f64() for _ in range(4)]; t = [r.f64() for _ in range(3)]; s = [r.f64() for _ in range(3)]
    return "Transform(q=%s,t=%s,s=%s)" % ([F(x) for x in q], [F(x) for x in t], [F(x) for x in s])
@op(0x2C)
def _intbyte(r): return str(r.u8())
@op(0x2D)
def _nointerface(r): return "null"
@op(0x2E)
def _dyncast(r):
    cls = nm(r.ptr()); return "Cast<%s>(%s)" % (cls, expr(r))
@op(0x2F)
def _structconst(r):
    s = nm(r.ptr()); r.i32(); return "%s{%s}" % (s, ", ".join(args(r)))
@op(0x30)
def _endstructconst(r): return END
@op(0x31)
def _setarray(r):
    arr = expr(r); return "%s = [%s]" % (arr, ", ".join(args(r)))
@op(0x32)
def _endarray(r): return END
@op(0x33)
def _propconst(r): return "prop:" + nm(r.ptr())
@op(0x34)
def _ustring(r): return repr(r.wstr())
@op(0x35)
def _i64(r): return str(r.i64())
@op(0x36)
def _u64(r): return str(r.u64())
@op(0x37)
def _double(r): return F(r.f64())
@op(0x38)
def _cast(r):
    kind = r.u8(); return "cast%d(%s)" % (kind, expr(r))
@op(0x39)
def _setset(r):
    s = expr(r); r.i32(); return "%s = {%s}" % (s, ", ".join(args(r)))
@op(0x3A)
def _endset(r): return END
@op(0x3B)
def _setmap(r):
    m = expr(r); r.i32(); return "%s = map{%s}" % (m, ", ".join(args(r)))
@op(0x3C)
def _endmap(r): return END
@op(0x3D)
def _setconst(r):
    r.ptr(); r.i32(); return "{%s}" % ", ".join(args(r))
@op(0x3E)
def _endsetconst(r): return END
@op(0x3F)
def _mapconst(r):
    r.ptr(); r.ptr(); r.i32(); return "map{%s}" % ", ".join(args(r))
@op(0x40)
def _endmapconst(r): return END
@op(0x41)
def _vec3f(r): return "Vec3f(%s,%s,%s)" % (F(r.f32()), F(r.f32()), F(r.f32()))
@op(0x42)
def _structmember(r):
    m = nm(r.ptr()); return "%s.%s" % (expr(r), m)
@op(0x43)
def _letmc(r):
    var = expr(r); return "%s = %s" % (var, expr(r))
@op(0x44)
def _letdel(r):
    var = expr(r); return "%s = %s" % (var, expr(r))
@op(0x45)
def _localvirtual(r):
    n = r.name(); return call(n, args(r))
@op(0x46)
def _localfinal(r):
    f = nm(r.ptr()); return call(f, args(r))
@op(0x48)
def _localout(r): return nm(r.ptr())
@op(0x4B)
def _instdelegate(r): return "delegate(%s)" % r.name()
@op(0x4C)
def _pushflow(r): return "push_flow L%d" % r.u32()
@op(0x4D)
def _popflow(r): return "pop_flow"
@op(0x4E)
def _computedjump(r): return "goto_computed %s" % expr(r)
@op(0x4F)
def _popflowifnot(r): return "if not (%s) pop_flow" % expr(r)
@op(0x50)
def _breakpoint(r): return "breakpoint"
@op(0x51)
def _ifacectx(r): return "iface(%s)" % expr(r)
@op(0x52)
def _objtoiface(r):
    cls = nm(r.ptr()); return "ToInterface<%s>(%s)" % (cls, expr(r))
@op(0x53)
def _eos(r): return "end_of_script"
@op(0x54)
def _crossiface(r):
    cls = nm(r.ptr()); return "CrossInterface<%s>(%s)" % (cls, expr(r))
@op(0x55)
def _ifacetoobj(r):
    cls = nm(r.ptr()); return "ToObject<%s>(%s)" % (cls, expr(r))
@op(0x5A)
def _wiretp(r): return "tracepoint"
@op(0x5B)
def _skipoffset(r): return "L%d" % r.u32()
@op(0x5C)
def _addmc(r):
    d = expr(r); return "%s += %s" % (d, expr(r))
@op(0x5D)
def _clearmc(r): return "%s.Clear()" % expr(r)
@op(0x5E)
def _tracepoint(r): return "tracepoint"
@op(0x5F)
def _letobj(r):
    var = expr(r); return "%s = %s" % (var, expr(r))
@op(0x60)
def _letweak(r):
    var = expr(r); return "%s = %s" % (var, expr(r))
@op(0x61)
def _binddel(r):
    n = r.name(); d = expr(r); o = expr(r); return "%s.Bind(%s, %s)" % (d, o, n)
@op(0x62)
def _removemc(r):
    d = expr(r); return "%s -= %s" % (d, expr(r))
@op(0x63)
def _callmc(r):
    f = nm(r.ptr()); d = expr(r); return call(f, args(r), d)
@op(0x64)
def _letpersistent(r):
    p = nm(r.ptr()); return "frame.%s = %s" % (p, expr(r))
@op(0x65)
def _arrayconst(r):
    r.ptr(); r.i32(); return "[%s]" % ", ".join(args(r))
@op(0x66)
def _endarrayconst(r): return END
@op(0x67)
def _softobj(r): return "SoftObject(%s)" % expr(r)
@op(0x68)
def _callmath(r):
    f = nm(r.ptr()); return call(f, args(r))
@op(0x69)
def _switch(r):
    n = r.u16(); r.u32(); idx = expr(r)
    cases = []
    for _ in range(n):
        k = expr(r); r.u32(); v = expr(r); cases.append("%s: %s" % (k, v))
    d = expr(r)
    return "switch(%s){%s, default: %s}" % (idx, "; ".join(cases), d)
@op(0x6A)
def _instr(r):
    kind = r.u8()
    if kind == 4:
        r.name()
    return "instrumentation"
@op(0x6B)
def _arraygetbyref(r):
    a = expr(r); return "%s[%s]" % (a, expr(r))
@op(0x6C)
def _sparse(r): return "sparse." + nm(r.ptr())
@op(0x6D)
def _fieldpath(r): return "fieldpath(%s)" % expr(r)


def statements(script):
    """Yield (offset, text) for each top-level statement."""
    r = R(script)
    while r.i < len(r.b):
        start = r.i
        try:
            s = expr(r)
        except Exception as e:  # keep going: report and stop this function
            yield start, "!! decode error: %s" % e
            return
        if s is END:
            continue
        yield start, s
        if s == "end_of_script":
            return


def dump_function(objs, cls, fn):
    f = objs.get(cls + ":" + fn)
    if not f:
        print("// no function", fn)
        return
    script = base64.b64decode(f.get("script") or "")
    params = [p["name"] for p in (f.get("properties") or []) if "CPF_Parm" in p.get("flags", "")]
    print("function %s(%s)  // %d bytes" % (fn, ", ".join(params), len(script)))
    targets = set()
    lines = list(statements(script))
    for _, s in lines:
        for tok in s.replace("(", " ").replace(")", " ").split():
            if tok.startswith("L") and tok[1:].isdigit():
                targets.add(int(tok[1:]))
    every = os.environ.get("KISMET_ALL_OFFSETS") == "1"  # label every statement (ubergraph entry points are not jump targets)
    for off, s in lines:
        label = ("L%d:" % off) if (every or off in targets) else ""
        print("  %-8s %s" % (label, s))
    print()


def load_names(jmap_path):
    """names.tsv (ComparisonIndex<TAB>string) sits next to the .jmap, produced in game by HSDump's ResolveNames."""
    import os
    path = os.path.join(os.path.dirname(os.path.abspath(jmap_path)), "names.tsv")
    if os.path.exists(path):
        for line in open(path, encoding="utf-8", errors="replace"):
            parts = line.rstrip("\n").split("\t", 1)
            if len(parts) == 2 and parts[0].isdigit():
                NAMES[int(parts[0])] = parts[1]


def main():
    load_names(sys.argv[1])
    objs = load(sys.argv[1])
    cls = sys.argv[2]
    fns = sys.argv[3:]
    if fns == ["--all"]:
        fns = sorted(k.split(":", 1)[1] for k, v in objs.items()
                     if isinstance(v, dict) and v.get("type") == "Function" and k.startswith(cls + ":"))
    for fn in fns:
        dump_function(objs, cls, fn)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
