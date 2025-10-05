Param()

# repo root assumed as current directory when invoking this script
$ErrorActionPreference = 'Stop'

Write-Host "==> Patching using directives in src\UI\MainWindow.Partial.SetupFlow.cs"
$setupPath = "src\UI\MainWindow.Partial.SetupFlow.cs"
if (Test-Path $setupPath) {
  $code = Get-Content $setupPath -Raw

  # Remove wrong Core sub-namespace usings
  $code = $code -replace 'using\s+Core\.(Models|Philips|Sony);\s*', ''

  # Ensure a single 'using Core;' exists
  if ($code -notmatch '(?m)^\s*using\s+Core;') {
    # Put it after System.* usings if present, otherwise at top
    if ($code -match '(?ms)^(?<head>(?:\s*using\s+System[^\r\n]*;\s*)+)(?<rest>.*)$') {
      $code = ($Matches['head'] + "using Core;`r`n" + $Matches['rest'])
    } else {
      $code = "using Core;`r`n" + $code
    }
  }

  Set-Content $setupPath $code -Encoding UTF8
  Write-Host "   - Fixed usings in $setupPath"
} else {
  Write-Warning "   - File not found: $setupPath (skipping)"
}

Write-Host "==> De-duping Tasks button handler in src\UI\MainWindow.Partial.TasksButton.cs"
$tasksBtnPath = "src\UI\MainWindow.Partial.TasksButton.cs"
if (Test-Path $tasksBtnPath) {
  $tb = Get-Content $tasksBtnPath -Raw

  # If this partial defines MainWindow and contains CreateTasksButton_Click, comment the whole file to avoid duplicate symbol.
  if ($tb -match 'partial\s+class\s+MainWindow' -and $tb -match 'CreateTasksButton_Click') {
    $commented = "/*`r`nNOTE: This file was commented out by apply_04_fix_build.ps1 to remove a duplicate CreateTasksButton_Click handler.`r`n" + $tb + "`r`n*/`r`n"
    Set-Content $tasksBtnPath $commented -Encoding UTF8
    Write-Host "   - Commented out duplicate partial to avoid handler collision."
  } else {
    Write-Host "   - No duplicate handler pattern found; leaving file as-is."
  }
} else {
  Write-Warning "   - File not found: $tasksBtnPath (skipping)"
}

Write-Host "✅ Patch applied. Now rebuild:"
Write-Host "   dotnet build .\ATVCompanion.sln -c Release"
