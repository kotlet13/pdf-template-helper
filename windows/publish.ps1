param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [string]$Output = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$cli = Join-Path $PSScriptRoot 'src/PdfTemplateHelper.Cli/PdfTemplateHelper.Cli.csproj'
$gui = Join-Path $PSScriptRoot 'src/PdfTemplateHelper.Gui/PdfTemplateHelper.Gui.csproj'
dotnet publish $cli -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $Output "$Runtime/cli")
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
dotnet publish $gui -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $Output "$Runtime/gui")
if ($LASTEXITCODE -ne 0) { throw 'GUI publish failed.' }
$repository = Split-Path -Parent $PSScriptRoot
foreach ($kind in @('cli', 'gui')) {
    $destination = Join-Path $Output "$Runtime/$kind"
    foreach ($notice in @('LICENSE', 'LICENSE-itext.md', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $repository $notice) -Destination (Join-Path $destination $notice) -Force
    }
    @'
PDF Template Helper 1.0.0
Corresponding source and build instructions:
https://github.com/kotlet13/pdf-template-helper

This distribution is built from that project's Windows sources and publish.ps1.
Obtain the matching release/source snapshot from the distributor when using a development build.
The application and iText licensing notices accompany this executable.
Noto Sans font licensing is in fonts/OFL.txt.
'@ | Set-Content -LiteralPath (Join-Path $destination 'SOURCE-OFFER.txt') -Encoding utf8
}
Write-Host "Published self-contained helper to $Output/$Runtime. Keep fonts/ alongside each executable."
