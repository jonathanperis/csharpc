// Framework exception constructors name their inner-exception parameter differently ("innerException", "inner", "e");
// every one must keep it, as .NET does.
using System;
using System.Reflection;

var cause = new InvalidOperationException("the cause");
Show(new MissingMethodException("missing method", cause));
Show(new MissingMemberException("missing member", cause));
Show(new MissingFieldException("missing field", cause));
Show(new MemberAccessException("member access", cause));
Show(new TargetInvocationException(cause));
Show(new TargetInvocationException("target invocation", cause));
Show(new UriFormatException("uri format", cause));
Show(new InvalidCastException("invalid cast", cause));
Show(new TypeInitializationException("Some.Type", cause));

try { throw new MissingMethodException("thrown", new MissingMethodException("Member not found.")); }
catch (MissingMethodException e) { Show(e); }

static void Show(Exception e) =>
    Console.WriteLine($"{e.GetType().Name}: {e.Message} | inner: {(e.InnerException is { } i ? i.GetType().Name + ": " + i.Message : "none")}");
