# apply_06_fix_tasksbutton.ps1
Param(
  [string]$RepoRoot = "."
)
$ErrorActionPreference = "Stop"

function Copy-IntoPath($relPath) {
  $src = Join-Path $PSScriptRoot "..\$relPath"
  $dst = Join-Path $RepoRoot $relPath
  $dstDir = Split-Path $dst -Parent
  if (!(Test-Path $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
  Copy-Item -LiteralPath $src -Destination $dst -Force
  Write-Host "Updated $relPath"
}

# Replace the malformed TasksButton partial with a clean version
Copy-IntoPath "src\UI\MainWindow.Partial.TasksButton.cs"

Write-Host "Patch applied."
