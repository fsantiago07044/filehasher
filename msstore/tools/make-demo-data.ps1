# msstore/tools/make-demo-data.ps1
#
# Builds the demo folder the Store screenshots are taken against, on the Win10
# build/test VM. Run this first, then capture-screenshots.ps1.
#
# The data is deliberately boring: generated files plus FileHasher's own
# artifacts. The repo's own test fixture (FileHasherApp.Tests\fixtures\
# msi-test.msi) is NOT used for the inner-MSI shot, because it contains
# third-party binaries (putty, rufus, 7za, busybox) whose names have no place
# in a public Store listing.

$ErrorActionPreference = 'Stop'
$demo = 'C:\Users\fabian\Documents\Release Artifacts'
Remove-Item $demo -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $demo | Out-Null

Copy-Item 'C:\Program Files\FileHasher\FileHasher.exe' (Join-Path $demo 'FileHasher.exe') -Force

Set-Content (Join-Path $demo 'release-notes.txt') "Release notes`r`n=============`r`n`r`nSample text file used to demonstrate hashing and sidecar verification." -Encoding UTF8
Set-Content (Join-Path $demo 'build-manifest.json') "{`r`n  `"product`": `"Sample build manifest`",`r`n  `"files`": 5`r`n}" -Encoding UTF8
Set-Content (Join-Path $demo 'checksums.csv') "file,algorithm,hash`r`nsample.dat,SHA256,(computed at run time)" -Encoding UTF8
Set-Content (Join-Path $demo 'README.md') "# Sample folder`r`n`r`nDemonstration data for hashing a folder of release artifacts." -Encoding UTF8

# A spread of binary files, so the results list looks like real work rather
# than four rows in an acre of white space. Fixed seed: reproducible sizes.
$rand = New-Object Random 1234
foreach ($n in 1..12) {
  $buf = New-Object byte[] (4096 * $rand.Next(4, 260))
  $rand.NextBytes($buf)
  [IO.File]::WriteAllBytes((Join-Path $demo ('sample-data-{0:d2}.dat' -f $n)), $buf)
}
$buf = New-Object byte[] 262144; $rand.NextBytes($buf)
[IO.File]::WriteAllBytes((Join-Path $demo 'payload.bin'), $buf)

# FileHasher's own released MSI, for the inner-MSI scan shot. Hashing our own
# installer is on-brand, and the parent hash in the screenshot is verifiably
# the one published in that release's .msi.sha256 sidecar.
$own = 'C:\Users\fabian\Documents\FileHasher-0.3.1.msi'
if (-not (Test-Path $own)) {
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  $ProgressPreference = 'SilentlyContinue'
  Invoke-WebRequest 'https://github.com/fsantiago07044/filehasher/releases/download/v0.3.1/FileHasher-0.3.1.msi' -OutFile $own -UseBasicParsing
}

icacls $demo /grant "$env:COMPUTERNAME\fabian:(OI)(CI)F" /T /Q | Out-Null
"{0} files, {1:N1} MB" -f (Get-ChildItem $demo).Count, ((Get-ChildItem $demo | Measure-Object Length -Sum).Sum / 1MB)
