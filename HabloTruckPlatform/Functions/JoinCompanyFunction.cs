using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Functions.Contracts;
using HabloTruckPlatform.Infrastructure.Telemetry;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class JoinCompanyFunction
{
    private readonly IUserStore _users;
    private readonly IInviteCodeStore _invites;
    private readonly CompanyJoinHandler _join;
    private readonly ILogger<JoinCompanyFunction> _logger;
    private readonly IApiKeyValidator _apiKeyValidator;

    public JoinCompanyFunction(
        IUserStore users,
        IInviteCodeStore invites,
        CompanyJoinHandler join,
        IApiKeyValidator apiKeyValidator,
        ILogger<JoinCompanyFunction>? logger = null)
    {
        _users = users;
        _invites = invites;
        _join = join;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger ?? NullLogger<JoinCompanyFunction>.Instance;
    }

    [Function("JoinCompany")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "company/join")] HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;

        var unauthorized = await ApiKeyAuthorizationHelper.AuthorizeAsync(
            req,
            _apiKeyValidator,
            _logger,
            ct);

        if (unauthorized is not null)
            return unauthorized;
        var invocationId = ctx.InvocationId;
        var correlationId = LogContext.ResolveCorrelationId(
            FirstHeader(req, "x-correlation-id", "x-request-id"),
            invocationId);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "company_join_http",
            correlationId: correlationId,
            invocationId: invocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName}",
            LogContext.Categories.Entry,
            "company_join_http");

        JoinCompanyHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<JoinCompanyHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct);
        }
        catch
        {
            return await OutcomeJson(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" }, LogContext.Outcomes.ValidationFailed, "invalid_json", opWatch.ElapsedMilliseconds);
        }

        var inviteCode = body?.InviteCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(inviteCode))
            return await OutcomeJson(req, HttpStatusCode.BadRequest, new { ok = false, error = "InviteCode required" }, LogContext.Outcomes.ValidationFailed, "invite_code_required", opWatch.ElapsedMilliseconds);

        using var inviteScope = LogContext.BeginOperationScope(
            _logger,
            operationName: "company_join_http",
            correlationId: correlationId,
            invocationId: invocationId,
            inviteCode: inviteCode);

        if (string.IsNullOrWhiteSpace(body?.ManyChatSubscriberId) && string.IsNullOrWhiteSpace(body?.Email))
            return await OutcomeJson(req, HttpStatusCode.BadRequest, new { ok = false, error = "ManyChatSubscriberId or Email required" }, LogContext.Outcomes.ValidationFailed, "identity_required", opWatch.ElapsedMilliseconds);

        // 1) Load invite.
        var inviteWatch = Stopwatch.StartNew();
        var invite = await _invites.GetAsync(inviteCode, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            LogContext.Categories.Persistence,
            "invite.get",
            "InviteCodes",
            inviteWatch.ElapsedMilliseconds,
            invite is not null);

        if (invite is null)
            return await OutcomeJson(req, HttpStatusCode.NotFound, new { ok = false, error = "Invite not found" }, LogContext.Outcomes.ValidationFailed, "invite_not_found", opWatch.ElapsedMilliseconds);

        // 2) Resolve/create user (subscriberId preferred).
        var userWatch = Stopwatch.StartNew();
        var user = await _users.GetOrCreateAsync(
            emailNormalized: body?.Email,
            manyChatSubscriberId: body?.ManyChatSubscriberId,
            phoneE164: body?.PhoneE164,
            ct: ct);

        _logger.LogDebug(
            "Persistence step completed. LogCategory={LogCategory} Step={Step} DurationMs={DurationMs} UserId={UserId}",
            LogContext.Categories.Step,
            "user_get_or_create",
            userWatch.ElapsedMilliseconds,
            user.UserId);

        // 3) Idempotency: already in this company -> do NOT consume invite.
        if (!string.IsNullOrWhiteSpace(user.CompanyId) &&
            string.Equals(user.CompanyId, invite.CompanyId, StringComparison.OrdinalIgnoreCase))
        {
            return await OutcomeJson(req, HttpStatusCode.OK, new
            {
                ok = true,
                alreadyJoined = true,
                companyId = invite.CompanyId,
                entitlementId = invite.EntitlementId
            }, LogContext.Outcomes.NoActionNeeded, "already_joined", opWatch.ElapsedMilliseconds);
        }

        // 4) If user is already in a different company, block.
        if (!string.IsNullOrWhiteSpace(user.CompanyId) &&
            !string.Equals(user.CompanyId, invite.CompanyId, StringComparison.OrdinalIgnoreCase))
        {
            return await OutcomeJson(req, HttpStatusCode.Conflict, new
            {
                ok = false,
                error = "UserAlreadyInAnotherCompany",
                currentCompanyId = user.CompanyId,
                requestedCompanyId = invite.CompanyId
            }, LogContext.Outcomes.Denied, "user_in_another_company", opWatch.ElapsedMilliseconds);
        }

        // 5) Consume invite use (only now).
        var consumeWatch = Stopwatch.StartNew();
        var consumed = await _invites.TryConsumeAsync(inviteCode, DateTimeOffset.UtcNow, ct);

        _logger.LogDebug(
            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Consumed={Consumed}",
            LogContext.Categories.Persistence,
            "invite.try_consume",
            "InviteCodes",
            consumeWatch.ElapsedMilliseconds,
            consumed);

        if (!consumed)
            return await OutcomeJson(req, HttpStatusCode.Conflict, new { ok = false, error = "Invite expired/disabled/exhausted" }, LogContext.Outcomes.Denied, "invite_not_consumed", opWatch.ElapsedMilliseconds);

        // 6) Join company.
        var userPk = Buckets.UserBucketPk(user.UserId);

        await _join.HandleJoinAsync(
            new JoinCompanyRequest(
                UserPk: userPk,
                UserId: user.UserId,
                CompanyId: invite.CompanyId,
                EntitlementId: invite.EntitlementId,
                InviteCode: invite.Code),
            ct);

        return await OutcomeJson(req, HttpStatusCode.OK, new
        {
            ok = true,
            alreadyJoined = false,
            companyId = invite.CompanyId,
            entitlementId = invite.EntitlementId
        }, LogContext.Outcomes.Completed, "company_join_applied", opWatch.ElapsedMilliseconds);
    }

    private async Task<HttpResponseData> OutcomeJson(
        HttpRequestData req,
        HttpStatusCode code,
        object obj,
        string outcome,
        string reason,
        long durationMs)
    {
        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} DurationMs={DurationMs}",
            LogContext.Categories.Outcome,
            outcome,
            reason,
            (int)code,
            durationMs);

        return await Json(req, code, obj);
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(obj, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return res;
    }

    private static string? FirstHeader(HttpRequestData req, params string[] names)
    {
        foreach (var name in names)
        {
            if (req.Headers.TryGetValues(name, out var values))
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return null;
    }
}


