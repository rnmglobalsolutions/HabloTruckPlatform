using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Models;

public sealed record StripeUserScanPage(
    IReadOnlyList<User> Users,
    int StartBucket,
    int NextBucket,
    int BucketsScanned,
    bool Wrapped);
