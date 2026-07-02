# PostToolUse hook: after any edit to a .cs file, build the solution and run the library tests.
# Exit 0 = pass/skip, exit 2 = failure (stderr is fed back to Claude as a blocking error).
$ErrorActionPreference = 'Continue'

# Hook payload arrives as JSON on stdin (tool_input.file_path = the edited file).
$hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
$filePath = $hookInput.tool_input.file_path
if (-not $filePath -or $filePath -notmatch '\.cs$') { exit 0 }

# dotnet is not on PATH in fresh shells on this machine.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

$buildOutput = dotnet build (Join-Path $repoRoot 'DynamicConfig.sln') --nologo -v quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("dotnet build FAILED after editing $filePath`n$($buildOutput | Out-String)")
    exit 2
}

# Solution build above already compiled the test project; --no-build keeps this fast.
$testOutput = dotnet test (Join-Path $repoRoot 'tests/DynamicConfig.Library.Tests') --nologo --no-build -v quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("dotnet test FAILED after editing $filePath`n$($testOutput | Out-String)")
    exit 2
}

exit 0
