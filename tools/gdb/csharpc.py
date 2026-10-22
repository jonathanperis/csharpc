# gdb pretty-printers for programs compiled by csharpc -g.
#   (gdb) source tools/gdb/csharpc.py
# or add `add-auto-load-safe-path` + `source` to ~/.gdbinit.
#
# csharpc describes runtime objects as pointers to structs mirroring csrt's layout
# (docs/11-debugging.md): System.String { __mt, Length, chars[] } and T[] { __mt, Length, data[] }.
import gdb

MAX_ELEMENTS = 64


def _struct_of(value):
    t = value.type.strip_typedefs()
    if t.code != gdb.TYPE_CODE_PTR:
        return None
    target = t.target().strip_typedefs()
    return target if target.code == gdb.TYPE_CODE_STRUCT else None


def _utf16(ptr, length):
    data = bytes(gdb.selected_inferior().read_memory(ptr, 2 * length))
    return data.decode("utf-16-le", errors="replace")


class StringPrinter:
    def __init__(self, value):
        self.value = value

    def to_string(self):
        if int(self.value) == 0:
            return "null"
        obj = self.value.dereference()
        length = int(obj["Length"])
        chars = obj["chars"].address.cast(gdb.lookup_type("char").pointer())
        text = _utf16(int(chars), min(length, 4096))
        escaped = text.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n").replace("\t", "\\t")
        return '"' + escaped + '"' + ("..." if length > 4096 else "")

    def display_hint(self):
        return None


class ArrayPrinter:
    def __init__(self, value):
        self.value = value

    def to_string(self):
        if int(self.value) == 0:
            return "null"
        length = int(self.value.dereference()["Length"])
        return "%s, Length = %d" % (self.value.type.strip_typedefs().target().name, length)

    def children(self):
        if int(self.value) == 0:
            return
        obj = self.value.dereference()
        length = int(obj["Length"])
        element = obj["data"].type.strip_typedefs().target()
        base = obj["data"].address.cast(element.pointer())
        for i in range(min(length, MAX_ELEMENTS)):
            yield "[%d]" % i, (base + i).dereference()

    def display_hint(self):
        return "array"


def lookup(value):
    target = _struct_of(value)
    if target is None or target.name is None:
        return None
    if target.name == "System.String":
        return StringPrinter(value)
    if target.name.endswith("[]"):
        return ArrayPrinter(value)
    return None


gdb.pretty_printers.append(lookup)
