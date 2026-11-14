using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Models;

namespace pcb_deftek.Presentation.ViewModels;

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
    public ObservableCollection<FilterOption<InspectionStatus>> StatusOptions { get; }

    /// <summary>Gets the multi-select defect-class options.</summary>
    public ObservableCollection<FilterOption<DefectClass>> ClassOptions { get; }

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
    /// Initializes a new instance of the <see cref="HistoryFilterViewModel"/> class.
    /// </summary>
    public HistoryFilterViewModel()
    {
        StatusOptions = new ObservableCollection<FilterOption<InspectionStatus>>
        {
            new(InspectionStatus.Pass, "PASS"),
            new(InspectionStatus.Fail, "FAIL")
        };

        ClassOptions = new ObservableCollection<FilterOption<DefectClass>>(
            DefectKnowledgeBase.GetAll()
                .OrderBy(info => info.ClassId)
                .Select(info => new FilterOption<DefectClass>(
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
        SelectedPreset != DateRangePreset.None ||
        StatusOptions.Any(option => option.IsSelected) ||
        ClassOptions.Any(option => option.IsSelected);

    /// <summary>
    /// Gets the status filter chip label.
    /// </summary>
    public string StatusFilterLabel
    {
        get
        {
            var selected = SelectedStatuses;
            return selected.Count switch
            {
                0 => "All statuses",
                1 => selected.First() == InspectionStatus.Pass ? "PASS" : "FAIL",
                _ => "PASS & FAIL"
            };
        }
    }

    /// <summary>
    /// Gets the defect-class filter chip label.
    /// </summary>
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

    /// <summary>
    /// Resets every filter to its default value and raises a single change notification.
    /// </summary>
    public void ClearAll()
    {
        _suppressNotifications = true;
        try
        {
            SelectedPreset = DateRangePreset.None;
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
        if (e.PropertyName == nameof(FilterOption<InspectionStatus>.IsSelected))
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

        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(SelectedStatuses));
        OnPropertyChanged(nameof(SelectedClasses));
        OnPropertyChanged(nameof(StatusFilterLabel));
        OnPropertyChanged(nameof(ClassFilterLabel));
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCustomRangeValidation()
    {
        IsCustomRangeValid = DateRangePresetResolver.TryValidateCustomRange(
            CustomStartDate,
            CustomEndDate,
            out string? error);
        CustomRangeError = error;
    }

    private (DateTime? StartUtc, DateTime? EndUtc) ResolveDateBounds()
    {
        DateRangeBounds bounds = DateRangePresetResolver.Resolve(
            SelectedPreset,
            CustomStartDate,
            CustomEndDate,
            DateTime.Today);
        return (bounds.StartUtc, bounds.EndUtc);
    }
}
