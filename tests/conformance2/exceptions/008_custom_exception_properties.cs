try { throw new ValidationException("Name", "is required"); }
catch (ValidationException e) { Console.WriteLine($"{e.Field}|{e.Message}|{e}"); }
class ValidationException(string field, string msg) : Exception($"{field} {msg}") {
    public string Field => field;
    public override string ToString() => $"ValidationException[{Field}]";
}
