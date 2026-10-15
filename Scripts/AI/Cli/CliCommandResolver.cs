/*
 * Copyright (c) 2023-2026 Beijing Yiqu Technology Co., Ltd.
 *
 * All Rights Reserved.
 *
 * The software and associated documentation (including but not limited to source code,
 * object code, documents, images, audio, and video files) are the intellectual property
 * of Beijing Yiqu Technology Co., Ltd.
 *
 * For commercial use or licensing inquiries, please contact:
 * Email: efunstudio@gmail.com
 * Website: https://efunstudio.cn
 */

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

namespace UGF.EditorTools.Psd2UGUI.Cli
{
    public static class CliCommandResolver
    {
        public readonly struct LaunchSpec
        {
            public readonly string FileName;
            public readonly string Arguments;
            public readonly bool UseStandardInput;

            public LaunchSpec(string fileName, string arguments, bool useStandardInput)
            {
                FileName = fileName;
                Arguments = arguments ?? string.Empty;
                UseStandardInput = useStandardInput;
            }
        }

        public static bool TryResolve(string commandName, string commandArguments, string promptPath, out LaunchSpec launchSpec, out string error)
        {
            launchSpec = default;
            error = null;

            if (string.IsNullOrWhiteSpace(commandName))
            {
                error = "CLI command name is empty.";
                return false;
            }

            string resolvedPath = ResolveCommandPath(commandName);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                error = $"未找到 CLI 命令: {commandName}";
                return false;
            }

            string extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
            if (IsWindows())
            {
                switch (extension)
                {
                    case ".cmd":
                    case ".bat":
                        string inputRedirect = string.IsNullOrEmpty(promptPath) ? string.Empty : $" < \"{promptPath}\"";
                        // 实测：在这里插 `chcp 65001>nul&` 会吞掉后面的 < prompt 重定向，cmd 直接不执行目标工具，
                        // 所以控制台 OEM 码页只影响报错文本可读性，不改这里的引号结构。
                        launchSpec = new LaunchSpec(
                            Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                            $"/d /s /c \"\"{resolvedPath}\" {commandArguments}{inputRedirect}\"",
                            false);
                        return true;

                    case ".ps1":
                        string inputPipe = string.IsNullOrEmpty(promptPath) ? string.Empty
                            : $"Get-Content -Raw -Encoding UTF8 -LiteralPath '{EscapeForSingleQuotedPowerShell(promptPath)}' | ";
                        // -Command 的双引号串里，裸 " 会提前结束命令、$ 会被 PowerShell 展开：
                        // 实测未转义时脚本一个参数都收不到，按 \" 与 `$ 转义后含空格/中文/& 的路径可逐字送达。
                        launchSpec = new LaunchSpec(
                            "powershell.exe",
                            $"-NoProfile -ExecutionPolicy Bypass -Command \"$OutputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding; {inputPipe}& '{EscapeForSingleQuotedPowerShell(resolvedPath)}' {EscapeForDoubleQuotedPowerShell(commandArguments)}\"",
                            false);
                        return true;
                }
            }

            launchSpec = new LaunchSpec(resolvedPath, commandArguments, true);
            return true;
        }

        public static string ResolveCommandPath(string commandName)
        {
            if (Path.IsPathRooted(commandName) && File.Exists(commandName))
            {
                return commandName;
            }

            string[] candidateFileNames = BuildCandidateFileNames(commandName);
            foreach (var directory in EnumerateSearchDirectories())
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                for (int i = 0; i < candidateFileNames.Length; i++)
                {
                    string candidatePath = Path.Combine(directory, candidateFileNames[i]);
                    if (File.Exists(candidatePath))
                    {
                        return candidatePath;
                    }
                }
            }

            return null;
        }

        private static string[] BuildCandidateFileNames(string commandName)
        {
            string extension = Path.GetExtension(commandName);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return new[] { commandName };
            }

            if (!IsWindows())
            {
                return new[]
                {
                    commandName,
                    commandName + ".sh"
                };
            }

            return new[] { commandName + ".cmd", commandName + ".exe", commandName + ".bat", commandName + ".ps1", commandName };
        }

        private static IEnumerable<string> EnumerateSearchDirectories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pathDir in SplitPath(Environment.GetEnvironmentVariable("PATH")))
            {
                if (seen.Add(pathDir))
                {
                    yield return pathDir;
                }
            }

            if (IsWindows())
            {
                foreach (var pathDir in SplitPath(TryGetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)))
                {
                    if (seen.Add(pathDir))
                    {
                        yield return pathDir;
                    }
                }

                foreach (var pathDir in SplitPath(TryGetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)))
                {
                    if (seen.Add(pathDir))
                    {
                        yield return pathDir;
                    }
                }
            }
        }

        private static IEnumerable<string> SplitPath(string pathValue)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                yield break;
            }

            var parts = pathValue.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(part))
                {
                    yield return part;
                }
            }
        }

        private static string EscapeForSingleQuotedPowerShell(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
        }

        // -Command "<...>" 的参数要过两层解析：CreateProcess 认 \"，PowerShell 认 `$。
        private static string EscapeForDoubleQuotedPowerShell(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("`", "``").Replace("\"", "\\\"").Replace("$", "`$");
        }

        private static string TryGetEnvironmentVariable(string name, EnvironmentVariableTarget target)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name, target);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }
    }
}
#endif
