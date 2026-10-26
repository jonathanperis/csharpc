// new AggregateException(..., innerExceptions): the constructors' argument checks and .NET's Message
// ("<message> (<inner 1 message>) (<inner 2 message>)"), called by FunctionCodeGen.Exceptions.cs.
// The runtime's AggregateException keeps the first inner exception as InnerException.
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    internal static class AggregateExceptionSupport
    {
        public static Exception[] FromArray(Exception[] innerExceptions)
        {
            if (innerExceptions == null) throw new ArgumentNullException("innerExceptions");
            Exception[] copy = new Exception[innerExceptions.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = innerExceptions[i];
                if (copy[i] == null) throw new ArgumentException("An element of innerExceptions was null.");
            }
            return copy;
        }

        public static Exception[] FromEnumerable(IEnumerable<Exception> innerExceptions)
        {
            if (innerExceptions == null) throw new ArgumentNullException("innerExceptions");
            return FromArray(new List<Exception>(innerExceptions).ToArray());
        }

        public static Exception[] FromSingle(Exception innerException)
        {
            if (innerException == null) throw new ArgumentNullException("innerException");
            return new Exception[] { innerException };
        }

        /// <summary>`message` null: a null message argument (Exception's "Exception of type ... was thrown.").</summary>
        public static string Message(string message, Exception[] innerExceptions)
        {
            string text = message ?? "Exception of type 'System.AggregateException' was thrown.";
            for (int i = 0; i < innerExceptions.Length; i++) text = text + " (" + innerExceptions[i].Message + ")";
            return text;
        }

        public static Exception First(Exception[] innerExceptions) => innerExceptions.Length > 0 ? innerExceptions[0] : null;

        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_exception_inner_count")]
        private static extern int InnerCount(IntPtr exception);

        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_exception_inner_at")]
        private static extern IntPtr InnerAt(IntPtr exception, int index);

        private static Exception[] Inners(Exception exception)
        {
            IntPtr self = CoreIntrinsics.ObjectToPointer(exception);
            var inners = new Exception[InnerCount(self)];
            for (int i = 0; i < inners.Length; i++) inners[i] = (Exception)CoreIntrinsics.PointerToObject(InnerAt(self, i));
            return inners;
        }

        /// <summary>AggregateException.InnerExceptions: the exceptions it was created with, read-only.</summary>
        public static System.Collections.ObjectModel.ReadOnlyCollection<Exception> InnerExceptions(Exception aggregate) =>
            new System.Collections.ObjectModel.ReadOnlyCollection<Exception>(Inners(aggregate));

        /// <summary>
        /// AggregateException.Flatten: the inner exceptions of nested AggregateExceptions, breadth first, in a
        /// new AggregateException with this one's own message (its Message without the inner messages).
        /// </summary>
        public static AggregateException Flatten(Exception aggregate)
        {
            var flattened = new List<Exception>();
            var toFlatten = new List<Exception> { aggregate };
            int next = 0;
            while (toFlatten.Count > next)
            {
                foreach (Exception inner in Inners(toFlatten[next++]))
                {
                    if (inner == null) continue;
                    if (inner is AggregateException) toFlatten.Add(inner);
                    else flattened.Add(inner);
                }
            }
            return new AggregateException(BaseMessage(aggregate), flattened);
        }

        /// <summary>The message the AggregateException was created with: csharpc stores Message with the inner messages appended.</summary>
        private static string BaseMessage(Exception aggregate)
        {
            string full = aggregate.Message;
            string suffix = "";
            foreach (Exception inner in Inners(aggregate)) suffix += " (" + inner.Message + ")";
            return full.EndsWith(suffix, StringComparison.Ordinal) ? full.Substring(0, full.Length - suffix.Length) : full;
        }

        /// <summary>
        /// AggregateException.Handle: the predicate sees every inner exception in order; those it doesn't handle
        /// are rethrown in a new AggregateException with this one's Message.
        /// </summary>
        public static void Handle(Exception aggregate, Func<Exception, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException("predicate");
            List<Exception> unhandled = null;
            foreach (Exception inner in Inners(aggregate))
                if (!predicate(inner)) (unhandled ??= new List<Exception>()).Add(inner);
            if (unhandled != null) throw new AggregateException(aggregate.Message, unhandled);
        }

        /// <summary>
        /// AggregateException.GetBaseException: unwrap while there is exactly one inner exception (and it is
        /// itself an AggregateException or any exception); with 0 or 2+ inners, return this aggregate.
        /// </summary>
        public static Exception GetBaseException(Exception aggregate)
        {
            Exception back = aggregate;
            while (back is AggregateException)
            {
                Exception[] inners = Inners(back);
                if (inners.Length != 1 || inners[0] == null) break;
                back = inners[0];
            }
            return back;
        }

        /// <summary>Exception.GetBaseException: walk InnerException to the innermost.</summary>
        public static Exception GetBaseExceptionDefault(Exception exception)
        {
            Exception back = exception;
            while (back.InnerException != null) back = back.InnerException;
            return back;
        }

        /// <summary>
        /// Exception.GetBaseException when the compile-time type is Exception (or another library
        /// exception): AggregateException overrides the walk; everything else uses InnerException.
        /// </summary>
        public static Exception GetBaseExceptionDispatch(Exception exception) =>
            exception is AggregateException ? GetBaseException(exception) : GetBaseExceptionDefault(exception);
    }
}
