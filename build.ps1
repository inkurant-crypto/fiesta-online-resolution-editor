# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 inkurant-crypto

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDirectory = Join-Path $projectRoot 'release'
$outputFile = Join-Path $outputDirectory 'FiestaResolutionEditor-v1.7.exe'

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw 'C# compiler not found. Install the .NET Framework 4.8 Developer Pack or Visual Studio Build Tools.'
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$arguments = @(
    '/nologo',
    '/target:winexe',
    "/out:$outputFile",
    "/win32manifest:$(Join-Path $projectRoot 'FiestaResolutionEditor.manifest')",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    (Join-Path $projectRoot 'AssemblyInfo.cs'),
    (Join-Path $projectRoot 'FiestaResolutionEditor.cs')
)

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$hash = (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  FiestaResolutionEditor-v1.7.exe" | Set-Content -LiteralPath (Join-Path $outputDirectory 'SHA256SUMS.txt') -Encoding ASCII

Write-Host "Built: $outputFile"
Write-Host "SHA-256: $hash"
