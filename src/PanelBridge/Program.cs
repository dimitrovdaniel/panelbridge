using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PanelBridge.Panels;
using PanelBridge.Panels.Econ;
using PanelBridge.Panels.SortRefer;
using PanelBridge.Persistence;
using PanelBridge.Security;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.UseMiddleware<AuthMiddleware>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

var connectionString = builder.Configuration.GetConnectionString("PanelBridge")
    ?? throw new InvalidOperationException(
        "Missing connection string 'PanelBridge'. Set ConnectionStrings__PanelBridge in app settings.");

builder.Services.AddDbContext<PanelBridgeDbContext>(o => o.UseSqlServer(connectionString));

builder.Services
    .AddOptions<SortReferOptions>()
    .Bind(builder.Configuration.GetSection(SortReferOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<EconOptions>()
    .Bind(builder.Configuration.GetSection(EconOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<BridgeSecurityOptions>()
    .Bind(builder.Configuration.GetSection(BridgeSecurityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddHttpClient<SortReferClient>((sp, http) =>
    {
        var opts = sp.GetRequiredService<IOptions<SortReferOptions>>().Value;
        http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddStandardResilienceHandler();

builder.Services.AddSingleton<EconClient>();
builder.Services.AddScoped<IPanelClient>(sp => sp.GetRequiredService<SortReferClient>());
builder.Services.AddSingleton<IPanelClient>(sp => sp.GetRequiredService<EconClient>());
builder.Services.AddScoped<PanelClientRegistry>();

builder.Services.AddSingleton<PanelBridge.Functions.DocumentsFeature>();

builder.Build().Run();
