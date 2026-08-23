[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string] $PublishDir,

	[Parameter(Mandatory = $true)]
	[string] $Version,

	[string] $OutputDir = "bin\release"
)

$ErrorActionPreference = "Stop"
$publish = [IO.Path]::GetFullPath($PublishDir)
$output = [IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path -LiteralPath $publish -PathType Container)) { throw "找不到发布目录: $publish" }
New-Item -ItemType Directory -Path $output -Force | Out-Null

$requiredFiles = @(
	"Nori.Desktop.exe",
	"Nori.Desktop.dll",
	"Nori.Desktop.deps.json",
	"Nori.Desktop.runtimeconfig.json",
	"Live2DCubismCore.dll",
	"wwwroot\index.html"
)
foreach ($relativePath in $requiredFiles) {
	$requiredPath = Join-Path $publish $relativePath
	if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
		throw "发布目录缺少必要文件: $relativePath"
	}
}

if (Get-ChildItem -LiteralPath $publish -Recurse -File -Filter "dotnet.exe" -ErrorAction SilentlyContinue) {
	throw "Windows ZIP 不得携带 .NET Runtime (必须是 framework-dependent)"
}
if (Get-ChildItem -LiteralPath $publish -Recurse -File -Filter "*.map" -ErrorAction SilentlyContinue) {
	throw "发布目录仍含 source map, 不能打包"
}

$metadataOutput = Join-Path $output "metadata"
Remove-Item -LiteralPath $metadataOutput -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $metadataOutput -Force | Out-Null
node (Join-Path $PSScriptRoot "generate-release-metadata.mjs") `
	--publish-dir $publish --version $Version --rid win-x64 --output-dir $metadataOutput

$artifactName = "nori-$Version-win-x64-framework-dependent.zip"
$artifactPath = Join-Path $output $artifactName
$staging = Join-Path $output ".staging"
$expanded = Join-Path $output ".expanded-smoke"
Remove-Item -LiteralPath $staging, $expanded, $artifactPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -Path (Join-Path $publish "*") -Destination $staging -Recurse -Force
$metadataFiles = @("THIRD-PARTY-NOTICES.json", "THIRD-PARTY-NOTICES.md", "SBOM.cdx.json", "RELEASE-MANIFEST.json")
foreach ($metadataFile in $metadataFiles) {
	$metadataPath = Join-Path $metadataOutput $metadataFile
	Copy-Item -LiteralPath $metadataPath -Destination $staging -Force
	Copy-Item -LiteralPath $metadataPath -Destination $output -Force
}
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $artifactPath -CompressionLevel Optimal

# 解压后再检查一次, 避免 ZIP 根目录或必要文件路径打错。
Expand-Archive -LiteralPath $artifactPath -DestinationPath $expanded -Force
foreach ($relativePath in $requiredFiles + @("THIRD-PARTY-NOTICES.json", "SBOM.cdx.json", "RELEASE-MANIFEST.json")) {
	if (-not (Test-Path -LiteralPath (Join-Path $expanded $relativePath) -PathType Leaf)) {
		throw "ZIP 内容缺少必要文件: $relativePath"
	}
}
if (Test-Path -LiteralPath (Join-Path $expanded "shared")) {
	throw "ZIP 包含 .NET shared runtime, 不是 framework-dependent 包"
}

$hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$artifactPath.sha256"
[IO.File]::WriteAllText($checksumPath, "$hash  $artifactName`n", [Text.UTF8Encoding]::new($false))

Remove-Item -LiteralPath $staging, $expanded -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Windows framework-dependent ZIP 已生成: $artifactPath"
Write-Host "SHA-256: $checksumPath"
