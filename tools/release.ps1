<#
Builds, checks and packages a release of L4D2 8-Player Co-op (one app: host and join) and its Setup.

  tools\release.ps1                     build + check + package into release\, copy to Documents\L4D2 8 Player
  tools\release.ps1 -Version 1.2.0      same, after raising the version in Directory.Build.props
  tools\release.ps1 -Publish            also tag v<version>, push, and create the GitHub release (needs gh, signed in);
                                        raise the version and commit before publishing
  tools\release.ps1 -Publish -NotesFile notes.md    use your own release notes instead of the commit list
  tools\release.ps1 -NoDeploy           skip the copy to Documents\L4D2 8 Player

Checks: every built-in plugin compiles with the app's SourcePawn toolchain, the app and the Setup build, the
host side's self-test and both sides' screenshot modes run (tours included), and both Setup paths install an
app that starts.
#>
param(
    [string]$Version,
    [switch]$Publish,
    [switch]$NoDeploy,
    [string]$NotesFile
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$out = Join-Path $root 'release'
$tmp = Join-Path $env:TEMP 'l4d2-8p-release'   # short path: spcomp and the exes dislike very long ones

function Step($m) { Write-Host "== $m" -ForegroundColor Cyan }
function Fail($m) { Write-Host "FAILED: $m" -ForegroundColor Red; exit 1 }

# ---- version
$props = Join-Path $root 'Directory.Build.props'
if ($Version -and $Publish) { Fail "raise the version with -Version, commit, then run -Publish on its own" }
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail "version must look like 1.2.0" }
    (Get-Content $props -Raw) -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>" | Set-Content $props -NoNewline -Encoding utf8
}
$Version = ([xml](Get-Content $props)).Project.PropertyGroup.Version
Step "version $Version"

if ($Publish) {
    if (git status --porcelain) { Fail "commit your changes first (git status isn't clean)" }
    if (git tag --list "v$Version") { Fail "tag v$Version already exists; pass -Version with a new number" }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail "GitHub CLI (gh) isn't installed" }
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $tmp, "$tmp\smx", "$tmp\shots" | Out-Null

# ---- plugins
Step "compiling built-in plugins"
$tc = Join-Path $env:LOCALAPPDATA 'L4D2-8P-Manager\toolchain'
if (-not (Test-Path "$tc\spcomp.exe")) { Fail "SourcePawn toolchain not found in $tc (run one install from the Manager first)" }
foreach ($sp in Get-ChildItem 'L4D2EightPlayer\Plugins\*.sp') {
    Copy-Item $sp.FullName "$tmp\smx\" -Force
    $res = & "$tc\spcomp.exe" "$tmp\smx\$($sp.Name)" "-i$tc\include" "-i$tc\extra" "-o$tmp\smx\$($sp.BaseName).smx" -O2 -v2 2>&1
    if ($LASTEXITCODE -ne 0 -or ($res -match 'error \d+')) { $res | Write-Host; Fail "$($sp.Name) doesn't compile" }
    if ($res -match 'warning \d+') { $res -match 'warning' | Write-Host -ForegroundColor Yellow }
    Write-Host "   $($sp.Name)"
}

# ---- apps
Step "building"
# the setup last: it carries the app just built
foreach ($p in 'L4D2EightPlayer', 'L4D2EightPlayer.Setup') {
    $res = dotnet build $p -c Release 2>&1
    if ($LASTEXITCODE -ne 0) { $res | Select-String 'error' | Write-Host; Fail "$p doesn't build" }
}
$app = 'L4D2EightPlayer\bin\Release\L4D2-8P-Coop.exe'
$setup = 'L4D2EightPlayer.Setup\bin\Release\L4D2-8P-Setup.exe'
foreach ($exe in $app, $setup) {
    $v = (Get-Item $exe).VersionInfo.ProductVersion
    if ($v -notlike "$Version*") { Fail "$exe reports version $v, expected $Version" }
}

