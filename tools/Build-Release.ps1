<#
.SYNOPSIS
针对当前分支对应的真实游戏版本，构建并验证可安装的 Mod 压缩包。
.EXAMPLE
./tools/Build-Release.ps1 -GamePath 'D:/Steam/steamapps/common/Slay the Spire 2' -GodotPath 'D:/Godot/Godot_console.exe'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GamePath,
    [Parameter(Mandatory)][string]$GodotPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$GodotPath = (Resolve-Path -LiteralPath $GodotPath).Path
$manifest = Get-Content -Raw -LiteralPath "$projectRoot/TheBookOfAges.json" | ConvertFrom-Json
$release = Get-Content -Raw -LiteralPath "$GamePath/release_info.json" | ConvertFrom-Json
$gameVersion = $release.version.TrimStart('v')
if ($gameVersion -ne $manifest.min_game_version) {
    throw "当前分支面向 $($manifest.min_game_version)，实际游戏为 $gameVersion。请切换对应分支或传入正确游戏目录。"
}
if (-not (Test-Path -LiteralPath "$GamePath/data_sts2_windows_x86_64/sts2.dll")) {
    throw '未找到 Windows 游戏程序集。此脚本用于 Windows 本机构建，生成的 Mod DLL 为跨平台程序集。'
}

$channel = if ($manifest.version -match '-beta\.') { 'beta' } else { 'stable' }
$releaseName = "TheBookOfAges-$($manifest.version)-sts2-$gameVersion-$channel"
$outputDir = "$projectRoot/artifacts/releases/$releaseName"
$stageDir = "$projectRoot/artifacts/staging/$([Guid]::NewGuid().ToString('N'))"
$probeDir = "$stageDir/pack-probe"
New-Item -ItemType Directory -Force $outputDir, $probeDir | Out-Null

Push-Location $projectRoot
try {
    # 使用新目录承接每次发布，避免导出失败时把上次的 PCK 当成新产物。
    & dotnet publish TheBookOfAges.csproj -c Release "-p:Sts2Path=$GamePath" "-p:ModsPath=$stageDir/mods/" "-p:GodotPath=$GodotPath" "-p:Version=$($manifest.version)" -v:minimal
    if ($LASTEXITCODE -ne 0) { throw '编译或 Godot 导出失败，未生成交付包。' }

    $modDir = "$stageDir/mods/TheBookOfAges"
    $required = @('TheBookOfAges.dll', 'TheBookOfAges.json', 'TheBookOfAges.pck')
    $files = @(Get-ChildItem -LiteralPath $modDir -File -Recurse)
    if ($files.Count -ne 3) { throw "产物应恰好包含三个文件，实际为 $($files.Count)。" }
    foreach ($name in $required) {
        if ((Get-Item -LiteralPath "$modDir/$name").Length -le 0) { throw "产物为空：$name" }
    }

    # 不使用源码项目进行资源验证，否则缺失资源可能从磁盘回退读取，造成假通过。
    '[application]', 'config/name="GM资源包独立验证"', '[rendering]', 'renderer/rendering_method="gl_compatibility"' |
        Set-Content -LiteralPath "$probeDir/project.godot" -Encoding utf8
    $resources = @(Get-ChildItem -LiteralPath "$projectRoot/TheBookOfAges" -Recurse -File |
        Where-Object Extension -In '.png', '.json' |
        ForEach-Object { 'res://' + [IO.Path]::GetRelativePath($projectRoot, $_.FullName).Replace('\', '/') })
    ConvertTo-Json -InputObject $resources | Set-Content -LiteralPath "$stageDir/resources.json" -Encoding utf8
    $probeOutput = & $GodotPath --headless --path $probeDir --script "$PSScriptRoot/Verify-ModPack.gd" -- "$modDir/TheBookOfAges.pck" "$stageDir/resources.json" 2>&1
    $probeExit = $LASTEXITCODE
    $probeOutput | Set-Content -LiteralPath "$outputDir/资源验证.log" -Encoding utf8
    if ($probeExit -ne 0 -or -not ($probeOutput -match 'GM_PACK_VERIFIED')) {
        throw "资源包独立验证失败，详见 $outputDir/资源验证.log"
    }

    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法获取 Git 提交信息。' }
    & git diff --quiet HEAD --
    $trackedChanges = $LASTEXITCODE -ne 0
    $hashes = @($files | ForEach-Object {
        [ordered]@{ file = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    [ordered]@{
        mod_version = $manifest.version
        game_version = $gameVersion
        game_commit = $release.commit
        channel = $channel
        baselib_min_version = $manifest.dependencies[0].min_version
        git_commit = $commit
        tracked_changes = $trackedChanges
        built_at_utc = [DateTime]::UtcNow.ToString('o')
        files = $hashes
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$outputDir/构建信息.json" -Encoding utf8

    Copy-Item -LiteralPath $modDir -Destination $outputDir -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/my_doc/兼容版安装与构建说明.md" -Destination "$outputDir/安装说明.md" -Force
    Compress-Archive -LiteralPath $modDir, "$outputDir/安装说明.md", "$outputDir/构建信息.json" -DestinationPath "$outputDir/$releaseName.zip" -Force
    $zipHash = Get-FileHash -LiteralPath "$outputDir/$releaseName.zip" -Algorithm SHA256
    "$($zipHash.Hash)  $releaseName.zip" | Set-Content -LiteralPath "$outputDir/SHA256SUMS.txt" -Encoding utf8
    Write-Host "构建及资源验证通过：$outputDir/$releaseName.zip"
}
finally {
    Pop-Location
}
exit 0
