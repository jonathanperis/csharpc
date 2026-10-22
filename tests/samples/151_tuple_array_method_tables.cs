var depts = new[] { (1, "Eng"), (2, "Ops"), (3, "Empty") };
var emps = new[] { ("ann", 1), ("ben", 2), ("cid", 1) };
foreach (var j in depts.Join(emps, d => d.Item1, e => e.Item2, (d, e) => $"{d.Item2}:{e.Item1}")) Console.WriteLine(j);
foreach (var g in depts.GroupJoin(emps, d => d.Item1, e => e.Item2, (d, es) => $"{d.Item2}[{string.Join(",", es.Select(e => e.Item1))}]")) Console.WriteLine(g);
