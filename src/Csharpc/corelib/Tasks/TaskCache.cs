// Task.FromResult's cache, as .NET's (Task.FromResult, TaskCache): true and false, the Int32 values -1 to 8, and the default value of a
// reference type or of a value type without references of 1, 2, 4, 8 or 16 bytes each have one shared completed task, so
// ReferenceEquals(Task.FromResult(true), Task.FromResult(true)) holds as in .NET and CoreLib code that returns these results (SemaphoreSlim's
// uncontended WaitAsync) allocates nothing. Other results get a new task, built by the code generator (Intrinsics, Task.FromResult).
#nullable disable
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal static class TaskCache
    {
        private const int InclusiveInt32Min = -1, ExclusiveInt32Max = 9;

        private static readonly Task<bool> s_trueTask = Create(true), s_falseTask = Create(false);
        private static readonly Task<int>[] s_int32Tasks = CreateInt32Tasks();

        private static Task<T> Create<T>(T value)
        {
            var source = new TaskCompletionSource<T>();
            source.TrySetResult(value);
            return source.Task;
        }

        private static Task<int>[] CreateInt32Tasks()
        {
            var tasks = new Task<int>[ExclusiveInt32Max - InclusiveInt32Min];
            for (int i = 0; i < tasks.Length; i++) tasks[i] = Create(i + InclusiveInt32Min);
            return tasks;
        }

        private static class DefaultResult<T>
        {
            internal static readonly Task<T> Task = Create<T>(default);
        }

        /// <summary>The shared task for <paramref name="result"/>, or null when .NET would create a new one.</summary>
        public static Task<T> Cached<T>(T result)
        {
            // A null reference or a Nullable<T> without a value, whatever its padding holds (.NET tests `result is null` first).
            if (result is null) return DefaultResult<T>.Task;
            if (!typeof(T).IsValueType) return null;
            if (typeof(T) == typeof(bool))
                return Unsafe.As<Task<T>>(Unsafe.As<T, bool>(ref result) ? s_trueTask : s_falseTask);
            if (typeof(T) == typeof(int))
            {
                int value = Unsafe.As<T, int>(ref result);
                return (uint)(value - InclusiveInt32Min) < ExclusiveInt32Max - InclusiveInt32Min ? Unsafe.As<Task<T>>(s_int32Tasks[value - InclusiveInt32Min]) : null;
            }
            // Bit patterns, not ==: floating-point -0.0 is not default.
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) return null;
            bool isDefault = Unsafe.SizeOf<T>() switch
            {
                1 => Unsafe.As<T, byte>(ref result) == 0,
                2 => Unsafe.As<T, ushort>(ref result) == 0,
                4 => Unsafe.As<T, uint>(ref result) == 0,
                8 => Unsafe.As<T, ulong>(ref result) == 0,
                16 => Unsafe.As<T, ulong>(ref result) == 0 && Unsafe.Add(ref Unsafe.As<T, ulong>(ref result), 1) == 0,
                _ => false,
            };
            return isDefault ? DefaultResult<T>.Task : null;
        }
    }
}
