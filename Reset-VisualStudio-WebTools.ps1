$ErrorActionPreference = 'Stop'

if (Get-Process -Name devenv -ErrorAction SilentlyContinue) {
	Write-Host 'Close every Visual Studio window, then run this script again.' -ForegroundColor Yellow
	exit 1
}

$cacheDirectories = @(
	(Join-Path $PSScriptRoot '.vs'),
	(Join-Path (Split-Path $PSScriptRoot -Parent) '.vs')
)

foreach ($cacheDirectory in ($cacheDirectories | Select-Object -Unique)) {
	if (Test-Path $cacheDirectory) {
		Remove-Item -LiteralPath $cacheDirectory -Recurse -Force
		Write-Host "Removed Visual Studio workspace cache: $cacheDirectory"
	}
}

$visualStudioRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio'
if (Test-Path $visualStudioRoot) {
	Get-ChildItem -LiteralPath $visualStudioRoot -Directory | ForEach-Object {
		$componentCache = Join-Path $_.FullName 'ComponentModelCache'
		if (Test-Path $componentCache) {
			Remove-Item -LiteralPath $componentCache -Recurse -Force
			Write-Host "Removed Visual Studio component cache: $componentCache"
		}
	}
}

Write-Host 'Cache reset complete. Reopen the project and select the IIS Express profile.' -ForegroundColor Green
