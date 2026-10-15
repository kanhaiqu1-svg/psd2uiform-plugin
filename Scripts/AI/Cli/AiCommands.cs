using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UGF.EditorTools.Psd2UGUI.Cli
{
    public static class AiCommands
    {
        public static void Validate(AiProfile profile)
        {
            if ((uint)profile.mode >= (uint)AiProfiles.LlmModes.Length || profile.requestTimeoutMinutes < 1)
                throw new ArgumentException("CLI 模式或请求超时无效。");
        }
        public static List<string> Cli(AiProfile p, string root, AiTaskRequest request, string rawPath, int openCodeMajor = 2)
        {
            Validate(p); var args = new List<string>();
            switch (p.mode)
            {
                case AiMode.Qoder:
                    args.AddRange(new[] { "--print", "--output-format", "stream-json", "--no-session-persistence", "--permission-mode", "bypass_permissions" });
                    break;
                case AiMode.Codex:
                    args.AddRange(new[] { "-a", "never", "exec", "-s", "workspace-write", "--skip-git-repo-check", "--ephemeral", "--json", "-C", ".", "-o", AiFiles.Relative(root, rawPath) });
                    foreach (var r in request.references) args.AddRange(new[] { "-i", AiFiles.Relative(root, r.path) });
                    break;
                case AiMode.Claude:
                    args.AddRange(new[] { "--print", "--output-format", "stream-json", "--verbose", "--include-partial-messages", "--no-session-persistence", "--permission-mode", "bypassPermissions" });
                    break;
                case AiMode.OpenCode:
                    args.AddRange(new[] { "run", "--format", "json" }); if (openCodeMajor == 2) args.AddRange(new[] { "--standalone", "--auto" });
                    else if (openCodeMajor != 1) throw new ArgumentException("不支持的 OpenCode 版本：" + openCodeMajor);
                    foreach (var r in request.references) args.AddRange(new[] { "--file", AiFiles.Relative(root, r.path) });
                    break;
                case AiMode.Cursor:
                    args.AddRange(new[] { "--print", "--output-format", "stream-json", "--force", "--trust", "--workspace", "." });
                    break;
                default: throw new ArgumentException("不支持的 CLI：" + p.mode);
            }
            if (p.mode == AiMode.Codex) args.Add("-"); return args;
        }
        public static string Preview(AiProfile profile)
        {
            string root = Path.GetTempPath();
            var request = new AiTaskRequest();
            var args = Cli(profile, root, request, Path.Combine(root, "response.txt"));
            var text = new StringBuilder(AiNativeProcess.Quote(profile.executable));
            foreach (string arg in args) text.Append(' ').Append(AiNativeProcess.Quote(arg));
            return text.ToString();
        }
    }
}
