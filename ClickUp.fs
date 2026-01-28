namespace CuReporter

open System
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization
open CuReporter.Types
open CuReporter.Config

module ClickUp =

    let mutable debugMode = false

    let private log msg =
        if debugMode then printfn "[DEBUG] %s" msg

    let private jsonOptions =
        let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        let fsOptions = JsonFSharpOptions.Default().WithSkippableOptionFields()
        options.Converters.Add(JsonFSharpConverter(fsOptions))
        options

    type private TimeEntryUser = {
        Id: int
        Username: string
    }

    type private TimeEntryList = {
        Id: string
        Name: string
    }

    type private TimeEntryTask = {
        Id: string
        Name: string
        List: TimeEntryList option
    }

    type private TimeEntryResponse = {
        Id: string
        Task: TimeEntryTask option
        User: TimeEntryUser
        Start: string
        End: string option
        Duration: string
    }

    type private TimeEntriesResponse = {
        Data: TimeEntryResponse list
    }

    type private TaskHistoryItem = {
        Id: string
        [<JsonPropertyName("type")>]
        Type: int
        Date: string
        Field: string option
        User: TimeEntryUser
    }

    type private TaskResponse = {
        Id: string
        Name: string
        List: {| Id: string; Name: string |} option
    }

    type private TasksResponse = {
        Tasks: TaskResponse list
    }

    let private aestOffset = TimeSpan.FromHours(10.0)
    let private aestTimeZone = TimeZoneInfo.CreateCustomTimeZone("AEST", aestOffset, "Australian Eastern Standard Time", "AEST")

    let private getYesterdayAest () =
        let nowUtc = DateTimeOffset.UtcNow
        let nowAest = TimeZoneInfo.ConvertTime(nowUtc, aestTimeZone)
        let yesterdayAest = nowAest.Date.AddDays(-1.0)
        DateOnly.FromDateTime(yesterdayAest)

    let private toUnixMillis (dt: DateTimeOffset) =
        dt.ToUnixTimeMilliseconds()

    let private fromUnixMillis (ms: int64) =
        DateTimeOffset.FromUnixTimeMilliseconds(ms)

    type private TaskDetailList = {
        Id: string
        Name: string
    }

    type private TaskDetailResponse = {
        Id: string
        Name: string
        List: TaskDetailList option
    }

    let private createHttpClient (apiKey: string) =
        let client = new HttpClient()
        client.BaseAddress <- Uri("https://api.clickup.com/api/v2/")
        client.DefaultRequestHeaders.Add("Authorization", apiKey)
        client

    let private getTaskDetail (client: HttpClient) (taskId: string) : Async<Result<TaskDetailResponse, string>> =
        async {
            try
                let url = sprintf "task/%s" taskId
                log (sprintf "GET %s" url)
                let! response = client.GetAsync(url) |> Async.AwaitTask
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                log (sprintf "Response (%O): %s" response.StatusCode (if content.Length > 500 then content.Substring(0, 500) + "..." else content))

                if not response.IsSuccessStatusCode then
                    return Error (sprintf "Failed to fetch task %s" taskId)
                else
                    let task = JsonSerializer.Deserialize<TaskDetailResponse>(content, jsonOptions)
                    return Ok task
            with ex ->
                return Error ex.Message
        }

    let getTimeEntries (config: ClickUpConfig) (date: DateOnly) : Async<Result<TimeEntry list, string>> =
        async {
            use client = createHttpClient config.ApiKey

            let startOfDay = DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), aestOffset)
            let endOfDay = startOfDay.AddDays(1.0).AddMilliseconds(-1.0)

            let startMs = toUnixMillis startOfDay
            let endMs = toUnixMillis endOfDay

            let url = sprintf "team/%s/time_entries?start_date=%d&end_date=%d" config.WorkspaceId startMs endMs
            log (sprintf "GET %s" url)

            try
                let! response = client.GetAsync(url) |> Async.AwaitTask
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                log (sprintf "Response (%O): %s" response.StatusCode (if content.Length > 1000 then content.Substring(0, 1000) + "..." else content))

                if not response.IsSuccessStatusCode then
                    return Error (sprintf "ClickUp API error (%O): %s" response.StatusCode content)
                else
                    let parsed = JsonSerializer.Deserialize<TimeEntriesResponse>(content, jsonOptions)
                    log (sprintf "Parsed %d time entries" parsed.Data.Length)

                    let entries =
                        parsed.Data
                        |> List.map (fun e ->
                            let duration =
                                match Int64.TryParse(e.Duration) with
                                | true, ms -> TimeSpan.FromMilliseconds(float ms)
                                | false, _ -> TimeSpan.Zero

                            let start =
                                match Int64.TryParse(e.Start) with
                                | true, ms -> fromUnixMillis ms
                                | false, _ -> DateTimeOffset.MinValue

                            let endTime =
                                e.End |> Option.bind (fun s ->
                                    match Int64.TryParse(s) with
                                    | true, ms -> Some (fromUnixMillis ms)
                                    | false, _ -> None)

                            {
                                Id = e.Id
                                TaskId = e.Task |> Option.map (fun t -> t.Id)
                                TaskName = e.Task |> Option.map (fun t -> t.Name)
                                ListName = e.Task |> Option.bind (fun t -> t.List) |> Option.map (fun l -> l.Name)
                                UserId = string e.User.Id
                                UserName = e.User.Username
                                Duration = duration
                                Start = start
                                End = endTime
                            })

                    return Ok entries
            with ex ->
                return Error (sprintf "Failed to fetch time entries: %s" ex.Message)
        }

    let getUpdatedTasks (config: ClickUpConfig) (date: DateOnly) : Async<Result<TaskUpdate list, string>> =
        async {
            use client = createHttpClient config.ApiKey

            let startOfDay = DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), aestOffset)
            let endOfDay = startOfDay.AddDays(1.0).AddMilliseconds(-1.0)

            let startMs = toUnixMillis startOfDay
            let endMs = toUnixMillis endOfDay

            let url = sprintf "team/%s/task?date_updated_gt=%d&date_updated_lt=%d&include_closed=true&subtasks=true" config.WorkspaceId startMs endMs
            log (sprintf "GET %s" url)

            try
                let! response = client.GetAsync(url) |> Async.AwaitTask
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                log (sprintf "Response (%O): %s" response.StatusCode (if content.Length > 1000 then content.Substring(0, 1000) + "..." else content))

                if not response.IsSuccessStatusCode then
                    return Error (sprintf "ClickUp API error (%O): %s" response.StatusCode content)
                else
                    let parsed = JsonSerializer.Deserialize<TasksResponse>(content, jsonOptions)
                    log (sprintf "Parsed %d updated tasks" parsed.Tasks.Length)

                    let updates =
                        parsed.Tasks
                        |> List.map (fun t ->
                            {
                                TaskId = t.Id
                                TaskName = t.Name
                                ListName = t.List |> Option.map (fun l -> l.Name)
                                UserId = ""
                                UserName = "Unknown"
                                UpdatedAt = DateTimeOffset.UtcNow
                                ChangeType = "updated"
                            })

                    return Ok updates
            with ex ->
                return Error (sprintf "Failed to fetch updated tasks: %s" ex.Message)
        }

    let fetchDailyData (config: ClickUpConfig) (date: DateOnly option) : Async<Result<TimeEntry list * TaskUpdate list, string>> =
        async {
            let targetDate = date |> Option.defaultWith getYesterdayAest

            let! timeEntriesResult = getTimeEntries config targetDate
            let! updatedTasksResult = getUpdatedTasks config targetDate

            match timeEntriesResult, updatedTasksResult with
            | Ok entries, Ok updates ->
                use client = createHttpClient config.ApiKey

                let taskIdsNeedingDetails =
                    entries
                    |> List.choose (fun e ->
                        match e.TaskId, e.ListName with
                        | Some taskId, None -> Some taskId
                        | _ -> None)
                    |> List.distinct

                log (sprintf "Fetching details for %d tasks missing list info" taskIdsNeedingDetails.Length)

                let! taskDetails =
                    taskIdsNeedingDetails
                    |> List.map (getTaskDetail client)
                    |> Async.Parallel

                let taskListMap =
                    taskDetails
                    |> Array.choose (function
                        | Ok t -> t.List |> Option.map (fun l -> t.Id, l.Name)
                        | Error _ -> None)
                    |> Map.ofArray

                let enrichedEntries =
                    entries
                    |> List.map (fun e ->
                        match e.TaskId, e.ListName with
                        | Some taskId, None ->
                            { e with ListName = Map.tryFind taskId taskListMap }
                        | _ -> e)

                return Ok (enrichedEntries, updates)
            | Error e, _ -> return Error e
            | _, Error e -> return Error e
        }
