param(
    [Parameter(Mandatory=$true)]
    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"

# Paths
$uiDir = Join-Path $RepoRoot "src\UI"
if (-not (Test-Path $uiDir)) {
    throw "UI directory not found at '$uiDir'"
}

# We will keep only the handler living in this file
$keeper = Join-Path $uiDir "MainWindow.Partial.TasksButton.cs"

if (-not (Test-Path $keeper)) {
    Write-Warning "Expected keeper file not found at $keeper. The script will still attempt to remove duplicates in other files."
}

# Regex to remove the whole CreateTasksButton_Click method block
# - (?s) single-line: dot matches newline
# - (?m) multi-line: ^ and $ match line boundaries
# - We anchor at line starts optionally preceded by spaces
$pattern = '(?sm)^\s*(?:private|public|protected|internal)?\s+void\s+CreateTasksButton_Click\s*\([^\)]*\)\s*\{.*?\}\s*'

$files = Get-ChildItem -Path $uiDir -Filter "MainWindow*.cs" -File -Recurse

$changed = @()
foreach ($f in $files) {
    if ($keeper -and ($f.FullName -ieq $keeper)) {
        continue
    }
    $code = Get-Content $f.FullName -Raw
    $newCode = [regex]::Replace($code, $pattern, '')
    if ($newCode -ne $code) {
        Set-Content -Path $f.FullName -Value $newCode -Encoding UTF8
        $changed += $f.FullName
    }
}

# As a safety net, remove possible *second* definitions inside the keeper too (should be no-op)
if (Test-Path $keeper) {
    $kcode = Get-Content $keeper -Raw
    # Keep only the FIRST occurrence of the handler in the keeper and remove any subsequent ones
    $matches = [regex]::Matches($kcode, $pattern)
    if ($matches.Count -gt 1) {
        # Remove from the second onward
        $toRemove = $matches | Select-Object -Skip 1
        foreach ($m in $toRemove) {
            $kcode = $kcode.Remove($m.Index, $m.Length)
        }
        Set-Content -Path $keeper -Value $kcode -Encoding UTF8
        $changed += "$keeper (deduped)"
    }
}

# Also ensure the XAML wires up to the handler name only once
$xamlPath = Join-Path $uiDir "MainWindow.xaml"
if (Test-Path $xamlPath) {
    $xaml = Get-Content $xamlPath -Raw
    # Ensure there is only one Click handler assignment for CreateTasksButton
    # and that it points to CreateTasksButton_Click
    $xaml = $xaml -replace '(?s)(<Button\b[^>]*x:Name\s*=\s*"(?:CreateTasksButton|btnCreateTasks)"[^>]*?)\s+Click\s*=\s*"[A-Za-z0-9_]+"', '$1'
    # If the button has a name, add/normalize the Click attribute
    $xaml = $xaml -replace '(?s)(<Button\b[^>]*x:Name\s*=\s*"(?:CreateTasksButton|btnCreateTasks)"[^>]*?)\s*(/?>)', '$1 Click="CreateTasksButton_Click"$2'
    Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
    $changed += "$xamlPath (normalized Click handler)"
}

if ($changed.Count -eq 0) {
    Write-Host "No duplicate handlers found. Nothing changed."
} else {
    Write-Host "Updated files:"
    $changed | ForEach-Object { Write-Host " - $_" }
    Write-Host "Done. Rebuild the solution."
}
