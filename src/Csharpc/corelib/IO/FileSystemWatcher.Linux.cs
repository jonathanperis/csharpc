// FileSystemWatcher on Linux: port of dotnet/runtime's FileSystemWatcher.Linux.cs (release/11.0, MIT).
// All watchers share one inotify instance; a dedicated thread reads its events and each watcher raises
// its events in order on the thread pool. inotify is not recursive: subdirectories get watches of their
// own as they are found (events in a directory before its watch is added are lost, as in .NET).
// csharpc: libc's inotify calls instead of the System.Native shim, monitors instead of the
// ReaderWriterLockSlim, and a queue drained on the pool instead of a Channel.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CsharpcCoreLib
{
    public partial class FileSystemWatcherImpl
    {
        private const int PATH_MAX = 4096;

        private void StartRaisingEvents()
        {
            if (IsSuspended())
            {
                _enabled = true;
                return;
            }
            if (_watcher != null) return;
            _watcher = new INotify.Watcher(this);
            _enabled = true;
            _watcher.Start();
        }

        private void StopRaisingEvents()
        {
            _enabled = false;
            if (IsSuspended()) return;
            _watcher?.Stop();
            _watcher = null;
        }

        private const string MaxUserInstancesPath = "/proc/sys/fs/inotify/max_user_instances";
        private const string MaxUserWatchesPath = "/proc/sys/fs/inotify/max_user_watches";

        private INotify.Watcher _watcher;

        private static string ReadMaxUserLimit(string path)
        {
            try { return File.ReadAllText(path).Trim(); }
            catch { return null; }
        }

        // inotify(7) event bits.
        private const uint IN_ACCESS = 0x1, IN_MODIFY = 0x2, IN_ATTRIB = 0x4, IN_MOVED_FROM = 0x40, IN_MOVED_TO = 0x80,
            IN_CREATE = 0x100, IN_DELETE = 0x200, IN_MOVE_SELF = 0x800, IN_Q_OVERFLOW = 0x4000, IN_IGNORED = 0x8000,
            IN_ONLYDIR = 0x01000000, IN_DONT_FOLLOW = 0x02000000, IN_EXCL_UNLINK = 0x04000000, IN_MASK_ADD = 0x20000000,
            IN_ISDIR = 0x40000000;
        private const int IN_CLOEXEC = 0x80000;
        private const int EMFILE = 24, ENFILE = 23, ENOSPC = 28, ENOENT = 2, ENOTDIR = 20, EINTR = 4;

        private sealed unsafe class INotify
        {
            [DllImport("libc", SetLastError = true)] private static extern int inotify_init1(int flags);
            [DllImport("libc", SetLastError = true)] private static extern int inotify_add_watch(int fd, byte* path, uint mask);
            [DllImport("libc", SetLastError = true)] private static extern int inotify_rm_watch(int fd, int wd);
            [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte* buffer, nuint count);
            [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
            [DllImport("libc", SetLastError = true)] private static extern int poll(long* fds, nuint count, int timeout);

            private const int INotifyEventSize = 16;
            private const int BufferSize = 16384;

            private static readonly object s_watchersLock = new object();
            private static INotify s_currentINotify;
            private readonly List<Watcher> _watchers = new List<Watcher>();
            private readonly byte[] _buffer = new byte[BufferSize];
            private int _inotifyHandle;
            private bool _handleClosed;
            private readonly Dictionary<int, Watch> _wdToWatch = new Dictionary<int, Watch>();
            // .NET's _addLock (a ReaderWriterLockSlim): adding and removing watches, and stopping.
            private readonly object _addLock = new object();
            private bool _isThreadStopping;
            private bool _allWatchersStopped;
            private int _bufferAvailable;
            private int _bufferPos;
            private WatchedDirectory[] _dirBuffer = new WatchedDirectory[4];

            public INotify()
            {
                int handle = inotify_init1(IN_CLOEXEC);
                if (handle == -1)
                {
                    int errno = Marshal.GetLastPInvokeError();
                    switch (errno)
                    {
                        case EMFILE:
                            string maxValue = ReadMaxUserLimit(MaxUserInstancesPath);
                            throw new IOException(!string.IsNullOrEmpty(maxValue)
                                ? "The configured user limit (" + maxValue + ") on the number of inotify instances has been reached, or the per-process limit on the number of open file descriptors has been reached."
                                : "The configured user limit on the number of inotify instances has been reached, or the per-process limit on the number of open file descriptors has been reached.", errno);
                        case ENFILE:
                            throw new IOException("The system limit on the number of inotify instances has been reached.", errno);
                        default:
                            throw Posix.IOError(errno, string.Empty, false);
                    }
                }
                _inotifyHandle = handle;
            }

            private bool IsStopped => _isThreadStopping || _allWatchersStopped;

            private void AddWatcher(Watcher watcher) => _watchers.Add(watcher);

            private void StartThread()
            {
                try
                {
                    Thread thread = new Thread(obj => ((INotify)obj).ProcessEvents());
                    thread.IsBackground = true;
                    thread.Name = ".NET File Watcher";
                    thread.Start(this);
                }
                catch
                {
                    StopINotify();
                    throw;
                }
            }

            private void StopINotify()
            {
                lock (_addLock) _isThreadStopping = true;
                if (!_handleClosed)
                {
                    _handleClosed = true;
                    close(_inotifyHandle);
                }
            }

            private Watch GetOrAddWatch(int wd)
            {
                lock (_wdToWatch)
                {
                    if (!_wdToWatch.TryGetValue(wd, out Watch watch)) _wdToWatch[wd] = watch = new Watch(wd);
                    return watch;
                }
            }

            private WatchedDirectory AddOrUpdateWatchedDirectory(Watcher watcher, WatchedDirectory parent, string directoryPath, uint watchFilters, bool ignoreMissing = true)
            {
                WatchedDirectory inotifyWatchesToRemove = null;
                WatchedDirectory dir;
                lock (_addLock)
                {
                    lock (watcher)
                    {
                        if (_isThreadStopping || watcher.IsWatcherStopped || (parent != null && watcher.RootDirectory == null))
                            return null;

                        uint mask = watchFilters | IN_ONLYDIR | IN_EXCL_UNLINK | (parent == null ? IN_MOVE_SELF : IN_DONT_FOLLOW);
                        mask |= IN_MASK_ADD;
                        if (watcher.IncludeSubdirectories) mask |= IN_MOVED_TO | IN_MOVED_FROM;

                        byte* path = Utf8.ToNative(directoryPath);
                        int wd = inotify_add_watch(_inotifyHandle, path, mask);
                        int errno = Marshal.GetLastPInvokeError();
                        Libc.free(path);
                        if (wd == -1)
                        {
                            if (ignoreMissing && (errno == ENOENT || errno == ENOTDIR)) return null;
                            Exception exc;
                            if (errno == ENOSPC)
                            {
                                string maxValue = ReadMaxUserLimit(MaxUserWatchesPath);
                                exc = new IOException(!string.IsNullOrEmpty(maxValue)
                                    ? "The configured user limit (" + maxValue + ") on the number of inotify watches has been reached, or the operating system failed to allocate a required resource."
                                    : "The configured user limit on the number of inotify watches has been reached, or the operating system failed to allocate a required resource.", errno);
                            }
                            else exc = Posix.IOError(errno, directoryPath, true);
                            watcher.QueueError(exc);
                            return null;
                        }

                        Watch watch = GetOrAddWatch(wd);
                        if (parent == null)
                        {
                            dir = new WatchedDirectory(watch, watcher, "", null);
                            watcher.RootDirectory = dir;
                        }
                        else
                        {
                            string name = System.IO.Path.GetFileName(directoryPath);
                            int idx = parent.FindChild(name);
                            if (idx != -1)
                            {
                                dir = parent.Children[idx];
                                if (dir.Watch == watch) return dir;
                                bool removeINotifyWatches = false;
                                RemoveWatchedDirectoryFromParentAndWatches(dir, ref removeINotifyWatches);
                                if (removeINotifyWatches) inotifyWatchesToRemove = dir;
                            }
                            dir = new WatchedDirectory(watch, watcher, name, parent);
                            parent.InitializedChildren.Add(dir);
                        }
                        lock (watch) watch.Watchers.Add(dir);
                    }
                }
                if (inotifyWatchesToRemove != null) RemoveUnusedINotifyWatches(inotifyWatchesToRemove);
                return dir;
            }

            private void RemoveWatchedDirectory(WatchedDirectory dir, int ignoredFd = -1)
            {
                bool removeINotifyWatches = false;
                RemoveWatchedDirectoryFromParentAndWatches(dir, ref removeINotifyWatches);
                if (removeINotifyWatches) RemoveUnusedINotifyWatches(dir, ignoredFd);
            }

            private void RemoveUnusedINotifyWatches(WatchedDirectory removedDir, int ignoredFd = -1)
            {
                lock (_addLock)
                {
                    if (_isThreadStopping) return;
                    RemoveINotifyWatchWhenNoMoreWatchers(removedDir.Watch, ignoredFd);
                    if (_allWatchersStopped) return;
                    lock (removedDir.Watcher)
                    {
                        if (removedDir.Children is { } children)
                            foreach (WatchedDirectory child in children) RemoveINotifyWatchWhenNoMoreWatchers(child.Watch, ignoredFd);
                    }
                }
            }

            private void RemoveINotifyWatchWhenNoMoreWatchers(Watch watch, int ignoredFd)
            {
                lock (watch)
                {
                    if (watch.Watchers.Count != 0) return;
                    bool removed;
                    lock (_wdToWatch) removed = _wdToWatch.Remove(watch.WatchDescriptor);
                    if (removed && watch.WatchDescriptor != ignoredFd) inotify_rm_watch(_inotifyHandle, watch.WatchDescriptor);
                }
            }

            private void RemoveWatchedDirectoryFromParentAndWatches(WatchedDirectory dir, ref bool removeINotifyWatches)
            {
                if (dir.IsRootDir)
                {
                    lock (s_watchersLock)
                    {
                        _watchers.Remove(dir.Watcher);
                        _allWatchersStopped = _watchers.Count == 0;
                    }
                }
                Watcher watcher = dir.Watcher;
                lock (watcher)
                {
                    if (dir.IsRootDir)
                    {
                        if (watcher.RootDirectory == null) return;
                        watcher.RootDirectory = null;
                    }
                    else
                    {
                        int idx = dir.Parent.FindChild(dir.Name);
                        if (idx == -1) return;
                        dir.Parent.Children.RemoveAt(idx);
                    }
                    RemoveFromWatch(dir, ref removeINotifyWatches);
                    if (dir.Children is { } children)
                        foreach (var child in children) RemoveFromWatch(child, ref removeINotifyWatches);
                }
            }

            private static void RemoveFromWatch(WatchedDirectory dir, ref bool removeINotifyWatches)
            {
                Watch watch = dir.Watch;
                lock (watch)
                {
                    watch.Watchers.Remove(dir);
                    removeINotifyWatches |= watch.Watchers.Count == 0;
                }
            }

            private void ProcessEvents()
            {
                try
                {
                    lock (s_watchersLock)
                    {
                        if (_watchers.Count == 0)
                        {
                            StopINotify();
                            return;
                        }
                    }
                    var moved = new MovedFrom();
                    while (TryReadEvent(out NotifyEvent nextEvent))
                        if (!ProcessEvent(nextEvent, moved)) break;
                }
                catch (Exception ex)
                {
                    lock (s_watchersLock)
                    {
                        StopINotify();
                        foreach (var watcher in _watchers) watcher.QueueError(ex);
                    }
                }
            }

            /// <summary>Carried over from an IN_MOVED_FROM to the IN_MOVED_TO that may follow it.</summary>
            private sealed class MovedFrom
            {
                internal int WatchCount;
                internal string Name = "";
                internal uint Cookie;
                internal bool IsDir;
            }

            private const uint FileDirEvents = IN_CREATE | IN_DELETE | IN_MOVED_FROM | IN_MOVED_TO;
            private const uint EventMask = FileDirEvents | IN_ACCESS | IN_MODIFY | IN_ATTRIB;

            private bool ProcessEvent(NotifyEvent nextEvent, MovedFrom moved)
            {
                char[] pathBuffer = new char[PATH_MAX];
                uint mask = nextEvent.mask;

                if ((mask & IN_Q_OVERFLOW) != 0)
                {
                    lock (s_watchersLock)
                    {
                        StopINotify();
                        foreach (var watcher in _watchers) watcher.QueueError(CreateBufferOverflowException(watcher.BasePath));
                    }
                    return false;
                }

                WatchedDirectory[] movedFromDirs = Slice(_dirBuffer, 0, moved.WatchCount);

                // Synchronize with AddOrUpdateWatchedDirectory: newly added watch descriptors are in _wdToWatch.
                lock (_addLock) { }
                Watch watch;
                lock (_wdToWatch) _wdToWatch.TryGetValue(nextEvent.wd, out watch);

                WatchedDirectory[] dirs = watch != null ? GetWatchedDirectories(watch, ref _dirBuffer, movedFromDirs.Length) : Array.Empty<WatchedDirectory>();
                // GetWatchedDirectories may have grown _dirBuffer: the moved-from entries are copied with it.
                movedFromDirs = Slice(_dirBuffer, 0, movedFromDirs.Length);

                if (movedFromDirs.Length != 0)
                {
                    bool isMatchingMovedTo = (mask & IN_MOVED_TO) != 0 && moved.Cookie == nextEvent.cookie;
                    foreach (var movedFrom in movedFromDirs)
                    {
                        bool isRename = isMatchingMovedTo && FindMatchingWatchedDirectory(dirs, movedFrom.Watcher) != null;
                        if (isRename) continue;
                        if (moved.IsDir) RemoveWatchedDirectoryChild(movedFrom, moved.Name);
                        var watcher = movedFrom.Watcher;
                        if (!IsIgnoredEvent(watcher, IN_DELETE, moved.IsDir))
                            watcher.QueueEvent(WatcherEvent.Deleted(movedFrom, moved.Name));
                    }
                    if (!isMatchingMovedTo) movedFromDirs = Array.Empty<WatchedDirectory>();
                }

                bool isDir = (mask & (IN_ISDIR | IN_IGNORED)) != 0;

                if ((mask & IN_MOVED_FROM) != 0)
                {
                    bool eventAvailable = _bufferPos != _bufferAvailable;
                    if (!eventAvailable)
                    {
                        // struct pollfd { int fd; short events; short revents; }: a short wait for the MOVED_TO.
                        long pollfd = (uint)_inotifyHandle | (1L << 32) /* POLLIN */;
                        int ready = poll(&pollfd, 1, 2);
                        eventAvailable = ready > 0 && (pollfd >> 48) != 0;
                    }
                    if (eventAvailable)
                    {
                        moved.Name = nextEvent.name;
                        Array.Copy(dirs, 0, _dirBuffer, 0, dirs.Length);
                        moved.WatchCount = dirs.Length;
                        moved.Cookie = nextEvent.cookie;
                        moved.IsDir = isDir;
                        return true;
                    }
                }
                moved.WatchCount = 0;

                foreach (WatchedDirectory dir in dirs)
                {
                    Watcher watcher = dir.Watcher;
                    WatchedDirectory matchingFrom = (mask & IN_MOVED_TO) != 0 ? FindMatchingWatchedDirectory(movedFromDirs, watcher) : null;

                    if (isDir && watcher.IncludeSubdirectories)
                    {
                        if ((mask & (IN_CREATE | IN_MOVED_TO)) != 0)
                        {
                            if (matchingFrom != null) RenameWatchedDirectories(dir, nextEvent.name, matchingFrom, moved.Name);
                            string directoryPath = dir.GetPath(nextEvent.name, pathBuffer, fullPath: true);
                            watcher.WatchChildDirectories(dir, directoryPath);
                        }
                        else if ((mask & IN_MOVED_FROM) != 0)
                        {
                            RemoveWatchedDirectoryChild(dir, nextEvent.name);
                        }
                    }
                    if ((mask & IN_IGNORED) != 0 || ((mask & IN_MOVE_SELF) != 0 && dir.IsRootDir))
                    {
                        int ignoredFd = (mask & IN_IGNORED) != 0 ? nextEvent.wd : -1;
                        RemoveWatchedDirectory(dir, ignoredFd);
                        if (dir.IsRootDir && !watcher.IsWatcherStopped)
                            watcher.QueueError(CreateWatchedDirectoryDeletedOrMovedException(watcher.BasePath));
                        continue;
                    }

                    // To match Windows, no events for the root directory itself.
                    if (dir.IsRootDir && nextEvent.name.Length == 0) continue;
                    if (IsIgnoredEvent(watcher, mask, isDir)) continue;

                    switch (mask & EventMask)
                    {
                        case IN_CREATE:
                            watcher.QueueEvent(WatcherEvent.Created(dir, nextEvent.name));
                            break;
                        case IN_DELETE:
                            watcher.QueueEvent(WatcherEvent.Deleted(dir, nextEvent.name));
                            break;
                        case IN_ACCESS:
                        case IN_MODIFY:
                        case IN_ATTRIB:
                            watcher.QueueEvent(WatcherEvent.Changed(dir, nextEvent.name));
                            break;
                        case IN_MOVED_FROM:
                            watcher.QueueEvent(WatcherEvent.Deleted(dir, nextEvent.name));
                            break;
                        case IN_MOVED_TO:
                            watcher.QueueEvent(matchingFrom != null
                                ? WatcherEvent.Renamed(dir, nextEvent.name, matchingFrom, moved.Name)
                                : WatcherEvent.Created(dir, nextEvent.name));
                            break;
                    }
                }

                if (_allWatchersStopped)
                {
                    StopINotify();
                    return false;
                }
                return true;
            }

            private static WatchedDirectory[] Slice(WatchedDirectory[] buffer, int offset, int count)
            {
                var result = new WatchedDirectory[count];
                Array.Copy(buffer, offset, result, 0, count);
                return result;
            }

            private void RemoveWatchedDirectoryChild(WatchedDirectory dir, string movedFromName)
            {
                Watcher watcher = dir.Watcher;
                WatchedDirectory child = null;
                lock (watcher)
                {
                    int idx = dir.FindChild(movedFromName);
                    if (idx != -1) child = dir.Children[idx];
                }
                if (child != null) RemoveWatchedDirectory(child);
            }

            private static WatchedDirectory[] GetWatchedDirectories(Watch watch, ref WatchedDirectory[] buffer, int offset)
            {
                lock (watch)
                {
                    int watchersCount = watch.Watchers.Count;
                    int lengthNeeded = watchersCount + offset;
                    if (lengthNeeded > buffer.Length) Array.Resize(ref buffer, lengthNeeded);
                    watch.Watchers.CopyTo(buffer, offset);
                    return Slice(buffer, offset, watchersCount);
                }
            }

            private static WatchedDirectory FindMatchingWatchedDirectory(WatchedDirectory[] dirs, Watcher watcher)
            {
                foreach (var d in dirs)
                    if (d.Watcher == watcher) return d;
                return null;
            }

            private static bool IsIgnoredEvent(Watcher watcher, uint mask, bool isDir) =>
                (watcher.WatchFilters & mask) == 0 ||
                ((mask & FileDirEvents) != 0 &&
                    ((isDir && (watcher.NotifyFilters & NotifyFilters.DirectoryName) == 0) ||
                     (!isDir && (watcher.NotifyFilters & NotifyFilters.FileName) == 0)));

            private void RenameWatchedDirectories(WatchedDirectory moveTo, string moveToName, WatchedDirectory moveFrom, string moveFromName)
            {
                WatchedDirectory sourceToRemove = null;
                Watcher watcher = moveFrom.Watcher;
                lock (watcher)
                {
                    int sourceIdx = moveFrom.FindChild(moveFromName);
                    if (sourceIdx == -1) return;
                    WatchedDirectory source = moveFrom.Children[sourceIdx];
                    int dstIdx = moveTo.FindChild(moveToName);
                    if (dstIdx != -1) sourceToRemove = source;
                    else
                    {
                        moveFrom.Children.RemoveAt(sourceIdx);
                        WatchedDirectory renamed = CreateWatchedDirectoryFrom(moveTo, source, moveToName);
                        moveTo.InitializedChildren.Add(renamed);
                    }
                }
                if (sourceToRemove != null) RemoveWatchedDirectory(sourceToRemove);
            }

            private static WatchedDirectory CreateWatchedDirectoryFrom(WatchedDirectory parent, WatchedDirectory src, string name)
            {
                Watcher watcher = src.Watcher;
                WatchedDirectory newDir;
                Watch watch = src.Watch;
                lock (watch)
                {
                    newDir = new WatchedDirectory(watch, watcher, name, parent);
                    watch.Watchers.Remove(src);
                    watch.Watchers.Add(newDir);
                }
                if (src.Children is { } children)
                    foreach (var child in children) newDir.InitializedChildren.Add(CreateWatchedDirectoryFrom(newDir, child, child.Name));
                return newDir;
            }

            private bool TryReadEvent(out NotifyEvent notifyEvent)
            {
                if (_bufferAvailable == 0 || _bufferPos == _bufferAvailable)
                {
                    // Blocks until data is available or all watches are gone (0 bytes).
                    nint n;
                    fixed (byte* buf = &_buffer[0])
                    {
                        do n = read(_inotifyHandle, buf, (nuint)_buffer.Length);
                        while (n < 0 && Marshal.GetLastPInvokeError() == EINTR);
                    }
                    if (n < 0) throw Posix.IOError(Marshal.GetLastPInvokeError(), string.Empty, false);
                    _bufferAvailable = (int)n;
                    if (_bufferAvailable == 0)
                    {
                        notifyEvent = default;
                        return false;
                    }
                    _bufferPos = 0;
                }
                // struct inotify_event { int wd; uint32_t mask; uint32_t cookie; uint32_t len; char name[]; }
                NotifyEvent readEvent;
                readEvent.wd = BitConverter.ToInt32(_buffer, _bufferPos);
                readEvent.mask = BitConverter.ToUInt32(_buffer, _bufferPos + 4);
                readEvent.cookie = BitConverter.ToUInt32(_buffer, _bufferPos + 8);
                int nameLength = (int)BitConverter.ToUInt32(_buffer, _bufferPos + 12);
                readEvent.name = ReadName(_bufferPos + INotifyEventSize, nameLength);
                _bufferPos += INotifyEventSize + nameLength;
                notifyEvent = readEvent;
                return true;
            }

            private string ReadName(int position, int nameLength)
            {
                int lengthWithoutNullTerm = _buffer.AsSpan(position, nameLength).IndexOf((byte)0);
                if (lengthWithoutNullTerm < 0) lengthWithoutNullTerm = nameLength;
                return lengthWithoutNullTerm > 0 ? Encoding.UTF8.GetString(_buffer, position, lengthWithoutNullTerm) : string.Empty;
            }

            private struct NotifyEvent
            {
                internal int wd;
                internal uint mask;
                internal uint cookie;
                internal string name;
            }

            internal sealed class WatcherEvent
            {
                public const WatcherChangeTypes ErrorType = WatcherChangeTypes.All;

                public string Name;
                public WatchedDirectory Directory;
                public string OldName;
                public WatchedDirectory OldDirectory;
                public Exception Exception;
                public WatcherChangeTypes Type;

                private static WatcherEvent Make(WatcherChangeTypes type, WatchedDirectory dir, string name, WatchedDirectory oldDir = null, string oldName = null) =>
                    new WatcherEvent { Type = type, Directory = dir, Name = name, OldDirectory = oldDir, OldName = oldName };

                public static WatcherEvent Deleted(WatchedDirectory dir, string name) => Make(WatcherChangeTypes.Deleted, dir, name);
                public static WatcherEvent Created(WatchedDirectory dir, string name) => Make(WatcherChangeTypes.Created, dir, name);
                public static WatcherEvent Changed(WatchedDirectory dir, string name) => Make(WatcherChangeTypes.Changed, dir, name);
                public static WatcherEvent Renamed(WatchedDirectory dir, string name, WatchedDirectory oldDir, string oldName) =>
                    Make(WatcherChangeTypes.Renamed, dir, name, oldDir, oldName);
                public static WatcherEvent Error(Exception exception) => new WatcherEvent { Type = ErrorType, Exception = exception };

                public string GetName(char[] pathBuffer) => Directory.GetPath(Name, pathBuffer);
                public string GetOldName(char[] pathBuffer) => OldDirectory.GetPath(OldName, pathBuffer);
            }

            public sealed class Watcher
            {
                private readonly WeakReference<FileSystemWatcherImpl> _weakFsw;
                // .NET's unbounded single-reader Channel: events in order, handlers on the pool.
                private readonly Queue<WatcherEvent> _eventQueue = new Queue<WatcherEvent>();
                private bool _draining, _completed;
                private INotify _inotify;
                private bool _emitEvents;

                public string BasePath { get; }
                public NotifyFilters NotifyFilters { get; }
                public uint WatchFilters { get; }
                public bool IncludeSubdirectories { get; }
                public bool IsWatcherStopped { get; set; }
                public WatchedDirectory RootDirectory { get; set; }

                public Watcher(FileSystemWatcherImpl fsw)
                {
                    _weakFsw = new WeakReference<FileSystemWatcherImpl>(fsw);
                    BasePath = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(fsw.Path));
                    IncludeSubdirectories = fsw.IncludeSubdirectories;
                    NotifyFilters = fsw.NotifyFilter;
                    WatchFilters = TranslateFilters(NotifyFilters);
                }

                public void Start()
                {
                    INotify inotify;
                    WatchedDirectory rootDirectory;
                    lock (s_watchersLock)
                    {
                        inotify = s_currentINotify;
                        if (inotify == null || inotify.IsStopped)
                        {
                            inotify = new INotify();
                            inotify.StartThread();
                            s_currentINotify = inotify;
                        }
                        _inotify = inotify;
                        rootDirectory = _inotify.AddOrUpdateWatchedDirectory(this, null, BasePath, WatchFilters, ignoreMissing: false);
                        if (rootDirectory != null) _inotify.AddWatcher(this);
                    }
                    if (rootDirectory != null && IncludeSubdirectories) WatchChildDirectories(rootDirectory, BasePath, includeBasePath: false);
                    _emitEvents = true;
                }

                public void Stop()
                {
                    WatchedDirectory root;
                    lock (this)
                    {
                        if (IsWatcherStopped) return;
                        IsWatcherStopped = true;
                        _emitEvents = false;
                        root = RootDirectory;
                    }
                    lock (_eventQueue) _completed = true;
                    if (root != null) _inotify.RemoveWatchedDirectory(root);
                }

                private void Enqueue(WatcherEvent ev)
                {
                    lock (_eventQueue)
                    {
                        if (_completed) return;
                        _eventQueue.Enqueue(ev);
                        if (_draining) return;
                        _draining = true;
                    }
                    ThreadPool.UnsafeQueueUserWorkItem(static w => w.DequeueEvents(), this, false);
                }

                private void DequeueEvents()
                {
                    char[] pathBuffer = new char[PATH_MAX];
                    while (true)
                    {
                        WatcherEvent ev;
                        lock (_eventQueue)
                        {
                            if (_eventQueue.Count == 0 || IsWatcherStopped)
                            {
                                _eventQueue.Clear();
                                _draining = false;
                                return;
                            }
                            ev = _eventQueue.Dequeue();
                        }
                        try
                        {
                            EmitEvent(ev, pathBuffer);
                        }
                        catch (Exception ex)
                        {
                            // .NET's DequeueEvents: a throwing handler stops the watcher and raises Error.
                            if (!IsWatcherStopped)
                            {
                                Stop();
                                try { Fsw?.OnError(new ErrorEventArgs(ex)); }
                                catch { }
                            }
                        }
                    }
                }

                private void EmitEvent(WatcherEvent ev, char[] pathBuffer)
                {
                    FileSystemWatcherImpl fsw = Fsw;
                    if (fsw == null) return;
                    switch (ev.Type)
                    {
                        case WatcherEvent.ErrorType:
                            fsw.OnError(new ErrorEventArgs(ev.Exception));
                            if (ev.Exception is InternalBufferOverflowException && !IsWatcherStopped) fsw.Restart();
                            break;
                        case WatcherChangeTypes.Created:
                        case WatcherChangeTypes.Deleted:
                        case WatcherChangeTypes.Changed:
                            fsw.NotifyFileSystemEventArgs(ev.Type, ev.GetName(pathBuffer).AsSpan());
                            break;
                        case WatcherChangeTypes.Renamed:
                            fsw.NotifyRenameEventArgs(WatcherChangeTypes.Renamed, ev.GetName(pathBuffer).AsSpan(), ev.GetOldName(pathBuffer).AsSpan());
                            break;
                    }
                }

                internal bool WatchChildDirectories(WatchedDirectory parent, string path, bool includeBasePath = true)
                {
                    if (IsWatcherStopped) return false;
                    if (includeBasePath)
                    {
                        WatchedDirectory newParent = _inotify.AddOrUpdateWatchedDirectory(this, parent, path, WatchFilters, ignoreMissing: true);
                        if (newParent == null) return true;
                        parent = newParent;
                    }
                    try
                    {
                        // Not following links (EnumerationOptions.AttributesToSkip = ReparsePoint in .NET).
                        foreach (string childDir in Directory.EnumerateDirectories(path))
                        {
                            FileAttributes attributes = FileStatusImpl.GetAttributes(childDir, System.IO.Path.GetFileName(childDir));
                            if (attributes != (FileAttributes)(-1) && (attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if (!WatchChildDirectories(parent, childDir)) return false;
                        }
                    }
                    catch (DirectoryNotFoundException) { }
                    catch (IOException) when (File.Exists(path)) { } // path was replaced by a file
                    catch (Exception ex) { QueueError(ex); }
                    return true;
                }

                internal void QueueEvent(WatcherEvent ev)
                {
                    if (!_emitEvents) return;
                    Enqueue(ev);
                }

                internal void QueueError(Exception exception)
                {
                    if (IsWatcherStopped) return;
                    Enqueue(WatcherEvent.Error(exception));
                }

                private FileSystemWatcherImpl Fsw
                {
                    get
                    {
                        _weakFsw.TryGetTarget(out FileSystemWatcherImpl watcher);
                        return watcher;
                    }
                }

                private static uint TranslateFilters(NotifyFilters filters)
                {
                    uint result = IN_CREATE | IN_DELETE;
                    const NotifyFilters filtersForAccess = NotifyFilters.LastAccess;
                    const NotifyFilters filtersForModify = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size;
                    const NotifyFilters filtersForAttrib = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.LastAccess
                        | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size;
                    if ((filters & filtersForAccess) != 0) result |= IN_ACCESS;
                    if ((filters & filtersForModify) != 0) result |= IN_MODIFY;
                    if ((filters & filtersForAttrib) != 0) result |= IN_ATTRIB;
                    if ((filters & (NotifyFilters.FileName | NotifyFilters.DirectoryName)) != 0) result |= IN_MOVED_FROM | IN_MOVED_TO;
                    return result;
                }
            }

            internal sealed class WatchedDirectory
            {
                public Watch Watch { get; }
                public Watcher Watcher { get; }
                public string Name { get; }
                public WatchedDirectory Parent { get; }
                public bool IsRootDir => Parent == null;
                public List<WatchedDirectory> Children { get; set; }

                public WatchedDirectory(Watch watch, Watcher watcher, string name, WatchedDirectory parent)
                {
                    Watch = watch;
                    Watcher = watcher;
                    Name = name;
                    Parent = parent;
                }

                public List<WatchedDirectory> InitializedChildren => Children ??= new List<WatchedDirectory>();

                public int FindChild(string name)
                {
                    var children = Children;
                    if (children == null) return -1;
                    for (int i = 0; i < children.Count; i++)
                        if (children[i].Name == name) return i;
                    return -1;
                }

                /// <summary>The path relative to the watched root (or the full path).</summary>
                internal string GetPath(string childName, char[] pathBuffer, bool fullPath = false)
                {
                    var sb = new StringBuilder();
                    AppendPath(sb, childName, fullPath);
                    return sb.ToString();
                }

                private void AppendPath(StringBuilder sb, string childName, bool fullPath)
                {
                    if (Parent != null)
                    {
                        Parent.AppendPath(sb, "", fullPath);
                        fullPath = false;
                    }
                    if (fullPath) Append(sb, Watcher.BasePath);
                    Append(sb, Name);
                    Append(sb, childName);
                }

                private static void Append(StringBuilder sb, string path)
                {
                    if (string.IsNullOrEmpty(path)) return;
                    if (sb.Length != 0 && sb[sb.Length - 1] != '/') sb.Append('/');
                    sb.Append(path);
                }
            }

            internal sealed class Watch
            {
                public int WatchDescriptor { get; }
                public List<WatchedDirectory> Watchers { get; } = new List<WatchedDirectory>();
                public Watch(int watchDescriptor) { WatchDescriptor = watchDescriptor; }
            }
        }
    }
}
#endif
