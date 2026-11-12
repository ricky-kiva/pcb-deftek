using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.Navigation;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class StartupRoutingServiceTests
{
    private readonly INavigationService _navigationService;
    private readonly InMemorySettingsRepository _settingsRepository;
    private readonly StartupRoutingService _router;

    public StartupRoutingServiceTests()
    {
        var services = new ServiceCollection();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<LiveDetectionViewModel>();
        services.AddTransient<FileDetectionViewModel>();
        services.AddTransient<HistoryViewModel>();

        var provider = services.BuildServiceProvider();
        _navigationService = new NavigationService(provider);
        _settingsRepository = new InMemorySettingsRepository();
        _router = new StartupRoutingService(_settingsRepository, _navigationService);
    }

    [Fact]
    public async Task RouteStartupAsync_WhenSkipDashboardIsTrue_RoutesToLiveDetection()
    {
        // Arrange
        await _settingsRepository.SetSettingAsync("Settings:App:SkipDashboardOnStartup", true);
        await _settingsRepository.SetSettingAsync("Settings:App:DefaultStartupPage", "Dashboard");

        // Act
        await _router.RouteStartupAsync();

        // Assert
        _navigationService.CurrentViewModel.Should().BeOfType<LiveDetectionViewModel>();
    }

    [Fact]
    public async Task RouteStartupAsync_WhenSkipDashboardIsFalse_AndDefaultPageIsNull_DefaultsToDashboard()
    {
        // Arrange
        await _settingsRepository.SetSettingAsync("Settings:App:SkipDashboardOnStartup", false);

        // Act
        await _router.RouteStartupAsync();

        // Assert
        _navigationService.CurrentViewModel.Should().BeOfType<DashboardViewModel>();
    }

    [Theory]
    [InlineData("History", typeof(HistoryViewModel))]
    [InlineData("File-Based Detection", typeof(FileDetectionViewModel))]
    [InlineData("Live Detection", typeof(LiveDetectionViewModel))]
    [InlineData("Dashboard", typeof(DashboardViewModel))]
    [InlineData("UnknownPage", typeof(DashboardViewModel))]
    public async Task RouteStartupAsync_WhenDefaultPageConfigured_RoutesToMatchingViewModel(string defaultPage, Type expectedType)
    {
        // Arrange
        await _settingsRepository.SetSettingAsync("Settings:App:SkipDashboardOnStartup", false);
        await _settingsRepository.SetSettingAsync("Settings:App:DefaultStartupPage", defaultPage);

        // Act
        await _router.RouteStartupAsync();

        // Assert
        _navigationService.CurrentViewModel.Should().BeOfType(expectedType);
    }

    private class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetSettingAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(key, out var val))
            {
                if (typeof(T) == typeof(bool) && bool.TryParse(val, out var b))
                {
                    return Task.FromResult<T?>((T)(object)b);
                }
                if (typeof(T) == typeof(string))
                {
                    return Task.FromResult<T?>((T)(object)val);
                }
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _store[key] = value?.ToString() ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(_store);

        public Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Remove(key));
    }
}
