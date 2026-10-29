using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ToolDock.Services;

/// <summary>
/// Closes the requested instances first, then deletes or recycles files, then starts the app again.
/// The worker is this same executable and leaves before it takes an instance slot.
/// </summary>
public static class TroubleshootingReset
{
    public const string WorkerArgument = "--reset-worker";

    public static bool TryRunWorker(string[] args)
    {
        if (!TryReadWorkerRequest(args, out var mode, out var values))
        {
            return false;
        }

        var openSettings = false;
        var count = 1;
        try
        {
            var directory = AppPaths.DataDirectory;
            if (string.Equals(mode, "this", StringComparison.OrdinalIgnoreCase) &&
                values.Length >= 2 &&
                int.TryParse(values[0], out var slot) &&
                int.TryParse(values[1], out var pid))
            {
                WaitForExit(pid);
                DeleteConfig(ConfigPathForSlot(directory, slot));
                openSettings = true;
            }
            else if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
            {
                var pids = ParsePids(values);
                WaitForExit(pids);
                DeleteEveryInstanceConfig(directory);
                count = Math.Max(1, pids.Length);
            }
            else if (string.Equals(mode, "hard", StringComparison.OrdinalIgnoreCase))
            {
                WaitForExit(ParsePids(values));
                RecycleAppData(directory);
            }
        }
        catch
        {
            // The copies are already closed. Still open the app again.
        }

        LaunchInstances(count, openSettings);
        return true;
    }

    public static bool IsInstanceConfigFile(string fileName)
    {
        if (fileName.Equals("config.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string prefix = "config.instance";
        const string suffix = ".json";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
            fileName.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        var middle = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        foreach (var character in middle)
        {
            if (!char.IsDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadWorkerRequest(string[] args, out string mode, out string[] values)
    {
        mode = string.Empty;
        values = [];
        var index = Array.FindIndex(args, argument =>
            string.Equals(argument, WorkerArgument, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
        {
            return false;
        }

        mode = args[index + 1];
        values = args.Skip(index + 2).ToArray();
        return mode.Equals("this", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("hard", StringComparison.OrdinalIgnoreCase);
    }

    private static int[] ParsePids(string[] values)
    {
        var pids = new List<int>();
        foreach (var value in values)
        {
            if (int.TryParse(value, out var pid) && pid > 0)
            {
                pids.Add(pid);
            }
        }

        return pids.ToArray();
    }

    private static string ConfigPathForSlot(string directory, int slot)
    {
        var name = slot <= 1 ? "config.json" : $"config.instance{slot}.json";
        return Path.Combine(directory, name);
    }

    private static void DeleteEveryInstanceConfig(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(directory, "*.json"))
        {
            if (IsInstanceConfigFile(Path.GetFileName(path)))
            {
                DeleteConfig(path);
            }
        }
    }

    private static void DeleteConfig(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            DurableFile.DeleteFamily(path);
            if (!File.Exists(path) &&
                !File.Exists(DurableFile.GetBackupPath(path)) &&
                !File.Exists(path + ".bak"))
            {
                return;
            }

            Thread.Sleep(150);
        }
    }

    private static void RecycleAppData(string directory)
    {
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                Recycle(path);
            }
        }

        Recycle(Path.Combine(directory, DurableFile.BackupDirectoryName));
        Recycle(Path.Combine(directory, DurableFile.TempDirectoryName));
    }

    private static void Recycle(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        // A second null ends the list. The shell moves the item to the Recycle Bin and does not ask again.
        var from = Marshal.StringToHGlobalUni(path + "\0\0");
        try
        {
            var operation = new SHFILEOPSTRUCT
            {
                wFunc = 3,
                pFrom = from,
                fFlags = 0x0010 | 0x0040 | 0x0004 | 0x0400
            };
            SHFileOperation(ref operation);
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);

    private static void WaitForExit(params int[] pids)
    {
        foreach (var pid in pids.Distinct())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (!process.WaitForExit(TimeSpan.FromSeconds(20)) && !process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // The process may have exited between the check and the kill.
                    }

                    process.WaitForExit(TimeSpan.FromSeconds(5));
                }
            }
            catch (ArgumentException)
            {
                // The process is already gone.
            }
        }

        Thread.Sleep(200);
    }

    private static void LaunchInstances(int count, bool openSettings)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            exePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        var arguments = openSettings ? "--open-settings --spawned" : "--spawned";
        var directory = Path.GetDirectoryName(exePath);
        for (var i = 0; i < count; i++)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(directory) ? "" : directory,
                    UseShellExecute = true
                });
            }
            catch
            {
                // One failed start must not cancel the others.
            }
        }
    }
}
