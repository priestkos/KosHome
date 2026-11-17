# Runs every test project under tests/: xUnit projects with `dotnet test`, console harnesses with `dotnet run`
# (each returns a non-zero exit code on failure). The host tests need the Dalamud dev distribution installed.
$ErrorActionPreference = 'Stop'
$expected = 11  # 1 host + 10 app suites; a missing project must fail the run, not shrink it
$projects = @(Get-ChildItem -Path $PSScriptRoot -Recurse -Filter '*.csproj' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName)
if ($projects.Count -ne $expected) { throw "Expected $expected test projects, found $($projects.Count)" }

$failed = @()
foreach ($p in $projects) {
    $rel = $p.FullName.Substring($PSScriptRoot.Length + 1)  # not GetRelativePath: Windows PowerShell 5.1 lacks it
    if (Select-String -LiteralPath $p.FullName -Pattern 'xunit' -Quiet) {
        Write-Host "== $rel (dotnet test)"
        dotnet test $p.FullName -c Release
    } else {
        Write-Host "== $rel (dotnet run)"
        dotnet run --project $p.FullName -c Release
    }
    if ($LASTEXITCODE -ne 0) { $failed += $rel }
}
if ($failed.Count -gt 0) { throw "Test projects failed: $($failed -join ', ')" }
Write-Host "All $($projects.Count) test projects passed."
