// The repository's lines of source by responsibility, counted when the site is built, and a squarified treemap of them.
import fs from 'node:fs';
import path from 'node:path';

export type Leaf = { name: string; lines: number; files: number };
export type Area = { key: string; name: string; summary: string; leaves: Leaf[]; lines: number };

const repo = path.resolve(process.cwd(), '..');
const skipped = new Set(['bin', 'obj', 'node_modules', 'out', '.astro', '.git']);

function walk(dir: string, accept: (file: string) => boolean, out: string[] = []): string[] {
  if (!fs.existsSync(dir)) return out;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) { if (!skipped.has(entry.name)) walk(full, accept, out); }
    else if (accept(full)) out.push(full);
  }
  return out;
}

const lineCount = (file: string) => {
  const text = fs.readFileSync(file, 'utf8');
  return text.length === 0 ? 0 : text.split('\n').length - (text.endsWith('\n') ? 1 : 0);
};
const ext = (...exts: string[]) => (f: string) => exts.some((e) => f.endsWith(e));
const files = (rel: string, accept: (f: string) => boolean, deep = true) => {
  const dir = path.join(repo, rel);
  if (deep) return walk(dir, accept);
  return fs.existsSync(dir) ? fs.readdirSync(dir).map((n) => path.join(dir, n)).filter((f) => fs.statSync(f).isFile() && accept(f)) : [];
};

/** Files sorted into named groups by the first rule whose pattern matches the file name. */
function classify(list: string[], rules: [string, RegExp][], fallback: string): Leaf[] {
  const groups = new Map<string, Leaf>();
  for (const file of list) {
    const name = path.basename(file);
    const label = rules.find(([, pattern]) => pattern.test(name))?.[0] ?? fallback;
    const leaf = groups.get(label) ?? { name: label, lines: 0, files: 0 };
    leaf.lines += lineCount(file);
    leaf.files += 1;
    groups.set(label, leaf);
  }
  return [...groups.values()];
}

const leaf = (name: string, list: string[]): Leaf => ({ name, lines: list.reduce((s, f) => s + lineCount(f), 0), files: list.length });

