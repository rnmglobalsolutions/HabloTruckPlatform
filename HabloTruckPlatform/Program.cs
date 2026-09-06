using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Domain.Abstractions;
using HabloTruckPlatform.Security;
using HabloTruckPlatform.Infrastructure.Integrations.Email;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureLogging((ctx, logging) =>
    {
        logging.AddConfiguration(ctx.Configuration.GetSection("Logging"));

        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
        logging.AddFilter("Azure", LogLevel.Warning);

        // Explicitly allow Information logs for Application Insights provider.
        logging.AddFilter(
            "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider",
            LogLevel.Information);
    })
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
              .AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        var cfg = ctx.Configuration;

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Remove the default AI Warning-only rule after AI is registered.
        services.Configure<LoggerFilterOptions>(options =>
        {
            var aiRules = options.Rules
                .Where(rule => rule.ProviderName ==
                    "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider")
                .ToList();

            foreach (var rule in aiRules)
            {
                options.Rules.Remove(rule);
            }

            options.Rules.Add(new LoggerFilterRule(
                providerName: "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider",
                categoryName: null,
                logLevel: LogLevel.Information,
                filter: null));
        });

        // ---- Clock
        services.AddSingleton<IClock, SystemClock>();

        // ---- HTTP security
        services.AddOptions<HttpSecurityOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.HttpApiKey = configuration["HttpApiKey"] ?? string.Empty;
            });
        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();

        // ---- Policies
        var graceHours = int.Parse(cfg["GracePolicy__Hours"] ?? "72");
        var companyGraceDays = int.Parse(cfg["CompanyGracePolicy__Days"] ?? "7");
        services.AddSingleton(new GracePolicy(graceHours));
        services.AddSingleton(new CompanyGracePolicy(companyGraceDays));

        // ---- Table Storage
        var tableConn = cfg["TableStorageConnection"]
            ?? cfg["TableConnectionString"]
            ?? cfg["AzureWebJobsStorage"];

        if (string.IsNullOrWhiteSpace(tableConn))
            throw new InvalidOperationException(
                "Storage connection string is required. Configure TableStorageConnection, TableConnectionString, or AzureWebJobsStorage.");

        services.AddSingleton(_ => new TableServiceClient(tableConn));

        services.AddSingleton<ITableClientFactory, TableClientFactory>();
        services.AddSingleton<ITableRepository, TableRepository>();
        services.AddSingleton<StorageInitializer>();

        services.AddSingleton<IUserStore, TableUserStore>();
        services.AddSingleton<IUserResolver, TableUserResolver>();
        services.AddSingleton<IExternalIdentityStore, TableExternalIdentityStore>();
        services.AddSingleton(sp =>
        {
            var options = new ExternalAudienceOptions();
            cfg.GetSection("ExternalAudience").Bind(options);
            _ = options.GetNormalizedPreferredProvider();
            _ = options.GetNormalizedManyChatPreferredChannels();
            return options;
        });
        services.AddSingleton<IExternalAudiencePolicy, ExternalAudiencePolicy>();
        services.AddSingleton<IManyChatAudienceResolver, ManyChatAudienceResolver>();
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
        services.AddSingleton<IJobCheckpointStore, TableJobCheckpointStore>();
        services.AddSingleton<IStripeAdminClient, StripeAdminClient>();
        services.AddSingleton<IStripeSubscriptionGateway, StripeSubscriptionGateway>();
        services.AddSingleton<IStripeCheckoutService, StripeCheckoutService>();
        services.AddSingleton<IManyChatDispatchQueue>(_ => new AzureQueueManyChatDispatchQueue(tableConn));

        services.AddSingleton<AccessOrchestrator>();
        services.AddSingleton<GraceSweeperService>();
        services.AddSingleton<CompanyJoinHandler>();
        services.AddSingleton<EntitlementRecountService>();
        services.AddSingleton<FailedActionRetryService>();
        services.AddSingleton<EntitlementExpirySweeperService>();
        services.AddSingleton<StripeReconciliationService>();
        services.AddSingleton<StripeCheckoutHandler>();
        services.AddSingleton<StartFleetCheckoutUseCase>();
        services.AddSingleton<CompanyAdminInviteService>();
        services.AddSingleton<BillingRecoveryManyChatNotifier>();
        services.AddSingleton<CancelSubscriptionAtPeriodEndUseCase>();
        services.AddSingleton<ChangeSubscriptionPlanUseCase>();
        services.AddSingleton<UpdateCompanySeatQuantityUseCase>();
        services.AddSingleton<CreateStripePaymentMethodUpdateLinkUseCase>();
        services.AddSingleton<RetryStripeOpenInvoiceUseCase>();
        services.AddSingleton<SubscriptionReminderService>();
        services.AddSingleton<ManyChatDispatchQueueProcessorService>();

        services.AddSingleton<IStripeSubscriptionHandler, StripeSubscriptionHandler>();

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

        services.AddSingleton<Metrics>();
        services.AddSingleton<IAppMetrics>(sp => sp.GetRequiredService<Metrics>());

        services.Configure<ManyChatOptions>(ctx.Configuration.GetSection("ManyChat"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ManyChatOptions>>().Value);
        services.AddHttpClient<IManyChatSync, ManyChatSyncClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.Configure<AdminPaymentAlertEmailOptions>(ctx.Configuration.GetSection("AdminPaymentAlerts"));
        services.AddHttpClient<IAdminPaymentAlertNotifier, SendGridAdminPaymentAlertNotifier>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });
    })
    .Build();

using (var scope = host.Services.CreateScope())
{
    var init = scope.ServiceProvider.GetRequiredService<StorageInitializer>();
    await init.InitializeAsync();
}

await host.RunAsync();
