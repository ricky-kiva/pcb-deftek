using System;
using System.Threading.Tasks;
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

public class NavigationServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly NavigationService _navigationService;

    public NavigationServiceTests()
    {
        var services = new ServiceCollection();
        services.AddTransient<TestPageAViewModel>();
        services.AddTransient<TestPageBViewModel>();
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
    public void CurrentViewModel_Initially_IsNull()
    {
        _navigationService.CurrentViewModel.Should().BeNull();
        _navigationService.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void NavigateTo_GenericType_ResolvesInstanceAndUpdatesCurrentViewModel()
    {
        // Act
        _navigationService.NavigateTo<TestPageAViewModel>();

        // Assert
        _navigationService.CurrentViewModel.Should().BeOfType<TestPageAViewModel>();
        _navigationService.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void NavigateTo_Instance_UpdatesCurrentViewModelAndRaisesNavigatedEvent()
    {
        // Arrange
        var vm = new TestPageAViewModel();
        ViewModelBase? eventArg = null;
        _navigationService.Navigated += (sender, args) => eventArg = args;

        // Act
        _navigationService.NavigateTo(vm);

        // Assert
        _navigationService.CurrentViewModel.Should().BeSameAs(vm);
        eventArg.Should().BeSameAs(vm);
    }

    [Fact]
    public void NavigateTo_MultipleViews_EnablesGoBackAndRestoresPreviousViewModel()
    {
        // Arrange
        _navigationService.NavigateTo<TestPageAViewModel>();
        var firstVm = _navigationService.CurrentViewModel;

        // Act
        _navigationService.NavigateTo<TestPageBViewModel>();

        // Assert
        _navigationService.CurrentViewModel.Should().BeOfType<TestPageBViewModel>();
        _navigationService.CanGoBack.Should().BeTrue();

        // Act - GoBack
        _navigationService.GoBack();

        // Assert
        _navigationService.CurrentViewModel.Should().BeSameAs(firstVm);
        _navigationService.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public async Task NavigateToAsync_CallsLifecycleMethodsOnViewModels()
    {
        // Arrange
        var vmA = new TestPageAViewModel();
        var vmB = new TestPageBViewModel();

        // Act
        await _navigationService.NavigateToAsync(vmA);
        vmA.NavigatedToCalled.Should().BeTrue();
        vmA.NavigatedFromCalled.Should().BeFalse();

        await _navigationService.NavigateToAsync(vmB);
        vmA.NavigatedFromCalled.Should().BeTrue();
        vmB.NavigatedToCalled.Should().BeTrue();
    }

    [Fact]
    public void PageViewModels_HaveExpectedTitles()
    {
        var dashboard = new DashboardViewModel();
        var live = _serviceProvider.GetRequiredService<LiveDetectionViewModel>();
        var file = _serviceProvider.GetRequiredService<FileDetectionViewModel>();
        var history = _serviceProvider.GetRequiredService<HistoryViewModel>();

        dashboard.Title.Should().Be("Dashboard");
        live.Title.Should().Be("Live Detection");
        file.Title.Should().Be("File-Based Detection");
        history.Title.Should().Be("History");
    }

    private class TestPageAViewModel : ViewModelBase
    {
        public bool NavigatedToCalled { get; private set; }
        public bool NavigatedFromCalled { get; private set; }

        public override Task OnNavigatedToAsync()
        {
            NavigatedToCalled = true;
            return Task.CompletedTask;
        }

        public override Task OnNavigatedFromAsync()
        {
            NavigatedFromCalled = true;
            return Task.CompletedTask;
        }
    }

    private class TestPageBViewModel : ViewModelBase
    {
        public bool NavigatedToCalled { get; private set; }
        public bool NavigatedFromCalled { get; private set; }

        public override Task OnNavigatedToAsync()
        {
            NavigatedToCalled = true;
            return Task.CompletedTask;
        }

        public override Task OnNavigatedFromAsync()
        {
            NavigatedFromCalled = true;
            return Task.CompletedTask;
        }
    }
}
