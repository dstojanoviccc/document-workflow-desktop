param([string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
[xml]$properties = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must be a stable three-part version.' }
$publish = Join-Path $root 'artifacts/publish/win-x64'
$output = Join-Path $root "artifacts/release/$version"
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
# Clean only these generated directories; never install/user-state paths.
foreach ($path in @($publish, $output)) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (!$resolved.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output escaped artifacts directory.' }
    for ($ancestor = $resolved; $ancestor -and $ancestor.Length -ge $artifactRoot.Length; $ancestor = Split-Path $ancestor -Parent) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Generated output must not traverse a filesystem link.' }
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed ($LASTEXITCODE)." }
}
Push-Location $root
try {
    Invoke-DotNet -Arguments @('restore', 'DocumentWorkflow.sln')
    Invoke-DotNet -Arguments @('build', 'DocumentWorkflow.sln', '-c', 'Release', '--no-restore', '-warnaserror')
    Invoke-DotNet -Arguments @('test', 'DocumentWorkflow.sln', '-c', 'Release', '--no-build', '--no-restore')
    Invoke-DotNet -Arguments @('publish', 'src/DocumentWorkflow.App/DocumentWorkflow.App.csproj', '-p:PublishProfile=Windows', '-p:DebugType=none', '-p:DebugSymbols=false', '--output', $publish, '-warnaserror')
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish 'DocumentWorkflow.App.exe'))
    if ($info.ProductVersion.Split('+')[0] -ne $version) { throw 'Published binary does not match authoritative release version.' }
    if (!(Test-Path -LiteralPath (Join-Path $publish 'coreclr.dll')) -or !(Test-Path -LiteralPath (Join-Path $publish 'PresentationFramework.dll'))) { throw 'Publish is not a complete self-contained WPF application.' }
    if (Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object Extension -In '.db', '.pfx', '.pdb') { throw 'Unexpected development/state files in publish output.' }
    $zip = Join-Path $output "DocumentWorkflowDesktop-$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
    if (!$InnoCompiler) { $InnoCompiler = & (Join-Path $PSScriptRoot 'Get-InnoSetup.ps1') }
    & $InnoCompiler /Qp "/DAppVersion=$version" "/DPublishRoot=$publish" "/DOutputRoot=$output" (Join-Path $root 'packaging/DocumentWorkflowDesktop.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $output "DocumentWorkflowDesktop-$version-win-x64-setup.exe"
    if (!(Test-Path -LiteralPath $installer)) { throw 'Expected installer artifact was not produced.' }
    $lines = foreach ($file in @($zip, $installer)) { "$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))" }
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $lines, [Text.UTF8Encoding]::new($false))
    Write-Output "Release artifacts: $output"
} finally { Pop-Location }
