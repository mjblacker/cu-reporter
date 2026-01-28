namespace CuReporter

open System
open System.IO
open Tomlyn
open Tomlyn.Model

module Config =

    type ClickUpConfig = {
        ApiKey: string
        WorkspaceId: string
    }

    type DiscordConfig = {
        WebhookUrl: string
    }

    type AppConfig = {
        ClickUp: ClickUpConfig
        Discord: DiscordConfig
    }

    let private getConfigDirectory () =
        let appName = "cu-reporter"
        if OperatingSystem.IsWindows() then
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName)
        elif OperatingSystem.IsMacOS() then
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", appName)
        else
            let xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            let baseDir = if String.IsNullOrEmpty(xdgConfig) then
                              Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                          else xdgConfig
            Path.Combine(baseDir, appName)

    let getConfigPath () =
        Path.Combine(getConfigDirectory(), "config.toml")

    let private getString (table: TomlTable) (key: string) =
        match table.TryGetValue(key) with
        | true, value -> value :?> string
        | false, _ -> failwithf "Missing required config key: %s" key

    let private getTable (table: TomlTable) (key: string) =
        match table.TryGetValue(key) with
        | true, value -> value :?> TomlTable
        | false, _ -> failwithf "Missing required config section: [%s]" key

    let load (path: string option) : Result<AppConfig, string> =
        let configPath = path |> Option.defaultWith getConfigPath

        if not (File.Exists(configPath)) then
            Error $"Config file not found at: {configPath}\nRun 'cu-reporter init' to create a template."
        else
            try
                let content = File.ReadAllText(configPath)
                let model = Toml.ToModel(content)

                let clickupTable = getTable model "clickup"
                let discordTable = getTable model "discord"

                Ok {
                    ClickUp = {
                        ApiKey = getString clickupTable "api_key"
                        WorkspaceId = getString clickupTable "workspace_id"
                    }
                    Discord = {
                        WebhookUrl = getString discordTable "webhook_url"
                    }
                }
            with ex ->
                Error $"Failed to parse config: {ex.Message}"

    let createTemplate () =
        let configPath = getConfigPath()
        let configDir = Path.GetDirectoryName(configPath)

        if not (Directory.Exists(configDir)) then
            Directory.CreateDirectory(configDir) |> ignore

        let template = """# CU Reporter Configuration

[clickup]
api_key = "pk_YOUR_API_KEY_HERE"
workspace_id = "YOUR_WORKSPACE_ID_HERE"

[discord]
webhook_url = "https://discord.com/api/webhooks/YOUR_WEBHOOK_URL"
"""

        if File.Exists(configPath) then
            Error $"Config file already exists at: {configPath}"
        else
            File.WriteAllText(configPath, template)
            Ok configPath
