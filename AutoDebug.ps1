# 修复权限：允许脚本运行（只需在PowerShell执行一次）
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser

$slnPath = "C:\Users\ben_l\Documents\Coding\CMetro\MetroMarkdownEditor\MetroMarkdownEditor.sln" # 请确保路径正确
$vsVersion = "VisualStudio.DTE.14.0"

function Start-AutoDebug {
    try {
        Write-Host "--- 自动化调试启动 ---" -ForegroundColor Cyan
        
        # 1. 检查 VS2015 是否运行
        $vsProcess = Get-Process "devenv" -ErrorAction SilentlyContinue
        if ($null -eq $vsProcess) {
            Write-Host "错误：未发现正在运行的 Visual Studio 2015，请先手动打开它！" -ForegroundColor Red
            return
        }

        # 2. 获取 COM 对象
        Write-Host "正在连接 VS2015 (DTE)..."
        try {
            $dte = [Runtime.InteropServices.Marshal]::GetActiveObject($vsVersion)
        } catch {
            Write-Host "连接失败：请尝试以【管理员身份】同时运行 VS2015 和此脚本。" -ForegroundColor Yellow
            throw $_
        }

        # 3. 编译
        Write-Host "正在编译解决方案..." -ForegroundColor Yellow
        $dte.Solution.SolutionBuild.Build($true)
        
        if ($dte.Solution.SolutionBuild.LastBuildInfo -ne 0) {
            Write-Host "编译失败！请让 Antigravity 检查语法错误。" -ForegroundColor Red
            return
        }

        # 4. 启动调试
        Write-Host "启动调试 (F5)..." -ForegroundColor Green
        $dte.Debugger.Go($false)

        # 5. 循环监控
        Write-Host "监控中...（按 Ctrl+C 强制退出）"
        while ($true) {
            $mode = $dte.Debugger.CurrentMode
            if ($mode -eq 3) { # 中断模式 (报错了)
                $reason = $dte.Debugger.LastBreakReason
                $frame = $dte.Debugger.CurrentStackFrame
                $log = "报错原因: $reason `n文件: $($frame.FileName) `n行号: $($frame.LineNumber)"
                Write-Host "!!! 检测到报错 !!!" -ForegroundColor Red
                Write-Host $log
                $log | Out-File "debug_error.log"
                break
            }
            elseif ($mode -eq 0) { # 正常停止
                Write-Host "程序正常结束。" -ForegroundColor Green
                break
            }
            Start-Sleep -Milliseconds 500
        }
    }
    catch {
        Write-Host "发生意外错误: $($_.Exception.Message)" -ForegroundColor Red
    }
    finally {
        Write-Host "`n脚本运行完毕。按任意键退出..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
}

Start-AutoDebug