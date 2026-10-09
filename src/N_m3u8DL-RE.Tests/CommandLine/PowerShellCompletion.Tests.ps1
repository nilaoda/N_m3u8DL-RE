#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [string]$BinaryPath
)

$ErrorActionPreference = 'Stop'
$binary = (Get-Item -LiteralPath $BinaryPath).FullName
$originalPath = $env:PATH
$originalLocation = Get-Location
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('nm3u8dl-completion-' + [guid]::NewGuid())
try {
    $env:PATH = (Split-Path $binary) + [IO.Path]::PathSeparator + $env:PATH
    $script = @(& $binary --generate-completion powershell)
    if ($LASTEXITCODE -ne 0 -or $script.Count -eq 0) {
        throw 'Failed to export the embedded completion script'
    }
    $script | Out-String | Invoke-Expression
    $cases = @(
        @{ Line = 'N_m3u8DL-RE mux --muxer '; Expected = @('ffmpeg', 'mkvmerge') }
        @{ Line = 'N_m3u8DL-RE --sub-'; Expected = @('--sub-format', '--sub-only') }
        @{ Line = 'N_m3u8DL-RE --sub-format '; Expected = @('SRT', 'VTT') }
        @{ Line = 'N_m3u8DL-RE --sub-format V'; Expected = @('VTT') }
        @{ Line = 'N_m3u8DL-RE --log-level '; Expected = @('DEBUG', 'ERROR', 'INFO', 'OFF', 'WARN') }
        @{ Line = 'N_m3u8DL-RE --ui-language '; Expected = @('en-US', 'zh-CN', 'zh-TW') }
        @{ Line = 'N_m3u8DL-RE --custom-hls-scope '; Expected = @('ALL', 'AUDIO', 'VIDEO') }
        @{ Line = 'N_m3u8DL-RE --generate-completion '; Expected = @('powershell') }
        @{ Line = "N_m3u8DL-RE --save-dir 'one two' --sub-format V"; Expected = @('VTT') }
        @{ Line = 'N_m3u8DL-RE --save-dir "one two" --sub-format V'; Expected = @('VTT') }
        @{ Line = "N_m3u8DL-RE --save-dir '中文路径' --sub-format V"; Expected = @('VTT') }
        @{ Line = "N_m3u8DL-RE --sub-format 'V"; Expected = @("'VTT'") }
        @{ Line = 'N_m3u8DL-RE --sub-format "V'; Expected = @('"VTT"') }
        @{ Line = 'N_m3u8DL-RE --save-dir "C:\one two\" --sub-format V'; Expected = @('VTT') }
        @{ Line = 'Get-Date; N_m3u8DL-RE --sub-format V'; Expected = @('VTT') }
        @{ Line = "& '$($binary.Replace("'", "''"))' --sub-format V"; Expected = @('VTT') }
        @{ Line = 'N_m3u8DL-RE --sub-format V --log-level INFO'; Cursor = 26; Expected = @('VTT') }
        @{ Line = 'N_m3u8DL-RE $(throw "must not execute") --sub-format V'; Expected = @('VTT') }
    )
    foreach ($case in $cases) {
        $cursor = $case.Line.Length
        if ($case.ContainsKey('Cursor')) {
            $cursor = $case.Cursor
        }
        $result = TabExpansion2 $case.Line $cursor
        $actual = @($result.CompletionMatches | ForEach-Object CompletionText)
        if (($actual -join '|') -cne ($case.Expected -join '|')) {
            throw "Unexpected completions for $($case.Line): $($actual -join ', ')"
        }
    }

    # 无参数候选时由 PowerShell 补全本地文件和目录，包括带空格的路径。
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'media folder') -Force
    $null = New-Item -ItemType File -Path (Join-Path $fixture 'fixture.mpd')
    $programDirectory = Join-Path $fixture 'program folder'
    $null = New-Item -ItemType Directory -Path $programDirectory
    $copiedBinary = Join-Path $programDirectory (Split-Path $binary -Leaf)
    Copy-Item -LiteralPath $binary -Destination $copiedBinary
    $line = "& '$($copiedBinary.Replace("'", "''"))' --sub-format V"
    $result = TabExpansion2 $line $line.Length
    if (($result.CompletionMatches.CompletionText -join '|') -cne 'VTT') {
        throw 'Completion failed for an executable path containing spaces'
    }
    Set-Location $fixture
    foreach ($line in @('N_m3u8DL-RE --save-dir ./med', 'N_m3u8DL-RE ./fi')) {
        $result = TabExpansion2 $line $line.Length
        if ($result.CompletionMatches.Count -ne 1) {
            throw "Expected a path completion for $line"
        }
        $completedPath = $result.CompletionMatches[0].CompletionText.Trim([char[]]@('"', "'"))
        if (-not (Test-Path -LiteralPath $completedPath)) {
            throw "Invalid completed path: $completedPath"
        }
    }

    # 终端退出时的控制序列不能污染补全输出。
    $previousTerm = $env:TERM
    try {
        $env:TERM = 'xterm-256color'
        $line = 'N_m3u8DL-RE --sub-'
        $output = @(& $binary "[suggest:$($line.Length)]" $line)
        if ($LASTEXITCODE -ne 0 -or ($output -join '|') -cne '--sub-format|--sub-only') {
            throw 'Completion output contains unexpected text or terminal control sequences'
        }
        foreach ($arguments in @(
            @('--ui-language', 'en-US', '--generate-completion', 'powershell'),
            @('--generate-completion=powershell'),
            @('--generate-completion:powershell')
        )) {
            $generated = @(& $binary @arguments)
            $text = $generated -join "`n"
            if ($LASTEXITCODE -ne 0 -or -not $text.StartsWith('#requires') -or $text.Contains([string][char]27)) {
                throw 'Script export contains unexpected text or terminal control sequences'
            }
        }
    }
    finally {
        $env:TERM = $previousTerm
    }
    Write-Output "PowerShell $($PSVersionTable.PSVersion): $($cases.Count + 7) completion checks passed"
}
finally {
    Set-Location $originalLocation
    $env:PATH = $originalPath
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
