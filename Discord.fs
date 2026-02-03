namespace CuReporter

open System.Net.Http
open System.Text
open Thoth.Json.Net
open CuReporter.Config

module Discord =

    let mutable debugMode = false

    let private log msg =
        if debugMode then printfn "[DEBUG] %s" msg

    type private WebhookPayload = {
        Content: string
        Username: string
    }

    let sendMessage (config: DiscordConfig) (message: string) : Async<Result<unit, string>> =
        async {
            use client = new HttpClient()

            let payload = { Content = message; Username = "ClickUp Reporter" }
            let json = Encode.Auto.toString(0, payload, caseStrategy = CamelCase)

            log (sprintf "POST %s" config.WebhookUrl)
            log (sprintf "Payload: %s" json)

            use content = new StringContent(json, Encoding.UTF8, "application/json")

            try
                let! response = client.PostAsync(config.WebhookUrl, content) |> Async.AwaitTask

                if response.IsSuccessStatusCode then
                    log "Response: OK"
                    return Ok ()
                else
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    log (sprintf "Response (%O): %s" response.StatusCode body)
                    return Error (sprintf "Discord webhook failed (%O): %s" response.StatusCode body)
            with ex ->
                return Error (sprintf "Failed to send Discord message: %s" ex.Message)
        }

    let sendMessages (config: DiscordConfig) (messages: string list) : Async<Result<unit, string>> =
        async {
            log (sprintf "Sending %d messages to Discord" messages.Length)

            let rec sendAll (msgs: string list) =
                async {
                    match msgs with
                    | [] -> return Ok ()
                    | msg :: rest ->
                        log (sprintf "Message length: %d chars" msg.Length)
                        let! result = sendMessage config msg
                        match result with
                        | Ok () ->
                            do! Async.Sleep 500
                            return! sendAll rest
                        | Error e -> return Error e
                }

            return! sendAll messages
        }
