// Event sourcing / CQRS: immutable records, an event store, aggregates rebuilt from events,
// projections subscribed through C# events, commands validated with exceptions, snapshots via `with`.
var store = new EventStore();
var orders = new Projection();
var revenue = new RevenueProjection();
store.Appended += orders.Apply;
store.Appended += revenue.Apply;
store.Appended += e => { if (e is OrderCancelled c) Console.WriteLine($"  [audit] order {c.OrderId} cancelled: {c.Reason}"); };

var handler = new CommandHandler(store);
var clock = new DateTime(2026, 5, 1, 9, 0, 0);
Command[] commands =
[
    new PlaceOrder("o-1", "alice", clock),
    new AddLine("o-1", "widget", 3, 9.99m),
    new AddLine("o-1", "gadget", 1, 24.50m),
    new PlaceOrder("o-2", "bob", clock.AddMinutes(5)),
    new AddLine("o-2", "widget", 10, 9.49m),
    new SubmitOrder("o-1"),
    new AddLine("o-1", "late item", 1, 1m),       // rejected: already submitted
    new PlaceOrder("o-3", "carol", clock.AddHours(1)),
    new SubmitOrder("o-3"),                        // rejected: empty order
    new AddLine("o-3", "doohickey", 2, 99.95m),
    new SubmitOrder("o-2"),
    new CancelOrder("o-2", "customer changed mind"),
    new SubmitOrder("o-3"),
    new ShipOrder("o-1", "TRACK-001"),
    new CancelOrder("o-1", "too late"),            // rejected: shipped
    new ShipOrder("o-9", "X"),                     // rejected: unknown
];

foreach (var command in commands)
{
    try
    {
        var produced = handler.Handle(command);
        Console.WriteLine($"{command.GetType().Name,-12} {command.OrderId}: {produced} event(s)");
    }
    catch (DomainException e)
    {
        Console.WriteLine($"{command.GetType().Name,-12} {command.OrderId}: REJECTED ({e.Message})");
    }
}

Console.WriteLine($"\nevent stream ({store.Count} events, version by order):");
foreach (var (id, version) in store.Versions.OrderBy(kv => kv.Key)) Console.WriteLine($"  {id}: v{version}");
Console.WriteLine("\nread model:");
foreach (var row in orders.Rows.Values.OrderBy(r => r.Id))
    Console.WriteLine($"  {row.Id} {row.Customer,-6} {row.Status,-10} lines={row.Lines} total={row.Total,8:F2}");
Console.WriteLine($"\nrevenue: shipped {revenue.Shipped:F2}, pending {revenue.Pending:F2}, lost {revenue.Lost:F2}");

// Rebuild an aggregate purely from its event history and compare with the live projection.
var replayed = Order.Replay(store.Stream("o-1"));
Console.WriteLine($"replayed o-1: {replayed}");
var snapshot = replayed with { Lines = [] };
Console.WriteLine($"snapshot without lines: {snapshot.Lines.Count} lines, same id: {snapshot.Id == replayed.Id}, equal: {snapshot == replayed}");

// ---------------------------------------------------------------- events & commands
abstract record Event(string OrderId, DateTime At);
record OrderPlaced(string OrderId, string Customer, DateTime At) : Event(OrderId, At);
record LineAdded(string OrderId, string Product, int Quantity, decimal UnitPrice, DateTime At) : Event(OrderId, At);
record OrderSubmitted(string OrderId, decimal Total, DateTime At) : Event(OrderId, At);
record OrderShipped(string OrderId, string Tracking, DateTime At) : Event(OrderId, At);
record OrderCancelled(string OrderId, string Reason, DateTime At) : Event(OrderId, At);

abstract record Command(string OrderId);
record PlaceOrder(string OrderId, string Customer, DateTime At) : Command(OrderId);
record AddLine(string OrderId, string Product, int Quantity, decimal UnitPrice) : Command(OrderId);
record SubmitOrder(string OrderId) : Command(OrderId);
record ShipOrder(string OrderId, string Tracking) : Command(OrderId);
record CancelOrder(string OrderId, string Reason) : Command(OrderId);

class DomainException(string message) : Exception(message);

enum OrderStatus { Draft, Submitted, Shipped, Cancelled }

record OrderLine(string Product, int Quantity, decimal UnitPrice) { public decimal Total => Quantity * UnitPrice; }

// ---------------------------------------------------------------- aggregate
record Order(string Id, string Customer, OrderStatus Status, List<OrderLine> Lines, int Version)
{
    public decimal Total => Lines.Sum(l => l.Total);

    public static Order Replay(IEnumerable<Event> history)
    {
        Order? order = null;
        foreach (var e in history) order = Apply(order, e);
        return order ?? throw new DomainException("no history");
    }

