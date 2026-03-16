param(
  [Parameter(Mandatory=$true)]
  [string]$RootPath,

  # Optional: write results to CSV
  [string]$OutCsv = "",

  # Optional: include file metadata
  [switch]$IncludeMetadata,

  # Write one hash file per installer next to the file
  [switch]$WriteSidecarHashes,

  # Extension for the sidecar hash files (filename.exe.sha256)
  [string]$SidecarExtension = ".sha256",

  # Format of the sidecar file contents: "hashonly" or "sha256sum"
  [ValidateSet("hashonly","sha256sum")]
  [string]$SidecarFormat = "sha256sum"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $RootPath)) {
  throw "RootPath does not exist: $RootPath"
}

$results = New-Object System.Collections.Generic.List[object]

# Recursively enumerate .exe and .msi files
$files = @()
try {
  $files = Get-ChildItem -LiteralPath $RootPath -Recurse -File -Include *.exe, *.msi -Force -ErrorAction Stop
} catch {
  Write-Warning "Initial recursive enumeration hit an error; falling back to per-directory traversal. Error: $($_.Exception.Message)"

  $stack = New-Object System.Collections.Generic.Stack[string]
  $stack.Push((Resolve-Path -LiteralPath $RootPath).Path)

  while ($stack.Count -gt 0) {
    $dir = $stack.Pop()

    try {
      $files += Get-ChildItem -LiteralPath $dir -File -Include *.exe, *.msi -Force -ErrorAction Stop
    } catch {
      Write-Warning "Cannot list files in: $dir ($($_.Exception.Message))"
    }

    try {
      Get-ChildItem -LiteralPath $dir -Directory -Force -ErrorAction Stop | ForEach-Object {
        $stack.Push($_.FullName)
      }
    } catch {
      Write-Warning "Cannot list subdirectories in: $dir ($($_.Exception.Message))"
    }
  }
}

# Hard filter to only EXE/MSI (protects against -Include quirks)
$files = $files | Where-Object { $_.Extension -in ".exe", ".msi" }

foreach ($f in $files) {

  # Hash (separate try/catch so sidecar write errors don't look like hash errors)
  try {
    $h = Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256 -ErrorAction Stop
  } catch {
    Write-Warning "Hash failed for: $($f.FullName) ($($_.Exception.Message))"
    continue
  }

  # Optional: write sidecar hash file next to the installer
  if ($WriteSidecarHashes) {
    try {
      $sidecarPath = "$($f.FullName)$SidecarExtension"

      $content =
        if ($SidecarFormat -eq "hashonly") {
          $h.Hash
        } else {
          # sha256sum-like: "<hash> *<filename>"
          "$($h.Hash) *$($f.Name)"
        }

      # PowerShell 5.1 compatible: UTF-8 without BOM
      [System.IO.File]::WriteAllText(
        $sidecarPath,
        $content + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false))
      )
    } catch {
      Write-Warning "Sidecar write failed for: $($f.FullName) ($($_.Exception.Message))"
    }
  }

  # Record results
  if ($IncludeMetadata) {
    $results.Add([pscustomobject]@{
      Path         = $f.FullName
      SHA256       = $h.Hash
      LengthBytes  = $f.Length
      LastWriteUtc = $f.LastWriteTimeUtc
    })
  } else {
    $results.Add([pscustomobject]@{
      Path   = $f.FullName
      SHA256 = $h.Hash
    })
  }
}

# Output to console
$results | Sort-Object Path | Format-Table -AutoSize

# Optional CSV output
if ($OutCsv -and $OutCsv.Trim().Length -gt 0) {
  $results | Sort-Object Path | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $OutCsv
  Write-Host "Wrote CSV: $OutCsv"
}