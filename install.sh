#!/usr/bin/env bash
# Register this repo as a Claude Code plugin marketplace, install the daily plugin for the current user,
# and build its helper so the daily nudge works from the first session. Safe to re-run.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_ROOT="$REPO_ROOT"
DATA_DIR="${CLAUDE_CONFIG_DIR:-$HOME/.claude}/plugins/data/daily-daily-coding"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "The .NET SDK ('dotnet') is not on PATH. Install .NET 10 or newer first: https://dotnet.microsoft.com/download" >&2
  exit 1
fi

if command -v claude >/dev/null 2>&1; then
  echo "Adding marketplace 'daily-coding' from $REPO_ROOT ..."
  claude plugin marketplace add "$REPO_ROOT" || echo "(already added — continuing)"
  echo "Installing daily@daily-coding for this user ..."
  claude plugin install daily@daily-coding --scope user || echo "(install reported an error — it may already be installed; continuing)"
else
  cat <<EOF2
Claude Code CLI ('claude') not found on PATH. In the Claude Code app, run these two commands instead:
  /plugin marketplace add $REPO_ROOT
  /plugin install daily@daily-coding
EOF2
fi

echo "Building the daily helper into $DATA_DIR/bin ..."
dotnet build "$PLUGIN_ROOT/src/Daily.Cli/Daily.Cli.csproj" -c Release -o "$DATA_DIR/bin" -v q
dotnet "$DATA_DIR/bin/Daily.Cli.dll" stamp-write --data "$DATA_DIR" --plugin-root "$PLUGIN_ROOT" >/dev/null

cat <<'EOF2'

Done. Next:
  1. Start (or restart) Claude Code in any folder.
  2. Run /daily:setup to pick your level and workspace.
Updates: `git pull` in this repo, then /daily:setup to rebuild (the nudge reminds you).
EOF2
