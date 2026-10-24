$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path 'app','openai-plugin\app' -Force | Out-Null
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:app\QuotaText.exe src\QuotaText.cs
    if ($LASTEXITCODE -ne 0) { throw 'QuotaText build failed.' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /out:Setup.exe src\Setup.cs
    if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }
    Copy-Item -LiteralPath 'Setup.exe' -Destination 'openai-plugin\Setup.exe' -Force
    foreach ($name in @('QuotaText.exe','quota.mjs','fetch-quota.mjs')) {
        Copy-Item -LiteralPath (Join-Path 'app' $name) -Destination (Join-Path 'openai-plugin\app' $name) -Force
    }
    Write-Host 'Build completed.'
} finally { Pop-Location }
