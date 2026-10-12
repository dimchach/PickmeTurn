$ErrorActionPreference = 'Stop'
$prefix = 'WireGuardTunnel$FreeTurn'

try {
    $services = @(Get-CimInstance Win32_Service | Where-Object {
        $_.Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
    })

    foreach ($service in $services) {
        if ($service.State -ne 'Stopped') {
            & "$env:SystemRoot\System32\sc.exe" stop $service.Name | Out-Null
        }

        & "$env:SystemRoot\System32\sc.exe" delete $service.Name | Out-Null
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(20)

    do {
        $remaining = @(Get-CimInstance Win32_Service | Where-Object {
            $_.Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
        })

        if ($remaining.Count -eq 0) {
            exit 0
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    Write-Error "WireGuard services remain: $($remaining.Name -join ', ')"
    exit 1
}
catch {
    Write-Error $_
    exit 2
}
