using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace PoolCOGSOptimizer;

/// <summary>
/// Semantic Kernel plugin for Kusto data analysis with strict data validation
/// </summary>
public class KustoAnalysisPlugin
{
    private readonly Dictionary<string, string> _queryResults;

    public KustoAnalysisPlugin(Dictionary<string, string> queryResults)
    {
        _queryResults = queryResults;
    }

    [KernelFunction]
    [Description("Gets the actual Kusto query results for a specific query")]
    public string GetQueryResults(
        [Description("The query name (Query_1, Query_2, etc.)")] string queryName)
    {
        if (_queryResults.ContainsKey(queryName))
        {
            return _queryResults[queryName];
        }
        return "Query results not found for the specified query name.";
    }

    [KernelFunction]
    [Description("Extracts numeric values from Kusto query results")]
    public string ExtractNumericValues(
        [Description("The query results string")] string queryResults,
        [Description("The column name to extract values from")] string columnName)
    {
        if (string.IsNullOrEmpty(queryResults) || queryResults.StartsWith("Error:"))
        {
            return "No valid data available";
        }

        var lines = queryResults.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return "Insufficient data rows";

        // Find column index
        var headers = lines[0].Split('\t');
        int columnIndex = Array.IndexOf(headers, columnName);
        if (columnIndex == -1)
        {
            return $"Column '{columnName}' not found in results. Available columns: {string.Join(", ", headers)}";
        }

        // Extract values
        var values = new List<string>();
        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            if (lines[i].Contains("truncated")) continue; // Skip truncation message
            
            var cells = lines[i].Split('\t');
            if (cells.Length > columnIndex)
            {
                values.Add(cells[columnIndex]);
            }
        }

        return string.Join(", ", values);
    }

    [KernelFunction]
    [Description("Calculates statistics from numeric data in query results")]
    public string CalculateStatistics(
        [Description("The query results string")] string queryResults,
        [Description("The numeric column name")] string columnName)
    {
        var numericValues = ExtractNumericValues(queryResults, columnName);
        if (numericValues.StartsWith("No valid data") || numericValues.StartsWith("Column"))
        {
            return numericValues;
        }

        var values = numericValues.Split(", ")
            .Where(v => !string.IsNullOrWhiteSpace(v) && double.TryParse(v, out _))
            .Select(double.Parse)
            .ToList();

        if (!values.Any()) return "No numeric values found";

        var stats = new
        {
            ColumnName = columnName,
            Count = values.Count,
            Average = Math.Round(values.Average(), 2),
            Minimum = values.Min(),
            Maximum = values.Max(),
            Sum = Math.Round(values.Sum(), 2),
            Percentile95 = values.Count > 0 ? Math.Round(values.OrderBy(x => x).Skip((int)(values.Count * 0.95)).FirstOrDefault(), 2) : 0,
            RawValues = string.Join(", ", values.Take(10)) + (values.Count > 10 ? "..." : "")
        };

        return JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction]
    [Description("Gets a detailed summary of all available query results")]
    public string GetAvailableQueries()
    {
        if (!_queryResults.Any()) return "No query results available";

        var summary = new List<string>();
        foreach (var kvp in _queryResults)
        {
            var results = kvp.Value;
            var status = results.StartsWith("Error:") ? "FAILED" : "SUCCESS";
            
            if (status == "FAILED")
            {
                summary.Add($"{kvp.Key}: {status} - {results}");
            }
            else
            {
                var lines = results.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var dataRows = Math.Max(0, lines.Length - 1); // Subtract header
                var columns = lines.Length > 0 ? lines[0].Split('\t').Length : 0;
                
                summary.Add($"{kvp.Key}: {status} ({dataRows} rows, {columns} columns)");
                if (lines.Length > 0)
                {
                    summary.Add($"  Columns: {lines[0]}");
                }
                if (dataRows > 0 && lines.Length > 1)
                {
                    summary.Add($"  Sample: {lines[1]}");
                }
                if (dataRows == 0)
                {
                    summary.Add($"  WARNING: No data rows returned - query may have no matching data");
                }
            }
            summary.Add(""); // Add blank line for readability
        }

        return string.Join("\n", summary);
    }

    [KernelFunction]
    [Description("Validates if specific data exists in the query results")]
    public string ValidateDataExistence(
        [Description("The query name")] string queryName,
        [Description("Expected column names separated by comma")] string expectedColumns)
    {
        if (!_queryResults.ContainsKey(queryName))
        {
            return $"Query {queryName} not found";
        }

        var results = _queryResults[queryName];
        if (results.StartsWith("Error:"))
        {
            return $"Query {queryName} failed: {results}";
        }

        var lines = results.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 1) return "No data available";

        var actualColumns = lines[0].Split('\t');
        var expectedCols = expectedColumns.Split(',').Select(c => c.Trim()).ToArray();
        var missingColumns = expectedCols.Where(col => !actualColumns.Contains(col)).ToArray();

        var validation = new
        {
            QueryName = queryName,
            ExpectedColumns = expectedCols,
            ActualColumns = actualColumns,
            MissingColumns = missingColumns,
            DataRows = lines.Length - 1,
            ValidationPassed = !missingColumns.Any() && lines.Length > 1
        };

        return JsonSerializer.Serialize(validation, new JsonSerializerOptions { WriteIndented = true });
    }
}