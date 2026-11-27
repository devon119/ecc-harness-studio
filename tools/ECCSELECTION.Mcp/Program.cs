using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using ECCSELECTION.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// MCP Stdio transport reserves stdout exclusively for JSON-RPC 2.0 messages.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<McpWorkspaceOptions>();
builder.Services.AddSingleton<WorkspaceGuard>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<QualityGateService>();
builder.Services.AddSingleton<CatalogExportService>();
builder.Services.AddSingleton<PagesSyncService>();
builder.Services.AddSingleton<SpecScaffolderService>();
builder.Services.AddSingleton<UiContractCheckerService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
