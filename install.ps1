# Register this repo as a Claude Code plugin marketplace, install the daily plugin for the current user,
# and build its helper so the daily nudge works from the first session. Safe to re-run.
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path $PSScriptRoot).Path
$PluginRoot = $RepoRoot
$ConfigDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$DataDir = Join-Path $ConfigDir 'plugins\data\daily-daily-coding'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "The .NET SDK ('dotnet') is not on PATH. Install .NET 10 or newer first: https://dotnet.microsoft.com/download"
}

if (Get-Command claude -ErrorAction SilentlyContinue) {
    Write-Host "Adding marketplace 'daily-coding' from $RepoRoot ..."
    claude plugin marketplace add $RepoRoot
    if ($LASTEXITCODE -ne 0) { Write-Host '(already added - continuing)' }
    Write-Host 'Installing daily@daily-coding for this user ...'
    claude plugin install daily@daily-coding --scope user
    if ($LASTEXITCODE -ne 0) { Write-Host '(install reported an error - it may already be installed; continuing)' }
}
else {
    Write-Host "Claude Code CLI ('claude') not found on PATH. In the Claude Code app, run these two commands instead:"
    Write-Host "  /plugin marketplace add $RepoRoot"
    Write-Host '  /plugin install daily@daily-coding'
}

Write-Host "Building the daily helper into $DataDir\bin ..."
dotnet build (Join-Path $PluginRoot 'src\Daily.Cli\Daily.Cli.csproj') -c Release -o (Join-Path $DataDir 'bin') -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
dotnet (Join-Path $DataDir 'bin\Daily.Cli.dll') stamp-write --data $DataDir --plugin-root $PluginRoot | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'daily helper did not run after building' }

Write-Host ''
Write-Host 'Done. Next:'
Write-Host '  1. Start (or restart) Claude Code.'
Write-Host '  2. Run /daily:setup to pick your languages, a level per language and your workspace.'
Write-Host 'Updates: git pull in this repo, then /daily:setup to rebuild (the nudge reminds you).'
