// Environment.GetFolderPath on Unix: port of dotnet/runtime's Environment.GetFolderPathCore.Unix.cs
// (release/11.0, MIT): the XDG base directories and user-dirs.dirs, $HOME (or the password database).
// macOS's NSSearchPath folders come from NSFileManager as .NET's SystemNative_SearchPath; Windows' known folders are not ported yet.
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Environment")]
    internal static unsafe class SpecialFolders
    {
        public static string GetFolderPath(Environment.SpecialFolder folder) => GetFolderPath(folder, Environment.SpecialFolderOption.None);

        public static string GetFolderPath(Environment.SpecialFolder folder, Environment.SpecialFolderOption option)
        {
            if (!Enum.IsDefined(option))
                throw new ArgumentOutOfRangeException("option", option, "Illegal enum value: " + option + ".");
#if CSHARPC_TARGET_WINDOWS
            throw new NotSupportedException("Environment.GetFolderPath on Windows is not supported by csharpc yet.");
#else
            string path = GetSpecialFolder(folder) ?? string.Empty;
            if (path.Length == 0 || option == Environment.SpecialFolderOption.DoNotVerify || Readable(path)) return path;
            if (option == Environment.SpecialFolderOption.None) return string.Empty;
            Directory.CreateDirectory(path);
            return path;
#endif
        }

#if !CSHARPC_TARGET_WINDOWS
        [DllImport("libc")] private static extern int access(byte* path, int mode);

        private static bool Readable(string path)
        {
            byte* p = Utf8.ToNative(path);
            int rv = access(p, 4 /* R_OK */);
            Libc.free(p);
            return rv == 0;
        }

        private static string GetSpecialFolder(Environment.SpecialFolder folder)
        {
            switch (folder)
            {
                case Environment.SpecialFolder.CommonApplicationData: return "/usr/share";
                case Environment.SpecialFolder.CommonTemplates: return "/usr/share/templates";
#if CSHARPC_TARGET_MACOS
                case Environment.SpecialFolder.ProgramFiles: return "/Applications";
                case Environment.SpecialFolder.System: return "/System";
#endif
            }
            string home = HomeDirectory();
            if (string.IsNullOrEmpty(home)) home = "/";
            switch (folder)
            {
                case Environment.SpecialFolder.UserProfile: return home;
                case Environment.SpecialFolder.Templates: return ReadXdgDirectory(home, "XDG_TEMPLATES_DIR", "Templates");
#if CSHARPC_TARGET_MACOS
                // NSSearchPathDirectory values (Interop.SearchPath.cs): the user domain's folder, whatever $HOME says (#315).
                case Environment.SpecialFolder.Desktop:
                case Environment.SpecialFolder.DesktopDirectory: return SearchPath(12 /* NSDesktopDirectory */);
                case Environment.SpecialFolder.ApplicationData:
                case Environment.SpecialFolder.LocalApplicationData: return SearchPath(14 /* NSApplicationSupportDirectory */);
                case Environment.SpecialFolder.MyDocuments: return SearchPath(9 /* NSDocumentDirectory */);
                case Environment.SpecialFolder.MyMusic: return SearchPath(18 /* NSMusicDirectory */);
                case Environment.SpecialFolder.MyVideos: return SearchPath(17 /* NSMoviesDirectory */);
                case Environment.SpecialFolder.MyPictures: return SearchPath(19 /* NSPicturesDirectory */);
                case Environment.SpecialFolder.InternetCache: return SearchPath(13 /* NSCachesDirectory */);
                case Environment.SpecialFolder.Fonts: return Path.Combine(home, "Library", "Fonts");
                case Environment.SpecialFolder.Favorites: return Path.Combine(home, "Library", "Favorites");
#else
                case Environment.SpecialFolder.Desktop:
                case Environment.SpecialFolder.DesktopDirectory: return ReadXdgDirectory(home, "XDG_DESKTOP_DIR", "Desktop");
                case Environment.SpecialFolder.ApplicationData: return XdgConfig(home);
                case Environment.SpecialFolder.LocalApplicationData:
                    string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                    if (data == null || !data.StartsWith('/')) data = Path.Combine(home, ".local", "share");
                    return data;
                case Environment.SpecialFolder.MyDocuments: return ReadXdgDirectory(home, "XDG_DOCUMENTS_DIR", "Documents");
                case Environment.SpecialFolder.MyMusic: return ReadXdgDirectory(home, "XDG_MUSIC_DIR", "Music");
                case Environment.SpecialFolder.MyVideos: return ReadXdgDirectory(home, "XDG_VIDEOS_DIR", "Videos");
                case Environment.SpecialFolder.MyPictures: return ReadXdgDirectory(home, "XDG_PICTURES_DIR", "Pictures");
                case Environment.SpecialFolder.Fonts: return Path.Combine(home, ".fonts");
#endif
            }
            if (!Enum.IsDefined(folder))
                throw new ArgumentOutOfRangeException("folder", folder, "Illegal enum value: " + folder + ".");
            return null;
        }

#if CSHARPC_TARGET_WASI
        private static string HomeDirectory() => Environment.GetEnvironmentVariable("HOME");
#else
#if CSHARPC_TARGET_MACOS
        // SystemNative_SearchPath (pal_searchpath.m) through the Objective-C runtime: the last URL NSFileManager gives for the directory in
        // the user domain, as a path, or null. Foundation is loaded on first use (no link-time framework).
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC)] private static extern nint objc_getClass(byte* name);
        [DllImport(ObjC)] private static extern nint sel_registerName(byte* name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector, nuint directory, nuint domains);
        [DllImport(ObjC)] private static extern nint objc_autoreleasePoolPush();
        [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(nint pool);
        [DllImport("libc")] private static extern nint dlopen(byte* path, int mode);
        private static bool s_foundation;

        private static nint Class(string name)
        {
            byte* text = Utf8.ToNative(name);
            nint result = objc_getClass(text);
            Libc.free(text);
            return result;
        }

        private static nint Sel(string name)
        {
            byte* text = Utf8.ToNative(name);
            nint result = sel_registerName(text);
            Libc.free(text);
            return result;
        }

        private static string SearchPath(int directory)
        {
            if (!s_foundation)
            {
                byte* framework = Utf8.ToNative("/System/Library/Frameworks/Foundation.framework/Foundation");
                dlopen(framework, 1 /* RTLD_LAZY */);
                Libc.free(framework);
                s_foundation = true;
            }
            nint pool = objc_autoreleasePoolPush();
            try
            {
                nint fileManager = Send(Class("NSFileManager"), Sel("defaultManager"));
                if (fileManager == 0) return null;
                nint urls = Send(fileManager, Sel("URLsForDirectory:inDomains:"), (nuint)directory, 1 /* NSUserDomainMask */);
                nint url = urls == 0 ? 0 : Send(urls, Sel("lastObject"));
                nint path = url == 0 ? 0 : Send(url, Sel("path"));
                nint utf8 = path == 0 ? 0 : Send(path, Sel("UTF8String"));
                return utf8 == 0 ? null : Utf8.FromNative((byte*)utf8);
            }
            finally { objc_autoreleasePoolPop(pool); }
        }
#endif

        [DllImport("libc")] private static extern uint geteuid();
        [DllImport("libc")] private static extern byte* getpwuid(uint uid);

        /// <summary>PersistedFiles.GetHomeDirectory: $HOME, else the password database's directory.</summary>
        private static string HomeDirectory()
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home)) return home;
            byte* pw = getpwuid(geteuid());
            if (pw == null) return null;
