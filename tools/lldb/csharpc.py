# lldb formatters for programs compiled by csharpc -g.
#   (lldb) command script import tools/lldb/csharpc.py
import lldb


def string_summary(valobj, internal_dict):
    if valobj.GetValueAsUnsigned(0) == 0:
        return "null"
    obj = valobj.Dereference()
    length = obj.GetChildMemberWithName("Length").GetValueAsUnsigned(0)
    chars = obj.GetChildMemberWithName("chars").GetLoadAddress()
    error = lldb.SBError()
    data = valobj.GetProcess().ReadMemory(chars, 2 * min(length, 4096), error)
    if not error.Success():
        return "<unreadable>"
    return '"' + data.decode("utf-16-le", errors="replace") + '"'


def __lldb_init_module(debugger, internal_dict):
    debugger.HandleCommand('type summary add -F csharpc.string_summary "System.String *" "string"')