export function measure(): Area[] {
  const cs = ext('.cs');
  const codegen = classify(files('src/Csharpc/CodeGen', cs), [
    ['IL front-end lowering', /(^|\.)Il[A-Z]|\.Il\.cs$|DynamicIl|IlOperations/],
    ['Reflection & metadata', /Reflection|TypeInfo|Attributes|AssemblyTypes|MemberRecords|NestedTypes|DynamicDependency/],
    ['C# dynamic', /Dynamic/],
    ['Generics & dispatch', /Generic|Interfaces|VTables|InterfaceStubs|Devirtual|ExactTypes|Dictionar/],
    ['Exceptions & stack traces', /Exception|Filters|StackTrace|DebugInfo/],
    ['Async & threading', /Async|Thread|Coroutine|Concurrent|Task|Barrier|Lock/],
    ['Interop & unsafe code', /PInvoke|NativeCall|Marshal|ReverseThunk|Unsafe|FunctionPointer|Callback/],
    ['Library mapping (intrinsics)', /Intrinsic|BclValueTypes|CoreLib|\.Bcl|Primitives|Simd|Int128|Decimal|Number|String|Console/],
    ['Types & layout', /Struct|TypeMapper|Record|Tuple|Enum|DisplayClass|Closure|Layout|Boxing|Mangler/],
    ['Optimization', /HeapToStack|Escape|Inline|Optimi/],
  ], 'Core lowering');
  const runtime = classify(files('src/runtime', ext('.c', '.h'), false), [
    ['Garbage collector', /gc|alloc|heap|memory|nursery|card/],
    ['Exceptions & unwinding', /eh|unwind|except|trace|signal/],
    ['Threads & async', /thread|async|task|sync|monitor|timer/],
    ['Interop & OS', /pinvoke|os|file|env|process|socket|dl/],
    ['Types & objects', /type|reflect|records|object|array|enum|bcl/],
    ['Strings & console', /string|console|format|number|utf|text|span/],
  ], 'Startup & helpers');
  runtime.push(leaf('ICU shim (from .NET)', files('src/runtime/globalization', ext('.c', '.h'))));
  const corelibDirs: Record<string, string> = {
    Crypto: 'Crypto & TLS', Net: 'Networking', IO: 'IO & OS contract', Reflection: 'Reflection', Tasks: 'Tasks & async',
    Globalization: 'Globalization contract', Text: 'Text', Threading: 'Threading', Serialization: 'Serialization', Numerics: 'Numerics',
  };
  const corelib: Leaf[] = [];
  const corelibRoot = path.join(repo, 'src/Csharpc/corelib');
  const otherDirs: string[] = [];
  for (const entry of fs.existsSync(corelibRoot) ? fs.readdirSync(corelibRoot, { withFileTypes: true }) : []) {
    if (!entry.isDirectory()) continue;
    const list = files(`src/Csharpc/corelib/${entry.name}`, cs);
    if (corelibDirs[entry.name]) corelib.push(leaf(corelibDirs[entry.name], list)); else otherDirs.push(...list);
  }
  corelib.push(...classify([...files('src/Csharpc/corelib', cs, false), ...otherDirs], [
    ['Collections & LINQ', /List|Dictionary|Collection|Set|Queue|Stack|Array|Linq|Enumerable|Span|Memory|Hashtable/],
    ['Strings & text', /String|Text|Char|Rune|Utf|Encoding|Regex|Interpolat|Format|Parse|Json/],
    ['Numbers, dates & math', /Number|Int|Decimal|Double|Float|Math|Date|Time|Guid|Version|Half|Convert|BigInteger|Random/],
    ['Runtime support', /Reflect|Type|Attribute|Runtime|Core|Upstream|Shadow|Environment|AppContext|GC|Activator|Resource/],
    ['Exceptions & diagnostics', /Exception|Stack|Diagnostic|Trace|Debug|Event/],
    ['Threading & tasks', /Thread|Task|Lock|Monitor|Timer|Cancel|Lazy|Async|Interlocked|Wait|Semaphore|Mutex/],
  ], 'Other library members'));
  const merged = new Map<string, Leaf>();
  for (const l of corelib) {
    const key = l.name === 'Threading & tasks' ? 'Threading' : l.name;
    const m = merged.get(key) ?? { name: key, lines: 0, files: 0 };
    m.lines += l.lines; m.files += l.files; merged.set(key, m);
  }

  const areas: Omit<Area, 'lines'>[] = [
    { key: 'codegen', name: 'Code generation', summary: 'Roslyn CFG and IL to LLVM IR: one partial per feature area', leaves: codegen },
    { key: 'frontend', name: 'Front ends & driver', summary: 'C# sources, projects and IL in; linking, LLVM and targets out', leaves: [
      leaf('Roslyn front end', files('src/Csharpc/Frontend', cs, false)),
      leaf('IL front end', files('src/Csharpc/Frontend/Il', cs)),
      leaf('Driver & toolchain', files('src/Csharpc/Driver', cs)),
      leaf('LLVM bindings', files('src/Csharpc/Llvm', cs)),
      leaf('WebAssembly passes', files('src/Csharpc/Wasm', cs)),
      leaf('MSBuild SDK', files('src/Csharpc.Sdk', ext('.cs', '.props', '.targets', '.csproj'))),
      leaf('Types served from .NET IL (lists)', files('src/Csharpc/upstream-types', ext('.txt'))),
    ] },
    { key: 'runtime', name: 'C runtime (csrt)', summary: 'GC, exception dispatch, threads and OS boundaries in C', leaves: runtime },
    { key: 'corelib', name: 'Corelib', summary: 'Library code in C#: BCL types csharpc provides and the contracts the framework IL calls', leaves: [...merged.values()] },
    { key: 'tests', name: 'Tests', summary: 'Programs whose expected output is recorded from real .NET', leaves: [
      leaf('Differential samples', files('tests/samples', cs)),
      leaf('Conformance corpora', [...files('tests/conformance', cs), ...files('tests/conformance2', cs)]),
      leaf('Projects & IL', files('tests/projects', cs)),
      leaf('Real-world programs', files('tests/programs', cs)),
      leaf('Test scripts', files('tests', ext('.sh'), false)),
    ] },
    { key: 'support', name: 'Tools, docs & CI', summary: 'The slicer, fuzzers, bootstrap, benchmarks, these docs and the workflows', leaves: [
      leaf('Documentation', files('docs', ext('.md'))),
      leaf('Tools', files('tools', ext('.cs', '.sh', '.py'))),
      leaf('Benchmarks', files('benchmarks', ext('.cs', '.sh'))),
      leaf('CI workflows', files('.github', ext('.yml'))),
    ] },
  ];
  return areas.map((a) => {
    const leaves = a.leaves.filter((l) => l.lines > 0).sort((x, y) => y.lines - x.lines);
    return { ...a, leaves, lines: leaves.reduce((s, l) => s + l.lines, 0) };
  });
}

export type Rect = { x: number; y: number; w: number; h: number };

/** Squarified treemap (Bruls, Huizing, van Wijk): values laid out in rows that keep each rectangle close to square. */
export function squarify<T>(items: T[], value: (t: T) => number, rect: Rect): (T & Rect)[] {
  const total = items.reduce((s, t) => s + value(t), 0);
  if (total <= 0) return [];
  const scale = (rect.w * rect.h) / total;
  const queue = items.map((t) => ({ t, area: value(t) * scale })).sort((a, b) => b.area - a.area);
  const out: (T & Rect)[] = [];
  let { x, y, w, h } = rect;
  const worst = (row: number[], side: number) => {
    const sum = row.reduce((s, a) => s + a, 0);
    return Math.max(...row.map((a) => Math.max((side * side * a) / (sum * sum), (sum * sum) / (side * side * a))));
  };
  while (queue.length) {
    const side = Math.min(w, h);
    const row = [queue.shift()!];
    while (queue.length && worst([...row.map((r) => r.area), queue[0].area], side) <= worst(row.map((r) => r.area), side)) row.push(queue.shift()!);
    const sum = row.reduce((s, r) => s + r.area, 0);
    const thickness = sum / side;
    let offset = 0;
    for (const r of row) {
      const length = r.area / thickness;
      out.push(w >= h ? { ...r.t, x, y: y + offset, w: thickness, h: length } : { ...r.t, x: x + offset, y, w: length, h: thickness });
      offset += length;
    }
    if (w >= h) { x += thickness; w -= thickness; } else { y += thickness; h -= thickness; }
  }
  return out;
}
