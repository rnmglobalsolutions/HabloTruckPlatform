using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Factory;

public static class TableQueries
{
    public static string PkEquals(string pk)
        => TableClient.CreateQueryFilter($"PartitionKey eq {pk}");

    public static string PkBeginsWith(string prefix)
        => TableClient.CreateQueryFilter($"PartitionKey ge {prefix} and PartitionKey lt {NextPrefix(prefix)}");

    public static string PkRkEquals(string pk, string rk)
        => TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RowKey eq {rk}");

    public static string RowKeyBeginsWith(string pk, string rkPrefix)
        => TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RowKey ge {rkPrefix} and RowKey lt {NextPrefix(rkPrefix)}");

    // Produces the smallest string strictly greater than all strings that start with prefix
    private static string NextPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return "\uFFFF";
        return prefix + "\uFFFF";
    }
}