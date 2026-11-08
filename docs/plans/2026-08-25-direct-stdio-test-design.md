# Direct stdio test execution

## Problem

The Test action runs stdio MCP commands through `/bin/bash -c`, while Fetch Tools starts the configured executable directly. Shell parsing breaks executable paths containing spaces and makes Test behave differently from actual MCP clients.

## Design

Start the configured executable directly for Test. Pass every configured argument through `ProcessStartInfo.ArgumentList`, apply server environment variables through `ProcessStartInfo.Environment`, and exchange the MCP initialize message over redirected standard input and output.

Keep the existing login-shell PATH resolution performed during application initialization. Do not load aliases, shell functions, or other profile state when starting an MCP server.

## Verification

Add a regression test proving that a command path containing spaces remains the process filename and that arguments and environment values are passed without shell parsing. Run the complete test suite and build.
