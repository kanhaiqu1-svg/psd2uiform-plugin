using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI.Cli
{
    public static class AiFiles
    {
        public static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp"; File.WriteAllText(temporary, text, Utf8);
            Replace(temporary, path);
        }
        // Readers and viewers may hold a task file while the pipeline replaces it; retry transient locks.
        public static void Replace(string temp, string path)
        {
            for (int attempt = 1; ; ++attempt)
            {
                try { if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); return; }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < 6) { System.Threading.Thread.Sleep(40 * attempt); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                { throw new IOException("文件被其他进程占用，写入失败：" + path + "。关闭占用该文件的程序后重试。", e); }
            }
        }
        public static byte[] ReadBytes(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buffer = new byte[stream.Length]; int offset = 0;
                while (offset < buffer.Length) { int read = stream.Read(buffer, offset, buffer.Length - offset); if (read <= 0) break; offset += read; }
                return buffer;
            }
        }
        public static void Write<T>(string path, T value) => WriteText(path, JsonUtility.ToJson(value, true));
        public static T Read<T>(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Utf8))
                return JsonUtility.FromJson<T>(reader.ReadToEnd());
        }
        public static string Hash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        public static string HashFile(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        public static string Relative(string root, string path)
        {
            var baseUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            string relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString());
            if (relative.StartsWith("../", StringComparison.Ordinal) || relative.Contains("://")) throw new ArgumentException("任务产物必须位于任务目录内：" + path);
            return relative;
        }
    }
    public static class AiProfiles
    {
        public static readonly AiMode[] LlmModes = { AiMode.Claude, AiMode.Codex, AiMode.OpenCode, AiMode.Qoder, AiMode.Cursor };
        public static readonly string[] LlmModeNames = Array.ConvertAll(LlmModes, mode => mode.ToString());
        public static AiProfile Preset(AiMode mode)
        {
            AiCommands.Validate(new AiProfile { mode = mode });
            string executable;
            switch (mode)
            {
                case AiMode.Claude: executable = "claude"; break;
                case AiMode.Codex: executable = "codex"; break;
                case AiMode.OpenCode: executable = "opencode"; break;
                case AiMode.Qoder: executable = "qodercli"; break;
                case AiMode.Cursor: executable = "agent"; break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
            return new AiProfile { mode = mode, executable = executable };
        }
        public static string Fingerprint(AiProfile profile)
        {
            var b = new StringBuilder(JsonUtility.ToJson(profile)); var files = new List<string>();
            string exe = CliCommandResolver.ResolveCommandPath(profile.executable); if (exe != null) files.Add(exe);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (profile.mode == AiMode.Codex) files.Add(Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(user, ".codex"), "config.toml"));
            else if (profile.mode == AiMode.Claude) files.Add(Path.Combine(user, ".claude", "settings.json"));
            else if (profile.mode == AiMode.Qoder) files.Add(Path.Combine(Environment.GetEnvironmentVariable("QODER_CONFIG_DIR") ?? Path.Combine(user, ".qoder"), "settings.json"));
            else if (profile.mode == AiMode.Cursor)
            {
                string config = Environment.GetEnvironmentVariable("CURSOR_CONFIG_DIR");
                if (string.IsNullOrEmpty(config))
                {
                    string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                    config = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !string.IsNullOrEmpty(xdg)
                        ? Path.Combine(xdg, "cursor") : Path.Combine(user, ".cursor");
                }
                files.Add(Path.Combine(config, "cli-config.json"));
            }
            else { files.Add(Path.Combine(user, ".config", "opencode", "opencode.json")); files.Add(Path.Combine(user, ".config", "opencode", "opencode.jsonc")); }
            foreach (string path in files) if (File.Exists(path)) { var f = new FileInfo(path); b.Append(f.FullName).Append(f.Length).Append(f.LastWriteTimeUtc.Ticks); }
            return AiFiles.Hash(b.ToString());
        }
    }
}
