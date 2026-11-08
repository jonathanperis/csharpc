// AppDomain.DomainUnload: handlers can be added and removed (null too); .NET Core has one application domain that is never
// unloaded, so the event is never raised, not at process exit either. (Npgsql's PoolManager subscribes to it.)

AppDomain domain = AppDomain.CurrentDomain;
EventHandler first = (sender, e) => Console.WriteLine("first: " + (sender == domain));
domain.DomainUnload += first;
domain.DomainUnload += (sender, e) => Console.WriteLine("second");
domain.DomainUnload -= first;
domain.DomainUnload -= first;
domain.DomainUnload += null;
domain.DomainUnload -= null;
Console.WriteLine("registered");
