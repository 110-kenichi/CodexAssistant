param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $locator)) { throw 'Visual Studio 2022 と .NET desktop development / Visual Studio extension development をインストールしてください。' }
$msbuild = & $locator -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'MSBuildが見つかりません。' }
& $msbuild (Join-Path $PSScriptRoot 'CodexAssistant.sln') /restore /m /nr:false /nologo "/p:Configuration=$Configuration"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& (Join-Path $PSScriptRoot "tests/bin/$Configuration/CodexAssistant.Tests.exe")
if ($LASTEXITCODE -ne 0) { throw 'CLI integration tests failed.' }
Write-Host "VSIX: $PSScriptRoot/src/CodexAssistant/bin/$Configuration/net472/CodexAssistant.vsix"
