param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectRoot = Split-Path (Split-Path $root)
$toolRoot = Join-Path $projectRoot '.tools'
$sdk = Join-Path $toolRoot 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $toolRoot 'cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $toolRoot 'packages'
$project = Join-Path $root 'FluentFlyout-2.14.0\FluentFlyout-2.14.0\FluentFlyoutWPF\FluentFlyout.csproj'
$config = Join-Path $root 'NuGet.Config'
if ($SelfContained) { $config = Join-Path $root 'Installer\NuGet.selfcontained.config' }
& $sdk publish $project -c Release -r win-x64 --self-contained ($SelfContained.IsPresent.ToString().ToLowerInvariant()) --configfile $config -o (Join-Path $root 'artifacts\package-win-x64')
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'README-1.1.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'NOTICE.md') -Destination (Join-Path $root 'artifacts\package-win-x64')
