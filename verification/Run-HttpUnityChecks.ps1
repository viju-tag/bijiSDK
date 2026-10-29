param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.0.68f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pythonPath = (Get-Command python -ErrorAction Stop).Source
$runRoot = Join-Path $PSScriptRoot ('http-runs\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$publisher = Join-Path $projectRoot 'backend\publishing\publish.py'
$server = Join-Path $projectRoot 'backend\delivery\server.py'
& $pythonPath $publisher --data-dir $runRoot init-demo
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize development publications.' }

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$url = "http://127.0.0.1:$port/"
$serverArguments = @(('"' + $server + '"'), '--data-dir', ('"' + $runRoot + '"'), '--host', '127.0.0.1', '--port', $port)
$serverProcess = Start-Process -FilePath $pythonPath -ArgumentList $serverArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'server.stdout.log') -RedirectStandardError (Join-Path $runRoot 'server.stderr.log')
$faultProcess = $null
$previousFaultUrl = $env:BIJI_HTTP_FAULT_URL
$previousUrl = $env:BIJI_HTTP_TEST_URL
$previousData = $env:BIJI_HTTP_TEST_DATA
$previousPython = $env:BIJI_HTTP_TEST_PYTHON
$exitCode = 1
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($serverProcess.HasExited) { throw 'Development server exited before readiness.' }
        try {
            Invoke-RestMethod -Uri ($url + 'health') -TimeoutSec 1 | Out-Null
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $ready) { throw 'Development server did not become ready.' }
    $faultListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $faultListener.Start()
    $faultPort = $faultListener.LocalEndpoint.Port
    $faultListener.Stop()
    $faultUrl = "http://127.0.0.1:$faultPort/"
    $faultScript = Join-Path $PSScriptRoot 'http_fault_server.py'
    $faultArguments = @(('"' + $faultScript + '"'), '--port', $faultPort)
    $faultProcess = Start-Process -FilePath $pythonPath -ArgumentList $faultArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'fault.stdout.log') -RedirectStandardError (Join-Path $runRoot 'fault.stderr.log')
    $faultReady = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($faultProcess.HasExited) { throw 'Fault server exited.' }
        try { Invoke-RestMethod -Uri ($faultUrl + 'health') -TimeoutSec 1 | Out-Null; $faultReady = $true; break }
        catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $faultReady) { throw 'Fault server did not become ready.' }
    $env:BIJI_HTTP_FAULT_URL = $faultUrl
    $env:BIJI_HTTP_TEST_URL = $url
    $env:BIJI_HTTP_TEST_DATA = $runRoot
    $env:BIJI_HTTP_TEST_PYTHON = $pythonPath
    & (Join-Path $PSScriptRoot 'Run-UnityChecks.ps1') -UnityPath $UnityPath
    $exitCode = $LASTEXITCODE
} finally {
    $env:BIJI_HTTP_FAULT_URL = $previousFaultUrl
    if ($null -ne $faultProcess) {
        if (-not $faultProcess.HasExited) { Stop-Process -Id $faultProcess.Id }
        $faultProcess.Dispose()
    }
    $env:BIJI_HTTP_TEST_URL = $previousUrl
    $env:BIJI_HTTP_TEST_DATA = $previousData
    $env:BIJI_HTTP_TEST_PYTHON = $previousPython
    if (-not $serverProcess.HasExited) { Stop-Process -Id $serverProcess.Id }
    $serverProcess.Dispose()
}
exit $exitCode
