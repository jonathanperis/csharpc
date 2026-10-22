// A text adventure engine driven by a scripted command list: enums, records, switch expressions,
// a parser for verbs/objects, inventory, and a small state machine for doors and the ending.
var game = new Game(World.Build());
string[] script =
[
    "look", "inventory", "take lamp", "go north", "look", "take key", "go east", "use key",
    "go east", "read note", "take coin", "take coin", "drop lamp", "go west", "go west", "south",
    "go south", "go up", "dance", "xyzzy", "go north", "go north", "go east", "go east", "give coin",
];
foreach (var line in script)
{
    Console.WriteLine($"> {line}");
    foreach (var output in game.Execute(line)) Console.WriteLine("  " + output);
    if (game.State == GameState.Won) { Console.WriteLine("*** You won! ***"); break; }
}
Console.WriteLine($"moves: {game.Moves}, score: {game.Score}, visited: {string.Join(", ", game.Visited.Order())}");

enum Direction { North, South, East, West, Up, Down }
enum GameState { Playing, Won }
enum ItemKind { Tool, Key, Treasure, Readable }

record Item(string Name, ItemKind Kind, string Description, int Value = 0);

record Exit(Direction Direction, string Target, string? LockedBy = null);

sealed class Room(string name, string description)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public List<Exit> Exits { get; } = [];
    public List<Item> Items { get; } = [];
    public bool Dark { get; init; }
}

static class World
{
    public static Dictionary<string, Room> Build()
    {
        var rooms = new Dictionary<string, Room>
        {
            ["hall"] = new("hall", "A dusty entrance hall. Light spills in from the south."),
            ["library"] = new("library", "Shelves of mouldy books line the walls.") { Dark = true },
            ["study"] = new("study", "A cramped study with a heavy oak desk."),
            ["vault"] = new("vault", "A small vault, cold and silent."),
            ["garden"] = new("garden", "An overgrown garden. A troll guards a bridge to the east."),
            ["tower"] = new("tower", "The top of a crumbling tower. You can see for miles."),
        };
        void Link(string a, Direction d, string b, string? lockedBy = null) => rooms[a].Exits.Add(new Exit(d, b, lockedBy));
        Link("hall", Direction.North, "library");
        Link("library", Direction.South, "hall");
        Link("library", Direction.East, "study");
        Link("study", Direction.West, "library");
        Link("study", Direction.East, "vault", lockedBy: "key");
        Link("vault", Direction.West, "study");
        Link("hall", Direction.South, "garden");
        Link("garden", Direction.North, "hall");
        Link("garden", Direction.Up, "tower");
        Link("tower", Direction.Down, "garden");
        rooms["hall"].Items.Add(new Item("lamp", ItemKind.Tool, "A brass oil lamp."));
        rooms["library"].Items.Add(new Item("key", ItemKind.Key, "A tarnished iron key."));
        rooms["vault"].Items.Add(new Item("coin", ItemKind.Treasure, "A gleaming gold coin.", 50));
        rooms["vault"].Items.Add(new Item("note", ItemKind.Readable, "\"The troll likes gold.\""));
        return rooms;
    }
}

sealed class Game(Dictionary<string, Room> rooms)
{
    private Room _here = rooms["hall"];
    private readonly List<Item> _inventory = [];
    private readonly HashSet<string> _unlocked = [];
    private bool _trollPaid;
    public GameState State { get; private set; } = GameState.Playing;
    public int Moves { get; private set; }
    public int Score => _inventory.Sum(i => i.Value) + (_trollPaid ? 100 : 0) + Visited.Count * 5;
    public HashSet<string> Visited { get; } = ["hall"];

    private bool CanSee => !_here.Dark || _inventory.Any(i => i.Name == "lamp");

