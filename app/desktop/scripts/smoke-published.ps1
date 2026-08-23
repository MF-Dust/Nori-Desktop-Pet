[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string] $BinaryPath,

	[ValidateSet("first-run", "initialized")]
	[string] $Mode = "first-run",

	[string] $Profile = "",
	[int] $TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

$binary = [IO.Path]::GetFullPath($BinaryPath)
if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
	throw "找不到发布可执行文件: $binary"
}
if ($TimeoutSeconds -lt 5 -or $TimeoutSeconds -gt 300) {
	throw "TimeoutSeconds 必须在 5 到 300 之间"
}

$ownsProfile = [string]::IsNullOrWhiteSpace($Profile)
if ($ownsProfile) {
	$Profile = Join-Path ([IO.Path]::GetTempPath()) ("nori-smoke-{0}" -f ([Guid]::NewGuid().ToString("N")))
}
$profile = [IO.Path]::GetFullPath($Profile)
New-Item -ItemType Directory -Path $profile -Force | Out-Null
$databasePath = Join-Path $profile "data\nori.db"
if (Test-Path -LiteralPath $databasePath) {
	throw "profile 不是隔离的临时目录, 已存在 nori.db: $databasePath"
}
$readinessPath = Join-Path $profile "readiness.json"
Remove-Item -LiteralPath $readinessPath -Force -ErrorAction SilentlyContinue
$stdoutPath = Join-Path $profile "smoke.stdout.log"
$stderrPath = Join-Path $profile "smoke.stderr.log"
Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

$process = $null
try {
	$workingDirectory = Split-Path -Parent $binary
	$arguments = "--smoke-test $Mode --profile `"$profile`""
	$process = Start-Process -FilePath $binary -ArgumentList $arguments -WorkingDirectory $workingDirectory `
		-RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru

	$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
	while (-not (Test-Path -LiteralPath $readinessPath -PathType Leaf)) {
		if ($process.HasExited) {
			$stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { "" }
			throw "发布程序在 readiness checkpoint 前退出 (code=$($process.ExitCode)): $stderr"
		}
		if ([DateTime]::UtcNow -gt $deadline) {
			throw "等待 readiness checkpoint 超时 ($TimeoutSeconds 秒): $readinessPath"
		}
		Start-Sleep -Milliseconds 200
	}

	$ready = Get-Content -LiteralPath $readinessPath -Raw | ConvertFrom-Json
	if ($ready.status -ne "ready") { throw "readiness status 不正确: $($ready.status)" }
	if ($ready.mode -ne $Mode) { throw "readiness mode 不正确: $($ready.mode)" }
	$expectedDataDir = [IO.Path]::GetFullPath((Join-Path $profile "data"))
	$actualDataDir = [IO.Path]::GetFullPath([string]$ready.data_dir)
	if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualDataDir, $expectedDataDir)) {
		throw "冒烟程序使用了 profile 之外的数据目录: $actualDataDir"
	}

	$exitDeadline = [DateTime]::UtcNow.AddSeconds(10)
	while (-not $process.HasExited -and [DateTime]::UtcNow -lt $exitDeadline) {
		Start-Sleep -Milliseconds 200
	}
	if (-not $process.HasExited) {
		Stop-Process -Id $process.Id -Force
		throw "冒烟程序写入 readiness 后未在 10 秒内退出"
	}
	if ($process.ExitCode -ne 0) {
		throw "冒烟程序退出码不是 0: $($process.ExitCode)"
	}
	Write-Host "发布冒烟通过: $Mode ($binary)"
}
finally {
	if ($null -ne $process -and -not $process.HasExited) {
		Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
	}
	if ($ownsProfile -and (Test-Path -LiteralPath $profile)) {
		Remove-Item -LiteralPath $profile -Recurse -Force -ErrorAction SilentlyContinue
	}
}
