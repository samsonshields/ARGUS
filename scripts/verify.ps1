param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Full
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousLocation = Get-Location
$previousConfiguration = $env:ARGUS_BUILD_CONFIGURATION
function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Verification failed: $Executable $($Arguments -join ' ')" }
}
try {
    Set-Location -LiteralPath $repositoryRoot
    $taskPython = Join-Path $repositoryRoot 'intelligence-service/.venv/Scripts/python.exe'
    if (-not (Test-Path -LiteralPath $taskPython)) { throw 'Create the Python venv using docs/setup.md first.' }
    Invoke-Checked -Executable dotnet -Arguments @('restore', 'Argus.sln')
    Invoke-Checked -Executable dotnet -Arguments @('build', 'Argus.sln', '-c', $Configuration, '--no-restore')
    Invoke-Checked -Executable dotnet -Arguments @('test', 'Argus.sln', '-c', $Configuration, '--no-build', '--no-restore')
    Invoke-Checked -Executable dotnet -Arguments @('format', 'Argus.sln', '--no-restore', '--verify-no-changes')
    Invoke-Checked -Executable $taskPython -Arguments @('-m', 'pytest', 'intelligence-service/tests', '-q', '-c', 'intelligence-service/pyproject.toml')
    Invoke-Checked -Executable $taskPython -Arguments @('-m', 'ruff', 'check', 'intelligence-service/app', 'intelligence-service/tests', 'scripts')
    Invoke-Checked -Executable $taskPython -Arguments @('-m', 'ruff', 'format', '--check', 'intelligence-service/app', 'intelligence-service/tests', 'scripts')
    Invoke-Checked -Executable $taskPython -Arguments @('-m', 'pip', 'check')
    if ($Full) {
        $env:ARGUS_BUILD_CONFIGURATION = $Configuration
        foreach ($script in @('smoke.py', 'smoke_nlp.py', 'smoke_candidate_review.py', 'smoke_graph.py', 'smoke_temporal.py', 'smoke_assessments.py', 'smoke_demo.py')) {
            Invoke-Checked -Executable $taskPython -Arguments @("scripts/$script")
        }
    }
} finally {
    $env:ARGUS_BUILD_CONFIGURATION = $previousConfiguration
    Set-Location -LiteralPath $previousLocation
}