#if CSHARPC_TARGET_MACOS
            // struct passwd (Darwin): pw_name, pw_passwd, pw_uid, pw_gid, pw_change, pw_class, pw_gecos, pw_dir.
            byte* dir = (byte*)*(nint*)(pw + 48);
#else
            // struct passwd (glibc, LP64): pw_name, pw_passwd, pw_uid, pw_gid, pw_gecos, pw_dir.
            byte* dir = (byte*)*(nint*)(pw + 4 * sizeof(nint));
#endif
            return Utf8.FromNative(dir);
        }
#endif

        private static string XdgConfig(string home)
        {
            string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (config == null || !config.StartsWith('/')) config = Path.Combine(home, ".config");
            return config;
        }

        private static string ReadXdgDirectory(string homeDir, string key, string fallback)
        {
            string envPath = Environment.GetEnvironmentVariable(key);
            if (envPath != null && envPath.StartsWith('/')) return envPath;
            string userDirsPath = Path.Combine(XdgConfig(homeDir), "user-dirs.dirs");
            if (Readable(userDirsPath))
            {
                try
                {
                    using (var reader = new StreamReader(userDirsPath))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int pos = 0;
                            SkipWhitespace(line, ref pos);
                            if (pos >= line.Length) continue;
                            if (!line.AsSpan(pos).StartsWith(key, StringComparison.Ordinal)) continue;
                            pos += key.Length;
                            SkipWhitespace(line, ref pos);
                            if (pos >= line.Length - 4 || line[pos] != '=') continue;
                            pos++;
                            SkipWhitespace(line, ref pos);
                            if (pos >= line.Length - 3 || line[pos] != '"') continue;
                            pos++;
                            bool relativeToHome = false;
                            const string RelativeToHomePrefix = "$HOME/";
                            if (line.AsSpan(pos).StartsWith(RelativeToHomePrefix, StringComparison.Ordinal))
                            {
                                relativeToHome = true;
                                pos += RelativeToHomePrefix.Length;
                            }
                            else if (line[pos] != '/') continue;
                            int endPos = line.IndexOf('"', pos);
                            if (endPos <= pos) continue;
                            return relativeToHome ? Path.Join(homeDir, line.Substring(pos, endPos - pos)) : line.Substring(pos, endPos - pos);
                        }
                    }
                }
                catch (Exception) { }
            }
            return Path.Combine(homeDir, fallback);
        }

        private static void SkipWhitespace(string line, ref int pos)
        {
            while (pos < line.Length && char.IsWhiteSpace(line[pos])) pos++;
        }
#endif
    }
}
