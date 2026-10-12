$ErrorActionPreference = 'Stop'
$eventName = 'Local\PickmeTurn.Shutdown'
$processName = 'PickmeTurn'
$timeoutSeconds = 30

try {
    $processes = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)

    if ($processes.Count -eq 0) {
        exit 0
    }

    try {
        $shutdownEvent = [System.Threading.EventWaitHandle]::OpenExisting($eventName)
    }
    catch [System.Threading.WaitHandleCannotBeOpenedException] {
        Write-Error 'PickmeTurn is running but does not expose the graceful shutdown event. Close the application manually and retry.'
        exit 2
    }

    try {
        $shutdownEvent.Set() | Out-Null
    }
    finally {
        $shutdownEvent.Dispose()
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)

    do {
        $processes = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)

        if ($processes.Count -eq 0) {
            exit 0
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    Write-Error "PickmeTurn did not exit within $timeoutSeconds seconds. Uninstallation must not continue."
    exit 3
}
catch {
    Write-Error $_
    exit 4
}
