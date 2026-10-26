# SupportDesk AI

A .NET 10 support-ticket application with a controller API and a separate stdio
MCP host over the same Core service and SQLite database. Currently implemented:
create, retrieve, list, and update Tickets, status changes, and dashboard counts.
Analysis uses explicitly selected mock AI. Gemini and Draft Replies are pending.
No AI credentials are needed for the mock demonstration.

## Prerequisites and setup

Ubuntu 24.04 and .NET SDK 10.0.113.
Run these commands from the repository root:

```bash
dotnet restore
dotnet tool restore
dotnet build -m:1
```

Set this environment variable in **every terminal** that runs migrations or either
host. Choose an existing writable directory and use the **same absolute file
path** for both hosts:

```bash
export ConnectionStrings__SupportDesk="Data Source=$PWD/supportdesk.db"
export AI__Provider=Mock
```

The hosts reject missing or relative database paths and in-memory databases.
`.env.example` contains placeholders; .NET does not automatically load `.env`
files. Database files and local `.env` files are ignored by Git.

Set `AI__Provider=Mock` for each host to enable the deterministic mock provider.
Its Summary and SuggestedAction explicitly say Mock; it does not contact an AI
provider and does not infer real suggestions from Ticket text. Missing or
unsupported provider selection disables Analysis with a controlled provider error;
ordinary Ticket operations still work. There is no automatic fallback to Mock.
`AI__Model` and `AI__ApiKey` are reserved for the later Gemini implementation.

## Explicit migrations

Apply the committed migration before running either host:

```bash
dotnet ef database update --project src/SupportDesk.Core --startup-project src/SupportDesk.Api
```

The API is the migration startup project; migrations and the DbContext live in
Core. Neither host creates the schema or migrates on startup. Re-running the
command applies only outstanding migrations. Keep the database file to preserve
Tickets across restarts.

## REST and Swagger

```bash
dotnet run --project src/SupportDesk.Api --no-launch-profile -- --urls http://localhost:5050 --environment Development
```

Open Swagger at <http://localhost:5050/swagger>. Development enables Swagger;
exception responses remain sanitized in both Development and Production.

```bash
curl -i http://localhost:5050/api/tickets \
  -H 'Content-Type: application/json' \
  -d '{"title":"Unable to sign in","description":"Synthetic demo: password reset did not help."}'
curl -i http://localhost:5050/api/tickets/1
curl -i http://localhost:5050/api/tickets
curl -i http://localhost:5050/api/dashboard/summary
curl -i -X PUT http://localhost:5050/api/tickets/1 \
  -H 'Content-Type: application/json' \
  -d '{"title":"Unable to sign in","description":"Synthetic demo: still blocked.","priority":"High"}'
curl -i -X PATCH http://localhost:5050/api/tickets/1/status \
  -H 'Content-Type: application/json' -d '{"status":"Closed"}'
curl -i -X PATCH http://localhost:5050/api/tickets/1/status \
  -H 'Content-Type: application/json' -d '{"status":"Open"}'
curl -i -X POST http://localhost:5050/api/tickets/1/analyze
```

Creation returns HTTP 201, a Location header for the new Ticket, and the Ticket
DTO. Retrieval returns HTTP 200 or 404. Replace `1` with the returned ID. Title
and Description are trimmed, required, and limited to 200 and 5,000 characters
after trimming. Validation failures return HTTP 400 ProblemDetails with a useful
message. For example, a blank Title returns `Title is required.`; an unknown ID
returns `Ticket 2147483647 was not found.`. Malformed requests receive a generic
400 ProblemDetails; unexpected exceptions receive a generic 500 ProblemDetails.

New Tickets start with `status: "Open"`, `priority: "Medium"`, null Analysis
fields, and identical UTC `createdAt`/`updatedAt` values. Enum fields use named
strings. Create inputs contain only Title and Description; no enum inputs are
accepted by these operations. The enum converters reject numeric and unknown
values when deserializing typed enum contracts.

## MCP stdio

In a terminal with the same database and provider environment variables:

```bash
dotnet run --project src/SupportDesk.Mcp --no-build
```

The host waits for an MCP client over stdin/stdout. Logs go to stderr. It exposes
seven tools at this stage: `create_ticket(title, description)`,
`get_ticket(id)`, `list_tickets()`, `get_dashboard_summary()`,
`update_ticket(id, title, description, priority)`, and
`update_ticket_status(id, status)`, and `analyze_ticket(id)`, with typed inputs,
output schemas, and structured results.
Validation and unknown-ID failures set `isError: true` with controlled messages.
All tools use the same Core service and database as REST.

