// Graph algorithms on generated graphs: Dijkstra (PriorityQueue), BFS/DFS, topological sort,
// union-find (Kruskal), strongly connected components (Tarjan), and A* on a grid.
var g = Graph.Generate(nodes: 60, edgesPerNode: 3, seed: 7);
Console.WriteLine($"graph: {g.NodeCount} nodes, {g.EdgeCount} edges");

var (dist, prev) = g.Dijkstra(0);
foreach (var target in new[] { 5, 17, 42, 59 })
{
    var path = Graph.PathTo(prev, target);
    Console.WriteLine(dist[target] == int.MaxValue
        ? $"0 -> {target}: unreachable"
        : $"0 -> {target}: cost {dist[target]}, path {string.Join(" > ", path)}");
}

var bfs = g.Bfs(0);
Console.WriteLine($"BFS layers from 0: {string.Join(", ", bfs.GroupBy(kv => kv.Value).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Count()}"))}");
Console.WriteLine($"DFS preorder (first 15): {string.Join(" ", g.Dfs(0).Take(15))}");

var sccs = g.StronglyConnectedComponents();
Console.WriteLine($"SCCs: {sccs.Count}, largest {sccs.Max(c => c.Count)}, singletons {sccs.Count(c => c.Count == 1)}");

var mst = g.KruskalMst();
Console.WriteLine($"MST (undirected view): {mst.Count} edges, total weight {mst.Sum(e => e.Weight)}");

var dag = new Dictionary<string, string[]>
{
    ["app"] = ["web", "data", "log"],
    ["web"] = ["http", "log"],
    ["data"] = ["sql", "log"],
    ["sql"] = ["net"],
    ["http"] = ["net"],
    ["log"] = [],
    ["net"] = [],
};
Console.WriteLine($"build order: {string.Join(" ", Graph.TopologicalSort(dag))}");
dag["net"] = ["app"];
try { Graph.TopologicalSort(dag); }
catch (InvalidOperationException e) { Console.WriteLine($"cycle detected: {e.Message}"); }

var grid = new[]
{
    "S....#....",
    ".##..#.##.",
    "..#....#..",
    "#.####.#.#",
    "......#...",
    ".####.#.#.",
    "......#.#G",
};
var route = AStar.Solve(grid);
Console.WriteLine($"A*: {(route is null ? "no route" : $"{route.Count - 1} steps")}");
if (route is not null)
{
    var canvas = grid.Select(r => r.ToCharArray()).ToArray();
    foreach (var (r, c) in route.Skip(1).SkipLast(1)) canvas[r][c] = '*';
    foreach (var row in canvas) Console.WriteLine("  " + new string(row));
}

record struct Edge(int From, int To, int Weight);

class Graph
{
    private readonly List<Edge>[] _adjacency;
    public int NodeCount => _adjacency.Length;
    public int EdgeCount => _adjacency.Sum(a => a.Count);

    private Graph(int n) => _adjacency = Enumerable.Range(0, n).Select(_ => new List<Edge>()).ToArray();

    public static Graph Generate(int nodes, int edgesPerNode, uint seed)
    {
        var g = new Graph(nodes);
        uint state = seed;
        int Next(int max) { state = state * 1103515245u + 12345u; return (int)((state >> 16) % (uint)max); }
        for (int u = 0; u < nodes; u++)
            for (int k = 0; k < edgesPerNode; k++)
            {
                int v = Next(nodes);
                if (v != u && !g._adjacency[u].Any(e => e.To == v)) g._adjacency[u].Add(new Edge(u, v, 1 + Next(20)));
            }
        return g;
    }

    public (int[] Dist, int[] Prev) Dijkstra(int source)
    {
        var dist = Enumerable.Repeat(int.MaxValue, NodeCount).ToArray();
        var prev = Enumerable.Repeat(-1, NodeCount).ToArray();
        var queue = new PriorityQueue<int, int>();
        dist[source] = 0;
        queue.Enqueue(source, 0);
        while (queue.TryDequeue(out var u, out var d))
        {
            if (d > dist[u]) continue;
            foreach (var e in _adjacency[u])
            {
                var nd = d + e.Weight;
                if (nd < dist[e.To])
                {
                    dist[e.To] = nd;
                    prev[e.To] = u;
                    queue.Enqueue(e.To, nd);
                }
            }
        }
        return (dist, prev);
    }

    public static List<int> PathTo(int[] prev, int target)
    {
        var path = new List<int>();
        for (int v = target; v != -1; v = prev[v]) path.Add(v);
        path.Reverse();
        return path;
    }

