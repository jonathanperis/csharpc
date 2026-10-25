// The library method that can't be compiled is reached through an interface call whose implementation
// is a base-class virtual overridden by the concrete class: an error, not a CSLLVM0007 warning.
Console.WriteLine(IlReachability.Providers.Create().Load());
