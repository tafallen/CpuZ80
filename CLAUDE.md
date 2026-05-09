# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build everything
dotnet build

# Run all tests (includes coverage enforcement — fails if branch coverage < 85%)
dotnet test

# Run tests for a specific project
dotnet test tests/CpuZ80.Tests/

# Run a single test class
dotnet test --filter "ClassName=CpuZ80.Tests.ArithmeticTests"

# Run tests with coverage collection
dotnet test --collect:"XPlat Code Coverage"

# Run ZEXALL instruction exerciser
dotnet run -c Release --project tests/CpuZ80.Exerciser
```

## Consolidated Instructions

See [AGENTS.md](./AGENTS.md) for the shared architectural guidelines, engineering standards, and workflow instructions for this repository.
