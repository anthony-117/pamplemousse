#!/bin/sh
# Points git at the versioned hooks in .githooks/ (run once per clone).
set -e
cd "$(git rev-parse --show-toplevel)"
git config core.hooksPath .githooks
chmod +x .githooks/* 2>/dev/null || true
echo "Git hooks installed: pre-push runs format, analyzers and all tests before every push."
