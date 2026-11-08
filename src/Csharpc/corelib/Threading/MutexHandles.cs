// System.Threading.Mutex and Semaphore: the argument checks, name handling and scopes in front of the runtime's
// wait-handle object (src/runtime/threads.c: EVENT_MUTEX, EVENT_SEMAPHORE); FunctionCodeGen.NamedHandles.cs calls
// these for `new Mutex(...)`, `new Semaphore(...)`, OpenExisting and TryOpenExisting. WaitOne, ReleaseMutex, Dispose and
// the WaitAny/WaitAll composition are the generic wait-handle lowering.
//
// Messages and exception types are .NET 11's on Linux and macOS (probed with `dotnet run`):
//  * named Mutex objects are shared by every handle with the same name and scope in the process; "Local\x" is "x",
//    "Global\x" is a different scope, and the NamedWaitHandleOptions overloads select the scope by
//    (CurrentUserOnly, CurrentSessionOnly) and reject a prefix that contradicts CurrentSessionOnly;
//  * the legacy overloads are the scope (user only, session only) for "x"/"Local\x" and (user only) for "Global\x";
//  * named Semaphores (and EventWaitHandles) are PlatformNotSupportedException on Unix, which is what we do everywhere.
// Mutexes shared between processes are not supported (the runtime stops with NotSupportedException when another
// process holds the name), and named mutexes are not supported on Windows yet.
#nullable disable
using System;
using System.IO;
using System.Threading;

namespace CsharpcCoreLib
{
    /// <summary>The options of a named wait handle; stored inverted so that default(T) is (true, true) like .NET's.</summary>
    [Shadows("System.Threading.NamedWaitHandleOptions")]
    public struct NamedWaitHandleOptionsImpl
    {
        private bool _notCurrentUserOnly;
        private bool _notCurrentSessionOnly;

        public bool CurrentUserOnly { get => !_notCurrentUserOnly; set => _notCurrentUserOnly = !value; }
        public bool CurrentSessionOnly { get => !_notCurrentSessionOnly; set => _notCurrentSessionOnly = !value; }
    }

    internal static class MutexHandles
    {
        private const int UserOnly = 1, SessionOnly = 2;
        private const string LocalPrefix = "Local\\", GlobalPrefix = "Global\\";

        public static object Create(bool initiallyOwned, string name)
        {
            if (string.IsNullOrEmpty(name)) return CreateUnnamed(initiallyOwned);
            bool global = name.StartsWith(GlobalPrefix, StringComparison.Ordinal);
            return CreateNamed(initiallyOwned, StripPrefix(name), UserOnly | (global ? 0 : SessionOnly));
        }

        public static object CreateWithOptions(bool initiallyOwned, string name, NamedWaitHandleOptions options)
        {
            if (string.IsNullOrEmpty(name)) return CreateUnnamed(initiallyOwned);
            string stripped = StripCheckingOptions(name, options, out int scope);
            return CreateNamed(initiallyOwned, stripped, scope);
        }

        /// <summary>Whether the last constructor call on this thread created the mutex (false: it opened an existing named one).</summary>
        public static bool CreatedNew() => CoreIntrinsics.csrt_mutex_created_new() != 0;

        private static object CreateUnnamed(bool initiallyOwned)
        {
            object handle = CoreIntrinsics.csrt_mutex_new();
            if (initiallyOwned) CoreIntrinsics.csrt_event_wait(handle, 0);
            return handle;
        }

        private static object CreateNamed(bool initiallyOwned, string name, int scope)
        {
            ValidateName(name);
            object handle = CoreIntrinsics.csrt_mutex_open(name, scope, true);
            // Only the call that creates the mutex owns it.
            if (initiallyOwned && CoreIntrinsics.csrt_mutex_created_new() != 0) CoreIntrinsics.csrt_event_wait(handle, 0);
            return handle;
        }

        public static object OpenExisting(string name) => Open(name, true, default, true);
        public static object OpenExistingWithOptions(string name, NamedWaitHandleOptions options) => Open(name, false, options, true);
        public static object TryOpen(string name) => Open(name, true, default, false);
        public static object TryOpenWithOptions(string name, NamedWaitHandleOptions options) => Open(name, false, options, false);

        private static object Open(string name, bool legacy, NamedWaitHandleOptions options, bool throwIfMissing)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (name.Length == 0) throw new ArgumentException("The value cannot be an empty string.", "name");
            int scope;
            string stripped;
            if (legacy)
            {
                scope = UserOnly | (name.StartsWith(GlobalPrefix, StringComparison.Ordinal) ? 0 : SessionOnly);
                stripped = StripPrefix(name);
            }
            else stripped = StripCheckingOptions(name, options, out scope);
            ValidateName(stripped);
            object handle = CoreIntrinsics.csrt_mutex_open(stripped, scope, false);
            if (handle == null && throwIfMissing) throw new WaitHandleCannotBeOpenedException();
            return handle;
        }

        private static string StripPrefix(string name) =>
            name.StartsWith(LocalPrefix, StringComparison.Ordinal) ? name.Substring(LocalPrefix.Length)
            : name.StartsWith(GlobalPrefix, StringComparison.Ordinal) ? name.Substring(GlobalPrefix.Length)
            : name;

        private static string StripCheckingOptions(string name, NamedWaitHandleOptions options, out int scope)
        {
            bool session = options.CurrentSessionOnly;
            scope = (options.CurrentUserOnly ? UserOnly : 0) | (session ? SessionOnly : 0);
            bool global = name.StartsWith(GlobalPrefix, StringComparison.Ordinal);
            bool local = name.StartsWith(LocalPrefix, StringComparison.Ordinal);
            if ((global && session) || (local && !session))
                throw new ArgumentException("The specified name '" + name + "' has a prefix that is not compatible with the specified option 'CurrentSessionOnly'.", "name");
            return StripPrefix(name);
        }

        private static void ValidateName(string name)
        {
#if CSHARPC_TARGET_WINDOWS
            throw new NotSupportedException("csharpc: named mutexes are not supported on Windows yet.");
#else
            if (name.IndexOf('/') >= 0) throw new IOException("The value may not contain directory separator characters.");
#endif
        }
    }

    internal static class SemaphoreHandles
    {
        private const string NamedNotSupported = "The named version of this synchronization primitive is not supported on this platform.";

        public static object Create(int initialCount, int maximumCount, string name)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(initialCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
            if (initialCount > maximumCount)
                throw new ArgumentException("The initial count for the semaphore must be greater than or equal to zero and less than the maximum count.");
            if (name != null) throw new PlatformNotSupportedException(NamedNotSupported); // "" is a name here, unlike Mutex's
            return CoreIntrinsics.csrt_semaphore_new(initialCount, maximumCount);
        }

        /// <summary>Semaphore.Release(releaseCount): the count before the release; SemaphoreFullException above the maximum.</summary>
        public static int Release(object semaphore, int releaseCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(releaseCount);
            return CoreIntrinsics.csrt_event_set(semaphore, releaseCount);
        }

        public static object OpenExisting(string name) => Open(name);
        public static object OpenExistingWithOptions(string name, NamedWaitHandleOptions options) => Open(name);
        public static object TryOpen(string name) => Open(name);
        public static object TryOpenWithOptions(string name, NamedWaitHandleOptions options) => Open(name);

        // Unix .NET refuses before it looks at the name.
        private static object Open(string name) => throw new PlatformNotSupportedException(NamedNotSupported);
    }
}
