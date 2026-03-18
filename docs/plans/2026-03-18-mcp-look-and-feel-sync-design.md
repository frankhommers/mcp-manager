# MCP Look and Feel Sync Design

## Goal

Bring `mcp-manager` into the same product family as `git-auto-sync` and `rclone-mount` without changing its core job: managing MCP servers and export targets.

## Design Direction

Use a hybrid approach:

- adopt the calmer Fluent shell, spacing, theming, and icon discipline from `git-auto-sync`
- borrow the clearer information architecture from `rclone-mount`
- keep `mcp-manager`'s domain model and operational flows intact

This avoids a full rewrite while still making the app feel like it belongs to the same suite.

## App Structure

The app moves from a single long editor page to four top-level states:

- `Overview` for status, counts, recent activity, and quick actions
- `Servers` for browsing and editing MCP server definitions
- `Targets` for browsing and editing export targets
- `Settings` for bridge commands and app-level behavior

The overview becomes the default landing screen. Server and target editing follow a browse-left, detail-right pattern instead of dumping all content into the home screen.

## Visual Language

- Fluent-first resources and `DynamicResource` usage instead of hardcoded dark colors
- subtle cards, borders, corner radius, and spacing that work in both light and dark themes
- no emoji-based UI controls or labels
- consistent iconography for navigation, status, actions, targets, and connection types
- calmer copy with short, product-like labels and descriptions

## Interaction Model

- primary actions stay visible but limited: add, import, test, export, save
- diagnostics stay available but move into contained panels instead of dominating the flow
- empty states explain the next step clearly and use one obvious CTA
- status stays in a compact top or bottom area instead of being repeated in many places

## Screen-Level Changes

### Overview

- summary cards for server count, target count, clipboard availability, and current status
- quick actions for add server, add target, import config, and export all
- recent or contextual guidance instead of a giant empty wall

### Servers

- left panel with grouped server list and transport badges
- right panel with structured cards for identity, connection, command, tools, and testing
- reduced visual noise in debug/test output blocks

### Targets

- left panel with target list and type indicator
- right panel with cards for target info, export options, enabled servers, and preview/export actions
- clipboard and global targets keep their special behavior but fit the same visual system

### Settings

- dedicated page with bridge commands and bridge validation
- bridge placeholders and examples remain, but layout becomes cleaner and less technical at first glance

## ViewModel Impact

`MainWindowViewModel` should expose explicit navigation state instead of inferring every screen from selection state alone. Existing selection models can still be reused for details.

Converters that currently return emoji or raw brushes should move toward semantic styling helpers that support consistent, theme-friendly UI.

## File Impact

Primary files:

- `src/McpManager/App.axaml`
- `src/McpManager/Views/MainWindow.axaml`
- `src/McpManager/ViewModels/MainWindowViewModel.cs`
- `src/McpManager/ViewModels/Converters.cs`

Likely supporting additions:

- new view files under `src/McpManager/Views/` for overview and split detail sections
- optional reusable styles or resource dictionaries under `src/McpManager/`
- vector or symbolic assets under `src/McpManager/Assets/`

## Verification Strategy

- keep the solution building after each structural change
- verify navigation state transitions with focused viewmodel checks where practical
- manually validate the Avalonia UI for layout, empty states, light/dark behavior, and action flow

## Non-Goals

- no domain rewrite of registry, import, export, or MCP transport logic
- no major behavior changes to how targets or servers are persisted
- no attempt to make `mcp-manager` identical to the other apps pixel-for-pixel
