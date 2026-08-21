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
using System.Text;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class ClaudeCodeCliProvider : CliAiProviderBase
    {
        public override string ProviderId => "claude-code-cli";

        public override AiProviderCapabilities Capabilities => new AiProviderCapabilities
        {
            SupportsImages = true,
            SupportsStrictJson = true,
            UsesVisibleCliExecution = true
        };

        protected override string ExecutablePath => "claude";

        protected override string BuildArguments(AiAnalysisRequest request)
        {
            var args = new List<string>
            {
                "-p",
                "--output-format", "text",
                "--permission-mode", "bypassPermissions",
                "--no-session-persistence"
            };

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                args.Add("--add-dir");
                AppendQuoted(args, request.WorkingDirectory);
            }

            return JoinArguments(args);
        }

        protected override string BuildVisibleArguments(AiAnalysisRequest request)
        {
            // Claude Code 可见模式 = --print + stream-json + stdin pipe + PS 端 pretty-print。
            //
            // 为什么不用 TUI 模式：
            // - Claude TUI 把 positional prompt 仅"预填到输入框"，不会自动提交，需要用户按 Enter；
            // - 用 SendKeys 模拟 Enter 不稳 (焦点抢占、AppActivate 窗口标题被 TUI 改名等)；
            // - stdin pipe 会让 Claude 检测到非 TTY 并自动切到非交互模式，再要用 TUI 就要打补丁；
            // 结论：直接拥抱非交互 + stream-json，把所有事件流式拉到 PS 端再 pretty-print，可达成
            // "实时看到大模型分析思考过程"的目标，且零交互、零焦点依赖。
            //
            // - --print: 非交互模式
            // - --output-format stream-json: 流式 JSON 事件 (thinking / tool_use / text / tool_result / result)
            // - --include-partial-messages: 启用增量 delta (thinking_delta / text_delta / input_json_delta)，
            //   能看到字符级别的生成过程
            // - --verbose: --print 模式下输出全部事件 (默认会过滤一部分)
            // - --permission-mode bypassPermissions: 跳过权限确认
            // - --add-dir <wd>: 允许 Claude 在工程根目录读写
            var args = new List<string>
            {
                "--print",
                "--output-format", "stream-json",
                "--include-partial-messages",
                "--verbose",
                "--permission-mode", "bypassPermissions"
            };

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                args.Add("--add-dir");
                AppendQuoted(args, request.WorkingDirectory);
            }

            return JoinArguments(args);
        }

        protected override string BuildVisibleCliInvocation(string commandArguments, AiJobContext context)
        {
            // Claude provider 继续提供 thinking / tool / response 的细粒度流式事件，
            // 但具体窗口渲染交给 CliAiProviderBase 的共享显示层处理。
            const string preludeScript = @"
$script:allTextBuilder = New-Object System.Text.StringBuilder
$script:lastTextBlockBuilder = New-Object System.Text.StringBuilder
$script:currentTextBlockBuilder = $null
$script:currentContentBlockType = ''
";
            const string epilogueScript = @"
$fallbackText = $script:allTextBuilder.ToString()
if ($script:lastTextBlockBuilder.Length -gt 0) {
    $fallbackText = $script:lastTextBlockBuilder.ToString()
}
if (-not (Test-Path -LiteralPath $rawOutputPath) -and -not [string]::IsNullOrWhiteSpace($fallbackText)) {
    [System.IO.File]::WriteAllText($rawOutputPath, $fallbackText, $utf8NoBom)
}
";
            return BuildSharedVisibleCliInvocation(commandArguments, StreamAdapterFunction, "Convert-ClaudeVisibleCliEvent", preludeScript, epilogueScript);
        }

        /// <summary>
        /// PowerShell 函数：把 Claude --output-format stream-json 的一行事件 pretty-print 到控制台。
        /// 解析失败的行原样灰色显示（兜底），让用户至少能看到"在动"。
        /// </summary>
        private const string StreamAdapterFunction = @"
