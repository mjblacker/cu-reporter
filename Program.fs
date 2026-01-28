namespace CuReporter

open System
open Argu
open CuReporter.Config
open CuReporter.ClickUp
open CuReporter.Discord
open CuReporter.Report

type CliArgs =
    | [<AltCommandLine("-n")>] Dry_Run
    | [<AltCommandLine("-d")>] Date of string
    | [<AltCommandLine("-c")>] Config of string
    | [<AltCommandLine("-v")>] Verbose
    | Debug
    | [<CliPrefix(CliPrefix.None)>] Init

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Dry_Run -> "Print report to console without posting to Discord"
            | Date _ -> "Date to report on (YYYY-MM-DD format, defaults to yesterday AEST)"
            | Config _ -> "Path to config file (defaults to standard config location)"
            | Verbose -> "Enable verbose output"
            | Debug -> "Show API requests and responses"
            | Init -> "Create a template config file"

module Program =

    let private aestOffset = TimeSpan.FromHours(10.0)
    let private aestTimeZone = TimeZoneInfo.CreateCustomTimeZone("AEST", aestOffset, "Australian Eastern Standard Time", "AEST")

    let private getYesterdayAest () =
        let nowUtc = DateTimeOffset.UtcNow
        let nowAest = TimeZoneInfo.ConvertTime(nowUtc, aestTimeZone)
        let yesterdayAest = nowAest.Date.AddDays(-1.0)
        DateOnly.FromDateTime(yesterdayAest)

    let private parseDate (dateStr: string) =
        match DateOnly.TryParseExact(dateStr, "yyyy-MM-dd") with
        | true, date -> Ok date
        | false, _ -> Error (sprintf "Invalid date format: %s. Expected YYYY-MM-DD" dateStr)

    let private runInit () =
        match Config.createTemplate() with
        | Ok path ->
            printfn "Created config template at: %s" path
            printfn "Edit the file to add your ClickUp API key, workspace ID, and Discord webhook URL."
            0
        | Error msg ->
            eprintfn "Error: %s" msg
            1

    let private runReport (dryRun: bool) (dateOpt: string option) (configPath: string option) (verbose: bool) (debug: bool) =
        // Set debug mode on modules
        ClickUp.debugMode <- debug
        Discord.debugMode <- debug
        Report.debugMode <- debug

        let date =
            match dateOpt with
            | Some d ->
                match parseDate d with
                | Ok date -> date
                | Error msg ->
                    eprintfn "Error: %s" msg
                    exit 1
            | None -> getYesterdayAest()

        if verbose then
            let dateStr = date.ToString("yyyy-MM-dd")
            printfn "Fetching data for: %s" dateStr

        match Config.load configPath with
        | Error msg ->
            eprintfn "Error: %s" msg
            1
        | Ok config ->
            if verbose then
                printfn "Using workspace ID: %s" config.ClickUp.WorkspaceId

            let result =
                fetchDailyData config.ClickUp (Some date)
                |> Async.RunSynchronously

            match result with
            | Error msg ->
                eprintfn "Error: %s" msg
                1
            | Ok (timeEntries, taskUpdates) ->
                if verbose then
                    printfn "Found %d time entries and %d updated tasks" timeEntries.Length taskUpdates.Length

                let report = buildReport date timeEntries taskUpdates

                // On Sunday or Monday, skip Discord if no data
                let todayAest = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, aestTimeZone).DayOfWeek
                let isWeekendDay = todayAest = DayOfWeek.Sunday || todayAest = DayOfWeek.Monday
                let hasNoData = timeEntries.IsEmpty && taskUpdates.IsEmpty

                if isWeekendDay && hasNoData && not dryRun then
                    printfn "No activity to report for %s (skipping Discord on %A)." (date.ToString("yyyy-MM-dd")) todayAest
                    0
                elif dryRun then
                    printfn ""
                    printfn "%s" (formatForConsole report taskUpdates)
                    0
                else
                    let messages = formatForDiscord report taskUpdates

                    if debug then
                        printfn "[DEBUG] Messages to send:"
                        for i, msg in messages |> List.indexed do
                            printfn "[DEBUG] Message %d (%d chars):" (i + 1) msg.Length
                            printfn "%s" msg
                            printfn ""

                    let sendResult =
                        sendMessages config.Discord messages
                        |> Async.RunSynchronously

                    match sendResult with
                    | Ok () ->
                        printfn "Report posted to Discord successfully."
                        0
                    | Error msg ->
                        eprintfn "Error: %s" msg
                        1

    [<EntryPoint>]
    let main argv =
        let parser = ArgumentParser.Create<CliArgs>(programName = "cu-reporter")

        try
            let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)

            if results.Contains Init then
                runInit()
            else
                let dryRun = results.Contains Dry_Run
                let dateOpt = results.TryGetResult Date
                let configPath = results.TryGetResult Config
                let verbose = results.Contains Verbose
                let debug = results.Contains Debug

                runReport dryRun dateOpt configPath verbose debug
        with
        | :? ArguParseException as ex ->
            printfn "%s" ex.Message
            0
