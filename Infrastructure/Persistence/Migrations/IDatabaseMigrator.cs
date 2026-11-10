using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Infrastructure.Persistence.Migrations;

/// <summary>
/// Contract for managing bidirectional SQLite database migrations with transactional safety and checksum verification.
/// </summary>
public interface IDatabaseMigrator
{
    /// <summary>
    /// Applies all pending forward migrations up to the latest available version.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<MigrationResult> MigrateToLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back applied migrations down to the specified target version (e.g. 0 to revert all).
    /// </summary>
    /// <param name="targetVersion">The target schema version to revert down to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<MigrationResult> RollbackAsync(int targetVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current schema version recorded in the database (0 if un-migrated).
    /// </summary>
    Task<int> GetCurrentVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the audit history of all applied migrations.
    /// </summary>
    Task<IReadOnlyList<MigrationHistoryEntry>> GetMigrationHistoryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Output record detailing the result of a migration execution.
/// </summary>
public record MigrationResult(
    bool Success,
    int PreviousVersion,
    int CurrentVersion,
    int MigrationsApplied,
    string Message);

/// <summary>
/// Audit record of an applied schema migration stored in the database.
/// </summary>
public record MigrationHistoryEntry(
    int Version,
    string Description,
    DateTime AppliedAtUtc,
    string Checksum,
    long ExecutionDurationMs);