    public Dictionary<int, int> Bfs(int source)
    {
        var depth = new Dictionary<int, int> { [source] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var e in _adjacency[u])
                if (depth.TryAdd(e.To, depth[u] + 1)) queue.Enqueue(e.To);
        }
        return depth;
    }

    public IEnumerable<int> Dfs(int source)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(source);
        while (stack.Count > 0)
        {
            var u = stack.Pop();
            if (!seen.Add(u)) continue;
            yield return u;
            for (int i = _adjacency[u].Count - 1; i >= 0; i--)
                if (!seen.Contains(_adjacency[u][i].To)) stack.Push(_adjacency[u][i].To);
        }
    }

    public List<List<int>> StronglyConnectedComponents()
    {
        int index = 0;
        var indices = new int[NodeCount];
        var low = new int[NodeCount];
        var onStack = new bool[NodeCount];
        Array.Fill(indices, -1);
        var stack = new Stack<int>();
        var result = new List<List<int>>();

        void Connect(int v)
        {
            indices[v] = low[v] = index++;
            stack.Push(v);
            onStack[v] = true;
            foreach (var e in _adjacency[v])
            {
                if (indices[e.To] == -1) { Connect(e.To); low[v] = Math.Min(low[v], low[e.To]); }
                else if (onStack[e.To]) low[v] = Math.Min(low[v], indices[e.To]);
            }
            if (low[v] != indices[v]) return;
            var component = new List<int>();
            int w;
            do { w = stack.Pop(); onStack[w] = false; component.Add(w); } while (w != v);
            result.Add(component);
        }

        for (int v = 0; v < NodeCount; v++) if (indices[v] == -1) Connect(v);
        return result;
    }

    public List<Edge> KruskalMst()
    {
        var sets = new UnionFind(NodeCount);
        var mst = new List<Edge>();
        foreach (var e in _adjacency.SelectMany(a => a).OrderBy(e => e.Weight).ThenBy(e => e.From).ThenBy(e => e.To))
            if (sets.Union(e.From, e.To)) mst.Add(e);
        return mst;
    }

    public static List<string> TopologicalSort(Dictionary<string, string[]> dependencies)
    {
        var order = new List<string>();
        var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
        void Visit(string node, Stack<string> trail)
        {
            if (state.TryGetValue(node, out var s))
            {
                if (s == 1) throw new InvalidOperationException(string.Join(" -> ", trail.Reverse().Append(node)));
                return;
            }
            state[node] = 1;
            trail.Push(node);
            foreach (var dep in dependencies[node]) Visit(dep, trail);
            trail.Pop();
            state[node] = 2;
            order.Add(node);
        }
        foreach (var node in dependencies.Keys.Order()) Visit(node, new Stack<string>());
        return order;
    }
}

class UnionFind
{
    private readonly int[] _parent, _rank;
    public UnionFind(int n) { _parent = Enumerable.Range(0, n).ToArray(); _rank = new int[n]; }
    public int Find(int x) => _parent[x] == x ? x : _parent[x] = Find(_parent[x]);
    public bool Union(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra == rb) return false;
        if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
        _parent[rb] = ra;
        if (_rank[ra] == _rank[rb]) _rank[ra]++;
        return true;
    }
}

static class AStar
{
    public static List<(int Row, int Col)>? Solve(string[] grid)
    {
        (int, int) Find(char ch)
        {
            for (int r = 0; r < grid.Length; r++)
                if (grid[r].IndexOf(ch) is var c and >= 0) return (r, c);
            throw new ArgumentException($"no '{ch}' on the grid");
        }
        var start = Find('S');
        var goal = Find('G');
        int H((int R, int C) p) => Math.Abs(p.R - goal.Item1) + Math.Abs(p.C - goal.Item2);
        var open = new PriorityQueue<(int, int), int>();
        var cost = new Dictionary<(int, int), int> { [start] = 0 };
        var came = new Dictionary<(int, int), (int, int)>();
        open.Enqueue(start, H(start));
        (int dr, int dc)[] moves = [(0, 1), (1, 0), (0, -1), (-1, 0)];
        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal)
            {
                var path = new List<(int, int)> { current };
                while (came.TryGetValue(current, out var p)) { current = p; path.Add(p); }
                path.Reverse();
                return path;
            }
            foreach (var (dr, dc) in moves)
            {
                var next = (current.Item1 + dr, current.Item2 + dc);
                if (next.Item1 < 0 || next.Item2 < 0 || next.Item1 >= grid.Length || next.Item2 >= grid[0].Length) continue;
                if (grid[next.Item1][next.Item2] == '#') continue;
                var c = cost[current] + 1;
                if (cost.TryGetValue(next, out var old) && old <= c) continue;
                cost[next] = c;
                came[next] = current;
                open.Enqueue(next, c + H(next));
            }
        }
        return null;
    }
}
