using System.Security.Cryptography;
using System.Text;

namespace HabloTruckPlatform.Domain.Ids;

public static class Buckets
{
    public const int UserBucketCount = 200;
    public const int LookupBucketCount = 50;

    // Stable hash (DO NOT use string.GetHashCode() because it varies per process/runtime)
    public static int StableHashMod(string value, int mod)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", nameof(value));
        if (mod <= 0) throw new ArgumentOutOfRangeException(nameof(mod));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        // use first 4 bytes as uint
        uint n = BitConverter.ToUInt32(bytes, 0);
        return (int)(n % (uint)mod);
    }

    public static string UserBucketPk(string userId)
    {
        var bucket = StableHashMod(userId, UserBucketCount);
        return $"{TablePrefixes.User}#{bucket:D3}";
    }

    public static string ManyChatLookupPk(string subscriberId)
    {
        var bucket = StableHashMod(subscriberId, LookupBucketCount);
        return $"{TablePrefixes.ManyChat}#{bucket:D2}";
    }

    public static string StripeCustomerLookupPk(string customerId)
    {
        var bucket = StableHashMod(customerId, LookupBucketCount);
        return $"{TablePrefixes.StripeCustomer}#{bucket:D2}";
    }

    public static string EmailLookupPk(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        var normalized = email.Trim().ToLowerInvariant();

        var prefix = normalized.Length >= 2 ? normalized[..2] : "xx";
        return $"{TablePrefixes.Email}#{prefix}";
    }

    public static string TimeIndexPk(DateTimeOffset utc, string format)
    {
        return $"{TablePrefixes.Grace}#{utc:yyyyMMddHH}";
    }

    public static string EntitlementExpiryPk(DateTimeOffset utcDate)
    {
        // Bucket por día (sweeper diario)
        // formato: HT#EE#yyyyMMdd
        return $"{TablePrefixes.EntitlementExpiry}#{utcDate:yyyyMMdd}";
    }

    public static string TimeIndexRk(long ticks, string id)
    {
        return $"{ticks:D19}_{id}";
    }

    public static string TimeIndexRk(long ticks, params string[] parts)
    {
        var suffix = string.Join("_", parts);
        return $"{ticks:D19}_{suffix}";
    }

    public static string EntitlementExpiryRk(long endTicks, string companyId, string entitlementId)
    {
        // Orden lexicográfico por tiempo
        // permite query eficiente y procesamiento en orden
        return $"{endTicks:D19}_{companyId}_{entitlementId}";
    }

    public static int GetUserBucket(string userId)
        => StableHashMod(userId, UserBucketCount);
}