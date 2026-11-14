using System;
using System.Threading;
using FluentAssertions;
using pcb_deftek.Presentation.Services;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardProgressServiceTests
{
    [Fact]
    public void NewService_StartsIdleAtZero()
    {
        var sut = new DashboardProgressService();

        sut.Inference.Status.Should().Be(DashboardProgressStatus.Idle);
        sut.Inference.Percentage.Should().Be(0);
        sut.DataExport.Status.Should().Be(DashboardProgressStatus.Idle);
        sut.DataExport.Percentage.Should().Be(0);
    }

    [Fact]
    public void ReportInference_UpdatesStateAndRaisesPropertyChanged()
    {
        var sut = new DashboardProgressService();
        string? changed = null;
        sut.PropertyChanged += (_, args) => changed = args.PropertyName;

        sut.ReportInference(DashboardProgressStatus.Running, 42.5, "Detecting defects");

        sut.Inference.Status.Should().Be(DashboardProgressStatus.Running);
        sut.Inference.Percentage.Should().Be(42.5);
        sut.Inference.Message.Should().Be("Detecting defects");
        changed.Should().Be(nameof(DashboardProgressService.Inference));
    }

    [Fact]
    public void ReportDataExport_ClampsPercentageToValidRange()
    {
        var sut = new DashboardProgressService();

        sut.ReportDataExport(DashboardProgressStatus.Completed, 150, null);

        sut.DataExport.Status.Should().Be(DashboardProgressStatus.Completed);
        sut.DataExport.Percentage.Should().Be(100);
        sut.DataExport.Message.Should().Be("Completed");
    }

    [Fact]
    public void Reset_ReturnsBothOperationsToIdle()
    {
        var sut = new DashboardProgressService();
        sut.ReportInference(DashboardProgressStatus.Failed, 20, "Inference failed");
        sut.ReportDataExport(DashboardProgressStatus.Running, 60, "Exporting");

        sut.Reset();

        sut.Inference.Should().Be(DashboardProgressState.Idle);
        sut.DataExport.Should().Be(DashboardProgressState.Idle);
    }

    [Fact]
    public void Updates_AreDispatchedThroughTheCapturedUiContext()
    {
        var context = new RecordingSynchronizationContext();
        var sut = new DashboardProgressService(context);

        sut.ReportInference(DashboardProgressStatus.Running, 10, "Running");

        context.SendCount.Should().Be(1);
        sut.Inference.Percentage.Should().Be(10);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int SendCount { get; private set; }

        public override void Send(SendOrPostCallback d, object? state)
        {
            SendCount++;
            d(state);
        }
    }
}