# ---- checks
Step "self-test and screenshots"
$p = Start-Process $app -ArgumentList "--selftest --out `"$tmp\selftest.txt`" --detect --shot `"$tmp\shots`"" -PassThru -Wait
if ($p.ExitCode -ne 0) { Get-Content "$tmp\selftest.txt" -ErrorAction SilentlyContinue | Write-Host; Fail "host self-test exit code $($p.ExitCode)" }
if (-not (Test-Path "$tmp\shots\tab3.png")) { Fail "host screenshots missing" }
if (-not (Test-Path "$tmp\shots\tour9.png")) { Fail "host tour screenshots missing" }
$p = Start-Process $app -ArgumentList "--join-shot `"$tmp\shots\join.png`" --tour" -PassThru -Wait
if ($p.ExitCode -ne 0 -or -not (Test-Path "$tmp\shots\join-tour5.png")) { Fail "join screenshots or tour missing" }
# the skin picker on a fake game full of skin add-ons (tools\make_skin_fixtures.py, needs Python)
$fx = "$tmp\skinfx"
python -I tools\make_skin_fixtures.py $fx | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "couldn't build the skin test add-ons (tools\make_skin_fixtures.py needs Python)" }
$p = Start-Process $app -ArgumentList "--skins-test `"$fx`" `"$tmp\skins.txt`"" -PassThru -Wait
if ($p.ExitCode -ne 0) { Get-Content "$tmp\skins.txt" | Write-Host; Fail "the skin picker test failed" }
$p = Start-Process $app -ArgumentList "--skins-shot `"$fx`" `"$tmp\shots`"" -PassThru -Wait
if ($p.ExitCode -ne 0 -or -not (Test-Path "$tmp\shots\skins-pack-question.png")) { Fail "skin picker screenshots missing" }
$p = Start-Process $app -ArgumentList "--shell-test `"$tmp\shell.txt`"" -PassThru -Wait
if ($p.ExitCode -ne 0) { Get-Content "$tmp\shell.txt" -ErrorAction SilentlyContinue | Write-Host; Fail "the HOST | JOIN switch check failed" }
$p = Start-Process $setup -ArgumentList "--shot `"$tmp\shots`"" -PassThru -Wait
if ($p.ExitCode -ne 0 -or -not (Test-Path "$tmp\shots\choose-join.png")) { Fail "Setup screenshots failed" }

# both setup paths into a scratch folder, without shortcuts or an Installed apps entry
foreach ($path in 'join', 'host') {
    $p = Start-Process $setup -ArgumentList "--silent --path $path --dir `"$tmp\installed`" --no-start-menu --no-desktop --no-register --out `"$tmp\setup-$path.txt`"" -PassThru -Wait
    if ($p.ExitCode -ne 0) { Get-Content "$tmp\setup-$path.txt" | Write-Host; Fail "Setup's $path install failed" }
}
$p = Start-Process "$tmp\installed\L4D2-8P-Coop.exe" -ArgumentList "--join-shot `"$tmp\installed.png`"" -PassThru -Wait
if ($p.ExitCode -ne 0 -or -not (Test-Path "$tmp\installed.png")) { Fail "the app the Setup installed didn't start" }

# ---- package
Step "packaging into release\"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item $app, $setup, 'L4D2EightPlayer\README.md' $out
Copy-Item "$tmp\shots" "$out\shots" -Recurse

# ---- deploy
$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'L4D2 8 Player'
if (-not $NoDeploy -and (Test-Path $docs)) {
    Step "copying to $docs"
    # the app running from there locks its exe; that shouldn't stop the release
    try { Copy-Item $app, $setup, 'L4D2EightPlayer\README.md' $docs -Force -ErrorAction Stop }
    catch { Write-Host "   not copied ($($_.Exception.Message)); close the app there and copy release\ by hand" -ForegroundColor Yellow }
    # what releases before 2.0 put there (the two apps and the Join zip): replaced by the one app
    foreach ($old in 'L4D2-8P-Manager.exe', 'L4D2-8P-Join.exe', 'L4D2-8P-Join.zip') {
        try { Remove-Item (Join-Path $docs $old) -ErrorAction Stop; Write-Host "   removed the older $old" } catch [System.Management.Automation.ItemNotFoundException] { } catch { Write-Host "   couldn't remove $old ($($_.Exception.Message))" -ForegroundColor Yellow }
    }
}

# ---- publish
if ($Publish) {
    Step "publishing v$Version"
    git tag -a "v$Version" -m "v$Version"
    git push origin HEAD "v$Version"
    if ($LASTEXITCODE -ne 0) { Fail "git push failed" }
    if ($NotesFile) { gh release create "v$Version" "$out\L4D2-8P-Setup.exe" "$out\L4D2-8P-Coop.exe" --title "v$Version" --notes-file $NotesFile }
    else { gh release create "v$Version" "$out\L4D2-8P-Setup.exe" "$out\L4D2-8P-Coop.exe" --title "v$Version" --generate-notes }
    if ($LASTEXITCODE -ne 0) { Fail "gh release create failed" }
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Step "done: release\ holds L4D2-8P-Setup.exe, L4D2-8P-Coop.exe, README.md and screenshots"
