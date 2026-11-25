// Environment.GetFolderPath for the user's folders (#315), with HOME pointed at a scratch directory (1562_special_folders.env):
// Linux takes them from $HOME and the XDG variables; macOS from NSFileManager's user domain, whatever HOME says (as .NET's
// SystemNative_SearchPath). DoNotVerify: the path whether or not it exists.
using System;

static class Program
{
    static void Main()
    {
        string home = Environment.GetEnvironmentVariable("HOME")!;
        Console.WriteLine($"HOME: {home}");
        foreach (var folder in new[] {
            Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.InternetCache,
            Environment.SpecialFolder.Fonts, Environment.SpecialFolder.Favorites, Environment.SpecialFolder.UserProfile })
        {
            string path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
            string shown = path.StartsWith(home, StringComparison.Ordinal) ? "$HOME" + path.Substring(home.Length) : path;
            Console.WriteLine($"{folder}: {shown}");
        }
    }
}
