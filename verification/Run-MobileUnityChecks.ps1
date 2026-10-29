param(
    [ValidateSet('Prepare', 'Editor', 'Android')]
    [string]$Mode = 'Prepare',
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.0.68f1\Editor\Unity.exe',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'mobile-runs\2026-10-29'),
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'
$sdkRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = Join-Path $PSScriptRoot 'MobileUnityProject'
$templateRoot = Join-Path $PSScriptRoot 'mobile'
$assetRoot = Join-Path $projectRoot 'Assets\MobileVerification'
$editorRoot = Join-Path $assetRoot 'Editor'
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)

if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity not found: $UnityPath" }
if ($TimeoutMinutes -lt 1) { throw 'TimeoutMinutes must be at least 1.' }

function Invoke-VerificationUnity {
    param([string]$Method, [string]$LogName, [switch]$Quit, [switch]$Android)

    $logPath = Join-Path $OutputRoot $LogName
    $arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $projectRoot + '"'),
        '-executeMethod', ('BijiSDK.MobileVerification.Editor.MobileProjectBuilder.' + $Method),
        '-logFile', ('"' + $logPath + '"'), '-bijiOutputRoot', ('"' + $OutputRoot + '"'))
    if ($Quit) { $arguments += '-quit' }
    if ($Android) { $arguments += @('-buildTarget', 'Android') }
    Write-Output "Unity $Method started. Log: $logPath"
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $started = [DateTime]::UtcNow
    $lastProgress = $started
    try {
        while (-not $process.WaitForExit(1000)) {
            $now = [DateTime]::UtcNow
            if (($now - $started).TotalMinutes -ge $TimeoutMinutes) {
                # Terminate only the specific Unity process created by this invocation.
                Stop-Process -Id $process.Id
                throw "Unity $Method exceeded $TimeoutMinutes minutes. Inspect $logPath"
            }
            if (($now - $lastProgress).TotalSeconds -ge 30) {
                Write-Output ("Unity {0} running ({1:N0}s)." -f $Method, ($now - $started).TotalSeconds)
                $lastProgress = $now
            }
        }
        $exitCode = $process.ExitCode
        Write-Output "Unity $Method exit code: $exitCode"
        if ($exitCode -ne 0) { throw "Unity $Method failed with exit code $exitCode. Inspect $logPath" }
    }
    finally {
        $process.Dispose()
    }
}

function Copy-VerificationSources {
    foreach ($name in @('MobileConfigChecks.cs', 'MobileConfigProbe.cs')) {
        $source = Join-Path $templateRoot $name
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing runtime verification template: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $assetRoot $name) -Force
    }
    Copy-Item -LiteralPath (Join-Path $templateRoot 'MobileProjectBuilder.cs') `
        -Destination (Join-Path $editorRoot 'MobileProjectBuilder.cs') -Force
}

function Confirm-TransitiveNewtonsoft {
    $manifestPath = Join-Path $projectRoot 'Packages\manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.dependencies.PSObject.Properties.Name -contains 'com.unity.nuget.newtonsoft-json') {
        throw 'The clean project must resolve Newtonsoft transitively, without a direct manifest entry.'
    }
    $lockPath = Join-Path $projectRoot 'Packages\packages-lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $dependency = $lock.dependencies.'com.unity.nuget.newtonsoft-json'
    if ($null -eq $dependency -or $dependency.version -ne '3.2.1' -or $dependency.depth -lt 1) {
        throw 'Expected transitive Newtonsoft package 3.2.1 did not resolve.'
    }
    $details = "Newtonsoft automatically resolved: version=$($dependency.version) source=$($dependency.source) depth=$($dependency.depth)"
    Write-Output $details
    $details | Set-Content -LiteralPath (Join-Path $OutputRoot 'package-import-result.txt')
}

foreach ($folder in @($editorRoot, $OutputRoot)) {
    [IO.Directory]::CreateDirectory($folder) | Out-Null
}

if ($Mode -eq 'Prepare') {
    foreach ($folder in @('Packages', 'ProjectSettings')) {
        [IO.Directory]::CreateDirectory((Join-Path $projectRoot $folder)) | Out-Null
    }
    $packagePath = (Join-Path $sdkRoot 'clients\unity').Replace('\', '/')
    $manifest = @{
        dependencies = @{
            'com.biji.sdk' = "file:$packagePath"
            'com.unity.modules.imgui' = '1.0.0'
            'com.unity.modules.jsonserialize' = '1.0.0'
            'com.unity.modules.unitywebrequest' = '1.0.0'
        }
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $projectRoot 'Packages\manifest.json'), $manifest)
    [IO.File]::WriteAllText((Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'),
        'm_EditorVersion: 6000.0.68f1' + [Environment]::NewLine)
    Copy-Item -LiteralPath (Join-Path $templateRoot 'MobileProjectBuilder.cs') `
        -Destination (Join-Path $editorRoot 'MobileProjectBuilder.cs') -Force

    # First import the actual UPM sample; runtime checks compile against its assembly.
    Invoke-VerificationUnity -Method 'ImportSample' -LogName 'unity-package-import.log' -Quit
    Confirm-TransitiveNewtonsoft
    Copy-VerificationSources
    Invoke-VerificationUnity -Method 'Setup' -LogName 'unity-project-setup.log' -Quit
}
else {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'Assets\Scenes\BijiSDKMobileTest.unity'))) {
        throw 'Run -Mode Prepare before running Editor checks or an Android build.'
    }
    Copy-VerificationSources
    Confirm-TransitiveNewtonsoft
    if ($Mode -eq 'Editor') {
        Invoke-VerificationUnity -Method 'RunEditorChecks' -LogName 'unity-pages-editor.log'
    }
    else {
        Invoke-VerificationUnity -Method 'BuildAndroid' -LogName 'unity-android-build.log' -Quit -Android
    }
}

Write-Output "Mobile verification $Mode completed. Project: $projectRoot; output: $OutputRoot"
