# One-click build LevelLibrary (no Visual Studio needed)
#
# -Backup : take a snapshot into the backup library FIRST (rule: back up before big changes).
#           Library: C:\Users\Administrator\Desktop\LevelLibrary_backups
#
# ASCII ONLY on purpose: Windows PowerShell 5.1 parses .ps1 as GBK.
param([switch]$Backup, [string]$Note = "before-build")

if (Test-Path "C:\Users\Administrator\.dotnet\sdk10") {
    $env:PATH = "C:\Users\Administrator\.dotnet\sdk10;" + $env:PATH
    $env:DOTNET_ROOT = "C:\Users\Administrator\.dotnet\sdk10"
}
$ErrorActionPreference = "Stop"
$proj = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $proj

if ($Backup) {
    $bk = "C:\Users\Administrator\Desktop\LevelLibrary_backups\backup.ps1"
    if (Test-Path $bk) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $bk -Note $Note -ProjectDir $proj
        if ($LASTEXITCODE -ne 0) { Write-Host "BACKUP FAILED - not building." -ForegroundColor Red; exit 1 }
    } else {
        Write-Host ("backup script not found: " + $bk) -ForegroundColor Yellow
    }
}

Write-Host "==> dotnet build (net48) ..."
dotnet build LevelLibrary.csproj -c Release -v minimal
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED!" -ForegroundColor Red; exit 1 }
Write-Host "Deployed to Mods\LevelLibrary" -ForegroundColor Green
Write-Host "DONE" -ForegroundColor Green
