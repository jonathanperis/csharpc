// SpinWait, CountdownEvent and Barrier as the framework implements them: SpinWait's count and yield threshold, SpinUntil;
// CountdownEvent's counts, Signal/AddCount/TryAddCount/Reset and their exceptions; Barrier's phases across threads, the
// post-phase action and BarrierPostPhaseException, participants added and removed, and the argument and state checks.
using System;
using System.Threading;

static class Program
{
    static void Main()
    {
        var spinner = new SpinWait();
        var steps = new System.Text.StringBuilder();
        for (int i = 0; i < 12; i++) { steps.Append(spinner.NextSpinWillYield ? 'Y' : 's'); spinner.SpinOnce(); }
        Console.WriteLine($"SpinWait: {steps} count {spinner.Count}");
        spinner.Reset();
        Console.WriteLine($"after Reset: count {spinner.Count}, next yields {spinner.NextSpinWillYield}");
        spinner.SpinOnce(sleep1Threshold: -1);
        Console.WriteLine($"SpinOnce(-1): count {spinner.Count}");
        try { spinner.SpinOnce(-2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"SpinOnce(-2): {e.ParamName}"); }
        SpinWait.SpinUntil(() => true);
        Console.WriteLine($"SpinUntil(true) returned, SpinUntil(false, 0): {SpinWait.SpinUntil(() => false, 0)}, SpinUntil(false, 10): {SpinWait.SpinUntil(() => false, 10)}");
        try { SpinWait.SpinUntil(null); } catch (ArgumentNullException e) { Console.WriteLine($"SpinUntil(null): {e.ParamName}"); }
        try { SpinWait.SpinUntil(() => true, -2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"SpinUntil(-2): {e.Message}"); }

        using (var countdown = new CountdownEvent(3))
        {
            Console.WriteLine($"countdown: initial {countdown.InitialCount}, current {countdown.CurrentCount}, set {countdown.IsSet}");
            Console.WriteLine($"Signal(): {countdown.Signal()}, current {countdown.CurrentCount}");
            countdown.AddCount(2);
            Console.WriteLine($"AddCount(2): current {countdown.CurrentCount}, TryAddCount {countdown.TryAddCount()}, current {countdown.CurrentCount}");
            Console.WriteLine($"Signal(5): {countdown.Signal(5)}, set {countdown.IsSet}, Wait(0) {countdown.Wait(0)}");
            try { countdown.Signal(); } catch (InvalidOperationException e) { Console.WriteLine($"Signal at zero: {e.Message}"); }
            try { countdown.AddCount(); } catch (InvalidOperationException e) { Console.WriteLine($"AddCount when set: {e.Message}"); }
            Console.WriteLine($"TryAddCount when set: {countdown.TryAddCount()}");
            countdown.Reset(2);
            Console.WriteLine($"Reset(2): initial {countdown.InitialCount}, current {countdown.CurrentCount}, Wait(10) {countdown.Wait(10)}");
            var signaler = new Thread(() => { countdown.Signal(); countdown.Signal(); });
            signaler.Start();
            countdown.Wait();
            signaler.Join();
            Console.WriteLine($"waited for another thread's signals: set {countdown.IsSet}");
            try { countdown.Signal(0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"Signal(0): {e.ParamName}"); }
            try { new CountdownEvent(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"new CountdownEvent(-1): {e.ParamName}"); }
            try { countdown.Wait(new CancellationToken(true)); } catch (OperationCanceledException) { Console.WriteLine("Wait(canceled token): OperationCanceledException"); }
        }

        int phasesSeen = 0;
        using (var barrier = new Barrier(3, b => phasesSeen++))
        {
            Console.WriteLine($"barrier: participants {barrier.ParticipantCount}, remaining {barrier.ParticipantsRemaining}, phase {barrier.CurrentPhaseNumber}");
            var sums = new long[3];
            var workers = new Thread[3];
            for (int w = 0; w < 3; w++)
            {
                int id = w;
                workers[w] = new Thread(() => { for (int phase = 0; phase < 4; phase++) { sums[id] += phase; barrier.SignalAndWait(); } });
                workers[w].Start();
            }
            foreach (var t in workers) t.Join();
            Console.WriteLine($"after 4 phases: phase {barrier.CurrentPhaseNumber}, post-phase actions {phasesSeen}, sums {sums[0]} {sums[1]} {sums[2]}");
            Console.WriteLine($"AddParticipant -> phase {barrier.AddParticipant()}, participants {barrier.ParticipantCount}");
            barrier.RemoveParticipants(2);
            Console.WriteLine($"RemoveParticipants(2): participants {barrier.ParticipantCount}");
            try { barrier.RemoveParticipants(5); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"RemoveParticipants(5): {e.ParamName}"); }
            Console.WriteLine($"SignalAndWait(0) alone: {barrier.SignalAndWait(0)}, phase {barrier.CurrentPhaseNumber}");
        }

        using (var failing = new Barrier(1, _ => throw new InvalidOperationException("post-phase failed")))
        {
            try { failing.SignalAndWait(); }
            catch (BarrierPostPhaseException e) { Console.WriteLine($"post-phase exception: {e.GetType().Name}: {e.Message} / inner {e.InnerException?.Message}"); }
            Console.WriteLine($"phase after a failed action: {failing.CurrentPhaseNumber}");
        }
        try { new Barrier(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"new Barrier(-1): {e.ParamName}"); }
        var disposed = new Barrier(1);
        disposed.Dispose();
        try { disposed.SignalAndWait(); } catch (ObjectDisposedException e) { Console.WriteLine($"disposed: {e.Message}"); }
    }
}
