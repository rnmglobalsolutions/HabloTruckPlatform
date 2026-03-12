namespace HabloTruckPlatform.Domain.Tests.Security;

public sealed class HttpApiKeyProtectionCoverageTests
{
    [Fact]
    public void All_HttpTriggers_Except_StripeWebhook_Should_Use_ApiKeyGuard()
    {
        var root = FindRepositoryRoot();
        var functionsDir = Path.Combine(root, "HabloTruckPlatform", "Functions");

        var files = Directory.GetFiles(functionsDir, "*.cs", SearchOption.AllDirectories);

        var protectedHttpFiles = files
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Timer{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(Path.GetFileName(path), "StripeWebhookFunction.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("[HttpTrigger(", StringComparison.Ordinal));

        foreach (var file in protectedHttpFiles)
        {
            var code = File.ReadAllText(file);
            Assert.Contains("ApiKeyAuthorizationHelper.Authorize", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StripeWebhook_And_TimerFunctions_Should_Not_Use_ApiKeyGuard()
    {
        var root = FindRepositoryRoot();

        var stripeWebhookFile = Path.Combine(root, "HabloTruckPlatform", "Functions", "StripeWebhookFunction.cs");
        var stripeWebhookCode = File.ReadAllText(stripeWebhookFile);
        Assert.DoesNotContain("ApiKeyAuthorizationHelper.Authorize", stripeWebhookCode, StringComparison.Ordinal);

        var timerDir = Path.Combine(root, "HabloTruckPlatform", "Functions", "Timer");
        var timerFiles = Directory.GetFiles(timerDir, "*.cs", SearchOption.TopDirectoryOnly);

        foreach (var timerFile in timerFiles)
        {
            var code = File.ReadAllText(timerFile);
            Assert.DoesNotContain("ApiKeyAuthorizationHelper.Authorize", code, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var hasFunctionsProject = Directory.Exists(Path.Combine(current.FullName, "HabloTruckPlatform"));
            var hasTestsProject = Directory.Exists(Path.Combine(current.FullName, "HabloTruckPlatform.Tests"));

            if (hasFunctionsProject && hasTestsProject)
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found for coverage test.");
    }
}