    public static Order Apply(Order? order, Event e) => (order, e) switch
    {
        (null, OrderPlaced p) => new Order(p.OrderId, p.Customer, OrderStatus.Draft, [], 1),
        ({ } o, LineAdded l) => o with { Lines = [.. o.Lines, new OrderLine(l.Product, l.Quantity, l.UnitPrice)], Version = o.Version + 1 },
        ({ } o, OrderSubmitted) => o with { Status = OrderStatus.Submitted, Version = o.Version + 1 },
        ({ } o, OrderShipped) => o with { Status = OrderStatus.Shipped, Version = o.Version + 1 },
        ({ } o, OrderCancelled) => o with { Status = OrderStatus.Cancelled, Version = o.Version + 1 },
        _ => throw new DomainException($"cannot apply {e.GetType().Name}"),
    };

    public override string ToString() => $"{Id} by {Customer}: {Status}, {Lines.Count} lines, {Total:F2}, v{Version}";
}

// ---------------------------------------------------------------- infrastructure
class EventStore
{
    private readonly List<Event> _events = [];
    public event Action<Event>? Appended;
    public int Count => _events.Count;
    public Dictionary<string, int> Versions { get; } = [];

    public IEnumerable<Event> Stream(string id) => _events.Where(e => e.OrderId == id);

    public void Append(Event e, int expectedVersion)
    {
        var current = Versions.GetValueOrDefault(e.OrderId);
        if (current != expectedVersion) throw new DomainException($"concurrency conflict on {e.OrderId}: expected v{expectedVersion}, was v{current}");
        _events.Add(e);
        Versions[e.OrderId] = current + 1;
        Appended?.Invoke(e);
    }
}

class CommandHandler(EventStore store)
{
    private DateTime _now = new(2026, 5, 1, 12, 0, 0);

    public int Handle(Command command)
    {
        _now = _now.AddMinutes(7);
        var order = store.Versions.ContainsKey(command.OrderId) ? Order.Replay(store.Stream(command.OrderId)) : null;
        var events = Decide(order, command).ToList();
        var version = order?.Version ?? 0;
        foreach (var e in events) store.Append(e, version++);
        return events.Count;
    }

    private IEnumerable<Event> Decide(Order? order, Command command)
    {
        switch (command)
        {
            case PlaceOrder p:
                if (order is not null) throw new DomainException("order already exists");
                yield return new OrderPlaced(p.OrderId, p.Customer, p.At);
                break;
            case AddLine l:
                Require(order).Status.MustBe(OrderStatus.Draft, "only draft orders can change");
                if (l.Quantity <= 0) throw new DomainException("quantity must be positive");
                yield return new LineAdded(l.OrderId, l.Product, l.Quantity, l.UnitPrice, _now);
                break;
            case SubmitOrder s:
                var o = Require(order);
                o.Status.MustBe(OrderStatus.Draft, "already submitted");
                if (o.Lines.Count == 0) throw new DomainException("cannot submit an empty order");
                yield return new OrderSubmitted(s.OrderId, o.Total, _now);
                break;
            case ShipOrder sh:
                Require(order).Status.MustBe(OrderStatus.Submitted, "order is not ready to ship");
                yield return new OrderShipped(sh.OrderId, sh.Tracking, _now);
                break;
            case CancelOrder c:
                var co = Require(order);
                if (co.Status is OrderStatus.Shipped or OrderStatus.Cancelled) throw new DomainException($"cannot cancel a {co.Status.ToString().ToLower()} order");
                yield return new OrderCancelled(c.OrderId, c.Reason, _now);
                break;
        }
    }

    private static Order Require(Order? order) => order ?? throw new DomainException("unknown order");
}

static class StatusRules
{
    public static void MustBe(this OrderStatus actual, OrderStatus expected, string message)
    {
        if (actual != expected) throw new DomainException(message);
    }
}

// ---------------------------------------------------------------- projections
class Projection
{
    public sealed record Row(string Id, string Customer, OrderStatus Status, int Lines, decimal Total);
    public Dictionary<string, Row> Rows { get; } = [];

    public void Apply(Event e)
    {
        Rows[e.OrderId] = e switch
        {
            OrderPlaced p => new Row(p.OrderId, p.Customer, OrderStatus.Draft, 0, 0m),
            LineAdded l => Rows[l.OrderId] with { Lines = Rows[l.OrderId].Lines + 1, Total = Rows[l.OrderId].Total + l.Quantity * l.UnitPrice },
            OrderSubmitted => Rows[e.OrderId] with { Status = OrderStatus.Submitted },
            OrderShipped => Rows[e.OrderId] with { Status = OrderStatus.Shipped },
            OrderCancelled => Rows[e.OrderId] with { Status = OrderStatus.Cancelled },
            _ => Rows[e.OrderId],
        };
    }
}

class RevenueProjection
{
    private readonly Dictionary<string, decimal> _submitted = [];
    public decimal Shipped { get; private set; }
    public decimal Pending => _submitted.Values.Sum();
    public decimal Lost { get; private set; }

    public void Apply(Event e)
    {
        switch (e)
        {
            case OrderSubmitted s: _submitted[s.OrderId] = s.Total; break;
            case OrderShipped sh when _submitted.Remove(sh.OrderId, out var total): Shipped += total; break;
            case OrderCancelled c when _submitted.Remove(c.OrderId, out var lost): Lost += lost; break;
        }
    }
}
