using System;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.Navigation;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class MainViewModelTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationService _navigationService;

    public MainViewModelTests()
    {
        var services = new ServiceCollection();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<LiveDetectionViewModel>();
        services.AddSingleton(new Mock<ICameraService>().Object);
        services.AddSingleton(new Mock<ILiveInspectionEngine>().Object);
        services.AddSingleton(new Mock<ILiveSnapshotService>().Object);
        services.AddSingleton(new Mock<IFileDiscoveryService>().Object);
        services.AddSingleton(new Mock<IFilePickerService>().Object);
        services.AddSingleton(new Mock<IBatchInspectionEngine>().Object);
        services.AddSingleton(new Mock<INotificationService>().Object);
        services.AddSingleton(new Mock<IUserSettingsService>().Object);
        services.AddSingleton(new Mock<IInspectionRepository>().Object);
        services.AddSingleton(new Mock<IHistoryThumbnailService>().Object);
        services.AddSingleton(new Mock<IViewportCropService>().Object);
        services.AddSingleton(new Mock<IInspectionDeletionService>().Object);
        services.AddSingleton(new Mock<IConfirmationService>().Object);
        services.AddTransient<FileDetectionViewModel>();
        services.AddTransient<InspectionViewerViewModel>();
        services.AddTransient<HistoryViewModel>();

        _serviceProvider = services.BuildServiceProvider();
        _navigationService = new NavigationService(_serviceProvider);
    }

    [Fact]
    public void Constructor_InitializesWithDefaultState()
    {
        var vm = new MainViewModel(_navigationService);

        vm.NavigationService.Should().BeSameAs(_navigationService);
        vm.IsSidebarExpanded.Should().BeTrue();
    }

    [Fact]
    public void ToggleSidebarCommand_TogglesIsSidebarExpanded()
    {
        var vm = new MainViewModel(_navigationService);

        vm.ToggleSidebarCommand.Execute(null);
        vm.IsSidebarExpanded.Should().BeFalse();

        vm.ToggleSidebarCommand.Execute(null);
        vm.IsSidebarExpanded.Should().BeTrue();
    }

    [Fact]
    public void NavigationCommands_NavigateToMatchingViewModels()
    {
        var vm = new MainViewModel(_navigationService);

        vm.NavigateToDashboardCommand.Execute(null);
        _navigationService.CurrentViewModel.Should().BeOfType<DashboardViewModel>();

        vm.NavigateToLiveDetectionCommand.Execute(null);
        _navigationService.CurrentViewModel.Should().BeOfType<LiveDetectionViewModel>();

        vm.NavigateToFileDetectionCommand.Execute(null);
        _navigationService.CurrentViewModel.Should().BeOfType<FileDetectionViewModel>();

        vm.NavigateToHistoryCommand.Execute(null);
        _navigationService.CurrentViewModel.Should().BeOfType<HistoryViewModel>();
    }

    [Fact]
    public void OpenExternalUrlCommand_WithValidUrl_ExecutesLauncher()
    {
        string? launchedUrl = null;
        var vm = new MainViewModel(_navigationService, url => launchedUrl = url);

        vm.OpenExternalUrlCommand.Execute("https://rickyslash.my.id");

        launchedUrl.Should().Be("https://rickyslash.my.id");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://insecure.url")]
    [InlineData("ftp://unsafe.url")]
    [InlineData("javascript:alert(1)")]
    public void OpenExternalUrlCommand_WithNonHttpsUrl_RejectsAndDoesNotLaunch(string? invalidUrl)
    {
        string? launchedUrl = null;
        var vm = new MainViewModel(_navigationService, url => launchedUrl = url);

        vm.OpenExternalUrlCommand.Execute(invalidUrl);

        launchedUrl.Should().BeNull();
    }
}
