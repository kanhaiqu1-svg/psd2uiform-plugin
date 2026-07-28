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

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class OpenCodeCliProvider : CliAiProviderBase
    {
        public override string ProviderId => "opencode-cli";

        public override AiProviderCapabilities Capabilities => new AiProviderCapabilities
        {
            SupportsImages = true,
            SupportsStrictJson = true,
            UsesVisibleCliExecution = true
        };

        protected override string ExecutablePath => "opencode";

        /// <summary>
        /// 非交互/无头模式：使用 --format json 输出机器可读的 JSON 事件流，
        /// 便于基类从 stdout 中提取最终的 patch JSON 结果。
        /// </summary>
        protected override string BuildArguments(AiAnalysisRequest request)
        {
            var args = new List<string>
            {
                "run",
                "--format", "json",
                "--dangerously-skip-permissions",
                "--no-replay"
            };

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                args.Add("--dir");
                AppendQuoted(args, request.WorkingDirectory);
            }

            if (request.ImageInputPaths != null)
            {
                for (int i = 0; i < request.ImageInputPaths.Length; i++)
                {
                    var imagePath = request.ImageInputPaths[i];
                    if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                    {
                        continue;
                    }

                    args.Add("--file");
                    AppendQuoted(args, imagePath);
                }
            }

            return JoinArguments(args);
        }

        /// <summary>
        /// 可见模式也走 JSON 事件流，再由共享命令窗口显示层统一渲染。
        /// `--thinking` 让 OpenCode 继续返回 reasoning block。
        /// </summary>
        protected override string BuildVisibleArguments(AiAnalysisRequest request)
        {
            var args = new List<string>
            {
                "run",
                "--format", "json",
                "--dangerously-skip-permissions",
                "--no-replay",
                "--thinking"
            };

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                args.Add("--dir");
                AppendQuoted(args, request.WorkingDirectory);
            }

            if (request.ImageInputPaths != null)
            {
                for (int i = 0; i < request.ImageInputPaths.Length; i++)
                {
                    var imagePath = request.ImageInputPaths[i];
                    if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                    {
                        continue;
                    }

                    args.Add("--file");
                    AppendQuoted(args, imagePath);
                }
            }

            return JoinArguments(args);
        }

        protected override string BuildVisibleCliInvocation(string commandArguments, AiJobContext context)
        {
            return BuildSharedVisibleCliInvocation(commandArguments, StreamAdapterFunction, "Convert-OpenCodeVisibleCliEvent");
        }

        private const string StreamAdapterFunction = @"
function Convert-OpenCodeVisibleCliEvent {
    param([string]$Line)
    if ([string]::IsNullOrWhiteSpace($Line)) { return $null }

    try { $ev = $Line | ConvertFrom-Json -ErrorAction Stop } catch {
        return [pscustomobject]@{ text = $Line; color = 'DarkGray' }
    }

    $type = ''
    if ($ev.PSObject.Properties['type']) {
        $type = [string]$ev.type
    }

    $part = $null
    if ($ev.PSObject.Properties['part']) {
        $part = $ev.part
    }

    $partType = ''
    if ($null -ne $part -and $part.PSObject.Properties['type']) {
        $partType = [string]$part.type
    }

    $partText = ''
    if ($null -ne $part -and $part.PSObject.Properties['text']) {
        $partText = [string]$part.text
    }

    switch ($type) {
        'step_start' {
            return [pscustomobject]@{
                text = '==> Phase: running task'
                color = 'Cyan'
                blankLineBefore = $true
                dedupeKey = 'phase'
                dedupeValue = 'running task'
            }
        }
        'step_finish' {
            $summary = 'step completed'
            if ($null -ne $part -and $part.PSObject.Properties['tokens'] -and $null -ne $part.tokens) {
                $outputTokens = 0
                if ($part.tokens.PSObject.Properties['output']) {
                    $outputTokens = [int]$part.tokens.output
                }

                $reasoningTokens = 0
                if ($part.tokens.PSObject.Properties['reasoning']) {
                    $reasoningTokens = [int]$part.tokens.reasoning
                }
                if ($outputTokens > 0 -and $reasoningTokens > 0) {
                    $summary = 'step completed (output=' + $outputTokens + ', reasoning=' + $reasoningTokens + ')'
                } elseif ($outputTokens > 0) {
                    $summary = 'step completed (output=' + $outputTokens + ')'
                }
            }

            return [pscustomobject]@{
                text = '==> Result: ' + $summary
                color = 'Cyan'
                blankLineBefore = $true
            }
        }
        'reasoning' {
            if ([string]::IsNullOrWhiteSpace($partText)) { return $null }
            return @(
                [pscustomobject]@{
                    text = '--- Thinking ---'
                    color = 'DarkYellow'
                    blankLineBefore = $true
                    dedupeKey = 'section'
                    dedupeValue = 'thinking'
                },
                [pscustomobject]@{
                    text = $partText
                    color = 'DarkYellow'
                }
            )
        }
        'text' {
            if ([string]::IsNullOrWhiteSpace($partText)) { return $null }
            return @(
                [pscustomobject]@{
                    text = '--- Response ---'
                    color = 'White'
                    blankLineBefore = $true
                    dedupeKey = 'section'
                    dedupeValue = 'response'
                },
                [pscustomobject]@{
                    text = $partText
                    color = 'White'
                }
            )
        }
        'error' {
            $errorText = 'opencode error'
            if (-not [string]::IsNullOrWhiteSpace($partText)) {
                $errorText = $partText
            } elseif ($ev.PSObject.Properties['message']) {
                $errorText = [string]$ev.message
            }
            return [pscustomobject]@{
                text = 'ERROR: ' + $errorText
                color = 'Red'
                blankLineBefore = $true
            }
        }
    }

    if ($partType -eq 'tool' -or $partType -eq 'tool-call') {
        $toolText = $partText
        if ([string]::IsNullOrWhiteSpace($toolText)) {
            $toolText = $partType
        }
        return [pscustomobject]@{
            text = '[Tool] ' + $toolText
            color = 'Green'
            blankLineBefore = $true
        }
    }

    return [pscustomobject]@{
        text = $Line
        color = 'DarkGray'
    }
}
";
    }
}
#endif
