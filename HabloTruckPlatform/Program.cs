using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Infrastructure.Integrations.ManyChat;
using HabloTruckPlatform.Infrastructure.Storage;
using HabloTruckPlatform.Infrastructure.Storage.Factory;
using HabloTruckPlatform.Infrastructure.Storage.Stores;
using HabloTruckPlatform.Infrastructure.Stripe;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
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

        // ---- App Insights (needed if Metrics uses TelemetryClient)
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // ---- Policies
        var graceHours = int.Parse(cfg["GracePolicy__Hours"] ?? "72");
        var companyGraceDays = int.Parse(cfg["CompanyGracePolicy__Days"] ?? "7");
        services.AddSingleton(new GracePolicy(graceHours));
        services.AddSingleton(new CompanyGracePolicy(companyGraceDays));

        // ---- Table Storage (Azure Tables)
        var tableConn = cfg["TableStorageConnection"] ?? cfg["AzureWebJobsStorage"] ?? "UseDevelopmentStorage=true";
        services.AddSingleton(_ => new TableServiceClient(tableConn));

        services.AddSingleton<ITableClientFactory, TableClientFactory>();
        services.AddSingleton<ITableRepository, TableRepository>();
        services.AddSingleton<StorageInitializer>();

        // ---- Stores (Infrastructure implementations)
        services.AddSingleton<IUserStore, TableUserStore>();
        services.AddSingleton<IUserResolver, TableUserResolver>();
        services.AddSingleton<IGraceIndexStore, TableGraceIndexStore>();
        services.AddSingleton<IStripeEventStore, TableStripeEventStore>();
        services.AddSingleton<ICompanyStore, TableCompanyStore>();
        services.AddSingleton<IEntitlementStore, TableEntitlementStore>();
        services.AddSingleton<ISeatAssignmentStore, TableSeatAssignmentStore>();
        services.AddSingleton<IEntitlementExpiryIndexStore, TableEntitlementExpiryIndexStore>();
        services.AddSingleton<IInviteCodeStore, TableInviteCodeStore>();
        services.AddSingleton<IFailedActionStore, TableFailedActionStore>();
        services.AddSingleton<IStripeEventAuditStore, TableStripeEventAuditStore>();
        services.AddSingleton<ISubscriptionReminderStore, TableSubscriptionReminderStore>();
        services.AddSingleton<IStripeAdminClient, StripeAdminClient>();
        services.AddSingleton<IStripeSubscriptionGateway, StripeSubscriptionGateway>();
        services.AddSingleton<IStripeCheckoutService, StripeCheckoutService>();

        // ---- UseCases / Handlers (Application layer)
        services.AddSingleton<AccessOrchestrator>();
        services.AddSingleton<GraceSweeperService>();
        services.AddSingleton<CompanyJoinHandler>();
        services.AddSingleton<EntitlementRecountService>();
        services.AddSingleton<FailedActionRetryService>();
        services.AddSingleton<EntitlementExpirySweeperService>();
        services.AddSingleton<StripeReconciliationService>();
        services.AddSingleton<StripeCheckoutHandler>();
        services.AddSingleton<CancelSubscriptionAtPeriodEndUseCase>();
        services.AddSingleton<SubscriptionReminderService>();

        // Stripe orchestration handler
        services.AddSingleton<IStripeSubscriptionHandler, StripeSubscriptionHandler>();
        // Stripe configuration
        services.Configure<StripeOptions>(ctx.Configuration.GetSection("Stripe"));
        services.AddSingleton(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<StripeOptions>>().Value;
            opt.Validate();
            Stripe.StripeConfiguration.ApiKey = opt.StripeSecretKey;
            return opt;
        });

        services.AddSingleton<StripeSignatureValidator>();
        services.AddSingleton<StripeEventParser>();

        // ---- Telemetry
        services.AddSingleton<Metrics>();

        // ---- ManyChat
        services.Configure<ManyChatOptions>(ctx.Configuration.GetSection("ManyChat"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ManyChatOptions>>().Value);
        services.AddHttpClient<IManyChatSync, ManyChatSyncClient>();
    })
    .Build();

// Ensure tables on startup
using (var scope = host.Services.CreateScope())
{
    var init = scope.ServiceProvider.GetRequiredService<StorageInitializer>();
    await init.InitializeAsync();
}

await host.RunAsync();
