param(
    [string]$CompilerPath,
    [string]$DotnetPath
)

$ErrorActionPreference = 'Stop'
$releaseRoot = $PSScriptRoot
$toolRoot = Join-Path (Split-Path (Split-Path $releaseRoot)) '.tools'
if (-not $DotnetPath) {
    $DotnetPath = Join-Path $toolRoot 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $DotnetPath)) {
        $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
    }
}
if (-not $CompilerPath) {
    $CompilerPath = Join-Path $toolRoot 'InnoSetup7\ISCC.exe'
    if (-not (Test-Path -LiteralPath $CompilerPath)) {
        $CompilerPath = (Get-Command ISCC.exe -ErrorAction Stop).Source
    }
}
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup compiler not found. Specify -CompilerPath.'
}
if (-not (Test-Path -LiteralPath (Join-Path (Split-Path $CompilerPath) 'Languages\ChineseSimplified.isl'))) {
    throw 'The compiler requires Languages\ChineseSimplified.isl.'
}

$releaseVersion = (Get-Content -LiteralPath (Join-Path $releaseRoot 'VERSION') -Raw).Trim()
$project = Join-Path $releaseRoot 'FluentFlyout-2.14.0\FluentFlyout-2.14.0\FluentFlyoutWPF\FluentFlyout.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
if ($projectXml.Project.PropertyGroup.Version -ne $releaseVersion) {
    throw 'Project version differs from VERSION.'
}
$installerScript = Join-Path $releaseRoot 'Installer\CapsLockFlyout.iss'
$installerText = Get-Content -LiteralPath $installerScript -Raw
if (-not $installerText.Contains('#define MyAppVersion "' + $releaseVersion + '"')) {
    throw 'Installer version differs from VERSION.'
}

# A fresh payload prevents both live-file replacement and inclusion of user data.
$buildName = 'installer-build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$buildRoot = Join-Path $releaseRoot ('artifacts\' + $buildName)
$payloadDir = Join-Path $buildRoot 'payload'
$outputDir = Join-Path $releaseRoot 'artifacts\installer'
New-Item -ItemType Directory -Path $buildRoot, $payloadDir, $outputDir -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $toolRoot 'cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $toolRoot 'packages'
$config = Join-Path $releaseRoot 'Installer\NuGet.selfcontained.config'

& $DotnetPath publish $project -c Release -r win-x64 --self-contained true --configfile $config -o $payloadDir |
    Tee-Object -FilePath (Join-Path $buildRoot 'publish.log')
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

foreach ($name in @('README-1.1.md', 'LICENSE', 'NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $releaseRoot $name) -Destination $payloadDir
}
foreach ($required in @('CapsLockFlyout.exe', 'CapsLockFlyout.dll', 'CapsLockFlyout.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payloadDir $required))) {
        throw "Self-contained payload is missing $required."
    }
}
if (Test-Path -LiteralPath (Join-Path $payloadDir 'Data')) {
    throw 'Runtime Data must not be bundled.'
}
$appVersion = (Get-Item -LiteralPath (Join-Path $payloadDir 'CapsLockFlyout.exe')).VersionInfo.FileVersion
if ($appVersion -ne "$releaseVersion.0") { throw "Unexpected executable version: $appVersion" }

& $CompilerPath "/DPackageSourceDir=$payloadDir" "/O$outputDir" $installerScript |
    Tee-Object -FilePath (Join-Path $buildRoot 'compiler.log')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Get-Item -LiteralPath (Join-Path $outputDir 'CapsLockFlyout-Setup.exe')
if ($installer.VersionInfo.ProductVersion.Trim() -ne $releaseVersion) {
    throw "Unexpected installer version: $($installer.VersionInfo.ProductVersion)"
}
$hash = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $($installer.Name)" | Set-Content -LiteralPath (Join-Path $outputDir 'SHA256SUMS.txt') -Encoding ascii

$manifest = [ordered]@{
    version = $releaseVersion
    builtAt = (Get-Date).ToString('o')
    setupFile = $installer.Name
    bytes = $installer.Length
    sha256 = $hash
    applicationVersion = $appVersion
    compilerVersion = (Get-Item -LiteralPath $CompilerPath).VersionInfo.ProductVersion
    selfContained = $true
    payloadFiles = @(Get-ChildItem -LiteralPath $payloadDir -File -Recurse).Count
    dataBundled = $false
    buildEvidence = $buildName
    installationTested = $false
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDir 'BUILD-INFO.json') -Encoding UTF8
Write-Host "Installer: $($installer.FullName)"
Write-Host "SHA-256: $hash"
Write-Host 'Build complete. No installation, service changes, or GitHub upload performed.'
