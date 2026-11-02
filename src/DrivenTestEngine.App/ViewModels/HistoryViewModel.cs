using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.Core.History;
using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Mvvm;
using DrivenTestEngine.Core.Tests;
using Microsoft.Win32;

namespace DrivenTestEngine.App.ViewModels;

/// <summary>Local archive of every run the server reported, with CSV export.</summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _testFilter = string.Empty;
    private string _totalText = "0 runs";
    private string _passRateText = "—";
    private string _lastRunText = "no runs yet";

    public HistoryViewModel(AppServices services)
    {
        _services = services;

        Filters.Add(new FilterOption(string.Empty, "All tests"));
        foreach (var definition in TestCatalog.All)
        {
            Filters.Add(new FilterOption(definition.Id, definition.DisplayName.Replace(" Test", string.Empty)));
        }

        Filters[0].IsSelected = true;

        SelectFilterCommand = new RelayCommand(SelectFilter);
        ExportCommand = new RelayCommand(Export);
        ClearCommand = new RelayCommand(Clear);
        RefreshCommand = new RelayCommand(Reload);

        services.History.Changed += (_, _) => UiDispatcher.Post(Reload);
        Reload();
    }

    public ObservableCollection<HistoryEntry> Rows { get; } = new();

    public ObservableCollection<FilterOption> Filters { get; } = new();

    public RelayCommand SelectFilterCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public string TotalText
    {
        get => _totalText;
        private set => SetProperty(ref _totalText, value);
    }

    public string PassRateText
    {
        get => _passRateText;
        private set => SetProperty(ref _passRateText, value);
    }

    public string LastRunText
    {
        get => _lastRunText;
        private set => SetProperty(ref _lastRunText, value);
    }

    public bool HasRows => Rows.Count > 0;

    private void SelectFilter(object? parameter)
    {
        if (parameter is not FilterOption option)
        {
            return;
        }

        foreach (var filter in Filters)
        {
            filter.IsSelected = ReferenceEquals(filter, option);
        }

        _testFilter = option.Key;
        Reload();
    }

    private void Reload()
    {
        var all = _services.History.Entries;
        var filtered = string.IsNullOrEmpty(_testFilter)
            ? all
            : all.Where(e => string.Equals(e.TestId, _testFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        Rows.Clear();
        foreach (var entry in filtered)
        {
            Rows.Add(entry);
        }

        OnPropertyChanged(nameof(HasRows));

        TotalText = filtered.Count + (filtered.Count == 1 ? " run" : " runs");

        var completed = filtered.Where(e => e.State.IsTerminal()).ToList();
        if (completed.Count == 0)
        {
            PassRateText = "—";
        }
        else
        {
            var passed = completed.Count(e => e.State == TestRunState.Passed);
            var rate = passed * 100.0 / completed.Count;
            PassRateText = rate.ToString("0", CultureInfo.CurrentCulture) + "% pass";
        }

        LastRunText = filtered.Count == 0
            ? "no runs yet"
            : "last " + filtered[0].StartedAt.ToLocalTime().ToString("MMM d · HH:mm", CultureInfo.CurrentCulture);
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export test history",
            FileName = "dte-history-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".csv",
            Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var csv = string.IsNullOrEmpty(_testFilter)
                ? _services.History.ToCsv()
                : BuildFilteredCsv();
            File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(true));
            TotalText = "exported " + Rows.Count + " rows";
        }
        catch (IOException ex)
        {
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string BuildFilteredCsv()
    {
        var builder = new StringBuilder();
        builder.AppendLine("startedAt,test,state,metric,unit,threshold,runId,serverInstance,message");
        foreach (var row in Rows)
        {
            builder.AppendLine(string.Join(',',
                row.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                row.TestName,
                row.State.ToDisplayText(),
                row.Metric?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                row.Unit ?? string.Empty,
                row.Threshold?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                row.RunId ?? string.Empty,
                row.ServerInstanceId ?? string.Empty,
                (row.Message ?? string.Empty).Replace(",", ";")));
        }

        return builder.ToString();
    }

    private void Clear()
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            "Delete the locally stored test history? Server-side records are untouched.",
            "Clear history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            _services.History.Clear();
            Reload();
        }
    }
}
