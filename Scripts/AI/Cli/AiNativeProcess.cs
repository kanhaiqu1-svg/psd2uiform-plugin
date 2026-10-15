using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UGF.EditorTools.Psd2UGUI.Cli
{
    public static class AiNativeProcess
    {
        public static string Quote(string value)
        {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) { if (c == '\\') { ++slashes; continue; } b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); b.Append(c); slashes = 0; }
            return b.Append('\\', slashes * 2).Append('"').ToString();
        }
        public static bool Matches(OwnedProcess r)
        {
            try
            {
                using (var p = Process.GetProcessById(r.pid))
                    return !p.HasExited && Math.Abs((p.StartTime.ToUniversalTime() - Epoch).TotalSeconds - r.created) < .1 &&
                        string.Equals(Path.GetFullPath(Executable(p)), Path.GetFullPath(r.exe), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static string Executable(Process p)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix)
            {
                IntPtr handle = OpenProcess(0x1000, false, p.Id);
                if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    var value = new StringBuilder(4096); uint length = (uint)value.Capacity;
                    if (!QueryFullProcessImageNameW(handle, 0, value, ref length)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    return value.ToString();
                }
                finally { CloseHandle(handle); }
            }
            var path = new StringBuilder(4096);
            if (proc_pidpath(p.Id, path, (uint)path.Capacity) <= 0) throw new InvalidOperationException("Cannot inspect process executable");
            return path.ToString();
        }
        public static OwnedProcess Start(string exe, List<string> args, Dictionary<string, string> environment, string log, string job)
        {
            int pid = Environment.OSVersion.Platform == PlatformID.Unix ? StartMac(exe, args, environment, log) : StartWindows(exe, args, environment, log);
            using (var p = Process.GetProcessById(pid))
                return new OwnedProcess { pid = pid, created = (p.StartTime.ToUniversalTime() - Epoch).TotalSeconds, exe = Path.GetFullPath(exe), jobDir = job };
        }
        // OS-owned file descriptors survive Unity domain reload; no managed stdout callbacks or shell scripts.
        static int StartWindows(string exe, List<string> args, Dictionary<string, string> environment, string log)
        {
            var sa = new SecurityAttributes { length = Marshal.SizeOf<SecurityAttributes>(), inherit = true };
            IntPtr output = CreateFileW(log, 0x40000000, 3, ref sa, 4, 0x80, IntPtr.Zero);
            if (output == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            IntPtr input = CreateFileW("NUL", 0x80000000, 3, ref sa, 3, 0x80, IntPtr.Zero);
            IntPtr block = IntPtr.Zero;
            try
            {
                SetFilePointer(output, 0, IntPtr.Zero, 2);
                var start = new StartupInfo { size = Marshal.SizeOf<StartupInfo>(), flags = 0x100, input = input, output = output, error = output };
                var command = new StringBuilder(Quote(exe)); foreach (string arg in args) command.Append(' ').Append(Quote(arg));
                var keys = new List<string>(environment.Keys); keys.Sort(StringComparer.OrdinalIgnoreCase);
                var text = new StringBuilder(); foreach (string key in keys) text.Append(key).Append('=').Append(environment[key]).Append('\0'); text.Append('\0');
                block = Marshal.StringToHGlobalUni(text.ToString());
                if (!CreateProcessW(exe, command, IntPtr.Zero, IntPtr.Zero, true, 0x08000400, block, Path.GetDirectoryName(exe), ref start, out var process))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                CloseHandle(process.process); CloseHandle(process.thread); return process.pid;
            }
            finally { CloseHandle(output); if (input != new IntPtr(-1)) CloseHandle(input); if (block != IntPtr.Zero) Marshal.FreeHGlobal(block); }
        }
        static int StartMac(string exe, List<string> args, Dictionary<string, string> environment, string log)
        {
            IntPtr actions = Marshal.AllocHGlobal(1024), argv = IntPtr.Zero, envp = IntPtr.Zero;
            var allocated = new List<IntPtr>();
            try
            {
                if (posix_spawn_file_actions_init(actions) != 0) throw new IOException("posix_spawn initialization failed");
                Check(posix_spawn_file_actions_addopen(actions, 0, "/dev/null", 0, 0));
                Check(posix_spawn_file_actions_addopen(actions, 1, log, 0x209, 0x1A4)); // O_WRONLY|O_CREAT|O_APPEND, 0644
                Check(posix_spawn_file_actions_adddup2(actions, 1, 2));
                var values = new List<string> { exe }; values.AddRange(args); argv = Strings(values, allocated);
                values.Clear(); foreach (var entry in environment) values.Add(entry.Key + "=" + entry.Value); envp = Strings(values, allocated);
                int error = posix_spawn(out int pid, exe, actions, IntPtr.Zero, argv, envp); Check(error); return pid;
            }
            finally
            {
                posix_spawn_file_actions_destroy(actions); Marshal.FreeHGlobal(actions);
                if (argv != IntPtr.Zero) Marshal.FreeHGlobal(argv); if (envp != IntPtr.Zero) Marshal.FreeHGlobal(envp);
                foreach (var pointer in allocated) Marshal.FreeHGlobal(pointer);
            }
        }
        static void Check(int error) { if (error != 0) throw new IOException("posix_spawn error " + error); }
        static IntPtr Strings(List<string> values, List<IntPtr> allocated)
        {
            var array = Marshal.AllocHGlobal((values.Count + 1) * IntPtr.Size);
            for (int i = 0; i < values.Count; ++i)
            {
                byte[] data = Encoding.UTF8.GetBytes(values[i] + "\0"); var pointer = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, pointer, data.Length); allocated.Add(pointer); Marshal.WriteIntPtr(array, i * IntPtr.Size, pointer);
            }
            Marshal.WriteIntPtr(array, values.Count * IntPtr.Size, IntPtr.Zero); return array;
        }
        public static string Read(string exe, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            { string output = p.StandardOutput.ReadToEnd(); string error = p.StandardError.ReadToEnd(); p.WaitForExit(); if (p.ExitCode != 0) throw new IOException(error); return output; }
        }
        [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public int length; public IntPtr descriptor; [MarshalAs(UnmanagedType.Bool)] public bool inherit; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo
        {
            public int size; public string reserved, desktop, title; public int x, y, width, height, xCount, yCount, fill, flags;
            public short show, reservedCount; public IntPtr reservedPtr, input, output, error;
        }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr process, thread; public int pid, tid; }
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcessW(string application, StringBuilder command, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFileW(string file, uint access, uint share, ref SecurityAttributes attributes, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32")] static extern uint SetFilePointer(IntPtr file, int distance, IntPtr high, uint method);
        [DllImport("kernel32")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder path, ref uint size);
        [DllImport("/usr/lib/libproc.dylib")] static extern int proc_pidpath(int pid, StringBuilder path, uint size);
        [DllImport("libSystem.B.dylib")] static extern int posix_spawn(out int pid, string path, IntPtr actions, IntPtr attributes, IntPtr argv, IntPtr envp);
        [DllImport("libSystem.B.dylib")] static extern int posix_spawn_file_actions_init(IntPtr actions);
        [DllImport("libSystem.B.dylib")] static extern int posix_spawn_file_actions_destroy(IntPtr actions);
        [DllImport("libSystem.B.dylib")] static extern int posix_spawn_file_actions_addopen(IntPtr actions, int fd, string path, int flags, int mode);
        [DllImport("libSystem.B.dylib")] static extern int posix_spawn_file_actions_adddup2(IntPtr actions, int fd, int target);
    }
}
