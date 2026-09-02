# chocolatey/tools/chocolateyinstall.ps1
#
# TEMPLATE: {VERSION} and {SHA256} are substituted by the `chocolatey-push` CI
# job before `choco pack` runs. {SHA256} is the lowercase hex digest from
# FileHasher-{VERSION}.msi.sha256, which the job re-computes from the
# downloaded MSI and cross-checks against the published sidecar.
#
# There is no chocolateyuninstall.ps1 on purpose: the MSI registers itself in
# Add/Remove Programs, so Chocolatey's built-in auto-uninstaller removes it
# with the ProductCode it recorded at install time. `softwareName` below is
# the display-name pattern it matches on.

$ErrorActionPreference = 'Stop'

if (-not (Get-OSArchitectureWidth 64)) {
  throw 'FileHasher requires 64-bit Windows. No 32-bit build is published.'
}

$packageArgs = @{
  packageName    = 'filehasher'
  softwareName   = 'FileHasher*'
  fileType       = 'msi'
  url            = 'https://github.com/fsantiago07044/filehasher/releases/download/v{VERSION}/FileHasher-{VERSION}.msi'
  checksum       = '{SHA256}'
  checksumType   = 'sha256'
  # The MSI authors no UI, so /qn is the only mode it has; /norestart is
  # belt-and-braces (nothing it installs can ask for a reboot).
  silentArgs     = '/qn /norestart'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
