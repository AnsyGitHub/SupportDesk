using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SupportDesk.Core.Data;
using SupportDesk.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// Standard output is reserved for MCP protocol messages.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSupportDesk(builder.Configuration.GetConnectionString("SupportDesk"), builder.Configuration["AI:Provider"],
    builder.Configuration["AI:Model"], builder.Configuration["AI:ApiKey"]);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<Tools>(new JsonSerializerOptions(JsonSerializerOptions.Web));

await builder.Build().RunAsync();
