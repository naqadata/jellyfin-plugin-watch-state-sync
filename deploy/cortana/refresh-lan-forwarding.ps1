$ErrorActionPreference = "Stop"

$distro = "Ubuntu-24.04"
$wslIp = (wsl.exe -d $distro hostname -I).Trim().Split(" ")[0]
if (-not $wslIp) {
    throw "Could not determine the $distro WSL address"
}

$ports = @(18096, 32410)
foreach ($port in $ports) {
    netsh interface portproxy delete v4tov4 `
        listenport=$port listenaddress=0.0.0.0 2>$null | Out-Null
    netsh interface portproxy add v4tov4 `
        listenport=$port listenaddress=0.0.0.0 `
        connectport=$port connectaddress=$wslIp | Out-Null

    $ruleName = "Watch-State-Sync-Dev-$port"
    if (-not (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule `
            -Name $ruleName `
            -DisplayName "Watch State Sync Dev ($port)" `
            -Enabled True `
            -Direction Inbound `
            -Protocol TCP `
            -Action Allow `
            -LocalPort $port `
            -RemoteAddress LocalSubnet `
            -Profile Any | Out-Null
    }
}

Write-Output "Forwarding Cortana ports 18096 and 32410 to WSL at $wslIp"
