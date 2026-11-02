<#
Builds, checks and packages a release of the Manager and the Join app.

  tools\release.ps1                     build + check + package into release\, copy to Documents\L4D2 8 Player
  tools\release.ps1 -Version 1.2.0      same, after raising the version in Directory.Build.props
  tools\release.ps1 -Publish            also tag v<version>, push, and create the GitHub release (needs gh, signed in);
                                        raise the version and commit before publishing
  tools\release.ps1 -Publish -NotesFile notes.md    use your own release notes instead of the commit list
  tools\release.ps1 -NoDeploy           skip the copy to Documents\L4D2 8 Player

Checks: every built-in plugin compiles with the Manager's SourcePawn toolchain, both apps build, the Manager's
self-test and both apps' screenshot modes run, and the Join zip extracts and starts.
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
foreach ($p in 'L4D2EightPlayer', 'L4D2EightPlayer.Join') {
    $res = dotnet build $p -c Release 2>&1
    if ($LASTEXITCODE -ne 0) { $res | Select-String 'error' | Write-Host; Fail "$p doesn't build" }
}
$mgr = 'L4D2EightPlayer\bin\Release\L4D2-8P-Manager.exe'
$join = 'L4D2EightPlayer.Join\bin\Release\L4D2-8P-Join.exe'
foreach ($exe in $mgr, $join) {
    $v = (Get-Item $exe).VersionInfo.ProductVersion
    if ($v -notlike "$Version*") { Fail "$exe reports version $v, expected $Version" }
}

# ---- checks
Step "self-test and screenshots"
$p = Start-Process $mgr -ArgumentList "--selftest --out `"$tmp\selftest.txt`" --detect --shot `"$tmp\shots`"" -PassThru -Wait
if ($p.ExitCode -ne 0) { Get-Content "$tmp\selftest.txt" -ErrorAction SilentlyContinue | Write-Host; Fail "Manager self-test exit code $($p.ExitCode)" }
if (-not (Test-Path "$tmp\shots\tab3.png")) { Fail "Manager screenshots missing" }

# ---- package
Step "packaging into release\"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item $mgr, 'L4D2EightPlayer\README.md' $out
Copy-Item "$tmp\shots" "$out\shots" -Recurse

$stage = "$tmp\zip\L4D2-8P-Join"
New-Item -ItemType Directory -Force "$stage\licenses" | Out-Null
Copy-Item $join, 'L4D2EightPlayer.Join\READ ME FIRST.txt' $stage
Copy-Item 'L4D2EightPlayer\Fonts\OFL-*.txt' "$stage\licenses"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = Join-Path $out 'L4D2-8P-Join.zip'
$fs = [IO.File]::Open($zip, 'CreateNew')
$a = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
foreach ($f in Get-ChildItem $stage -Recurse -File) {
    # forward slashes so every unzip tool keeps the folders
    $name = 'L4D2-8P-Join/' + $f.FullName.Substring($stage.Length + 1).Replace('\', '/')
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($a, $f.FullName, $name, 'Optimal') | Out-Null
}
$a.Dispose(); $fs.Dispose()

Step "checking the Join zip"
Expand-Archive $zip "$tmp\unzipped"
$p = Start-Process "$tmp\unzipped\L4D2-8P-Join\L4D2-8P-Join.exe" -ArgumentList "--shot `"$tmp\join.png`"" -PassThru -Wait
if ($p.ExitCode -ne 0 -or -not (Test-Path "$tmp\join.png")) { Fail "the Join app from the zip didn't start" }
Copy-Item "$tmp\join.png" "$out\shots\join.png"
'{0:N0} KB zip' -f ((Get-Item $zip).Length / 1KB) | Write-Host

# ---- deploy
$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'L4D2 8 Player'
if (-not $NoDeploy -and (Test-Path $docs)) {
    Step "copying to $docs"
    Copy-Item $mgr, $join, 'L4D2EightPlayer\README.md', $zip $docs -Force
}

# ---- publish
if ($Publish) {
    Step "publishing v$Version"
    git tag -a "v$Version" -m "v$Version"
    git push origin HEAD "v$Version"
    if ($LASTEXITCODE -ne 0) { Fail "git push failed" }
    if ($NotesFile) { gh release create "v$Version" "$out\L4D2-8P-Manager.exe" $zip --title "v$Version" --notes-file $NotesFile }
    else { gh release create "v$Version" "$out\L4D2-8P-Manager.exe" $zip --title "v$Version" --generate-notes }
    if ($LASTEXITCODE -ne 0) { Fail "gh release create failed" }
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Step "done: release\ holds L4D2-8P-Manager.exe, L4D2-8P-Join.zip, README.md and screenshots"
