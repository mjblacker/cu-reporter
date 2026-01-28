namespace CuReporter

open System

module Types =

    type TimeEntry = {
        Id: string
        TaskId: string option
        TaskName: string option
        ListName: string option
        UserId: string
        UserName: string
        Duration: TimeSpan
        Start: DateTimeOffset
        End: DateTimeOffset option
    }

    type TaskUpdate = {
        TaskId: string
        TaskName: string
        ListName: string option
        UserId: string
        UserName: string
        UpdatedAt: DateTimeOffset
        ChangeType: string
    }

    type PersonSummary = {
        UserId: string
        UserName: string
        TimeEntries: TimeEntry list
        TaskUpdates: TaskUpdate list
        TotalTrackedTime: TimeSpan
    }

    type DailyReport = {
        Date: DateOnly
        Summaries: PersonSummary list
        TotalTrackedTime: TimeSpan
    }
