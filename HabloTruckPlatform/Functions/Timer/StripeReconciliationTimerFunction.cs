using HabloTruckPlatform.Application.UseCases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class StripeReconciliationTimerFunction
{
    private readonly StripeReconciliationService _service;
    private readonly ILogger<StripeReconciliationTimerFunction> _logger;

    public StripeReconciliationTimerFunction(
        StripeReconciliationService service,
        ILogger<StripeReconciliationTimerFunction> logger)
    {
        _service = service;
        _logger = logger;
    }

    [Function("StripeReconciliationTimer")]
    public async Task Run(
        [TimerTrigger("0 0 5 * * *")] TimerInfo timerInfo,
        FunctionContext context)
    {
        _logger.LogInformation("Stripe reconciliation timer started.");
        await _service.RunAsync(500, context.CancellationToken);
        _logger.LogInformation("Stripe reconciliation timer finished.");
    }
}