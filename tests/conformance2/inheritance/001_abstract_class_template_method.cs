Report r = new CsvReport();
Console.WriteLine(r.Render(new[] { "a", "b" }));
abstract class Report { public string Render(string[] rows) => Header() + string.Join(Sep, rows) + Footer(); protected abstract string Sep { get; } protected virtual string Header() => "<"; protected virtual string Footer() => ">"; }
class CsvReport : Report { protected override string Sep => ","; protected override string Header() => "csv:" + base.Header(); }
