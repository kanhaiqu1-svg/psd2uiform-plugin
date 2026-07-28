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
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class AiCliDebugConsole
    {
        internal sealed class Handle
        {
            private readonly AiJobContext context;
            private readonly Process process;
            private readonly string macWindowTitle;
            private bool closed;

            internal Handle(AiJobContext context, Process process, string macWindowTitle)
            {
                this.context = context;
                this.process = process;
                this.macWindowTitle = macWindowTitle;
            }

            internal void Close()
            {
                if (closed) return;
                closed = true;

                try
                {
                    SignalClose(context);

                    if (Application.platform == RuntimePlatform.OSXEditor)
                    {
                        CloseMacTerminalWindow(context, macWindowTitle);
                    }

                    if (process == null || process.HasExited)
                    {
                        return;
                    }

                    if (!process.CloseMainWindow() || !process.WaitForExit(800))
                    {
                        process.Kill();
                    }
                    AiJobFileUtility.AppendDebugLog(context, "Closed visible CLI debug console.");
                }
                catch (Exception ex)
                {
                    AiJobFileUtility.AppendDebugLog(context, $"Failed to close CLI debug console. error={ex.Message}");
                }
            }
        }

        internal static Handle TryLaunch(AiJobContext context, string providerId)
        {
            return TryLaunch(context, providerId, context != null ? context.DebugLogPath : null, null);
        }

        internal static Handle TryLaunch(AiJobContext context, string providerId, string logPath, string titleSuffix)
        {
            if (context == null || string.IsNullOrWhiteSpace(logPath))
            {
                return null;
            }

            try
            {
                TryDeleteCloseSignal(context);
                AiJobFileUtility.EnsureDirectory(Path.GetDirectoryName(logPath));
                if (!File.Exists(logPath))
                {
                    AiJobFileUtility.WriteText(logPath, string.Empty);
                }

                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        return LaunchWindowsConsole(context, providerId, logPath, titleSuffix);

                    case RuntimePlatform.OSXEditor:
                        return LaunchMacTerminal(context, providerId, logPath, titleSuffix);

                    case RuntimePlatform.LinuxEditor:
                        return LaunchLinuxTerminal(context, providerId, logPath, titleSuffix);
                }
            }
            catch (Exception ex)
            {
                AiJobFileUtility.AppendDebugLog(context, $"Failed to launch CLI debug console. error={ex.Message}");
            }

            return null;
        }

        private static Handle LaunchWindowsConsole(AiJobContext context, string providerId, string logPath, string titleSuffix)
        {
            string safeLogPath = logPath.Replace("'", "''");
            string closeSignalPath = context.DebugConsoleCloseSignalPath.Replace("'", "''");
            string titleLabel = string.IsNullOrWhiteSpace(titleSuffix) ? providerId : providerId + " - " + titleSuffix;
            string title = $"PSD2UIForm AI Debug - {titleLabel} - {context.JobId}".Replace("'", "''");
            string command =
                $"$Host.UI.RawUI.WindowTitle = '{title}'; " +
                $"Write-Host 'Tailing: {safeLogPath}'; " +
                $"if (!(Test-Path -LiteralPath '{safeLogPath}')) {{ New-Item -ItemType File -Path '{safeLogPath}' -Force | Out-Null }}; " +
                $"$tailJob = Start-Job -ScriptBlock {{ param($path) Get-Content -LiteralPath $path -Encoding UTF8 -Tail 40 -Wait }} -ArgumentList '{safeLogPath}'; " +
                $"try {{ while (!(Test-Path -LiteralPath '{closeSignalPath}')) {{ Receive-Job -Job $tailJob; Start-Sleep -Milliseconds 200; }} }} " +
                $"finally {{ Stop-Job -Job $tailJob -ErrorAction SilentlyContinue | Out-Null; Remove-Job -Job $tailJob -Force -ErrorAction SilentlyContinue | Out-Null; }}";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = Directory.GetParent(Application.dataPath).FullName
            };
            var process = Process.Start(startInfo);
            AiJobFileUtility.AppendDebugLog(context, "Launched visible Windows debug console for CLI output.");
            return new Handle(context, process, null);
        }

        private static Handle LaunchMacTerminal(AiJobContext context, string providerId, string logPath, string titleSuffix)
        {
            string safeLogPath = logPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string closeSignalPath = context.DebugConsoleCloseSignalPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string titleLabel = string.IsNullOrWhiteSpace(titleSuffix) ? providerId : providerId + " - " + titleSuffix;
            string title = $"PSD2UIForm AI Debug - {titleLabel} - {context.JobId}".Replace("\"", "\\\"");
            string script =
                $"tell application \"Terminal\" to do script \"printf '\\\\e]1;{title}\\\\a'; touch \\\"{safeLogPath}\\\"; printf 'Tailing: {safeLogPath}\\\\n'; tail -n 40 -f \\\"{safeLogPath}\\\" & TAIL_PID=$!; while [ ! -f \\\"{closeSignalPath}\\\" ]; do sleep 1; done; kill $TAIL_PID >/dev/null 2>&1; wait $TAIL_PID 2>/dev/null; exit\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = $"-e \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(startInfo);
            AiJobFileUtility.AppendDebugLog(context, "Launched visible macOS Terminal debug console for CLI output.");
            return new Handle(context, process, title);
        }

        private static Handle LaunchLinuxTerminal(AiJobContext context, string providerId, string logPath, string titleSuffix)
        {
            string safeLogPath = logPath.Replace("\"", "\\\"");
            string closeSignalPath = context.DebugConsoleCloseSignalPath.Replace("\"", "\\\"");
            string titleLabel = string.IsNullOrWhiteSpace(titleSuffix) ? providerId : providerId + " - " + titleSuffix;
            string title = $"PSD2UIForm AI Debug - {titleLabel} - {context.JobId}".Replace("\"", "\\\"");
            string command = $"touch \"{safeLogPath}\"; printf 'Tailing: {safeLogPath}\\n'; tail -n 40 -f \"{safeLogPath}\" & TAIL_PID=$!; while [ ! -f \"{closeSignalPath}\" ]; do sleep 1; done; kill $TAIL_PID >/dev/null 2>&1; wait $TAIL_PID 2>/dev/null";
            string[] terminalCandidates =
            {
                "x-terminal-emulator",
                "gnome-terminal",
                "konsole",
                "xfce4-terminal",
                "xterm"
            };

            for (int i = 0; i < terminalCandidates.Length; i++)
            {
                string terminal = terminalCandidates[i];
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = terminal,
                        Arguments = BuildLinuxTerminalArguments(terminal, title, command),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    AiJobFileUtility.AppendDebugLog(context, $"Launched visible Linux debug console using {terminal}.");
                    return new Handle(context, process, null);
                }
                catch
                {
                }
            }

            AiJobFileUtility.AppendDebugLog(context, "No supported Linux terminal emulator found for visible CLI debug console.");
            return null;
        }

        private static string BuildLinuxTerminalArguments(string terminal, string title, string command)
        {
            switch (terminal)
            {
                case "gnome-terminal":
                    return $"--title=\"{title}\" -- bash -lc \"{command}\"";
                case "konsole":
                    return $"--new-tab -p tabtitle=\"{title}\" -e bash -lc \"{command}\"";
                case "xfce4-terminal":
                    return $"--title=\"{title}\" -e \"bash -lc '{command}'\"";
                case "xterm":
                    return $"-T \"{title}\" -e bash -lc \"{command}\"";
                default:
                    return $"-T \"{title}\" -e bash -lc \"{command}\"";
            }
        }

        private static void SignalClose(AiJobContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.DebugConsoleCloseSignalPath))
            {
                return;
            }

            try
            {
                AiJobFileUtility.WriteText(context.DebugConsoleCloseSignalPath, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                AiJobFileUtility.AppendDebugLog(context, $"Failed to write debug console close signal. error={ex.Message}");
            }
        }

        private static void TryDeleteCloseSignal(AiJobContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.DebugConsoleCloseSignalPath) || !File.Exists(context.DebugConsoleCloseSignalPath))
            {
                return;
            }

            try
            {
                AiJobFileUtility.DeleteFileIfExists(context.DebugConsoleCloseSignalPath);
            }
            catch (Exception ex)
            {
                AiJobFileUtility.AppendDebugLog(context, $"Failed to delete stale debug console close signal. error={ex.Message}");
            }
        }

        private static void CloseMacTerminalWindow(AiJobContext context, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            string safeTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string script = $"tell application \"Terminal\" to close (every window whose name contains \"{safeTitle}\")";
            try
            {
                using (var closeProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    Arguments = $"-e \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    closeProcess?.WaitForExit(1000);
                }
            }
            catch (Exception ex)
            {
                AiJobFileUtility.AppendDebugLog(context, $"Failed to close macOS Terminal window. error={ex.Message}");
            }
        }
    }
}
#endif
