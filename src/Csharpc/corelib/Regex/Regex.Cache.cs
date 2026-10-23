// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;

namespace CsharpcCoreLib.RegularExpressions
{
    public partial class Regex
    {
        public static int CacheSize
        {
            get => RegexCache.MaxCacheSize;
            set
            {
                if (value < 0)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.value);
                }

                RegexCache.MaxCacheSize = value;
            }
        }
    }

    internal sealed class RegexCache
    {
        // The implementation is optimized to make cache hits fast and lock-free, only taking a global lock
        // when adding a new Regex to the cache.  Previous implementations of the cache took a global lock
        // on all accesses, negatively impacting scalability, in order to minimize costs when the cache
        // limit was hit and items needed to be dropped.  In such situations, however, we're having to
        // pay the relatively hefty cost of creating a new Regex, anyway, and if the consuming app cares
        // about such costs, it should either increase Regex.CacheSize or do its own Regex instance caching.

        private const int DefaultMaxCacheSize = 15;
        private const int MaxExamineOnDrop = 30;

        private static volatile Node? s_lastAccessed;
        // csharpc: a Dictionary guarded by the lock instead of a ConcurrentDictionary (reads take the lock too),
        // and lazily created collections (corelib types get no static constructor).
        private static Dictionary<Key, Node>? s_cacheDictionaryData;
        private static Dictionary<Key, Node> s_cacheDictionary => s_cacheDictionaryData ??= new Dictionary<Key, Node>(31);
        private static List<Node>? s_cacheListData;
        private static List<Node> s_cacheList => s_cacheListData ??= new List<Node>(DefaultMaxCacheSize);
        // csharpc: a small xorshift generator instead of System.Random (only picks eviction candidates).
        private static uint s_randomState;
        private static int NextRandom(int maxValue)
        {
            uint x = s_randomState == 0 ? 2463534242u : s_randomState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            s_randomState = x;
            return (int)(x % (uint)maxValue);
        }
        private static int s_maxCacheSizeMinusDefault; // csharpc: zero-initialized; the size is DefaultMaxCacheSize + this
        private static int s_maxCacheSize
        {
            get => s_maxCacheSizeMinusDefault + DefaultMaxCacheSize;
            set => s_maxCacheSizeMinusDefault = value - DefaultMaxCacheSize;
        }

        private static object? s_syncObj;
        private static object SyncObj => s_syncObj ?? Interlocked.CompareExchange(ref s_syncObj, new object(), null) ?? s_syncObj;

        public static int MaxCacheSize
        {
            get
            {
                lock (SyncObj)
                {
                    return s_maxCacheSize;
                }
            }
            set
            {
                Debug.Assert(value >= 0);

                lock (SyncObj)
                {
                    // Store the new max cache size
                    s_maxCacheSize = value;

                    if (value == 0)
                    {
                        // If the value is being changed to zero, just clear out the cache.
                        s_cacheDictionary.Clear();
                        s_cacheList.Clear();
                        s_lastAccessed = null;
                    }
                    else if (value < s_cacheList.Count)
                    {
                        // If the value is being changed to less than the number of items we're currently storing,
                        // just trim off the excess.  This is almost never done in practice (if Regex.CacheSize is set
                        // at all, it's almost always done once towards the beginning of the process, and when it is done,
                        // it's typically to either 0 or to a larger value than the current limit), so we're not concerned
                        // with ensuring the actual oldest items are trimmed away.
                        s_lastAccessed = s_cacheList[0];
                        for (int i = value; i < s_cacheList.Count; i++)
                        {
                            s_cacheDictionary.Remove(s_cacheList[i].Key);
                        }
                        s_cacheList.RemoveRange(value, s_cacheList.Count - value);

                        Debug.Assert(s_cacheList.Count == value);
                        Debug.Assert(s_cacheDictionary.Count == value);
                    }
                }
            }
        }

        public static Regex GetOrAdd(string pattern)
        {
            // Does not delegate to GetOrAdd(..., RegexOptions, ...) in order to avoid having
            // a statically-reachable path to the 'new Regex(..., RegexOptions, ...)', which
            // will force the Regex compiler to be reachable and thus rooted for trimming.

            Regex.ValidatePattern(pattern);

            CultureInfo culture = CultureInfo.CurrentCulture;
            Key key = new Key(pattern, culture.ToString(), RegexOptions.None, Regex.s_defaultMatchTimeout);

            Regex? regex = Get(key);
            if (regex is null)
            {
                regex = new Regex(pattern, culture);
                Add(key, regex);
            }

            return regex;
        }

        public static Regex GetOrAdd(string pattern, RegexOptions options, TimeSpan matchTimeout)
        {
            Regex.ValidatePattern(pattern);
            Regex.ValidateOptions(options);
            Regex.ValidateMatchTimeout(matchTimeout);

            CultureInfo culture = RegexParser.GetTargetCulture(options);
            Key key = new Key(pattern, culture.ToString(), options, matchTimeout);

            Regex? regex = Get(key);
            if (regex is null)
            {
                regex = new Regex(pattern, options, matchTimeout, culture);
                Add(key, regex);
            }

            return regex;
        }

        private static Regex? Get(Key key)
        {
            long lastAccessedStamp = 0;

            // We optimize for repeated usage of the same regular expression over and over,
            // by having a fast-path that stores the most recently used instance.  Check
            // to see if that instance is the one we want; if it is, we're done.
            if (s_lastAccessed is Node lastAccessed)
            {
                if (key.Equals(lastAccessed.Key))
                {
                    return lastAccessed.Regex;
                }

                // We had a last accessed item, but it didn't match the one being requested.
                // In case we need to replace the last accessed node, remember this one's stamp;
                // we'll use it to compute the new access value for the new node replacing it.
                lastAccessedStamp = Volatile.Read(ref lastAccessed.LastAccessStamp);
            }

            // Now consult the full cache.
            Node? node = null;
            bool found;
            lock (SyncObj)
            {
                found = s_maxCacheSize != 0 && s_cacheDictionary.TryGetValue(key, out node);
            }
            if (found)
            {
                // We found our item in the cache. Make this node's last access stamp one higher than
                // the previous one.  It's ok if multiple threads racing to update the last access cause
                // multiple nodes to have the same value; it's an approximate value meant only to help
                // remove the least valuable items when an item needs to be dropped from the cache.  We
                // do, however, need to read the old value and write the new value using Volatile.Read/Write,
                // in order to prevent tearing of the 64-bit value on 32-bit platforms, and to help ensure
                // that another thread subsequently sees this updated value.
                Volatile.Write(ref node.LastAccessStamp, lastAccessedStamp + 1);

                // Update our fast-path single-field cache.
                s_lastAccessed = node;

                // Return the cached regex.
                return node.Regex;
            }

            // Not in the cache.
            return null;
        }

        private static void Add(Key key, Regex regex)
        {
            lock (SyncObj)
            {
                Debug.Assert(s_cacheList.Count == s_cacheDictionary.Count);

                // If the cache has been disabled, there's nothing to add. And if between just checking
                // the cache in the caller and taking the lock, another thread could have added the regex.
                // If that occurred, there's also nothing to add, and we don't bother to update any of the
                // time stamp / fast-path field information, because hitting this race condition means it
                // was just updated, and we gain little by updating it again.
                if (s_maxCacheSize == 0 || s_cacheDictionary.TryGetValue(key, out _))
                {
                    return;
                }

                // If the cache is full, remove an item to make room for the new one.
                if (s_cacheList.Count == s_maxCacheSize)
                {
                    int itemsToExamine;
                    bool useRandom;

                    if (s_maxCacheSize <= MaxExamineOnDrop)
                    {
                        // Our maximum cache size is <= the number of items we're willing to examine (which is kept small simply
                        // to avoid spending a lot of time).  As such, we can just examine the whole list.
                        itemsToExamine = s_cacheList.Count;
                        useRandom = false;
                    }
                    else
                    {
                        // Our maximum cache size is > the number of items we're willing to examine, so we'll instead
                        // examine a random subset.  This isn't perfect: if the size of the list is only a tiny bit
                        // larger than the max we're willing to examine, there's a good chance we'll look at some of
                        // the same items twice.  That's fine; this doesn't need to be perfect.  We do not need a perfect LRU
                        // cache, just one that generally gets rid of older things when new things come in.
                        itemsToExamine = MaxExamineOnDrop;
                        useRandom = true;
                    }

                    // Pick the first item to use as the min.
                    int minListIndex = useRandom ? NextRandom(s_cacheList.Count) : 0;
                    long min = Volatile.Read(ref s_cacheList[minListIndex].LastAccessStamp);

                    // Now examine the rest, keeping track of the smallest access stamp we find.
                    for (int i = 1; i < itemsToExamine; i++)
                    {
                        int nextIndex = useRandom ? NextRandom(s_cacheList.Count) : i;
                        long next = Volatile.Read(ref s_cacheList[nextIndex].LastAccessStamp);
                        if (next < min)
                        {
                            minListIndex = nextIndex;
                            min = next;
                        }
                    }

                    // Remove the key found to have the smallest access stamp. List ordering isn't important, so rather than
                    // just removing the element at minListIndex, which would result in an O(N) shift down, we copy the last
                    // element to minListIndex, and then remove the last. (If minListIndex is the last, this is a no-op.)
                    s_cacheDictionary.Remove(s_cacheList[minListIndex].Key);
                    s_cacheList[minListIndex] = s_cacheList[^1];
                    s_cacheList.RemoveAt(s_cacheList.Count - 1);
                }

                // Finally add the regex.
                var node = new Node(key, regex);

                if (s_lastAccessed is { } lastAccessed)
                {
                    node.LastAccessStamp = Volatile.Read(ref lastAccessed.LastAccessStamp) + 1;
                }

                s_lastAccessed = node;
                s_cacheList.Add(node);
                s_cacheDictionary.TryAdd(key, node);

                Debug.Assert(s_cacheList.Count <= s_maxCacheSize);
                Debug.Assert(s_cacheList.Count == s_cacheDictionary.Count);
            }
        }

        internal readonly struct Key : IEquatable<Key>
        {
            private readonly string _pattern;
            private readonly string _culture;
            private readonly RegexOptions _options;
            private readonly TimeSpan _matchTimeout;

            public Key(string pattern, string culture, RegexOptions options, TimeSpan matchTimeout)
            {
                Debug.Assert(pattern != null, "Pattern must be provided");
                Debug.Assert(culture != null, "Culture must be provided");

                _pattern = pattern;
                _culture = culture;
                _options = options;
                _matchTimeout = matchTimeout;
            }

            public override bool Equals([NotNullWhen(true)] object? obj) =>
                obj is Key other && Equals(other);

            public bool Equals(Key other) =>
                _pattern.Equals(other._pattern) &&
                _culture.Equals(other._culture) &&
                _options == other._options &&
                _matchTimeout == other._matchTimeout;

            public override int GetHashCode() =>
                // Hash code only factors in pattern and options, as regex instances are unlikely to have
                // the same pattern and options but different culture and timeout.
                _pattern.GetHashCode() ^ (int)_options;
        }

        private sealed class Node
        {
            public readonly Key Key;
            public readonly Regex Regex;
            public long LastAccessStamp;

            public Node(Key key, Regex regex)
            {
                Key = key;
                Regex = regex;
            }
        }
    }
}
