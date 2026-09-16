#!/usr/bin/env bash
# Quick worktree setup with automatic guardrails configuration
# Usage: quick-worktree.sh <agent> <task> [base-branch]

set -euo pipefail

usage() {
  cat <<EOF
Usage: quick-worktree.sh <agent> <task> [base-branch]

Examples:
  quick-worktree.sh codex fix-auth
  quick-worktree.sh claude add-metrics origin/main
  quick-worktree.sh copilot docs main

Arguments:
  agent       Agent name (codex, claude, copilot)
  task        Task description (kebab-case)
  base-branch Base branch to branch from (overrides .agent-stack/modules/protocol/workspace.json and git default branch)
EOF
}

if [[ $# -lt 2 ]]; then
  usage
  exit 1
fi

AGENT="$1"
TASK="$2"
EXPLICIT_BASE="${3:-}"
REPO_ROOT="$(git rev-parse --show-toplevel)"

read_workspace_config() {
  local key="$1"
  local config="${REPO_ROOT}/.agent-stack/modules/protocol/workspace.json"
  if [[ ! -f "$config" ]]; then
    return 0
  fi
  node -e "const fs=require('fs'); const value=JSON.parse(fs.readFileSync(process.argv[1], 'utf8'))[process.argv[2]]; if (typeof value === 'string' && value.trim()) console.log(value.trim());" "$config" "$key" 2>/dev/null || true
}

resolve_base_branch() {
  if [[ -n "$EXPLICIT_BASE" ]]; then
    printf '%s\n' "$EXPLICIT_BASE"
    return
  fi

  local configured
  configured="$(read_workspace_config baseBranch)"
  if [[ -n "$configured" ]]; then
    printf '%s\n' "$configured"
    return
  fi

  local remote_head
  remote_head="$(git symbolic-ref --quiet --short refs/remotes/origin/HEAD 2>/dev/null || true)"
  if [[ -n "$remote_head" ]]; then
    printf '%s\n' "$remote_head"
    return
  fi

  for candidate in origin/main origin/master main master; do
    if git rev-parse --verify --quiet "$candidate" >/dev/null; then
      printf '%s\n' "$candidate"
      return
    fi
  done

  echo "Could not resolve base branch. Configure .agent-stack/modules/protocol/workspace.json baseBranch or pass one explicitly." >&2
  exit 1
}

resolve_worktree_root() {
  local configured
  configured="$(read_workspace_config worktreeRoot)"
  if [[ -n "$configured" ]]; then
    printf '%s\n' "$configured"
    return
  fi

  printf '../%s-worktrees\n' "$(basename "$REPO_ROOT")"
}

BASE="$(resolve_base_branch)"

BRANCH="feat/${AGENT}-${TASK}"
WORKTREE_ROOT="$(resolve_worktree_root)"
WORKTREE_PATH="${WORKTREE_ROOT}/${AGENT}-${TASK}"

echo "Creating worktree setup:"
echo "  Agent:    $AGENT"
echo "  Task:     $TASK"
echo "  Branch:   $BRANCH"
echo "  Path:     $WORKTREE_PATH"
echo "  Base:     $BASE"
echo ""

# Create worktrees directory if needed
mkdir -p "$WORKTREE_ROOT"
WORKTREE_ROOT_ABS="$(cd "$WORKTREE_ROOT" && pwd -P)"

# Allow sandboxed agents to run Git inside this repo's worktree root.
git config --global --add safe.directory "${WORKTREE_ROOT_ABS}/*"

# Create worktree
echo "Creating worktree..."
git worktree add -b "$BRANCH" "$WORKTREE_PATH" "$BASE"

# Configure guardrails
echo "Configuring guardrails..."
cd "$WORKTREE_PATH"

GIT_DIR="$(git rev-parse --git-dir)"
printf '%s\n' "$BRANCH" > "$GIT_DIR/agent-expected-branch"
printf '%s\n' "$(pwd -P)" > "$GIT_DIR/agent-expected-worktree"

# Set safe defaults
git config --local push.default current
git config --local pull.ff only

echo ""
echo "✓ Worktree created and configured!"
echo ""
echo "Verification:"
echo "  Current branch: $(git branch --show-current)"
echo "  Worktree path:  $(git rev-parse --show-toplevel)"
echo "  Safe root:      ${WORKTREE_ROOT_ABS}/*"
echo "  Expected branch: $(cat "$GIT_DIR/agent-expected-branch")"
echo "  Expected worktree: $(cat "$GIT_DIR/agent-expected-worktree")"
echo ""
echo "Next steps:"
echo "  cd $WORKTREE_PATH"
echo "  # Start your agent and begin work"
