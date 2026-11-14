using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using pcb_deftek.Presentation.Services;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardProgressServiceTests
{
    [Fact]
    public void NewService_StartsIdleAtZeroWithEmptyMessages()
    {
        var sut = new DashboardProgressService();

        sut.Inference.Status.Should().Be(DashboardProgressStatus.Idle);
        sut.Inference.Percentage.Should().Be(0);
        sut.Inference.Message.Should().BeEmpty();
        sut.DataExport.Status.Should().Be(DashboardProgressStatus.Idle);
        sut.DataExport.Percentage.Should().Be(0);
        sut.DataExport.Message.Should().BeEmpty();
        sut.HasActiveProcesses.Should().BeFalse();
    }

    [Fact]
    public void ReportInference_UpdatesStateAndRaisesPropertyChanged()
    {
        var sut = new DashboardProgressService();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        sut.ReportInference(DashboardProgressStatus.Running, 42.5, "Detecting defects");

        sut.Inference.Status.Should().Be(DashboardProgressStatus.Running);
        sut.Inference.Percentage.Should().Be(42.5);
        sut.Inference.Message.Should().Be("Detecting defects");
        changed.Should().Contain(nameof(DashboardProgressService.Inference));
    }

    [Fact]
    public void ReportDataExport_ClampsPercentageToValidRange()
    {
        var sut = new DashboardProgressService();

        sut.ReportDataExport(DashboardProgressStatus.Completed, 150, null);

        sut.DataExport.Status.Should().Be(DashboardProgressStatus.Completed);
        sut.DataExport.Percentage.Should().Be(100);
        sut.DataExport.Message.Should().BeEmpty();
    }

    [Fact]
    public void RunningProcess_RaisesHasActiveProcessesNotifications()
    {
        var sut = new DashboardProgressService();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        sut.ReportInference(DashboardProgressStatus.Running, 10, "Detecting");
        sut.HasActiveProcesses.Should().BeTrue();

        sut.ReportInference(DashboardProgressStatus.Completed, 100, null);
        sut.HasActiveProcesses.Should().BeFalse();

        changed.Should().Contain(nameof(IDashboardProgressService.HasActiveProcesses));
    }

    [Fact]
    public void Cancel_RunningProcess_CancelsTokenAndReportsCancelledWithAFreshToken()
    {
        var sut = new DashboardProgressService();
        CancellationToken token = sut.GetCancellationToken(DashboardProcess.Inference);
        sut.ReportInference(DashboardProgressStatus.Running, 40, "Detecting");

        sut.Cancel(DashboardProcess.Inference);

        sut.Inference.Status.Should().Be(DashboardProgressStatus.Cancelled);
        sut.Inference.Percentage.Should().Be(40);
        token.IsCancellationRequested.Should().BeTrue();
        sut.HasActiveProcesses.Should().BeFalse();
        sut.GetCancellationToken(DashboardProcess.Inference).IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void Cancel_IdleOrCompletedProcess_IsANoOp()
    {
        var sut = new DashboardProgressService();
        CancellationToken idleToken = sut.GetCancellationToken(DashboardProcess.DataExport);

        sut.Cancel(DashboardProcess.DataExport);

        sut.DataExport.Status.Should().Be(DashboardProgressStatus.Idle);
        idleToken.IsCancellationRequested.Should().BeFalse();

        sut.ReportDataExport(DashboardProgressStatus.Completed, 100, "Saved");
        CancellationToken completedToken = sut.GetCancellationToken(DashboardProcess.DataExport);

        sut.Cancel(DashboardProcess.DataExport);

        sut.DataExport.Status.Should().Be(DashboardProgressStatus.Completed);
        completedToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void CancelAll_CancelsEveryRunningProcess()
    {
        var sut = new DashboardProgressService();
        CancellationToken inference = sut.GetCancellationToken(DashboardProcess.Inference);
        CancellationToken export = sut.GetCancellationToken(DashboardProcess.DataExport);
        sut.ReportInference(DashboardProgressStatus.Running, 25, "Detecting");
        sut.ReportDataExport(DashboardProgressStatus.Running, 50, "Exporting");

        sut.CancelAll();

        sut.Inference.Status.Should().Be(DashboardProgressStatus.Cancelled);
        sut.DataExport.Status.Should().Be(DashboardProgressStatus.Cancelled);
        inference.IsCancellationRequested.Should().BeTrue();
        export.IsCancellationRequested.Should().BeTrue();
        sut.HasActiveProcesses.Should().BeFalse();
    }

    [Fact]
    public void TerminalReport_RenewsTheTokenForTheNextRun()
    {
        var sut = new DashboardProgressService();
        CancellationToken first = sut.GetCancellationToken(DashboardProcess.Inference);
        sut.ReportInference(DashboardProgressStatus.Running, 10, "Detecting");

        sut.ReportInference(DashboardProgressStatus.Completed, 100, "Done");
        CancellationToken second = sut.GetCancellationToken(DashboardProcess.Inference);

        first.IsCancellationRequested.Should().BeFalse();
        second.IsCancellationRequested.Should().BeFalse();
        second.Equals(first).Should().BeFalse();
    }

    [Fact]
    public void Reset_ReturnsBothOperationsToIdleAndReplacesTokens()
    {
        var sut = new DashboardProgressService();
        sut.ReportInference(DashboardProgressStatus.Failed, 20, "Inference failed");
        CancellationToken inference = sut.GetCancellationToken(DashboardProcess.Inference);
        sut.ReportDataExport(DashboardProgressStatus.Running, 60, "Exporting");
        CancellationToken export = sut.GetCancellationToken(DashboardProcess.DataExport);

        sut.Reset();

        sut.Inference.Should().Be(DashboardProgressState.Idle);
        sut.DataExport.Should().Be(DashboardProgressState.Idle);
        sut.HasActiveProcesses.Should().BeFalse();
        inference.IsCancellationRequested.Should().BeTrue();
        export.IsCancellationRequested.Should().BeTrue();
        sut.GetCancellationToken(DashboardProcess.Inference).IsCancellationRequested.Should().BeFalse();
        sut.GetCancellationToken(DashboardProcess.DataExport).IsCancellationRequested.Should().BeFalse();
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
