# Dapr .NET SDK - Microsoft Agent Framework Demo

## Prerequisites

- [.NET 8+](https://dotnet.microsoft.com/download) installed
- [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/)
- [Initialized Dapr environment](https://docs.dapr.io/getting-started/installation)
- [Dapr .NET SDK](https://docs.dapr.io/developing-applications/sdks/dotnet/)
- [Ollama](https://ollama.com/) installed

## Running the example
To run the sample locally, run this command in the `\examples\` directory to load the Dapr runtime:

```sh
dapr run --app-id wfapp --dapr-grpc-port 50001 --dapr-http-port 3500 --resources-path "Components/"
```

This will load the Conversation and State components into the Dapr runtime used by the Conversation and Workflow
components of the demonstration.

Next, run the application in another terminal window with `dotnet run`. It's currently configured to use port 5041 as
indicated above. It should start up and idle until you prompt it.

### Sending a legitimate email draft

Using a tool that can submit HTTP requests, send a POST request to `http://localhost:5041/draft` with the 
following body:

```json
{
    "body": "To whom it might concern, We need to talk about the upcoming conference. Would you mind getting back to me with your availability to speak next week? Thanks!"
}
```

This represents a draft email could be legitimately sent and shouldn't be considered spam. This will return a response
containing an `instanceId`.

Looking through your application logs, you should be able to observe the workflow eventually producing a response 
similar to the following:

> info: Dapr.Workflow.Worker.Internal.WorkflowOrchestrationContext[1603774202]
>      The agent responded with '**Subject:** Availability for Conference Discussion - Next Week
> 
> Dear [Recipient's Name],
> 
> Thank you for reaching out regarding the upcoming conference. I'd be happy to discuss further next week-please let me know a time that works best for you, and I'll confirm my availability accordingly.
> 
> Looking forward to coordinating.
> 
> Best regards,
> [Your Full Name]
> [Your Position]
> [Your Contact Information, if needed]'

While it's running or even after it runs, you can retrieve the status of the workflow by sending a GET request 
to http://localhost:5041/status/{instanceId} and populate the `instanceId` with the value returned from the previous 
request.

This should return a response containing the current status of the workflow. If the workflow has completed it may
look similar to, but with more up-to-date timestamp values.

```json
{
    "exists": true,
    "isWorkflowRunning": false,
    "isWorkflowCompleted": true,
    "createdAt": "2026-01-01T10:54:12.0935393+00:00",
    "lastUpdatedAt": "2026-01-01T10:54:13.0259422+00:00",
    "runtimeStatus": 3,
    "failureDetails": null
}
```

### Sending a spam email

Using a tool that can submit HTTP requests, send a POST request to `http://localhost:5041/draft` with the
following body:

```json
{
  "body": "SPAMSPAM_LOTSOFSPAM____!!!!!"
}
```

This time the workflow should contain a value that more closely resembles the following in the application logs and will
not include a proposed email response:

> info: Dapr.Workflow.Worker.Internal.WorkflowOrchestrationContext[1735556626]
Spam was detected from the agent with the reason: 'The email contains excessive use of the word 'SPAM' in a repetitive and non-contextual manner ('SPAMSPAM_LOTSOFSPAM____'), followed by multiple exclamation marks ('!!!!!'), which are common indicators of spam. Additionally, the lack of a clear subject line, sender details, or meaningful content further suggests it is likely spam.'
