param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.0.68f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$validationRoot = Join-Path $PSScriptRoot 'UnityProject'
$editorRoot = Join-Path $validationRoot 'Assets\Editor'
$sampleRoot = Join-Path $validationRoot 'Assets\OfflineSample'
foreach ($folder in @($editorRoot, $sampleRoot, (Join-Path $validationRoot 'Packages'), (Join-Path $validationRoot 'ProjectSettings'))) {
    [IO.Directory]::CreateDirectory($folder) | Out-Null
}
$packagePath = (Join-Path $projectRoot 'clients\unity').Replace('\','/')
$manifest = @{
    dependencies = @{
        'com.biji.sdk' = "file:$packagePath"
        'com.unity.nuget.newtonsoft-json' = '3.2.1'
        'com.unity.modules.imgui' = '1.0.0'
        'com.unity.modules.jsonserialize' = '1.0.0'
        'com.unity.modules.unitywebrequest' = '1.0.0'
    }
} | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $validationRoot 'Packages\manifest.json'), $manifest)
[IO.File]::WriteAllText((Join-Path $validationRoot 'ProjectSettings\ProjectVersion.txt'), "m_EditorVersion: 6000.0.68f1" + [Environment]::NewLine)
foreach ($name in @('CoreChecks.cs','TestSupport.cs','OfflineSuite.cs','HttpChecks.cs','HttpTestSupport.cs','PagesChecks.cs')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "tools\OfflineChecks\$name") -Destination (Join-Path $editorRoot $name) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityOfflineValidation.cs') -Destination (Join-Path $editorRoot 'UnityOfflineValidation.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityHttpIntegration.cs') -Destination (Join-Path $editorRoot 'UnityHttpIntegration.cs') -Force
foreach ($name in @('OfflineConfigSample.cs','PagesConfigTransport.cs','BijiSDK.OfflineSample.asmdef','offline-config.json')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "clients\unity\Samples~\OfflineConfig\$name") -Destination (Join-Path $sampleRoot $name) -Force
}
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity not found: $UnityPath" }
$logPath = Join-Path $PSScriptRoot 'unity-editor-checks.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'BijiSDK.OfflineChecks.UnityOfflineValidation.Run', '-logFile', ('"' + $logPath + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(240000)) {
    Stop-Process -Id $process.Id
    $process.Dispose()
    throw 'Unity verification timed out after four minutes; inspect the log.'
}
$exitCode = $process.ExitCode
$process.Dispose()
exit $exitCode
