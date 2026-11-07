#!/usr/bin/env bash
# Source-level debugging smoke test: compile tests/debuginfo/app.cs with -g and drive gdb in batch
# mode — breakpoint by C# file:line, locals/arguments, strings via tools/gdb/csharpc.py, fields
# through `this`, C# names in backtraces (also at -O2, through inlined frames). The same checks run on a
# program and library compiled from their assemblies (the IL front-end), whose lines, parameters and locals
# come from the portable PDBs (DebugApp's beside it, DebugLib's embedded).
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
command -v gdb > /dev/null || { echo "SKIP: gdb not installed"; exit 0; }
"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "build failed"; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll")
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
pass=0; fail=0

check() { # name, output file, expected substring...
  local name="$1" file="$2"; shift 2
  local ok=1
  for want in "$@"; do
    if ! grep -qF -- "$want" "$file"; then echo "FAIL $name: missing '$want'"; ok=0; fi
  done
  if [[ $ok == 1 ]]; then echo "PASS $name"; pass=$((pass + 1)); else fail=$((fail + 1)); sed 's/^/     /' "$file" | head -30; fi
}

run_gdb() { # binary, script → output
  gdb -batch -nx -x "$2" "$1" 2>&1 | grep -v "^\[\|warning: Error disabling\|libthread_db" > "$3"
}

APP="$ROOT/tests/debuginfo/app.cs"
"${CSHARPC[@]}" -g -O0 "$APP" -o "$OUT/app0" || { echo "FAIL compile -O0 -g"; exit 1; }
"${CSHARPC[@]}" -g -O2 "$APP" -o "$OUT/app2" || { echo "FAIL compile -O2 -g"; exit 1; }

cat > "$OUT/locals.gdb" <<GDB
set pagination off
source $ROOT/tools/gdb/csharpc.py
break app.cs:10
run
bt
print n
up
info locals
print shape->Radius
delete
break app.cs:6
continue
print label
print total
GDB
run_gdb "$OUT/app0" "$OUT/locals.gdb" "$OUT/locals.out"
check "-O0 breakpoint, args, backtrace" "$OUT/locals.out" \
  "Breakpoint 1, Program.<Main>\$.Fib(int) (n=6) at app.cs:10" \
  "in Program.<Main>\$(string [] *)" "\$1 = 6" "i = 1" "total = 0" "\$2 = 2.5"
check "-O0 strings via pretty-printer" "$OUT/locals.out" '= "fib total"' '= 42'

cat > "$OUT/this.gdb" <<GDB
set pagination off
break app.cs:17
run
print *this
print this->Radius
next
GDB
run_gdb "$OUT/app0" "$OUT/this.gdb" "$OUT/this.out"
check "-O0 instance method and this" "$OUT/this.out" "Circle.Area(void) (this=0x" "Radius = 2.5}" "= 2.5"

cat > "$OUT/opt.gdb" <<GDB
set pagination off
break app.cs:11
run
bt
GDB
run_gdb "$OUT/app2" "$OUT/opt.gdb" "$OUT/opt.out"
check "-O2 line table and inlined frames" "$OUT/opt.out" "Program.<Main>\$.Fib(int) (n=6) at app.cs:11" "Program.<Main>\$ (args=" "app.cs:4"

# The program as IL (csharpc app.dll -g): sequence points, parameters and locals from the assemblies' PDBs.
ILDIR="$OUT/il"
mkdir -p "$ILDIR"
cp -r "$ROOT/tests/debuginfo/il/DebugLib" "$ROOT/tests/debuginfo/il/DebugApp" "$ILDIR/"
if "$DOTNET" build "$ILDIR/DebugApp/DebugApp.csproj" -c Debug -o "$ILDIR/out" -v quiet -nologo > "$OUT/il-build.log" 2>&1 \
   && "${CSHARPC[@]}" -g -O0 "$ILDIR/out/DebugApp.dll" -o "$ILDIR/app0" > "$OUT/il-compile.log" 2>&1 \
   && "${CSHARPC[@]}" -g -O2 "$ILDIR/out/DebugApp.dll" -o "$ILDIR/app2" >> "$OUT/il-compile.log" 2>&1; then
  "$ILDIR/app0" > "$OUT/il-run.out" 2>&1
  check "IL -O0 -g binary runs" "$OUT/il-run.out" "sum=21"

  cat > "$OUT/il-locals.gdb" <<GDB
set pagination off
source $ROOT/tools/gdb/csharpc.py
break Counter.cs:10
run
bt
print i
print times
print *this
up
info locals
print name
print total
GDB
  run_gdb "$ILDIR/app0" "$OUT/il-locals.gdb" "$OUT/il-locals.out"
  check "IL -O0 line breakpoint in an embedded-PDB library, parameters, locals, this" "$OUT/il-locals.out" \
    "Breakpoint 1, DebugLib.Counter.Add(int) (this=0x" "times=3" "Counter.cs:10" \
    "DebugLib.Counter.Describe(" "Counter.cs:16" "Program.cs:9" "Program.cs:4" "\$1 = 0" "\$2 = 3" "step = 7"
  check "IL -O0 strings via pretty-printer" "$OUT/il-locals.out" '= "sum"'

  cat > "$OUT/il-main.gdb" <<GDB
set pagination off
break Program.cs:4
run
info locals
next
next
GDB
  run_gdb "$ILDIR/app0" "$OUT/il-main.gdb" "$OUT/il-main.out"
  check "IL -O0 program lines and top-level locals (portable PDB beside the assembly)" "$OUT/il-main.out" \
    "Program.cs:4" "counter = 0x" "sum=21"

  cat > "$OUT/il-opt.gdb" <<GDB
set pagination off
break Counter.cs:17
run
bt
GDB
  run_gdb "$ILDIR/app2" "$OUT/il-opt.gdb" "$OUT/il-opt.out"
  check "IL -O2 line table and inlined frames" "$OUT/il-opt.out" "DebugLib.Counter.Describe" "Counter.cs:17" "Program.cs:9" "Program.cs:4"

  # An assembly without a PDB still compiles with -g (no lines for it, no crash).
  rm -f "$ILDIR/out/DebugApp.pdb"
  if "${CSHARPC[@]}" -g -O0 "$ILDIR/out/DebugApp.dll" -o "$ILDIR/nopdb" > "$OUT/il-nopdb.log" 2>&1; then
    "$ILDIR/nopdb" > "$OUT/il-nopdb.out" 2>&1
    check "IL -g without the program's PDB" "$OUT/il-nopdb.out" "sum=21"
  else echo "FAIL IL -g without a PDB: compile"; sed 's/^/     /' "$OUT/il-nopdb.log" | head; fail=$((fail + 1)); fi
else
  echo "FAIL IL -g: build or compile"; sed 's/^/     /' "$OUT/il-build.log" "$OUT/il-compile.log" 2>/dev/null | head -30; fail=$((fail + 1))
fi

echo
echo "$pass passed, $fail failed"
[[ $fail == 0 ]]
