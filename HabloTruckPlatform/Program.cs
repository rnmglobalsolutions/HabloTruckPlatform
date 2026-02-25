using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
// using HabloTruckPlatform.Application.Config;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Infrastructure.Integrations.ManyChat;
using HabloTruckPlatform.Infrastructure.Storage;
using HabloTruckPlatform.Infrastructure.Stripe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
              .AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        var cfg = ctx.Configuration;

        // ---- Clock
        services.AddSingleton<IClock, SystemClock>();

        // ---- Policies
        var graceHours = int.Parse(cfg["GracePolicy__Hours"] ?? "72");
        var companyGraceDays = int.Parse(cfg["CompanyGracePolicy__Days"] ?? "7");
        services.AddSingleton(new GracePolicy(graceHours));
        services.AddSingleton(new CompanyGracePolicy(companyGraceDays));

        // ---- Table Storage (Azure Tables)
        var tableConn = cfg["TableStorageConnection"] ?? cfg["AzureWebJobsStorage"] ?? "UseDevelopmentStorage=true";
        services.AddSingleton(_ => new TableServiceClient(tableConn));

        // ---- Stores (Infrastructure implementations)
        services.AddSingleton<IUserStore, TableUserStore>();
        services.AddSingleton<IUserResolver, TableUserResolver>();
        services.AddSingleton<IGraceIndexStore, TableGraceIndexStore>();

        // TODO: later
        services.AddSingleton<IStripeEventStore, TableStripeEventStore>();
        services.AddSingleton<ICompanyStore, TableCompanyStore>();
        services.AddSingleton<IEntitlementStore, TableEntitlementStore>();
        services.AddSingleton<ISeatAssignmentStore, TableSeatAssignmentStore>();
        services.AddSingleton<IEntitlementExpiryIndexStore, TableEntitlementExpiryIndexStore>();
        services.AddSingleton<IInviteCodeStore, TableInviteCodeStore>();
        services.AddSingleton<IFailedActionStore, TableFailedActionStore>();

        // ---- UseCases
        services.AddSingleton<AccessOrchestrator>();
        services.AddSingleton<StripeSubscriptionHandler>();
        services.AddSingleton<CheckoutSessionHandler>();
        services.AddSingleton<GraceSweeperService>();
        services.AddSingleton<CompanyJoinHandler>();
        services.AddSingleton<EntitlementRecountService>();
        services.AddSingleton<FailedActionRetryService>();

        // Stripe configuration
        services.Configure<StripeOptions>(ctx.Configuration.GetSection("Stripe"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StripeOptions>>().Value);

        // ---- HttpClient for integrations (ManyChat later)
        services.AddHttpClient<IManyChatSync, ManyChatSyncClient>();
        services.Configure<ManyChatOptions>(ctx.Configuration.GetSection("ManyChat"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ManyChatOptions>>().Value);
    })
    .Build();

host.Run();