using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Models;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Date, defect-class, and severity filter state for the dashboard.
/// Raises one <see cref="FiltersChanged"/> notification per user-visible change.
/// </summary>
public sealed partial class DashboardFilterViewModel : ObservableObject
{
    private bool _suppressNotifications;

    /// <summary>
    /// Raised whenever a preset, custom date, class, or severity selection changes.
    /// </summary>
    public event EventHandler? FiltersChanged;

    /// <summary>Gets the multi-select defect-class options.</summary>
    public ObservableCollection<FilterOption<DefectClass>> ClassOptions { get; }

    /// <summary>Gets the multi-select severity options.</summary>
    public ObservableCollection<FilterOption<DefectSeverity>> SeverityOptions { get; }

    [ObservableProperty]
    private DateRangePreset _selectedPreset;

    [ObservableProperty]
    private DateTime? _customStartDate;

    [ObservableProperty]
    private DateTime? _customEndDate;

    [ObservableProperty]
    private bool _isCustomRangeValid = true;

    [ObservableProperty]
    private string? _customRangeError;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardFilterViewModel"/> class.
    /// </summary>
    public DashboardFilterViewModel()
    {
        ClassOptions = new ObservableCollection<FilterOption<DefectClass>>(
            DefectKnowledgeBase.GetAll()
                .OrderBy(info => info.ClassId)
                .Select(info => new FilterOption<DefectClass>(
                    info.Class,
                    $"{info.DisplayName} ({info.Abbreviation})")));

        SeverityOptions = new ObservableCollection<FilterOption<DefectSeverity>>(
            Enum.GetValues<DefectSeverity>()
                .OrderByDescending(value => value)
                .Select(value => new FilterOption<DefectSeverity>(value, DefectSeverityLabels.Get(value))));

        foreach (var option in ClassOptions)
        {
            option.PropertyChanged += OnOptionPropertyChanged;
        }

        foreach (var option in SeverityOptions)
        {
            option.PropertyChanged += OnOptionPropertyChanged;
        }
    }

    /// <summary>Gets the selected defect classes. Empty means all classes.</summary>
    public IReadOnlyCollection<DefectClass> SelectedClasses =>
        ClassOptions.Where(option => option.IsSelected).Select(option => option.Value).ToArray();

    /// <summary>Gets the selected severities. Empty means all severities.</summary>
    public IReadOnlyCollection<DefectSeverity> SelectedSeverities =>
        SeverityOptions.Where(option => option.IsSelected).Select(option => option.Value).ToArray();

    /// <summary>
    /// Gets a value indicating whether the projected query has a date or defect restriction.
    /// Selecting all nine classes with no severity is equivalent to no class filter.
    /// </summary>
    public bool HasActiveFilters =>
        !TryCreateFilter(out DashboardFilter? filter) ||
        filter.StartDateUtc is not null ||
        filter.EndDateUtc is not null ||
        filter.HasDefectFilters;

    /// <summary>Gets the date chip label.</summary>
    public string DateRangeLabel => SelectedPreset switch
    {
        DateRangePreset.None => "All time",
        DateRangePreset.Day => "Today",
        DateRangePreset.Week => "This week",
        DateRangePreset.Month => "This month",
        DateRangePreset.Custom when CustomStartDate.HasValue && CustomEndDate.HasValue =>
            $"{CustomStartDate.Value.ToString("d", CultureInfo.CurrentCulture)} – {CustomEndDate.Value.ToString("d", CultureInfo.CurrentCulture)}",
        _ => "Custom range"
    };

    /// <summary>Gets the defect-class chip label.</summary>
    public string ClassFilterLabel
    {
        get
        {
            var selected = SelectedClasses;
            return selected.Count switch
            {
                0 => "All classes",
                1 => DefectKnowledgeBase.GetInfo(selected.First()).Abbreviation,
                _ => $"{selected.Count} classes"
            };
        }
    }

    /// <summary>Gets the severity chip label.</summary>
    public string SeverityFilterLabel
    {
        get
        {
            var selected = SelectedSeverities;
            return selected.Count switch
            {
                0 => "All severities",
                1 => DefectSeverityLabels.Get(selected.First()),
                _ => $"{selected.Count} severities"
            };
        }
    }

    /// <summary>
    /// Projects the current state into a repository filter.
    /// Returns false for an invalid or incomplete custom range; no query should run.
    /// </summary>
    public bool TryCreateFilter([NotNullWhen(true)] out DashboardFilter? filter)
    {
        if (SelectedPreset == DateRangePreset.Custom &&
            (!IsCustomRangeValid || !CustomStartDate.HasValue || !CustomEndDate.HasValue))
        {
            filter = null;
            return false;
        }

        DateRangeBounds bounds = DateRangePresetResolver.Resolve(
            SelectedPreset,
            CustomStartDate,
            CustomEndDate,
            DateTime.Today);

        filter = new DashboardFilter(
            bounds.StartUtc,
            bounds.EndUtc,
            SelectedClasses,
            SelectedSeverities);
        return true;
    }

    /// <summary>
    /// Resets every filter and raises one change notification.
    /// </summary>
    public void ClearAll()
    {
        _suppressNotifications = true;
        try
        {
            SelectedPreset = DateRangePreset.None;
            CustomStartDate = null;
            CustomEndDate = null;

            foreach (var option in ClassOptions)
            {
                option.IsSelected = false;
            }

            foreach (var option in SeverityOptions)
            {
                option.IsSelected = false;
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        UpdateCustomRangeValidation();
        NotifyFiltersChanged();
    }

    partial void OnSelectedPresetChanged(DateRangePreset value)
    {
        UpdateCustomRangeValidation();
        NotifyFiltersChanged();
    }

    partial void OnCustomStartDateChanged(DateTime? value)
    {
        UpdateCustomRangeValidation();
        NotifyFiltersChanged();
    }

    partial void OnCustomEndDateChanged(DateTime? value)
    {
        UpdateCustomRangeValidation();
        NotifyFiltersChanged();
    }

    private void OnOptionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterOption<DefectClass>.IsSelected) ||
            e.PropertyName == nameof(FilterOption<DefectSeverity>.IsSelected))
        {
            NotifyFiltersChanged();
        }
    }

    private void UpdateCustomRangeValidation()
    {
        IsCustomRangeValid = DateRangePresetResolver.TryValidateCustomRange(
            CustomStartDate,
            CustomEndDate,
            out string? error);
        CustomRangeError = error;
    }

    private void NotifyFiltersChanged()
    {
        if (_suppressNotifications)
        {
            return;
        }

        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(SelectedClasses));
        OnPropertyChanged(nameof(SelectedSeverities));
        OnPropertyChanged(nameof(DateRangeLabel));
        OnPropertyChanged(nameof(ClassFilterLabel));
        OnPropertyChanged(nameof(SeverityFilterLabel));
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }
}
