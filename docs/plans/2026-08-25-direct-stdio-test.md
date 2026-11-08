# Direct Stdio Test Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the Test action start stdio MCP servers directly so executable paths containing spaces work exactly as they do in Fetch Tools.

**Architecture:** Add a Core stdio tester that uses the MCP SDK's `StdioClientTransport` and exposes the same result shape as the HTTP tester. The GUI delegates stdio Test to this service; the existing shell-based helper remains only for explicitly configured bridge command lines.

**Tech Stack:** .NET 10, C#, ModelContextProtocol SDK 1.2.0, xUnit

---

### Task 1: Specify direct stdio transport options

**Files:**
- Create: `tests/McpManager.Tests/StdioMcpTesterTests.cs`
- Modify: `src/McpManager.Core/McpManager.Core.csproj`

**Step 1: Write the failing test**

Add a test that builds options for `/Applications/Hopper Disassembler.app/Contents/MacOS/HopperMCPServer` and asserts that the full path remains `Command`, configured arguments remain separate, and environment values remain unchanged.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/McpManager.Tests/McpManager.Tests.csproj --filter StdioMcpTesterTests`

Expected: FAIL because `StdioMcpTester` does not exist.

### Task 2: Implement SDK-based stdio testing

**Files:**
- Create: `src/McpManager.Core/Services/StdioMcpTester.cs`
- Modify: `src/McpManager.Core/McpManager.Core.csproj`

**Step 1: Build transport options without shell parsing**

Create `StdioClientTransportOptions` using the server's `Command`, `Args`, and `EnvironmentVariables` directly.

**Step 2: Connect with the MCP SDK**

Use `McpClient.CreateAsync` with a ten-second linked timeout and return server name/version or a useful timeout/error result.

**Step 3: Run the focused test**

Run: `dotnet test tests/McpManager.Tests/McpManager.Tests.csproj --filter StdioMcpTesterTests`

Expected: PASS.

### Task 3: Use the tester from the GUI

**Files:**
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.cs:31-36,165-189,1191-1221`

**Step 1: Inject the stdio tester**

Construct `StdioMcpTester` in the default constructor and accept `IStdioMcpTester` in the testable constructor.

**Step 2: Replace the stdio shell call**

Pass the updated `McpServer` model to `TestInitializeAsync`, then copy `ResultText` and `StatusMessage` into the existing UI properties.

**Step 3: Preserve bridge behavior**

Leave `TestViaCommandAsync` available for HTTP bridge command templates only.

### Task 4: Verify the change

**Files:**
- No additional changes

**Step 1: Run all tests**

Run: `dotnet test McpManager.slnx`

Expected: all tests pass.

**Step 2: Build the application**

Run: `dotnet build McpManager.slnx --no-restore`

Expected: build succeeds with no errors.

**Step 3: Inspect the diff**

Confirm existing unrelated worktree changes remain intact and only the intended sections were changed.
