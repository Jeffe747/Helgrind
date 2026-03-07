param(
    [string]$ProjectPath = "./Helgrind/Helgrind.csproj",
    [switch]$NoBuild
)

$processes = Get-Process Helgrind -ErrorAction SilentlyContinue
if ($processes) {
    $processes | Stop-Process -Force
}

Push-Location (Split-Path -Parent $ProjectPath)
try {
    if ($NoBuild) {
        dotnet run --no-build
    }
    else {
        dotnet run
    }
}
finally {
    Pop-Location
}