Example client configuration (replace both absolute paths for your checkout):

```json
{
  "mcpServers": {
    "supportdesk": {
      "command": "dotnet",
      "args": ["/absolute/path/to/SupportDesk/src/SupportDesk.Mcp/bin/Debug/net10.0/SupportDesk.Mcp.dll"],
      "env": {
        "ConnectionStrings__SupportDesk": "Data Source=/absolute/path/to/SupportDesk/supportdesk.db",
        "AI__Provider": "Mock"
      }
    }
  }
}
```

Use the compiled DLL so build output cannot interfere with the stdio protocol.
The configured absolute database path works regardless of the client's working
directory. In MCP Inspector, select stdio with the same command, DLL argument,
and environment variable. List tools, then call:

```json
{"name":"create_ticket","arguments":{"title":"MCP demo","description":"Synthetic support request"}}
```

Use the returned `structuredContent.id` for:

```json
{"name":"get_ticket","arguments":{"id":1}}
```

Edit or reopen the Ticket with:

```json
{"name":"update_ticket","arguments":{"id":1,"title":"Edited demo","description":"Synthetic update","priority":"Critical"}}
{"name":"update_ticket_status","arguments":{"id":1,"status":"Closed"}}
{"name":"update_ticket_status","arguments":{"id":1,"status":"Open"}}
{"name":"analyze_ticket","arguments":{"id":1}}
```

PUT/update_ticket require Title, Description, and Priority; they leave Status
unchanged. Changes to trimmed Title or Description clear Category, Sentiment,
AiSummary, and SuggestedAction while preserving the submitted Priority.
Priority-only edits and text unchanged after trimming preserve saved Analysis. Status changes require Open, InProgress, Resolved, or
Closed and work from every current status. Resolved and Closed Tickets remain
editable. Every successful update, including identical inputs or repeated status,
refreshes UpdatedAt; CreatedAt is preserved. Missing, numeric (including numeric
strings), and unknown Priority/Status inputs are rejected. Failed operations leave
saved data unchanged. Successful writes return the updated Ticket (HTTP 200);
unknown IDs return HTTP 404 or controlled MCP errors.

Analysis returns Category, Priority, Sentiment, Summary, and SuggestedAction as a
typed response (HTTP 200 or MCP structuredContent). For example, Mock returns:

```json
{"category":"Other","priority":"Medium","sentiment":"Neutral","summary":"Mock Analysis: synthetic demonstration summary.","suggestedAction":"Mock suggestion: review the Ticket and contact the customer manually."}
```

Core validates all suggestions before saving: named enum values must be valid,
Summary and SuggestedAction must be nonblank after trimming, with maximum lengths
of 1,000 and 2,000 characters. Numeric and unknown enum JSON values are rejected.
Only the latest valid Analysis is saved. It replaces Category and Priority,
including manually chosen Priority, and stores Sentiment, AiSummary, and
SuggestedAction. Every successful Analysis refreshes UpdatedAt, including repeated
identical results. Resolved and Closed Tickets remain eligible.

Unknown IDs return 404; provider failure or malformed results return safe 502
ProblemDetails; typed provider timeouts return 504. MCP returns controlled errors.
Failures preserve saved data and timestamps. To check a real provider error in
this mock slice, restart a host with `AI__Provider` unset and request Analysis;
it returns 502/an MCP error while get/list/update remain available. Mock itself
always returns valid suggestions. Malformed output and timeout scenarios are
covered through substituted IAiTicketService behavior in the Core tests.

Check a blank title and ID `2147483647` to see controlled errors. Retrieve the
created ID through REST, then restart both hosts and retrieve it again.

## Tests and manual checks

After building, run the automated tests:

```bash
dotnet test --no-build --no-restore -m:1
```

Use Swagger and the REST examples above to exercise the available operations.
Connect an MCP client using the sample configuration, list the available tools,
and retrieve a REST-created Ticket through MCP to confirm shared data. Restart
both hosts with the same database path and retrieve the Ticket again to check
persistence. Check blank input and an unknown ID for controlled errors.

## Known limitations

This is a local assessment application. Filters, pagination, delete operations,
authentication, remote MCP hosting, and a frontend are not implemented. SQLite
is a shared local file. Use the same absolute path for both hosts.
