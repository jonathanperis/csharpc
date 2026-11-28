// [DynamicallyAccessedMembers] on a property applies to the values its setter receives, as ILLink applies it: a typeof
// assigned to the property (directly or in an object initializer) keeps the members Activator.CreateInstance needs,
// for types the program never constructs itself.
using System;
using System.Diagnostics.CodeAnalysis;

abstract class Box { public abstract string Name { get; } }
sealed class MailBox : Box { public override string Name => "mail"; }
sealed class CalendarBox : Box { public override string Name => "calendar"; }
sealed class TaskBox : Box { public TaskBox() { Console.WriteLine("TaskBox()"); } public override string Name => "tasks"; }

sealed class BoxInfo
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private Type boxType = typeof(MailBox);

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type BoxType { get => boxType; set => boxType = value; }

    public Box Create() => (Box)Activator.CreateInstance(BoxType)!;
}

static class Program
{
    static void Main()
    {
        var info = new BoxInfo();
        Console.WriteLine(info.Create().Name);
        info.BoxType = typeof(CalendarBox);
        Console.WriteLine(info.Create().Name);
        var other = new BoxInfo { BoxType = typeof(TaskBox) };
        Console.WriteLine(other.Create().Name);
    }
}
