// An interface re-implemented by a derived class (it names the interface again and implements its member explicitly) replaces the
// base class's implementation for every call through the interface, including `((I)this).M()` written inside the base class: the
// dispatch is on the object's run-time type. System.Net.Sockets' SocketAsyncContext relies on it (AsyncOperation.Process calls
// IThreadPoolWorkItem.Execute, re-implemented by ReadOperation and WriteOperation).
using System;

interface IWork
{
    void Execute();
    string Name { get; }
}

abstract class Operation : IWork
{
    public void Process() => ((IWork)this).Execute();
    public string Describe() => ((IWork)this).Name;
    void IWork.Execute() => Console.WriteLine("Operation.Execute");
    string IWork.Name => "Operation";
}

abstract class ReadOperation : Operation, IWork
{
    void IWork.Execute() => Console.WriteLine("ReadOperation.Execute on " + GetType().Name);
    string IWork.Name => "read";
}

sealed class ReceiveOperation : ReadOperation { }

class PlainOperation : Operation { }

class Generic<T> : IWork
{
    public void Run() => ((IWork)this).Execute();
    void IWork.Execute() => Console.WriteLine("Generic<" + typeof(T).Name + ">");
    string IWork.Name => "generic";
}

sealed class Derived : Generic<int>, IWork
{
    void IWork.Execute() => Console.WriteLine("Derived re-implementation");
    string IWork.Name => "derived";
}

static class Program
{
    static void Run(IWork work) => work.Execute();

    static void Main()
    {
        var receive = new ReceiveOperation();
        receive.Process();
        Console.WriteLine(receive.Describe());
        Run(receive);
        ((IWork)receive).Execute();
        Operation op = receive;
        op.Process();

        var plain = new PlainOperation();
        plain.Process();
        Console.WriteLine(plain.Describe());

        new Derived().Run();
        Generic<string> g = new Generic<string>();
        g.Run();
        IWork[] all = [receive, plain, new Derived()];
        foreach (var w in all) Console.WriteLine(w.Name);
    }
}
