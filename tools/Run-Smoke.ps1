<#
.SYNOPSIS
在隔离游戏副本和独立存档目录运行单人或本机双端检查，不连接 Steam。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GamePath,
    [Parameter(Mandatory)][string]$ModPath,
    [Parameter(Mandatory)][string]$BaseLibPath,
    [ValidateSet('single', 'multiplayer')][string]$Mode = 'single'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$ModPath = (Resolve-Path -LiteralPath $ModPath).Path
$BaseLibPath = (Resolve-Path -LiteralPath $BaseLibPath).Path
$release = Get-Content -Raw -LiteralPath "$GamePath/release_info.json" | ConvertFrom-Json
$manifest = Get-Content -Raw -LiteralPath "$ModPath/TheBookOfAges.json" | ConvertFrom-Json
if ($release.version.TrimStart('v') -ne $manifest.min_game_version) { throw '游戏版本与待测 Mod 不匹配。' }
$id = [Guid]::NewGuid().ToString('N')
$testRoot = "$root/.compat-work/smoke-$($release.version)-$id"
$userDirName = "SlayTheSpire2-GmCompat-$id"
New-Item -ItemType Directory -Force "$testRoot/data_sts2_windows_x86_64", "$testRoot/mods/GmCompatSmoke" | Out-Null
New-Item -ItemType File -Force "$root/.compat-work/.gdignore" | Out-Null

# 游戏文件只读使用硬链接，跨磁盘时复制；绝不改写链接目标。
foreach ($file in @((Get-ChildItem -LiteralPath $GamePath -File); (Get-ChildItem -LiteralPath "$GamePath/data_sts2_windows_x86_64" -File))) {
    if ($file.Name -eq 'override.cfg') { continue }
    $target = Join-Path $testRoot ([IO.Path]::GetRelativePath($GamePath, $file.FullName))
    try { New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null }
    catch { Copy-Item -LiteralPath $file.FullName -Destination $target }
}
foreach ($dependency in @(@{ name = 'TheBookOfAges'; path = $ModPath }, @{ name = 'BaseLib'; path = $BaseLibPath })) {
    New-Item -ItemType Directory -Force "$testRoot/mods/$($dependency.name)" | Out-Null
    foreach ($extension in 'dll', 'json', 'pck') {
        Copy-Item -LiteralPath "$($dependency.path)/$($dependency.name).$extension" -Destination "$testRoot/mods/$($dependency.name)/"
    }
}
'[application]', 'config/use_custom_user_dir=true', "config/custom_user_dir_name=`"$userDirName`"" |
    Set-Content -LiteralPath "$testRoot/override.cfg" -Encoding utf8
foreach ($playerId in 1, 1000) {
    $accountDir = "$env:APPDATA/$userDirName/default/$playerId"
    New-Item -ItemType Directory -Force $accountDir | Out-Null
    @{ schema_version = 7; mod_settings = @{ mods_enabled = $true }; seen_ea_disclaimer = $true; skip_intro_logo = $true; language = 'zhs' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$accountDir/settings.save" -Encoding utf8
}

& dotnet build "$PSScriptRoot/RuntimeSmoke/RuntimeSmoke.csproj" "-p:GameDataPath=$GamePath/data_sts2_windows_x86_64" "-p:ModAssemblyPath=$ModPath/TheBookOfAges.dll" "-p:BaseLibPath=$BaseLibPath" -v:minimal
if ($LASTEXITCODE -ne 0) { throw '运行时探针编译失败。' }
Copy-Item -LiteralPath "$PSScriptRoot/RuntimeSmoke/bin/Debug/net9.0/GmCompatSmoke.dll" -Destination "$testRoot/mods/GmCompatSmoke/"
@{ id = 'GmCompatSmoke'; name = '隔离验证探针'; version = '1.0.0'; has_dll = $true; has_pck = $false; affects_gameplay = $true; dependencies = @(@{ id = 'TheBookOfAges' }) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$testRoot/mods/GmCompatSmoke/GmCompatSmoke.json" -Encoding utf8

$roles = if ($Mode -eq 'single') { @('single') } else { @('host', 'client') }
$processes = @()
try {
    foreach ($role in $roles) {
        $extra = switch ($role) { 'host' { '-fastmp host_standard' }; 'client' { '-fastmp join -clientId 1000' }; default { '' } }
        $arguments = "--headless --force-steam off --quit-after 30000 --log-file `"$testRoot/$role.log`" $extra"
        $processes += Start-Process -FilePath "$testRoot/SlayTheSpire2.exe" -WorkingDirectory $testRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if ($role -eq 'host') { Start-Sleep -Seconds 4 }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(150)
    foreach ($process in $processes) {
        while (-not $process.WaitForExit(500)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw "游戏验证超时：$testRoot" }
        }
        if ($process.ExitCode -ne 0) { throw "测试进程异常退出，退出码=$($process.ExitCode)：$testRoot" }
    }
    foreach ($role in $roles) {
        $log = Get-Content -Raw -LiteralPath "$testRoot/$role.log"
        $marker = if ($role -eq 'single') { 'GM_SMOKE_ALL_PASSED' } else { "GM_MP_PASSED role=$role " }
        if (-not $log.Contains($marker) -or $log -match 'GM_(SMOKE|MP)_FAILED') { throw "检查未通过：$testRoot/$role.log" }
    }
    Write-Host "游戏运行时验证通过：$testRoot"
}
finally {
    # 仅关闭本次脚本创建的测试进程；保留日志与隔离数据以便复查。
    foreach ($process in $processes) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() } }
}
exit 0
