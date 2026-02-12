# CU Reporter

A command-line tool that generates daily work activity reports from ClickUp and posts them to Discord.

## Overview

CU Reporter fetches time entries and updated tasks from ClickUp for a specified date, aggregates the data by user, and formats it into readable reports. Reports can be posted to Discord via webhook or previewed in the console.

## Features

- Fetches time entries from ClickUp for a specified date
- Tracks task updates and modifications
- Aggregates data by user, list, and task
- Posts formatted reports to Discord
- Dry-run mode for previewing reports
- Timezone-aware (defaults to AEST)

## Prerequisites

- .NET 10.0 SDK (for building)
- ClickUp API key
- Discord webhook URL

## Build

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish as single-file executable
dotnet publish -c Release
```

The published executable will be located in `publish/`.

## Configuration

### Create Config File

Run the init command to create a template configuration file:

```bash
cu-reporter init
```

This creates a config file at the platform-specific location:

| Platform | Path |
|----------|------|
| Linux | `~/.config/cu-reporter/config.toml` |
| macOS | `~/Library/Application Support/cu-reporter/config.toml` |
| Windows | `%APPDATA%\cu-reporter\config.toml` |

### Config File Format

```toml
[clickup]
api_key = "pk_YOUR_API_KEY_HERE"
workspace_id = "YOUR_WORKSPACE_ID_HERE"

[discord]
webhook_url = "https://discord.com/api/webhooks/YOUR_WEBHOOK_URL"

[report]
# Optional exclusions applied to "Updated Tasks (no time tracked)" based on task name.
exclude_starts_with = ["[BO"]
exclude_contains = []
```

## Usage

```
cu-reporter [options] [command]
```

### Commands

| Command | Description |
|---------|-------------|
| `init` | Create a template config file |

### Options

| Option | Short | Description |
|--------|-------|-------------|
| `--dry-run` | `-n` | Print report to console without posting to Discord |
| `--date <YYYY-MM-DD>` | `-d` | Date to report on (defaults to yesterday AEST) |
| `--config <path>` | `-c` | Path to config file |
| `--verbose` | `-v` | Enable verbose output |
| `--debug` | | Show API requests and responses |

### Examples

```bash
# Generate and post yesterday's report to Discord
cu-reporter

# Preview report in console without posting
cu-reporter -n

# Generate report for a specific date
cu-reporter -d 2026-01-27

# Use a custom config file with verbose output
cu-reporter -c /path/to/config.toml -v

# Debug API calls
cu-reporter --debug -n
```

## Dependencies

- [Argu](https://github.com/fsprojects/Argu) - Command-line argument parsing
- [FSharp.SystemTextJson](https://github.com/Tarmil/FSharp.SystemTextJson) - JSON serialization
- [Tomlyn](https://github.com/xoofx/Tomlyn) - TOML configuration parsing
