using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anitime.Api.Infrastructure;

/// <summary>
/// The optional clientRequestId on habit, reminder and note creates. A repeat with the same id returns the original
/// item with 200; a filtered unique index on (UserId, ClientRequestId) over live rows settles concurrent repeats.
/// </summary>
public static class ClientRequests
{
    public const int MaxLength = 64;

    public static string? Normalize(string? clientRequestId) =>
        string.IsNullOrWhiteSpace(clientRequestId) ? null : clientRequestId.Trim();

    /// <summary>
    /// Whether to look for an earlier create. Done before validation so a retry still gets the original even if its
    /// body would no longer pass (a reminder held offline past its time); an over-long id is left to the validator.
    /// </summary>
    public static bool CanLookUp(string? clientRequestId) => clientRequestId is { Length: <= MaxLength };

    /// <summary>A concurrent repeat inserted first.</summary>
    public static bool IsDuplicate(DbUpdateException exception, string? clientRequestId, string indexName) =>
        clientRequestId is not null &&
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        postgres.ConstraintName == indexName;
}
