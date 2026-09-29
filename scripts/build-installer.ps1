param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsDir = Join-Path $repoRoot 'artifacts'
$stagingDir = Join-Path $artifactsDir 'installer-staging'
$outputDir = Join-Path $artifactsDir 'installer'
$scriptPath = Join-Path $repoRoot 'installer\RepoTransit.iss'

if (-not $IsccPath) {
    $candidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw '未找到 Inno Setup 6 的 ISCC.exe。请先安装 Inno Setup，或使用 -IsccPath 指定编译器。'
}

New-Item -ItemType Directory -Path $artifactsDir, $outputDir -Force | Out-Null
if (Test-Path -LiteralPath $stagingDir) {
    $resolved = (Resolve-Path -LiteralPath $stagingDir).Path
    if ($resolved -ne [IO.Path]::GetFullPath($stagingDir) -or
        -not $resolved.StartsWith($artifactsDir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理工作区外的暂存目录：$resolved"
    }
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}

& dotnet publish (Join-Path $repoRoot 'src\RepoTransit\RepoTransit.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeSourceRevisionInInformationalVersion=false "-p:Version=$Version" -o $stagingDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败。' }
if (-not (Test-Path -LiteralPath (Join-Path $stagingDir 'RepoTransit.exe'))) {
    throw '发布结果缺少 RepoTransit.exe。'
}

& $IsccPath "/DAppVersion=$Version" "/DSourceDir=$stagingDir" "/DOutputDir=$outputDir" $scriptPath
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 编译失败。' }

$installer = Join-Path $outputDir "RepoTransit-$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw "未找到安装包：$installer" }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($installer + '.sha256') -Value "$hash  $(Split-Path $installer -Leaf)" -Encoding ascii
Write-Output "安装包：$installer"
Write-Output "SHA-256：$hash"
