// SpinLock, ReaderWriterLockSlim and ThreadLocal<T> throughput, the program behind the speed numbers of docs/il-first/System.Threading.md
// (tools/il-first/measure-aot.sh): uncontended read and write sections, ThreadLocal<T>.Value, SpinLock sections. The output is checksums only.
using System;
using System.Threading;

var rw = new ReaderWriterLockSlim();
var tl = new ThreadLocal<int>(() => 1);
var sl = new SpinLock(false);
long sum = 0;
for (int i = 0; i < 20_000_000; i++) { rw.EnterReadLock(); sum++; rw.ExitReadLock(); }
for (int i = 0; i < 5_000_000; i++) { rw.EnterWriteLock(); sum++; rw.ExitWriteLock(); }
for (int i = 0; i < 50_000_000; i++) sum += tl.Value;
for (int i = 0; i < 20_000_000; i++) { bool taken = false; sl.Enter(ref taken); sum++; sl.Exit(); }
Console.WriteLine(sum);
