param([string]$Artifacts = (Join-Path $PSScriptRoot 'test-output'))
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'src/PdfTemplateHelper.Cli/PdfTemplateHelper.Cli.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
dotnet build (Join-Path $PSScriptRoot 'src/PdfTemplateHelper.Gui/PdfTemplateHelper.Gui.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'GUI build failed.' }
dotnet run --project (Join-Path $PSScriptRoot 'tests/PdfTemplateHelper.Tests/PdfTemplateHelper.Tests.csproj') -c Release -- $Artifacts
if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
$cliDll = Join-Path $PSScriptRoot 'src/PdfTemplateHelper.Cli/bin/Release/net10.0/pdf-template-helper.dll'
$stdoutFile = Join-Path $Artifacts 'error.stdout.txt'
$stderrFile = Join-Path $Artifacts 'error.stderr.txt'
dotnet $cliDll fill --job (Join-Path $Artifacts 'hostile.pdfjob') --out (Join-Path $Artifacts 'error.pdf') 1>$stdoutFile 2>$stderrFile
$exitCode = $LASTEXITCODE
$errorReply = Get-Content -LiteralPath $stderrFile -Raw | ConvertFrom-Json
if ($exitCode -eq 0 -or $errorReply.protocolVersion -ne 1 -or $errorReply.status -ne 'error' -or $errorReply.code -ne 'unsafe_path' -or (Get-Item -LiteralPath $stdoutFile).Length -ne 0) {
    throw 'CLI structured stderr/nonzero contract failed.'
}
Write-Host 'PASS CLI structured stderr/nonzero contract'
$successOut = Join-Path $Artifacts 'success.stdout.txt'
$successError = Join-Path $Artifacts 'success.stderr.txt'
dotnet $cliDll fill --job (Join-Path $Artifacts 'generic.pdfjob') --out (Join-Path $Artifacts 'cli-filled.pdf') 1>$successOut 2>$successError
if ($LASTEXITCODE -ne 0) { throw 'CLI success smoke test failed.' }
$successReply = Get-Content -LiteralPath $successOut -Raw | ConvertFrom-Json
if ($successReply.protocolVersion -ne 1 -or $successReply.status -ne 'ok' -or !(Test-Path -LiteralPath $successReply.output) -or (Get-Item -LiteralPath $successError).Length -ne 0) {
    throw 'CLI success stdout JSON contract failed.'
}
Write-Host 'PASS CLI success stdout contains only protocol JSON, no iText logging'
