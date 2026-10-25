// System.IO.FileSystemWatcher and its event arguments: port of dotnet/runtime's FileSystemWatcher.cs and
// FileSystemWatcher.Linux.cs (release/11.0, MIT). Linux watches with inotify through libc, one shared
// inotify instance read by a dedicated thread, events raised on the thread pool in order, as in .NET.
// Differences: the watcher is no System.ComponentModel.Component (no Site, Container or
// SynchronizingObject: those members are compile errors), and the locks are plain monitors. macOS
// (FSEvents) and Windows (ReadDirectoryChangesW) are not ported yet: EnableRaisingEvents throws there.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.FileSystemEventArgs")]
    public class FileSystemEventArgsImpl : EventArgs
    {
        private readonly WatcherChangeTypes _changeType;
        private readonly string _name;
        private readonly string _fullPath;

        public FileSystemEventArgsImpl(WatcherChangeTypes changeType, string directory, string name)
        {
            ArgumentNullException.ThrowIfNull(directory);
            _changeType = changeType;
            _name = name;
            _fullPath = Combine(directory, name);
        }

        internal static string Combine(string directory, string name)
        {
            bool hasSeparator = directory.Length != 0 && (directory[directory.Length - 1] == Path.DirectorySeparatorChar
                || directory[directory.Length - 1] == Path.AltDirectorySeparatorChar);
            return hasSeparator ? directory + name : directory + Path.DirectorySeparatorChar.ToString() + name;
        }

        public WatcherChangeTypes ChangeType => _changeType;
        public string FullPath => _fullPath;
        public string Name => _name;
    }

    [Shadows("System.IO.RenamedEventArgs")]
    public class RenamedEventArgsImpl : FileSystemEventArgsImpl
    {
        private readonly string _oldName;
        private readonly string _oldFullPath;

        public RenamedEventArgsImpl(WatcherChangeTypes changeType, string directory, string name, string oldName)
            : base(changeType, directory, name)
        {
            _oldName = oldName;
            _oldFullPath = Combine(directory, oldName);
        }

        public string OldFullPath => _oldFullPath;
        public string OldName => _oldName;
    }

    [Shadows("System.IO.ErrorEventArgs")]
    public class ErrorEventArgsImpl : EventArgs
    {
        private readonly Exception _exception;
        public ErrorEventArgsImpl(Exception exception) { _exception = exception; }
        public virtual Exception GetException() => _exception;
    }

    [Shadows("System.IO.InternalBufferOverflowException")]
    public class InternalBufferOverflowExceptionImpl : SystemException
    {
        public InternalBufferOverflowExceptionImpl() : base("Exception of type 'System.IO.InternalBufferOverflowException' was thrown.") { }
        public InternalBufferOverflowExceptionImpl(string message) : base(message) { }
        public InternalBufferOverflowExceptionImpl(string message, Exception inner) : base(message, inner) { }
    }

    [Shadows("System.IO.WaitForChangedResult")]
    public struct WaitForChangedResultImpl
    {
        internal WaitForChangedResultImpl(WatcherChangeTypes changeType, string name, string oldName, bool timedOut)
        {
            ChangeType = changeType;
            Name = name;
            OldName = oldName;
            TimedOut = timedOut;
        }

        public WatcherChangeTypes ChangeType { get; set; }
        public string Name { get; set; }
        public string OldName { get; set; }
        public bool TimedOut { get; set; }
    }

    [Shadows("System.IO.FileSystemWatcher")]
    public partial class FileSystemWatcherImpl : IDisposable
    {
        private readonly NormalizedFilterCollection _filters = new NormalizedFilterCollection();
        private string _directory;
        private const NotifyFilters c_defaultNotifyFilters = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
        private NotifyFilters _notifyFilters = c_defaultNotifyFilters;
        private bool _includeSubdirectories;
        private bool _enabled;
        private bool _initializing;
        private uint _internalBufferSize = 8192;
        private bool _disposed;

        private FileSystemEventHandler _onChangedHandler;
        private FileSystemEventHandler _onCreatedHandler;
        private FileSystemEventHandler _onDeletedHandler;
        private RenamedEventHandler _onRenamedHandler;
        private ErrorEventHandler _onErrorHandler;

        private const int c_notifyFiltersValidMask = (int)(NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName
            | NotifyFilters.FileName | NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size);

        public FileSystemWatcherImpl()
        {
            _directory = string.Empty;
        }

        public FileSystemWatcherImpl(string path)
        {
            CheckPathValidity(path);
            _directory = path;
        }

        public FileSystemWatcherImpl(string path, string filter)
        {
            CheckPathValidity(path);
            ArgumentNullException.ThrowIfNull(filter);
            _directory = path;
            Filter = filter;
        }

        public NotifyFilters NotifyFilter
        {
            get => _notifyFilters;
            set
            {
                if (((int)value & ~c_notifyFiltersValidMask) != 0)
                    throw new ArgumentException("The value of argument 'value' (" + (int)value + ") is invalid for Enum type 'NotifyFilters'.");
                if (_notifyFilters != value)
                {
                    _notifyFilters = value;
                    Restart();
                }
            }
        }

        public Collection<string> Filters => _filters;

        public bool EnableRaisingEvents
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                if (IsSuspended()) _enabled = value;
                else if (value) StartRaisingEventsIfNotDisposed();
                else StopRaisingEvents();
            }
        }

        public string Filter
        {
            get => Filters.Count == 0 ? "*" : Filters[0];
            set
            {
                Filters.Clear();
                Filters.Add(value);
            }
        }

        public bool IncludeSubdirectories
        {
            get => _includeSubdirectories;
            set
            {
                if (_includeSubdirectories != value)
                {
                    _includeSubdirectories = value;
                    Restart();
                }
            }
        }

        public int InternalBufferSize
        {
            get => (int)_internalBufferSize;
            set
            {
                if (_internalBufferSize != value)
                    _internalBufferSize = value < 4096 ? 4096 : (uint)value;
            }
        }

        public string Path
        {
            get => _directory;
            set
            {
                value ??= string.Empty;
                if (!string.Equals(_directory, value, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    if (value.Length == 0)
                        throw new ArgumentException("The directory name " + value + " is invalid.", "Path");
                    if (!Directory.Exists(value))
                        throw new ArgumentException("The directory name '" + value + "' does not exist.", "Path");
                    _directory = value;
                    Restart();
                }
            }
        }

        public event FileSystemEventHandler Changed { add => _onChangedHandler += value; remove => _onChangedHandler -= value; }
        public event FileSystemEventHandler Created { add => _onCreatedHandler += value; remove => _onCreatedHandler -= value; }
        public event FileSystemEventHandler Deleted { add => _onDeletedHandler += value; remove => _onDeletedHandler -= value; }
        public event ErrorEventHandler Error { add => _onErrorHandler += value; remove => _onErrorHandler -= value; }
        public event RenamedEventHandler Renamed { add => _onRenamedHandler += value; remove => _onRenamedHandler -= value; }

        /// <summary>Component.Dispose().</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    StopRaisingEvents();
                    _onChangedHandler = null;
                    _onCreatedHandler = null;
                    _onDeletedHandler = null;
                    _onRenamedHandler = null;
                    _onErrorHandler = null;
                }
            }
            finally
            {
                _disposed = true;
            }
        }

        private static void CheckPathValidity(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (path.Length == 0) throw new ArgumentException("The directory name " + path + " is invalid.", "path");
            if (!Directory.Exists(path)) throw new ArgumentException("The directory name '" + path + "' does not exist.", "path");
        }

        private bool MatchPattern(ReadOnlySpan<char> relativePath)
        {
            ReadOnlySpan<char> name = System.IO.Path.GetFileName(relativePath);
            if (name.Length == 0) return false;
            string[] filters = _filters.GetFilters();
            if (filters.Length == 0) return true;
            foreach (string filter in filters)
                if (MatchesSimpleExpression(filter, name, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())) return true;
            return false;
        }

        /// <summary>FileSystemName.MatchesSimpleExpression: '*' any run, '?' one char, '\' escapes.</summary>
        internal static bool MatchesSimpleExpression(ReadOnlySpan<char> expression, ReadOnlySpan<char> name, bool ignoreCase)
        {
            if (expression.Length == 0 || name.Length == 0) return false;
            if (expression.Length == 1 && expression[0] == '*') return true;
            return Match(expression, 0, name, 0, ignoreCase);
        }

        private static bool Match(ReadOnlySpan<char> p, int pi, ReadOnlySpan<char> s, int si, bool ignoreCase)
        {
            while (pi < p.Length)
            {
                char c = p[pi];
                if (c == '*')
                {
                    while (pi < p.Length && p[pi] == '*') pi++;
                    if (pi == p.Length) return true;
                    for (int k = si; k <= s.Length; k++) if (Match(p, pi, s, k, ignoreCase)) return true;
                    return false;
                }
                if (si >= s.Length) return false;
                if (c == '?') { si++; pi++; continue; }
                if (c == '\\' && pi + 1 < p.Length) c = p[++pi];
                char d = s[si];
                if (c != d && !(ignoreCase && char.ToUpperInvariant(c) == char.ToUpperInvariant(d))) return false;
                si++; pi++;
            }
            return si == s.Length;
        }

        private static InternalBufferOverflowException CreateBufferOverflowException(string directoryPath) =>
            new InternalBufferOverflowException("Too many changes at once in directory:" + directoryPath + ".");

        private static DirectoryNotFoundException CreateWatchedDirectoryDeletedOrMovedException(string directoryPath) =>
            new DirectoryNotFoundException("The directory being watched '" + directoryPath + "' was deleted or moved.");

        private void NotifyRenameEventArgs(WatcherChangeTypes action, ReadOnlySpan<char> name, ReadOnlySpan<char> oldName)
        {
            if (_onRenamedHandler != null && (MatchPattern(name) || MatchPattern(oldName)))
                OnRenamed(new RenamedEventArgs(action, _directory, name.IsEmpty ? null : name.ToString(), oldName.IsEmpty ? null : oldName.ToString()));
        }

        private FileSystemEventHandler GetHandler(WatcherChangeTypes changeType)
        {
            switch (changeType)
            {
                case WatcherChangeTypes.Created: return _onCreatedHandler;
                case WatcherChangeTypes.Deleted: return _onDeletedHandler;
                case WatcherChangeTypes.Changed: return _onChangedHandler;
            }
            return null;
        }

        private void NotifyFileSystemEventArgs(WatcherChangeTypes changeType, ReadOnlySpan<char> name)
        {
            FileSystemEventHandler handler = GetHandler(changeType);
            if (handler != null && MatchPattern(name.IsEmpty ? _directory.AsSpan() : name))
                handler((FileSystemWatcher)(object)this, new FileSystemEventArgs(changeType, _directory, name.IsEmpty ? null : name.ToString()));
        }

        protected void OnChanged(FileSystemEventArgs e) => _onChangedHandler?.Invoke((FileSystemWatcher)(object)this, e);
        protected void OnCreated(FileSystemEventArgs e) => _onCreatedHandler?.Invoke((FileSystemWatcher)(object)this, e);
        protected void OnDeleted(FileSystemEventArgs e) => _onDeletedHandler?.Invoke((FileSystemWatcher)(object)this, e);
        protected void OnError(ErrorEventArgs e) => _onErrorHandler?.Invoke((FileSystemWatcher)(object)this, e);
        protected void OnRenamed(RenamedEventArgs e) => _onRenamedHandler?.Invoke((FileSystemWatcher)(object)this, e);

        public WaitForChangedResult WaitForChanged(WatcherChangeTypes changeType) => WaitForChanged(changeType, Timeout.Infinite);

        public WaitForChangedResult WaitForChanged(WatcherChangeTypes changeType, int timeout)
        {
            var tcs = new TaskCompletionSource<WaitForChangedResult>();
            FileSystemEventHandler fseh = null;
            RenamedEventHandler reh = null;
            if ((changeType & (WatcherChangeTypes.Created | WatcherChangeTypes.Deleted | WatcherChangeTypes.Changed)) != 0)
            {
                fseh = (s, e) =>
                {
                    if ((e.ChangeType & changeType) != 0)
                        tcs.TrySetResult((WaitForChangedResult)(object)new WaitForChangedResultImpl(e.ChangeType, e.Name, null, false));
                };
                if ((changeType & WatcherChangeTypes.Created) != 0) Created += fseh;
                if ((changeType & WatcherChangeTypes.Deleted) != 0) Deleted += fseh;
                if ((changeType & WatcherChangeTypes.Changed) != 0) Changed += fseh;
            }
            if ((changeType & WatcherChangeTypes.Renamed) != 0)
            {
                reh = (s, e) =>
                {
                    if ((e.ChangeType & changeType) != 0)
                        tcs.TrySetResult((WaitForChangedResult)(object)new WaitForChangedResultImpl(e.ChangeType, e.Name, e.OldName, false));
                };
                Renamed += reh;
            }
            try
            {
                bool wasEnabled = EnableRaisingEvents;
                if (!wasEnabled) EnableRaisingEvents = true;
                tcs.Task.Wait(timeout);
                EnableRaisingEvents = wasEnabled;
            }
            finally
            {
                if (reh != null) Renamed -= reh;
                if (fseh != null)
                {
                    if ((changeType & WatcherChangeTypes.Changed) != 0) Changed -= fseh;
                    if ((changeType & WatcherChangeTypes.Deleted) != 0) Deleted -= fseh;
                    if ((changeType & WatcherChangeTypes.Created) != 0) Created -= fseh;
                }
            }
            return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : (WaitForChangedResult)(object)new WaitForChangedResultImpl(0, null, null, true);
        }

        public WaitForChangedResult WaitForChanged(WatcherChangeTypes changeType, TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(totalMilliseconds, -1, "timeout");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(totalMilliseconds, int.MaxValue, "timeout");
            return WaitForChanged(changeType, (int)totalMilliseconds);
        }

        private void Restart()
        {
            if (!IsSuspended() && _enabled)
            {
                StopRaisingEvents();
                StartRaisingEventsIfNotDisposed();
            }
        }

        private void StartRaisingEventsIfNotDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StartRaisingEvents();
        }

        public void BeginInit()
        {
            bool oldEnabled = _enabled;
            StopRaisingEvents();
            _enabled = oldEnabled;
            _initializing = true;
        }

        public void EndInit()
        {
            _initializing = false;
            if (_directory.Length != 0 && _enabled) StartRaisingEvents();
        }

        private bool IsSuspended() => _initializing;

        private sealed class NormalizedFilterCollection : Collection<string>
        {
            internal NormalizedFilterCollection() : base(new ImmutableStringList()) { }

            protected override void InsertItem(int index, string item) =>
                base.InsertItem(index, string.IsNullOrEmpty(item) || item == "*.*" ? "*" : item);

            protected override void SetItem(int index, string item) =>
                base.SetItem(index, string.IsNullOrEmpty(item) || item == "*.*" ? "*" : item);

            internal string[] GetFilters() => ((ImmutableStringList)Items).Items;

            private sealed class ImmutableStringList : IList<string>
            {
                public string[] Items = Array.Empty<string>();

                public string this[int index]
                {
                    get
                    {
                        string[] items = Items;
                        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)items.Length, "index");
                        return items[index];
                    }
                    set
                    {
                        string[] clone = (string[])Items.Clone();
                        clone[index] = value;
                        Items = clone;
                    }
                }

                public int Count => Items.Length;
                public bool IsReadOnly => false;
                public void Add(string item) => throw new NotSupportedException();
                public void Clear() => Items = Array.Empty<string>();
                public bool Contains(string item) => Array.IndexOf(Items, item) >= 0;
                public void CopyTo(string[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);
                public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)Items).GetEnumerator();
                public int IndexOf(string item) => Array.IndexOf(Items, item);

                public void Insert(int index, string item)
                {
                    string[] items = Items;
                    string[] newItems = new string[items.Length + 1];
                    items.AsSpan(0, index).CopyTo(newItems);
                    items.AsSpan(index).CopyTo(newItems.AsSpan(index + 1));
                    newItems[index] = item;
                    Items = newItems;
                }

                public bool Remove(string item) => throw new NotSupportedException();

                public void RemoveAt(int index)
                {
                    string[] items = Items;
                    string[] newItems = new string[items.Length - 1];
                    items.AsSpan(0, index).CopyTo(newItems);
                    items.AsSpan(index + 1).CopyTo(newItems.AsSpan(index));
                    Items = newItems;
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
        }

#if !CSHARPC_TARGET_LINUX
        private void StartRaisingEvents()
        {
            if (IsSuspended()) { _enabled = true; return; }
            throw new NotSupportedException("FileSystemWatcher on this platform is not supported by csharpc yet (only Linux's inotify is).");
        }

        private void StopRaisingEvents() { _enabled = false; }
#endif
    }
}
