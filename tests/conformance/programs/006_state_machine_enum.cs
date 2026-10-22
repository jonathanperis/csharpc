var s = State.Idle;
foreach (var ev in new[] { "start", "pause", "start", "stop" })
{
    s = (s, ev) switch
    {
        (State.Idle, "start") => State.Running,
        (State.Running, "pause") => State.Paused,
        (State.Paused, "start") => State.Running,
        (_, "stop") => State.Idle,
        _ => s,
    };
    Console.WriteLine($"{ev} -> {s}");
}
enum State { Idle, Running, Paused }
