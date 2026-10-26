# SupportDesk AI

A .NET 10 support-ticket application with a controller API and a separate stdio
MCP host over the same Core service and SQLite database. Currently implemented:
create, retrieve, and list Tickets, plus dashboard counts. Later tickets add
updates and AI operations. No AI credentials are needed for the current operations.

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
```

The hosts reject missing or relative database paths and in-memory databases.
`.env.example` contains placeholders; .NET does not automatically load `.env`
files. Database files and local `.env` files are ignored by Git.

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

In a terminal with the same database environment variable:

```bash
dotnet run --project src/SupportDesk.Mcp --no-build
```

The host waits for an MCP client over stdin/stdout. Logs go to stderr. It exposes
four tools at this stage: `create_ticket(title, description)`,
`get_ticket(id)`, `list_tickets()`, and `get_dashboard_summary()`, with typed inputs, output schemas, and structured Ticket results.
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
        "ConnectionStrings__SupportDesk": "Data Source=/absolute/path/to/SupportDesk/supportdesk.db"
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
