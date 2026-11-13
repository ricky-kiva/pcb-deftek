using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Available date-range presets for the history filter bar.
/// </summary>
public enum HistoryDatePreset
{
    None = 0,
    Day = 1,
    Week = 2,
    Month = 3,
    Custom = 4
}

/// <summary>
/// Selectable filter option used by multi-select status and defect-class filters.
/// </summary>
/// <typeparam name="T">Filtered value type.</typeparam>
public sealed partial class HistoryFilterOption<T> : ObservableObject
{
    /// <summary>Gets the value represented by this option.</summary>
    public T Value { get; }

    /// <summary>Gets the display label.</summary>
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryFilterOption{T}"/> class.
    /// </summary>
    public HistoryFilterOption(T value, string label)
    {
        Value = value;
        Label = label ?? throw new ArgumentNullException(nameof(label));
    }
}

/// <summary>
/// State and validation for the history filter bar. Raises <see cref="FiltersChanged"/>
/// on every user-visible change so the owning view model can reload from page one.
/// </summary>
public sealed partial class HistoryFilterViewModel : ObservableObject
{
    private bool _suppressNotifications;

    /// <summary>
    /// Raised whenever a preset, custom date, status, or class selection changes.
    /// </summary>
    public event EventHandler? FiltersChanged;

    /// <summary>Gets the multi-select status options.</summary>
    public ObservableCollection<HistoryFilterOption<InspectionStatus>> StatusOptions { get; }

    /// <summary>Gets the multi-select defect-class options.</summary>
    public ObservableCollection<HistoryFilterOption<DefectClass>> ClassOptions { get; }

    [ObservableProperty]
    private HistoryDatePreset _selectedPreset;

    [ObservableProperty]
    private DateTime? _customStartDate;

    [ObservableProperty]
    private DateTime? _customEndDate;

    [ObservableProperty]
    private bool _isCustomRangeValid = true;

    [ObservableProperty]
    private string? _customRangeError;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryFilterViewModel"/> class.
    /// </summary>
    public HistoryFilterViewModel()
    {
        StatusOptions = new ObservableCollection<HistoryFilterOption<InspectionStatus>>
        {
            new(InspectionStatus.Pass, "PASS"),
            new(InspectionStatus.Fail, "FAIL")
        };

        ClassOptions = new ObservableCollection<HistoryFilterOption<DefectClass>>(
            DefectKnowledgeBase.GetAll()
                .OrderBy(info => info.ClassId)
                .Select(info => new HistoryFilterOption<DefectClass>(
                    info.Class,
                    $"{info.DisplayName} ({info.Abbreviation})")));

        foreach (var option in StatusOptions)
        {
            option.PropertyChanged += OnOptionPropertyChanged;
        }

        foreach (var option in ClassOptions)
        {
            option.PropertyChanged += OnOptionPropertyChanged;
        }
    }

    /// <summary>
    /// Gets the selected statuses. Empty means both statuses.
    /// </summary>
    public IReadOnlyCollection<InspectionStatus> SelectedStatuses =>
        StatusOptions.Where(option => option.IsSelected).Select(option => option.Value).ToArray();

    /// <summary>
    /// Gets the selected defect classes. Empty means all classes.
    /// </summary>
    public IReadOnlyCollection<DefectClass> SelectedClasses =>
        ClassOptions.Where(option => option.IsSelected).Select(option => option.Value).ToArray();

    /// <summary>
    /// Gets a value indicating whether any filter is active.
    /// </summary>
    public bool HasActiveFilters =>
        SelectedPreset != HistoryDatePreset.None ||
        StatusOptions.Any(option => option.IsSelected) ||
        ClassOptions.Any(option => option.IsSelected);

    /// <summary>
    /// Resets every filter to its default value and raises a single change notification.
    /// </summary>
    public void ClearAll()
    {
        _suppressNotifications = true;
        try
        {
            SelectedPreset = HistoryDatePreset.None;
            CustomStartDate = null;
            CustomEndDate = null;

            foreach (var option in StatusOptions)
            {
                option.IsSelected = false;
            }

            foreach (var option in ClassOptions)
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

    /// <summary>
    /// Projects the current filter state into a repository query filter.
    /// Invalid custom ranges omit the date bounds.
    /// </summary>
    public InspectionQueryFilter ToQueryFilter(int pageNumber, int pageSize)
    {
        var (startUtc, endUtc) = ResolveDateBounds();

        return new InspectionQueryFilter
        {
            StartDateUtc = startUtc,
            EndDateUtc = endUtc,
            Statuses = SelectedStatuses,
            DefectClasses = SelectedClasses,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    partial void OnSelectedPresetChanged(HistoryDatePreset value)
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
        if (e.PropertyName == nameof(HistoryFilterOption<InspectionStatus>.IsSelected))
        {
            NotifyFiltersChanged();
        }
    }

    private void NotifyFiltersChanged()
    {
        if (_suppressNotifications)
        {
            return;
        }

        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCustomRangeValidation()
    {
        bool hasBoth = CustomStartDate.HasValue && CustomEndDate.HasValue;
        bool invalid = hasBoth && CustomStartDate!.Value.Date > CustomEndDate!.Value.Date;

        IsCustomRangeValid = !invalid;
        CustomRangeError = invalid ? "Start date must be on or before the end date." : null;
    }

    private (DateTime? StartUtc, DateTime? EndUtc) ResolveDateBounds()
    {
        if (SelectedPreset == HistoryDatePreset.None)
        {
            return (null, null);
        }

        if (SelectedPreset == HistoryDatePreset.Custom)
        {
            if (!IsCustomRangeValid || !CustomStartDate.HasValue || !CustomEndDate.HasValue)
            {
                return (null, null);
            }

            return (
                LocalStartOfDayUtc(CustomStartDate.Value),
                LocalEndOfDayUtc(CustomEndDate.Value));
        }

        var today = DateTime.Today;
        DateTime start;
        DateTime end;

        switch (SelectedPreset)
        {
            case HistoryDatePreset.Day:
                start = today;
                end = today;
                break;

            case HistoryDatePreset.Week:
                int firstDay = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                int offset = ((int)today.DayOfWeek - firstDay + 7) % 7;
                start = today.AddDays(-offset);
                end = start.AddDays(6);
                break;

            case HistoryDatePreset.Month:
                start = new DateTime(today.Year, today.Month, 1);
                end = start.AddMonths(1).AddDays(-1);
                break;

            default:
                return (null, null);
        }

        return (LocalStartOfDayUtc(start), LocalEndOfDayUtc(end));
    }

    private static DateTime LocalStartOfDayUtc(DateTime date) =>
        DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();

    private static DateTime LocalEndOfDayUtc(DateTime date) =>
        DateTime.SpecifyKind(date.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime();
}
