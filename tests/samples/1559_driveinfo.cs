// System.IO.DriveInfo from the framework's IL (#102): the drives of this machine, their readiness, format and sizes. Sizes and format
// names differ between machines, so only their relations are printed.
using System;
using System.IO;
using System.Linq;

static class Program
{
    static void Main()
    {
        string root = OperatingSystem.IsWindows() ? Path.GetPathRoot(Environment.SystemDirectory)! : "/";
        var drive = new DriveInfo(root);
        Console.WriteLine($"ready: {drive.IsReady} root: {drive.RootDirectory.FullName == root} name: {drive.Name == root}");
        Console.WriteLine($"format: {drive.DriveFormat.Length > 0} type known: {drive.DriveType != DriveType.Unknown}");
        Console.WriteLine($"sizes: {drive.TotalSize > 0} {drive.AvailableFreeSpace <= drive.TotalFreeSpace} {drive.TotalFreeSpace <= drive.TotalSize}");
        Console.WriteLine($"listed: {DriveInfo.GetDrives().Any(d => d.Name == root)}");
        if (!OperatingSystem.IsWindows())
        {
            var missing = new DriveInfo("/csharpc/no/such/mount");
            Console.WriteLine($"missing ready: {missing.IsReady}");
            try { _ = missing.TotalSize; Console.WriteLine("no exception"); }
            catch (DriveNotFoundException e) { Console.WriteLine(e.GetType().Name); }
        }
        else Console.WriteLine("missing ready: False\nDriveNotFoundException");
    }
}
