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
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class CodexCliProvider : CliAiProviderBase
    {
        public override string ProviderId => "codex-cli";
        public override AiProviderCapabilities Capabilities => new AiProviderCapabilities
        {
            SupportsImages = true,
            SupportsStrictJson = true,
            UsesVisibleCliExecution = true
        };

        protected override string ExecutablePath => "codex";

        protected override string BuildArguments(AiAnalysisRequest request)
        {
            var args = new List<string>
            {
                "-a", "never",
                "exec",
                "-s", "workspace-write",
                "--skip-git-repo-check",
                "--ephemeral",
                "--json"
            };

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                args.Add("-C");
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

                    args.Add("-i");
                    AppendQuoted(args, imagePath);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.OutputRawTextPath))
            {
                args.Add("-o");
                AppendQuoted(args, request.OutputRawTextPath);
            }

            args.Add("-");
            return JoinArguments(args);
        }

        protected override string BuildVisibleArguments(AiAnalysisRequest request)
        {
            // 可见模式不能直接启真实 Codex TUI：
            // - 用户在窗口内输入会直接影响同一会话，污染 AI 识别流程；
            // - 我们需要的是“只读流式进度窗口”，不是可交互终端。
            //
            // 因此这里改成和无头模式同一条 `codex exec --json` 非交互路径：
            // - bootstrap prompt 通过 stdin pipe 输入
            // - stdout 输出 JSON 事件流
            // - PowerShell runner 负责 pretty-print 到可见窗口，并把原始事件落盘
            // 这样窗口仍然实时显示 phase / item / error / result 进度，但用户输入不会再进入 Codex 会话。
            var args = new List<string>
            {
                "-a", "never",
                "exec",
                "-s", "workspace-write",
                "--skip-git-repo-check",
                "--ephemeral",
                "--json"
            };

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                args.Add("-C");
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

                    args.Add("-i");
                    AppendQuoted(args, imagePath);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.OutputRawTextPath))
            {
                args.Add("-o");
                AppendQuoted(args, request.OutputRawTextPath);
            }

            args.Add("-");
            return JoinArguments(args);
        }

        protected override string BuildVisibleCliInvocation(string commandArguments, AiJobContext context)
        {
            return BuildSharedVisibleCliInvocation(commandArguments, StreamAdapterFunction, "Convert-CodexVisibleCliEvent");
        }

        private const string StreamAdapterFunction = @"
function Normalize-CodexText {
    param([string]$Text, [int]$Limit = 240)
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }

    $normalized = $Text.Replace(""`r"", ' ').Replace(""`n"", ' ').Trim()
    while ($normalized.Contains('  ')) {
        $normalized = $normalized.Replace('  ', ' ')
    }

    if ($normalized.Length -gt $Limit) {
        return $normalized.Substring(0, $Limit) + '...'
    }

    return $normalized
}

function Get-CodexObjectString {
    param($Object, [string]$PropertyName)
    if ($null -eq $Object -or [string]::IsNullOrWhiteSpace($PropertyName)) { return '' }

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value) { return '' }

    if ($property.Value -is [string]) {
        return [string]$property.Value
    }

    return [string]$property.Value.ToString()
}

function Get-CodexPhaseForItemType {
    param([string]$ItemType)
    switch ($ItemType) {
        'mcp_tool_call'  { return 'executing tools' }
        'shell_command'  { return 'executing tools' }
        'function_call'  { return 'executing tools' }
        'reasoning'      { return 'reasoning' }
        'agent_message'  { return 'assembling result' }
        default          { return '' }
    }
}

function Summarize-CodexCommand {
    param([string]$CommandText)
    if ([string]::IsNullOrWhiteSpace($CommandText)) { return '' }

    $summary = Normalize-CodexText $CommandText 180
    $getContentIndex = $summary.IndexOf('Get-Content', [System.StringComparison]::OrdinalIgnoreCase)
    if ($getContentIndex -ge 0) {
        return Normalize-CodexText $summary.Substring($getContentIndex) 180
    }

    return $summary
}

function Get-CodexItemLabel {
    param($Item)
    if ($null -eq $Item) { return '' }

    $itemType = Get-CodexObjectString $Item 'type'
    $commandText = Get-CodexObjectString $Item 'command'
    if (-not [string]::IsNullOrWhiteSpace($commandText)) {
        return Summarize-CodexCommand $commandText
    }

    $tool = Get-CodexObjectString $Item 'tool'
    $server = Get-CodexObjectString $Item 'server'
    $title = ''
    if ($Item.PSObject.Properties['arguments'] -and $null -ne $Item.arguments) {
        $title = Get-CodexObjectString $Item.arguments 'title'
    }

    if (-not [string]::IsNullOrWhiteSpace($title)) {
        return Normalize-CodexText $title 180
    }

    if (-not [string]::IsNullOrWhiteSpace($tool)) {
        $toolLabel = $tool
        if (-not [string]::IsNullOrWhiteSpace($server)) {
            $toolLabel = $server + '/' + $tool
        }
        return Normalize-CodexText $toolLabel 180
    }

    if ($itemType -eq 'agent_message') {
        return 'assistant response'
    }

    if ($itemType -eq 'reasoning') {
        return 'reasoning'
    }

    return Normalize-CodexText $itemType 180
}

