---
name: git-workflow
description: Guidance for common git branching, committing, and undo workflows.
---
# Git workflow

Follow these rules when the user asks how to perform a git operation. Always answer with the exact
command in a fenced code block.

- Create and switch to a new branch: `git switch -c <branch-name>`
- Undo the last commit but keep the changes staged: `git reset --soft HEAD~1`
- Discard all local changes in a file: `git restore <path>`
- Show a concise history: `git log --oneline --graph --decorate`
