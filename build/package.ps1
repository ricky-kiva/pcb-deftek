[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version,
    [string]$InnoCompiler,
    [string]$PfxPath,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$Sign,
    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'pcb-deftek.csproj'
$installerScript = Join-Path $repoRoot 'installer\pcb-deftek.iss'

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

function Get-ProjectVersion {
    param([Parameter(Mandatory)][string]$ProjectFile)

    [xml]$project = Get-Content -LiteralPath $ProjectFile
    $propertyGroups = $project.Project.PropertyGroup
    foreach ($group in @($propertyGroups)) {
        if ($null -ne $group.Version -and -not [string]::IsNullOrWhiteSpace($group.Version)) {
            return [string]$group.Version
        }
    }

    throw "No <Version> property found in $ProjectFile"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion -ProjectFile $projectPath
}

if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Invalid version '$Version'. Expected Semantic Versioning form such as 0.10.0."
}

$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

function Assert-UnderArtifacts {
    param([Parameter(Mandatory)][string]$Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    $prefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to touch a path outside the artifacts directory: $full"
    }

    return $full
}

function Invoke-Sign {
    param([Parameter(Mandatory)][string[]]$Files)

    $signScript = Join-Path $repoRoot 'build\sign.ps1'
    if (-not (Test-Path -LiteralPath $signScript)) {
        throw "Signing script not found: $signScript"
    }

    $signArgs = @{ FilePath = $Files; TimestampUrl = $TimestampUrl }
    if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
        $signArgs['PfxPath'] = $PfxPath
    }

    & $signScript @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed with exit code $LASTEXITCODE"
    }
}

$publishDir = Assert-UnderArtifacts (Join-Path $artifactsRoot "pcb-deftek-$Version-$Runtime")
$zipPath = Assert-UnderArtifacts (Join-Path $artifactsRoot "PCB-Deftek-$Version-portable-$Runtime.zip")
$setupPath = Assert-UnderArtifacts (Join-Path $artifactsRoot "PCB-Deftek-$Version-setup.exe")
$checksumsPath = Assert-UnderArtifacts (Join-Path $artifactsRoot 'SHA256SUMS.txt')

foreach ($stale in @($publishDir, $zipPath, $setupPath)) {
    if (Test-Path -LiteralPath $stale) {
        Write-Verbose "Removing stale artifact: $stale"
        Remove-Item -LiteralPath $stale -Recurse -Force
    }
}

Write-Host "Publishing $projectPath ($Configuration, $Runtime, self-contained, version $Version)"
$publishArgs = @(
    'publish', $projectPath,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    "-p:Version=$Version",
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $publishDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$requiredFiles = @(
    (Join-Path $publishDir 'pcb-deftek.exe'),
    (Join-Path $publishDir 'Assets\Models\best.onnx')
)
foreach ($required in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Publish output is incomplete: missing $required"
    }
}

if ($Sign) {
    Write-Host 'Signing the application executable before packaging'
    Invoke-Sign -Files @((Join-Path $publishDir 'pcb-deftek.exe'))
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
Write-Host "Creating portable archive $zipPath"
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDir,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

if (-not $SkipInstaller) {
    $candidates = @(
        $InnoCompiler,
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) {
        throw 'Inno Setup compiler (ISCC.exe) was not found. Install it with "winget install JRSoftware.InnoSetup", pass -InnoCompiler, or use -SkipInstaller.'
    }

    if (-not (Test-Path -LiteralPath $installerScript)) {
        throw "Installer script not found: $installerScript"
    }

    Write-Host "Compiling installer with $iscc"
    & $iscc "/DAppVersion=$Version" "/DSourceDir=$publishDir" "/O$artifactsRoot" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $setupPath)) {
        throw "Installer was not produced: $setupPath"
    }
}

if ($Sign -and (Test-Path -LiteralPath $setupPath)) {
    Write-Host 'Signing the installer'
    Invoke-Sign -Files @($setupPath)
}

$checksumTargets = @($zipPath)
if (Test-Path -LiteralPath $setupPath) {
    $checksumTargets += $setupPath
}

$lines = foreach ($file in $checksumTargets) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "{0}  {1}" -f $hash, (Split-Path -Leaf $file)
}
[System.IO.File]::WriteAllLines($checksumsPath, $lines)

$publishFiles = Get-ChildItem -LiteralPath $publishDir -Recurse -File
$publishSize = [math]::Round(($publishFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
$zipSize = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)

Write-Host ''
Write-Host "Version:            $Version"
Write-Host "Publish directory:  $publishDir ($($publishFiles.Count) files, $publishSize MB)"
Write-Host "Portable archive:   $zipPath ($zipSize MB)"
if (Test-Path -LiteralPath $setupPath) {
    $setupSize = [math]::Round((Get-Item -LiteralPath $setupPath).Length / 1MB, 1)
    Write-Host "Installer:          $setupPath ($setupSize MB)"
}

Write-Host "Checksums:          $checksumsPath"
