param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installer = Join-Path $repoRoot "artifacts\installer\RepoTransit-$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw "安装包不存在：$installer" }
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{5E3D4A8B-0FD3-4BC5-AE31-C9E9C33892D0}_is1'
if (Test-Path -LiteralPath $registration) {
    throw '已检测到仓渡安装版。请在未安装仓渡的测试环境运行此检查，避免覆盖现有安装。'
}

$installDir = Join-Path $repoRoot ("artifacts\installer-smoke-" + [guid]::NewGuid().ToString('N'))
$app = Join-Path $installDir 'RepoTransit.exe'
$uninstaller = Join-Path $installDir 'unins000.exe'
$appProcess = $null
try {
    $setup = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$installDir`"") `
        -PassThru -Wait -WindowStyle Hidden
    if ($setup.ExitCode -ne 0) { throw "安装程序退出码：$($setup.ExitCode)" }
    if (-not (Test-Path -LiteralPath $app) -or -not (Test-Path -LiteralPath $uninstaller)) {
        throw '安装后缺少应用或卸载程序。'
    }
    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
    if ($actualVersion -ne $Version) { throw "应用版本错误：$actualVersion" }

    $appProcess = Start-Process -FilePath $app -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    $appProcess.Refresh()
    if ($appProcess.HasExited) { throw "安装后的应用提前退出：$($appProcess.ExitCode)" }
    Write-Output "安装、版本及启动检查通过：$Version"
}
finally {
    if ($null -ne $appProcess) {
        $appProcess.Refresh()
        if (-not $appProcess.HasExited) { Stop-Process -Id $appProcess.Id }
    }
    if (Test-Path -LiteralPath $uninstaller) {
        $remove = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
            -PassThru -Wait -WindowStyle Hidden
        if ($remove.ExitCode -ne 0) { throw "卸载程序退出码：$($remove.ExitCode)" }
        if (Test-Path -LiteralPath $app) { throw '卸载后应用文件仍存在。' }
        Write-Output '卸载检查通过。'
    }
}