function Get-CodexContentText {
    param($Value)
    if ($null -eq $Value) { return '' }

    if ($Value -is [string]) {
        return [string]$Value
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        foreach ($entry in $Value) {
            if ($null -eq $entry) { continue }

            $entryText = Get-CodexObjectString $entry 'text'
            if (-not [string]::IsNullOrWhiteSpace($entryText)) {
                return $entryText
            }

            $nestedContent = Get-CodexObjectString $entry 'content'
            if (-not [string]::IsNullOrWhiteSpace($nestedContent)) {
                return $nestedContent
            }
        }
    }

    return ''
}

function Get-CodexErrorText {
    param($EventObject)
    if ($null -eq $EventObject) { return '' }

    foreach ($propertyName in @('message', 'summary', 'title', 'content', 'text')) {
        $value = Get-CodexObjectString $EventObject $propertyName
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return Normalize-CodexText $value 320
        }
    }

    if ($EventObject.PSObject.Properties['error'] -and $null -ne $EventObject.error) {
        $errorText = Get-CodexObjectString $EventObject.error 'message'
        if ([string]::IsNullOrWhiteSpace($errorText)) {
            $errorText = Get-CodexObjectString $EventObject.error 'text'
        }
        if ([string]::IsNullOrWhiteSpace($errorText)) {
            $errorText = [string]$EventObject.error
        }
        if (-not [string]::IsNullOrWhiteSpace($errorText)) {
            return Normalize-CodexText $errorText 320
        }
    }

    if ($EventObject.PSObject.Properties['item'] -and $null -ne $EventObject.item) {
        $item = $EventObject.item

        if ($item.PSObject.Properties['error'] -and $null -ne $item.error) {
            $errorText = Get-CodexObjectString $item.error 'message'
            if ([string]::IsNullOrWhiteSpace($errorText)) {
                $errorText = Get-CodexObjectString $item.error 'text'
            }
            if ([string]::IsNullOrWhiteSpace($errorText)) {
                $errorText = [string]$item.error
            }
            if (-not [string]::IsNullOrWhiteSpace($errorText)) {
                return Normalize-CodexText $errorText 320
            }
        }

        if ($item.PSObject.Properties['result'] -and $null -ne $item.result) {
            $resultText = Get-CodexContentText $item.result.content
            if (-not [string]::IsNullOrWhiteSpace($resultText)) {
                return Normalize-CodexText $resultText 320
            }
        }
    }

    return ''
}

function Get-CodexResultText {
    param($Item)
    if ($null -eq $Item) { return '' }

    $itemText = Get-CodexObjectString $Item 'text'
    if (-not [string]::IsNullOrWhiteSpace($itemText)) {
        $trimmed = $itemText.Trim()
        if ($trimmed.StartsWith('{') -or $trimmed.StartsWith('[')) {
            return 'assistant response generated'
        }

        return Normalize-CodexText $itemText 240
    }

    return 'assistant response generated'
}

function Get-CodexTurnResultSummary {
    param($EventObject)
    if ($null -eq $EventObject -or -not $EventObject.PSObject.Properties['usage'] -or $null -eq $EventObject.usage) {
        return 'task completed'
    }

    $outputTokens = 0
    $reasoningTokens = 0
    if ($EventObject.usage.PSObject.Properties['output_tokens'] -and $null -ne $EventObject.usage.output_tokens) {
        $outputTokens = [int]$EventObject.usage.output_tokens
    }
    if ($EventObject.usage.PSObject.Properties['reasoning_output_tokens'] -and $null -ne $EventObject.usage.reasoning_output_tokens) {
        $reasoningTokens = [int]$EventObject.usage.reasoning_output_tokens
    }

    if ($reasoningTokens > 0 -and $outputTokens > 0) {
        return ('task completed (output=' + $outputTokens + ', reasoning=' + $reasoningTokens + ')')
    }
    if ($outputTokens > 0) {
        return ('task completed (output=' + $outputTokens + ')')
    }
    return 'task completed'
}

