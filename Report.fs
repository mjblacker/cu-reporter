namespace CuReporter

open System
open CuReporter.Types

module Report =

    let mutable debugMode = false

    let private log msg =
        if debugMode then printfn "[DEBUG] %s" msg

    let private formatDuration (duration: TimeSpan) =
        if duration.TotalHours >= 1.0 then
            sprintf "%dh %dm" (int duration.TotalHours) duration.Minutes
        elif duration.TotalMinutes >= 1.0 then
            sprintf "%dm" (int duration.TotalMinutes)
        else
            sprintf "%ds" (int duration.TotalSeconds)

    let buildReport (date: DateOnly) (timeEntries: TimeEntry list) (taskUpdates: TaskUpdate list) : DailyReport =
        log (sprintf "Building report with %d time entries and %d task updates" timeEntries.Length taskUpdates.Length)

        let entriesByUser =
            timeEntries
            |> List.groupBy (fun e -> e.UserId, e.UserName)

        let allUserIds =
            entriesByUser |> List.map fst |> List.distinct

        let summaries =
            allUserIds
            |> List.map (fun (userId, userName) ->
                let userEntries =
                    entriesByUser
                    |> List.tryFind (fun ((uid, _), _) -> uid = userId)
                    |> Option.map snd
                    |> Option.defaultValue []

                let totalTime =
                    userEntries
                    |> List.sumBy (fun e -> e.Duration.TotalMilliseconds)
                    |> TimeSpan.FromMilliseconds

                {
                    UserId = userId
                    UserName = userName
                    TimeEntries = userEntries
                    TaskUpdates = []
                    TotalTrackedTime = totalTime
                })
            |> List.sortByDescending (fun s -> s.TotalTrackedTime)

        let totalTime =
            summaries
            |> List.sumBy (fun s -> s.TotalTrackedTime.TotalMilliseconds)
            |> TimeSpan.FromMilliseconds

        log (sprintf "Built %d user summaries, total time: %s" summaries.Length (formatDuration totalTime))

        {
            Date = date
            Summaries = summaries
            TotalTrackedTime = totalTime
        }

    let formatForDiscord (report: DailyReport) (taskUpdates: TaskUpdate list) : string list =
        let dateStr = report.Date.ToString("yyyy-MM-dd")
        let header = sprintf "# 📋 Daily Work Report - %s" dateStr

        // Get all task IDs that have time tracked
        let trackedTaskIds =
            report.Summaries
            |> List.collect (fun s -> s.TimeEntries)
            |> List.choose (fun e -> e.TaskId)
            |> Set.ofList

        // Filter out task updates for tasks that already have time tracked
        let untrackedTaskUpdates =
            taskUpdates
            |> List.filter (fun t -> not (Set.contains t.TaskId trackedTaskIds))

        log (sprintf "Formatting for Discord: %d summaries, %d task updates (%d with time tracked, %d without)"
            report.Summaries.Length taskUpdates.Length trackedTaskIds.Count untrackedTaskUpdates.Length)

        let summarySection =
            if report.Summaries.IsEmpty && untrackedTaskUpdates.IsEmpty then
                "\nNo activity recorded for this day."
            else
                let personSummaries =
                    if report.Summaries.IsEmpty then
                        ""
                    else
                        report.Summaries
                        |> List.map (fun s ->
                            let timeStr = formatDuration s.TotalTrackedTime
                            let taskCount = s.TimeEntries |> List.choose (fun e -> e.TaskName) |> List.distinct |> List.length
                            sprintf "- **%s**: %s tracked across %d task(s)" s.UserName timeStr taskCount)
                        |> String.concat "\n"

                let totalTimeStr = formatDuration report.TotalTrackedTime
                if String.IsNullOrEmpty personSummaries then
                    sprintf "\n## 📊 Summary\nNo time tracked.\n\n**Updated Tasks:** %d" untrackedTaskUpdates.Length
                else
                    sprintf "\n## 📊 Summary\n%s\n\n**Total Team Time:** %s | **Updated Tasks:** %d" personSummaries totalTimeStr untrackedTaskUpdates.Length

        let timeTrackingDetails =
            report.Summaries
            |> List.filter (fun s -> not s.TimeEntries.IsEmpty)
            |> List.map (fun s ->
                let taskLines =
                    s.TimeEntries
                    |> List.groupBy (fun e -> e.ListName |> Option.defaultValue "(No list)")
                    |> List.map (fun (listName, entriesInList) ->
                        let tasksInList =
                            entriesInList
                            |> List.groupBy (fun e -> e.TaskId, e.TaskName)
                            |> List.map (fun ((_, taskName), entries) ->
                                let taskTime =
                                    entries
                                    |> List.sumBy (fun e -> e.Duration.TotalMilliseconds)
                                    |> TimeSpan.FromMilliseconds
                                let name = taskName |> Option.defaultValue "(No task)"
                                sprintf "- %s: %s" name (formatDuration taskTime))
                            |> String.concat "\n"
                        sprintf "**%s**\n%s" listName tasksInList)
                    |> String.concat "\n\n"

                sprintf "### %s\n%s" s.UserName taskLines)
            |> String.concat "\n\n"

        let updatedTasksSection =
            if untrackedTaskUpdates.IsEmpty then
                ""
            else
                let tasksByList =
                    untrackedTaskUpdates
                    |> List.groupBy (fun t -> t.ListName |> Option.defaultValue "(No list)")
                    |> List.map (fun (listName, tasks) ->
                        let taskLines =
                            tasks
                            |> List.map (fun t -> sprintf "- %s" t.TaskName)
                            |> String.concat "\n"
                        sprintf "**%s**\n%s" listName taskLines)
                    |> String.concat "\n\n"

                sprintf "\n## 📝 Updated Tasks (no time tracked)\n%s" tasksByList

        let fullMessage =
            [ header; summarySection ]
            @ (if String.IsNullOrWhiteSpace timeTrackingDetails then [] else [ "\n## ⏱️ Time Tracking Details"; timeTrackingDetails ])
            @ (if String.IsNullOrWhiteSpace updatedTasksSection then [] else [ updatedTasksSection ])
            |> String.concat "\n"

        log (sprintf "Full message length: %d" fullMessage.Length)

        if fullMessage.Length <= 2000 then
            [ fullMessage ]
        else
            let part1 = header + summarySection + "\n## ⏱️ Time Tracking Details\n" + timeTrackingDetails
            let part2 = updatedTasksSection
            [ part1; part2 ]
            |> List.filter (fun s -> not (String.IsNullOrWhiteSpace s) && s.Length <= 2000)
            |> function
               | [] -> [ (header + summarySection).[.. min 1999 (header + summarySection).Length - 1] ]
               | msgs -> msgs

    let formatForConsole (report: DailyReport) (taskUpdates: TaskUpdate list) : string =
        let sb = System.Text.StringBuilder()

        // Get all task IDs that have time tracked
        let trackedTaskIds =
            report.Summaries
            |> List.collect (fun s -> s.TimeEntries)
            |> List.choose (fun e -> e.TaskId)
            |> Set.ofList

        // Filter out task updates for tasks that already have time tracked
        let untrackedTaskUpdates =
            taskUpdates
            |> List.filter (fun t -> not (Set.contains t.TaskId trackedTaskIds))

        let dateStr = report.Date.ToString("yyyy-MM-dd")
        sb.AppendLine(sprintf "Daily Work Report - %s" dateStr) |> ignore
        sb.AppendLine(String.replicate 50 "=") |> ignore
        sb.AppendLine() |> ignore

        if report.Summaries.IsEmpty && untrackedTaskUpdates.IsEmpty then
            sb.AppendLine("No activity recorded for this day.") |> ignore
        else
            sb.AppendLine("SUMMARY") |> ignore
            sb.AppendLine(String.replicate 50 "-") |> ignore

            if report.Summaries.IsEmpty then
                sb.AppendLine("  No time tracked.") |> ignore
            else
                for s in report.Summaries do
                    let timeStr = formatDuration s.TotalTrackedTime
                    let taskCount = s.TimeEntries |> List.choose (fun e -> e.TaskName) |> List.distinct |> List.length
                    sb.AppendLine(sprintf "  %s: %s tracked across %d task(s)" s.UserName timeStr taskCount) |> ignore

            sb.AppendLine() |> ignore
            sb.AppendLine(sprintf "Total Team Time: %s" (formatDuration report.TotalTrackedTime)) |> ignore
            sb.AppendLine(sprintf "Updated Tasks: %d" untrackedTaskUpdates.Length) |> ignore
            sb.AppendLine() |> ignore

            if not report.Summaries.IsEmpty then
                sb.AppendLine("TIME TRACKING DETAILS") |> ignore
                sb.AppendLine(String.replicate 50 "-") |> ignore

                for s in report.Summaries |> List.filter (fun s -> not s.TimeEntries.IsEmpty) do
                    sb.AppendLine() |> ignore
                    sb.AppendLine(sprintf "  %s:" s.UserName) |> ignore

                    let listGroups =
                        s.TimeEntries
                        |> List.groupBy (fun e -> e.ListName |> Option.defaultValue "(No list)")

                    for (listName, entriesInList) in listGroups do
                        sb.AppendLine(sprintf "    [%s]" listName) |> ignore

                        let taskGroups =
                            entriesInList
                            |> List.groupBy (fun e -> e.TaskId, e.TaskName)

                        for ((_, taskName), entries) in taskGroups do
                            let taskTime =
                                entries
                                |> List.sumBy (fun e -> e.Duration.TotalMilliseconds)
                                |> TimeSpan.FromMilliseconds
                            let name = taskName |> Option.defaultValue "(No task)"
                            sb.AppendLine(sprintf "      - %s: %s" name (formatDuration taskTime)) |> ignore

            if not untrackedTaskUpdates.IsEmpty then
                sb.AppendLine() |> ignore
                sb.AppendLine("UPDATED TASKS (no time tracked)") |> ignore
                sb.AppendLine(String.replicate 50 "-") |> ignore

                let tasksByList =
                    untrackedTaskUpdates
                    |> List.groupBy (fun t -> t.ListName |> Option.defaultValue "(No list)")

                for (listName, tasks) in tasksByList do
                    sb.AppendLine(sprintf "  [%s]" listName) |> ignore
                    for t in tasks do
                        sb.AppendLine(sprintf "    - %s" t.TaskName) |> ignore

        sb.ToString()
