<#
  Publishes a new version of the plugin as a GitHub Release.
  Players running an older version see an update banner in SimHub on next start.

  Usage:
    .\release.ps1 -Version 1.1.0 -Notes "- Ajout de ...`n- Correction de ..."
    .\release.ps1 -Version 1.1.0 -NotesFile notes.md -Deploy
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Notes = "",
    [string]$NotesFile = "",
    # Also install the new DLL into the local SimHub (SimHub must be closed)
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
$root    = $PSScriptRoot
$tag     = "v$Version"
$simhub  = if ($env:SIMHUB_INSTALL_PATH) { $env:SIMHUB_INSTALL_PATH } else { "F:\Program Files (x86)\SimHub\" }
$msbuild = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$dllName = "User.BestLapRecords.dll"

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host "ERREUR : $msg" -ForegroundColor Red; exit 1 }

Push-Location $root
try {
    Step "Vérifications"
    $ghCmd = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue
    $script:ghExe = if ($ghCmd) { $ghCmd.Source } else { "C:\Program Files\GitHub CLI\gh.exe" }
    if (-not (Test-Path $script:ghExe)) { Fail "GitHub CLI (gh) introuvable : winget install --id GitHub.cli -e" }
    function gh { & $script:ghExe @args }
    gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "gh n'est pas connecté : lancez 'gh auth login'" }

    # Commit identity: the GitHub no-reply address keeps the personal e-mail out of the public history
    if (-not (git config user.email)) {
        $login = gh api user --jq .login
        $id    = gh api user --jq .id
        $env:GIT_AUTHOR_NAME     = $login
        $env:GIT_COMMITTER_NAME  = $login
        $env:GIT_AUTHOR_EMAIL    = "$id+$login@users.noreply.github.com"
        $env:GIT_COMMITTER_EMAIL = $env:GIT_AUTHOR_EMAIL
    }
    if (git tag --list $tag) { Fail "Le tag $tag existe déjà" }
    $last = git tag --list "v*" | ForEach-Object { [Version]($_.TrimStart('v')) } | Sort-Object | Select-Object -Last 1
    if ($last -and [Version]$Version -le $last) { Fail "La version $Version doit être supérieure à la dernière publiée (v$last)" }
    if ($NotesFile) { $Notes = Get-Content $NotesFile -Raw -Encoding UTF8 }
    if (-not $Notes.Trim()) { Fail "Ajoutez des notes de version (-Notes ou -NotesFile) : elles s'affichent dans le bandeau de mise à jour" }

    Step "Numéro de version $Version"
    $ai = Join-Path $root "Properties\AssemblyInfo.cs"
    $content = Get-Content $ai -Raw -Encoding UTF8
    $content = $content -replace 'AssemblyVersion\("[^"]*"\)',     "AssemblyVersion(""$Version.0"")"
    $content = $content -replace 'AssemblyFileVersion\("[^"]*"\)', "AssemblyFileVersion(""$Version.0"")"
    [IO.File]::WriteAllText($ai, $content, (New-Object Text.UTF8Encoding($true)))

    Step "Compilation Release"
    $env:SIMHUB_INSTALL_PATH = $simhub
    & $msbuild (Join-Path $root "User.BestLapRecords.csproj") /p:Configuration=Release /t:Rebuild /v:minimal /nologo "/p:PostBuildEvent=" |
        Where-Object { $_ -match 'error|->' }
    if ($LASTEXITCODE -ne 0) { Fail "La compilation a échoué" }
    $built = Join-Path $root "bin\Release\$dllName"
    $builtVersion = [Reflection.AssemblyName]::GetAssemblyName($built).Version
    if ($builtVersion -ne [Version]"$Version.0") { Fail "La DLL compilée est en $builtVersion" }

    Step "Préparation des fichiers"
    $dist = Join-Path $root "dist"
    Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory $dist | Out-Null
    Copy-Item $built $dist
    $hash = (Get-FileHash (Join-Path $dist $dllName) -Algorithm SHA256).Hash.ToLower()
    [IO.File]::WriteAllText((Join-Path $dist "$dllName.sha256"), "$hash  $dllName`n")
    [IO.File]::WriteAllText((Join-Path $dist "notes.md"), $Notes, (New-Object Text.UTF8Encoding($false)))
    Write-Host "SHA-256 : $hash"

    # git and gh report progress on stderr: judge them by exit code, not by output
    $ErrorActionPreference = "Continue"

    Step "Commit, tag et push"
    git add -A 2>&1 | Out-Null
    if (git status --porcelain) {
        git commit -q -m "Release $tag" 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail "Le commit a échoué" }
    }
    git tag -a $tag -m "Release $tag"
    if ($LASTEXITCODE -ne 0) { Fail "La création du tag a échoué" }
    git push -q origin HEAD --follow-tags 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "Le push a échoué" }

    Step "Release GitHub"
    gh release create $tag (Join-Path $dist $dllName) (Join-Path $dist "$dllName.sha256") `
        --title $tag --notes-file (Join-Path $dist "notes.md") 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "La création de la release a échoué" }
    $ErrorActionPreference = "Stop"

    if ($Deploy) {
        Step "Installation locale"
        if (Get-Process SimHubWPF -ErrorAction SilentlyContinue) { Write-Host "SimHub est ouvert : installation locale ignorée" -ForegroundColor Yellow }
        else { Copy-Item $built $simhub -Force; Write-Host "Installé dans $simhub" }
    }

    Write-Host "`nPublié : $tag" -ForegroundColor Green
    gh release view $tag --json url --jq .url
}
finally {
    Pop-Location
}