function Convert-CodexVisibleCliEvent {
    param([object]$Chunk)
    if ($null -eq $Chunk) { return $null }

    $line = [string]$Chunk
    if ([string]::IsNullOrWhiteSpace($line)) { return $null }

    $trimmed = $line.Trim()
    if ($trimmed.Length -lt 2 -or $trimmed[0] -ne '{') {
        return [pscustomobject]@{ text = $line; color = 'DarkGray' }
    }

    try {
        $ev = $trimmed | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return [pscustomobject]@{ text = $line; color = 'DarkGray' }
    }

    $type = [string]$ev.type

    switch ($type) {
        'thread.started' {
            return [pscustomobject]@{
                text = '==> Codex session started'
                color = 'Cyan'
                blankLineBefore = $true
                dedupeKey = 'session'
                dedupeValue = 'codex-thread'
            }
        }
        'turn.started' {
            return [pscustomobject]@{
                text = '==> Phase: running task'
                color = 'Cyan'
                blankLineBefore = $true
                dedupeKey = 'phase'
                dedupeValue = 'running task'
            }
        }
        'turn.completed' {
            return [pscustomobject]@{
                text = '==> Result: ' + (Get-CodexTurnResultSummary $ev)
                color = 'Cyan'
                blankLineBefore = $true
            }
        }
        'turn.failed' {
            $detail = Get-CodexErrorText $ev
            if ([string]::IsNullOrWhiteSpace($detail)) { $detail = 'task failed' }
            return [pscustomobject]@{
                text = 'ERROR: ' + $detail
                color = 'Red'
                blankLineBefore = $true
            }
        }
        'session.started' {
            return [pscustomobject]@{
                text = '==> Codex session started'
                color = 'Cyan'
                blankLineBefore = $true
                dedupeKey = 'session'
                dedupeValue = 'codex-session'
            }
        }
        'session.completed' {
            return [pscustomobject]@{
                text = '==> Result: session completed'
                color = 'Cyan'
                blankLineBefore = $true
            }
        }
        'session.failed' {
            $detail = Get-CodexErrorText $ev
            if ([string]::IsNullOrWhiteSpace($detail)) { $detail = 'session failed' }
            return [pscustomobject]@{
                text = 'ERROR: ' + $detail
                color = 'Red'
                blankLineBefore = $true
            }
        }
        'error' {
            $detail = Get-CodexErrorText $ev
            if ([string]::IsNullOrWhiteSpace($detail)) { $detail = 'unexpected codex error' }
            return [pscustomobject]@{
                text = 'ERROR: ' + $detail
                color = 'Red'
            }
        }
    }

    if ($type.StartsWith('item.', [System.StringComparison]::OrdinalIgnoreCase) -and $null -ne $ev.item) {
        $itemType = Get-CodexObjectString $ev.item 'type'
        $itemStatus = Get-CodexObjectString $ev.item 'status'
        $itemLabel = Get-CodexItemLabel $ev.item
        $phase = Get-CodexPhaseForItemType $itemType

        $events = New-Object System.Collections.Generic.List[object]
        if (-not [string]::IsNullOrWhiteSpace($phase)) {
            $events.Add([pscustomobject]@{
                text = '==> Phase: ' + $phase
                color = 'Cyan'
                blankLineBefore = $true
                dedupeKey = 'phase'
                dedupeValue = $phase
            })
        }

        if ($itemStatus -eq 'failed' -or $type.EndsWith('.failed', [System.StringComparison]::OrdinalIgnoreCase)) {
            $detail = Get-CodexErrorText $ev
            if ([string]::IsNullOrWhiteSpace($detail)) {
                $detail = 'item failed'
                if (-not [string]::IsNullOrWhiteSpace($itemLabel)) {
                    $detail = $itemLabel
                }
            } elseif (-not [string]::IsNullOrWhiteSpace($itemLabel)) {
                $detail = $itemLabel + ': ' + $detail
            }
            $events.Add([pscustomobject]@{
                text = 'ERROR: ' + $detail
                color = 'Red'
            })
            return $events
        }

        if ($itemType -eq 'agent_message' -and $type.EndsWith('.completed', [System.StringComparison]::OrdinalIgnoreCase)) {
            $events.Add([pscustomobject]@{
                text = '[Result] ' + (Get-CodexResultText $ev.item)
                color = 'White'
            })
            return $events
        }

        if ([string]::IsNullOrWhiteSpace($itemLabel)) {
            $itemLabel = $type
            if (-not [string]::IsNullOrWhiteSpace($itemType)) {
                $itemLabel = $itemType
            }
        }

        if ($type.EndsWith('.started', [System.StringComparison]::OrdinalIgnoreCase)) {
            $events.Add([pscustomobject]@{
                text = '[Item] ' + $itemLabel
                color = 'Green'
            })
            return $events
        }

        if ($type.EndsWith('.completed', [System.StringComparison]::OrdinalIgnoreCase)) {
            $events.Add([pscustomobject]@{
                text = '[Item] Done: ' + $itemLabel
                color = 'DarkGreen'
            })
            return $events
        }

        $events.Add([pscustomobject]@{
            text = '[Item] ' + $itemLabel
            color = 'Green'
        })
        return $events
    }

    $fallback = Get-CodexErrorText $ev
    if (-not [string]::IsNullOrWhiteSpace($fallback)) {
        return [pscustomobject]@{
            text = $fallback
            color = 'DarkYellow'
        }
    }

    return [pscustomobject]@{
        text = $line
        color = 'DarkGray'
    }
}
";
    }
}
#endif
