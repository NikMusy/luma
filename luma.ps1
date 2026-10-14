param([Parameter(Position=0)][string]$cmd = 'status')

$steam = 'C:\Program Files (x86)\Steam'
$stash = 'C:\Users\slaye\.luma-stash'
$snap  = Join-Path $stash '_steam-client'
$items = @(
    'AppList', 'GreenLuma2025_Files', 'DLLInjector.exe', 'DLLInjector.ini',
    'GreenLumaSettings_2025.exe', 'GreenLuma_2025_x64.dll', 'GreenLuma_2025_x86.dll', 'steam.cfg'
)

# Not part of the client: games, profile, caches, logs, auth tokens, GreenLuma itself
$xd = @('steamapps', 'userdata', 'config', 'logs', 'dumps', 'appcache', 'depotcache', 'AppList', 'GreenLuma2025_Files')
$xf = @('*.log', 'ssfn*', 'steam.cfg', 'DLLInjector.*', 'GreenLuma*')

function Stop-SteamStuff {
    Get-Process DLLInjector, steam, steamwebhelper, dota2 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
}

function Move-Set($fromRoot, $toRoot) {
    New-Item -ItemType Directory -Force $toRoot | Out-Null
    $moved = 0
    foreach ($i in $items) {
        $src = Join-Path $fromRoot $i
        $dst = Join-Path $toRoot $i
        if (-not (Test-Path $src)) { continue }
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        try { Move-Item $src $dst -Force -ErrorAction Stop; $moved++ }
        catch { Write-Host "FAIL: $i is locked ($($_.Exception.Message))" -ForegroundColor Red }
    }
    return $moved
}

function Copy-Client($from, $to) {
    New-Item -ItemType Directory -Force $to | Out-Null
    & robocopy $from $to /E /IS /IT /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD @xd /XF @xf | Out-Null
    return ($LASTEXITCODE -lt 8)
}

switch ($cmd) {
    'off' {
        Stop-SteamStuff
        # steam.cfg in the Steam folder = pinned custom client. Save it before it gets updated away.
        if (Test-Path (Join-Path $steam 'steam.cfg')) {
            Write-Host 'Saving custom Steam client to stash (1-2 GB, takes a minute)...'
            if (Copy-Client $steam $snap) { Write-Host 'Custom Steam saved.' -ForegroundColor Green }
            else { Write-Host 'Snapshot FAILED - aborting, nothing was changed.' -ForegroundColor Red; break }
        } elseif (-not (Test-Path $snap)) {
            Write-Host 'Note: no custom Steam snapshot exists (steam.cfg not found).' -ForegroundColor Yellow
        }
        $n = Move-Set $steam $stash
        Write-Host "luma OFF: $n item(s) moved to stash. Start Steam normally (it will update to the official client)." -ForegroundColor Green
    }
    'on' {
        $hasFiles = @($items | ? { Test-Path (Join-Path $stash $_) }).Count
        if (-not $hasFiles -and -not (Test-Path $snap)) { Write-Host 'Stash is empty - nothing to restore.' -ForegroundColor Yellow; break }
        Stop-SteamStuff
        if (Test-Path $snap) {
            Write-Host 'Restoring custom Steam client from stash...'
            if (Copy-Client $snap $steam) { Write-Host 'Custom Steam restored.' -ForegroundColor Green }
            else { Write-Host 'Client restore reported errors.' -ForegroundColor Red }
        }
        $n = Move-Set $stash $steam
        Write-Host "luma ON: client + $n item(s) restored. Start Steam via DLLInjector.exe." -ForegroundColor Green
    }
    default {
        $inSteam = @($items | ? { Test-Path (Join-Path $steam $_) })
        $inStash = @($items | ? { Test-Path (Join-Path $stash $_) })
        $state = if ($inSteam.Count) { 'ON (files in Steam)' } elseif ($inStash.Count) { 'OFF (files in stash)' } else { 'OFF (no files anywhere)' }
        Write-Host "luma: $state"
        Write-Host "  in Steam: $($inSteam -join ', ')"
        Write-Host "  in stash: $($inStash -join ', ')"
        Write-Host "  custom Steam snapshot: $(if (Test-Path $snap) { 'yes' } else { 'no' })"
    }
}
