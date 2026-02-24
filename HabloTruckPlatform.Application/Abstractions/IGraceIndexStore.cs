using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IGraceIndexStore
{
    /// <summary>
    /// Add or update an index row for grace expiration processing.
    /// </summary>
    Task UpsertAsync(UserRef userRef, DateTimeOffset graceEndsAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Remove grace index rows for a user (safe no-op if not present).
    /// </summary>
    Task DeleteForUserAsync(UserRef userRef, CancellationToken ct = default);

    /// <summary>
    /// Query all users whose grace has expired up to now for a given hour-bucket PK.
    /// </summary>
    Task<IReadOnlyList<GraceIndexItem>> QueryExpiredAsync(
        string gracePk, DateTimeOffset nowUtc, int take = 500, CancellationToken ct = default);

    /// <summary>
    /// // Ahora DeleteForUserAsync debe aceptar pk/rk.
    /// </summary>
    Task DeleteAsync(string pk, string rk, CancellationToken ct = default);
}