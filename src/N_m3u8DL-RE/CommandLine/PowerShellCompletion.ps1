#requires -Version 5.1

& {
    # 执行此脚本以注册补全；候选项来自程序的实际参数定义，不维护额外的参数表。
    $completer = {
        param($wordToComplete, $commandAst, $cursorPosition)

        $process = $null
        try {
            $command = Get-Command $commandAst.GetCommandName() -CommandType Application -ErrorAction Stop

            # 用 AST 还原光标前的参数，统一单双引号，并去掉 &、可执行文件路径等 PowerShell 语法。
            # 只读取语法树中的文本，不求值变量或执行用户输入中的表达式。
            $commandLine = 'N_m3u8DL-RE'
            foreach ($element in $commandAst.CommandElements | Select-Object -Skip 1) {
                if ($element.Extent.EndOffset -ge $cursorPosition) {
                    break
                }
                $value = $element.Extent.Text
                if ($element -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
                    $element -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
                    $value = $element.Value
                }
                $commandLine += ' "' + $value.Replace('"', '') + '"'
            }
            $commandLine += ' ' + $wordToComplete.Trim([char[]]@('"', "'"))

            # 使用 ProcessStartInfo 绕开 PowerShell 5.1 与 7 的原生命令传参差异。
            # 引号前和末尾的反斜杠需按原生参数规则转义，使整条待补全命令作为一个参数传入。
            $escapedLine = [regex]::Replace($commandLine, '(\\*)"', '$1$1\"')
            $escapedLine = [regex]::Replace($escapedLine, '(\\+)$', '$1$1')
            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = $command.Source
            $startInfo.Arguments = '[suggest:' + $commandLine.Length + '] "' + $escapedLine + '"'
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8

            $process = New-Object System.Diagnostics.Process
            $process.StartInfo = $startInfo
            $null = $process.Start()
            $output = $process.StandardOutput.ReadToEndAsync()
            $errorOutput = $process.StandardError.ReadToEndAsync()
            # 补全失败或程序无响应时及时返回，让 PowerShell 继续执行默认路径补全。
            if (-not $process.WaitForExit(3000)) {
                $process.Kill()
                return
            }
            if ($process.ExitCode -ne 0) {
                return
            }
            $null = $errorOutput.GetAwaiter().GetResult()
            foreach ($candidate in $output.GetAwaiter().GetResult() -split '\r?\n') {
                if ([string]::IsNullOrWhiteSpace($candidate)) {
                    continue
                }
                $completionText = $candidate
                if ($wordToComplete.StartsWith("'")) {
                    $completionText = "'" + $candidate + "'"
                }
                elseif ($wordToComplete.StartsWith('"')) {
                    $completionText = '"' + $candidate + '"'
                }
                $resultType = 'ParameterValue'
                if ($candidate.StartsWith('-')) {
                    $resultType = 'ParameterName'
                }
                [System.Management.Automation.CompletionResult]::new($completionText, $candidate, $resultType, $candidate)
            }
        }
        catch {
            # 未找到程序或补全进程异常时不向交互终端输出错误。
        }
        finally {
            if ($null -ne $process) {
                $process.Dispose()
            }
        }
    }

    # Windows PowerShell 5.1 在未指定 ParameterName 时注册原生命令补全，无需 -Native。
    $registration = @{
        CommandName = @('N_m3u8DL-RE', 'N_m3u8DL-RE.exe')
        ScriptBlock = $completer
    }
    if ((Get-Command Register-ArgumentCompleter).Parameters.ContainsKey('Native')) {
        $registration.Native = $true
    }
    Register-ArgumentCompleter @registration
}
