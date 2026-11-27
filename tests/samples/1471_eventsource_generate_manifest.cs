// EventSource.GenerateManifest: the ETW manifest .NET builds by reflection over an EventSource class (its [Event]
// methods, the nested Keywords, Tasks and Opcodes classes, enum parameters as value maps, messages turned into
// templates, the string table), OnlyIfNeededForRegistration, a broken source in lenient and Strict mode; then
// EventSource.SendCommand reaching OnEventCommand with its arguments, and GetSources.
#:property EventSourceSupport=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;

CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
var manifest = EventSource.GenerateManifest(typeof(ShopSource), "shop.dll");
Console.WriteLine(manifest);
Console.WriteLine(EventSource.GenerateManifest(typeof(ShopSource), "shop.dll", EventManifestOptions.OnlyIfNeededForRegistration) is { } needed ? needed == manifest : "(null)");
foreach (var options in new[] { EventManifestOptions.None, EventManifestOptions.Strict })
{
    try { Console.WriteLine(EventSource.GenerateManifest(typeof(BrokenSource), "broken.dll", options)?.Length > 0); }
    catch (Exception e) { Console.WriteLine($"{options}: {e.GetType().Name}: {e.Message}"); }
}
try { EventSource.GenerateManifest(null!, "x.dll"); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

var shop = ShopSource.Log;
EventSource.SendCommand(shop, (EventCommand)7, new Dictionary<string, string?> { ["mode"] = "fast", ["level"] = "3" });
EventSource.SendCommand(shop, EventCommand.SendManifest, null);
try { EventSource.SendCommand(shop, EventCommand.Update, null); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
Console.WriteLine(string.Join(", ", EventSource.GetSources().Where(s => s.Name.StartsWith("Demo-")).Select(s => $"{s.Name} {s.Guid}")));
Console.WriteLine(shop.IsEnabled());

enum Payment { Card = 1, Cash = 2, Voucher = 4 }

[Flags]
enum Flags { None = 0, Gift = 1, Express = 2 }

[EventSource(Name = "Demo-Shop")]
sealed class ShopSource : EventSource
{
    public static readonly ShopSource Log = new();

    public static class Keywords
    {
        public const EventKeywords Orders = (EventKeywords)1;
        public const EventKeywords Payments = (EventKeywords)2;
    }

    public static class Tasks
    {
        public const EventTask Checkout = (EventTask)1;
    }

    public static class Opcodes
    {
        public const EventOpcode Validate = (EventOpcode)11;
    }

    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Orders, Message = "Order {0} for {1} items")]
    public void OrderPlaced(string id, int items) => WriteEvent(1, id, items);

    [Event(2, Level = EventLevel.Warning, Keywords = Keywords.Payments, Task = Tasks.Checkout, Opcode = Opcodes.Validate)]
    public void PaymentChecked(Payment kind, Flags flags, double amount) => WriteEvent(2, kind, flags, amount);

    [Event(3, Level = EventLevel.Verbose, Version = 2, Channel = EventChannel.Debug)]
    public void CheckoutStart(long cart, bool member, Guid session) => WriteEvent(3, cart, member, session);

    [Event(4)]
    public void CheckoutStop(byte[] receipt) => WriteEvent(4, receipt);

    public void Helper(int x) { }

    [NonEvent]
    public void NotAnEvent(string s) { }

    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        var args = command.Arguments is null ? "null" : string.Join(",", command.Arguments.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"));
        Console.WriteLine($"OnEventCommand {command.Command} [{args}]");
    }
}

[EventSource(Name = "Demo-Broken")]
sealed class BrokenSource : EventSource
{
    [Event(1)]
    public void First(int a) => WriteEvent(1, a);

    [Event(1)]
    public void Again(int a) => WriteEvent(1, a);

    [Event(0)]
    public void Zero() => WriteEvent(0);

    [Event(5, Opcode = (EventOpcode)200)]
    public void BadOpcode() => WriteEvent(5);
}