function Convert-ClaudeVisibleCliEvent {
    param([string]$Line)
    if ([string]::IsNullOrWhiteSpace($Line)) { return $null }
    try { $ev = $Line | ConvertFrom-Json -ErrorAction Stop } catch { return [pscustomobject]@{ text = $Line; color = 'DarkGray' } }
    switch ($ev.type) {
        'system' {
            if ($ev.subtype -eq 'init') {
                return [pscustomobject]@{
                    text = '==> Claude session started (model: ' + $ev.model + ', tools: ' + $ev.tools.Count + ')'
                    color = 'Cyan'
                    blankLineBefore = $true
                    dedupeKey = 'session'
                    dedupeValue = 'claude-init'
                }
            }
        }
        'stream_event' {
            $e = $ev.event
            if ($e.type -eq 'content_block_start') {
                $script:currentContentBlockType = [string]$e.content_block.type
                if ($e.content_block.type -eq 'thinking') {
                    return [pscustomobject]@{
                        text = '--- Thinking ---'
                        color = 'DarkYellow'
                        blankLineBefore = $true
                        dedupeKey = 'section'
                        dedupeValue = 'thinking'
                    }
                } elseif ($e.content_block.type -eq 'tool_use') {
                    return [pscustomobject]@{
                        text = '[Tool: ' + $e.content_block.name + ']'
                        color = 'Green'
                        blankLineBefore = $true
                    }
                } elseif ($e.content_block.type -eq 'text') {
                    $script:currentTextBlockBuilder = New-Object System.Text.StringBuilder
                    return [pscustomobject]@{
                        text = '--- Response ---'
                        color = 'White'
                        blankLineBefore = $true
                        dedupeKey = 'section'
                        dedupeValue = 'response'
                    }
                }
            } elseif ($e.type -eq 'content_block_delta') {
                switch ($e.delta.type) {
                    'thinking_delta'   {
                        return [pscustomobject]@{
                            text = [string]$e.delta.thinking
                            color = 'DarkYellow'
                            noNewline = $true
                        }
                    }
                    'text_delta'       {
                        if ($script:currentContentBlockType -eq 'text' -and $null -ne $e.delta.text) {
                            [void]$script:allTextBuilder.Append([string]$e.delta.text)
                            if ($script:currentTextBlockBuilder -ne $null) {
                                [void]$script:currentTextBlockBuilder.Append([string]$e.delta.text)
                            }
                        }
                        return [pscustomobject]@{
                            text = [string]$e.delta.text
                            color = 'White'
                            noNewline = $true
                        }
                    }
                    'input_json_delta' {
                        return [pscustomobject]@{
                            text = [string]$e.delta.partial_json
                            color = 'DarkGreen'
                            noNewline = $true
                        }
                    }
                }
            } elseif ($e.type -eq 'content_block_stop') {
                if ($script:currentContentBlockType -eq 'text' -and $script:currentTextBlockBuilder -ne $null -and $script:currentTextBlockBuilder.Length -gt 0) {
                    $script:lastTextBlockBuilder = $script:currentTextBlockBuilder
                }
                $script:currentTextBlockBuilder = $null
                $script:currentContentBlockType = ''
                return [pscustomobject]@{ newline = $true }
            }
        }
        'user' {
            $c = $ev.message.content[0]
            if ($c.type -eq 'tool_result') {
                $content = ''
                if ($c.content -is [string]) {
                    $content = $c.content
                } else {
                    $content = ($c.content | ConvertTo-Json -Compress -Depth 5)
                }
                if ($content.Length -gt 240) { $content = $content.Substring(0, 240) + '...' }
                return [pscustomobject]@{
                    text = '  -> ' + $content
                    color = 'DarkGreen'
                }
            }
        }
        'result' {
            return [pscustomobject]@{
                text = '==> Done. Duration: ' + $ev.duration_ms + 'ms'
                color = 'Cyan'
                blankLineBefore = $true
            }
        }
    }

    return $null
}
";
    }
}
#endif
