using System;
using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Infrastructure.Export;
using pcb_deftek.Presentation.Services;

namespace pcb_deftek.Tests;

internal sealed class StubDashboardExportService : IDashboardExportService
{
    public Task<int> CountMatchingInspectionsAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<DashboardExportResult> ExportAsync(
        DashboardFilter filter,
        IProgress<DashboardExportProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DashboardExportResult.NoMatches());
}

internal sealed class StubConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Delete",
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