    public IEnumerable<string> Execute(string input)
    {
        Moves++;
        var words = input.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var (verb, noun) = words switch
        {
            [] => ("", ""),
            [var v] => (v, ""),
            [var v, .. var rest] => (v, string.Join(' ', rest)),
        };
        return verb switch
        {
            "look" or "l" => Look(),
            "inventory" or "i" => Inventory(),
            "go" => Go(noun),
            "north" or "south" or "east" or "west" or "up" or "down" => Go(verb),
            "take" => Take(noun),
            "drop" => Drop(noun),
            "use" => Use(noun),
            "read" => Read(noun),
            "give" => Give(noun),
            "xyzzy" => ["A hollow voice says \"fool\"."],
            _ => [$"I don't know how to '{verb}'."],
        };
    }

    private IEnumerable<string> Look()
    {
        if (!CanSee) { yield return "It is pitch dark. You might be eaten by a grue."; yield break; }
        yield return $"[{_here.Name}] {_here.Description}";
        if (_here.Items.Count > 0) yield return "You see: " + string.Join(", ", _here.Items.Select(i => i.Name));
        yield return "Exits: " + string.Join(", ", _here.Exits.Select(e => e.Direction.ToString().ToLowerInvariant()));
    }

    private IEnumerable<string> Inventory() =>
        _inventory.Count == 0 ? ["You are empty-handed."] : _inventory.Select(i => $"{i.Name} - {i.Description}");

    private IEnumerable<string> Go(string where)
    {
        if (!Enum.TryParse<Direction>(where, ignoreCase: true, out var dir)) return [$"'{where}' is not a direction."];
        var exit = _here.Exits.FirstOrDefault(e => e.Direction == dir);
        if (exit is null) return ["You can't go that way."];
        if (exit.LockedBy is { } key && !_unlocked.Contains(_here.Name + dir)) return [$"The way {where} is locked."];
        if (_here.Name == "garden" && dir == Direction.East && !_trollPaid) return ["The troll blocks the bridge."];
        _here = rooms[exit.Target];
        var first = Visited.Add(_here.Name);
        return [.. Look(), .. first ? new[] { "(first visit)" } : []];
    }

    private IEnumerable<string> Take(string name)
    {
        if (!CanSee) return ["You fumble around in the dark."];
        var item = _here.Items.Find(i => i.Name == name);
        if (item is null) return [$"There is no {name} here."];
        _here.Items.Remove(item);
        _inventory.Add(item);
        return [$"Taken: {item.Name}." + (item.Value > 0 ? $" (+{item.Value})" : "")];
    }

    private IEnumerable<string> Drop(string name)
    {
        var index = _inventory.FindIndex(i => i.Name == name);
        if (index < 0) return [$"You don't have a {name}."];
        _here.Items.Add(_inventory[index]);
        _inventory.RemoveAt(index);
        return ["Dropped."];
    }

    private IEnumerable<string> Use(string name)
    {
        if (!_inventory.Any(i => i.Name == name)) return [$"You don't have a {name}."];
        var locked = _here.Exits.FirstOrDefault(e => e.LockedBy == name);
        if (locked is null) return ["Nothing happens."];
        _unlocked.Add(_here.Name + locked.Direction);
        return [$"Click! The door to the {locked.Direction.ToString().ToLower()} unlocks."];
    }

    private IEnumerable<string> Read(string name)
    {
        var item = _inventory.Concat(_here.Items).FirstOrDefault(i => i.Name == name);
        return item switch
        {
            null => [$"There is no {name} to read."],
            { Kind: ItemKind.Readable } => [item.Description],
            _ => [$"The {name} has nothing written on it."],
        };
    }

    private IEnumerable<string> Give(string name)
    {
        if (_here.Name != "garden") return ["There is nobody here to give things to."];
        var item = _inventory.Find(i => i.Name == name);
        if (item is not { Kind: ItemKind.Treasure }) return ["The troll is not interested."];
        _inventory.Remove(item);
        _trollPaid = true;
        State = GameState.Won;
        return ["The troll grins, pockets the coin and lets you cross the bridge."];
    }
}
