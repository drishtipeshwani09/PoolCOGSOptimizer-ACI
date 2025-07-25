using Kusto.Data;
using Kusto.Data.Net.Client;
using System.Text.RegularExpressions;
using System.Text;

namespace PoolCOGSOptimizer;

/// <summary>
/// Service class for executing Kusto queries and extracting query text from AI responses
/// </summary>
public static class KustoService
{
    private const int DEFAULT_ROW_LIMIT = 100;

    /// <summary>
    /// Executes a single Kusto query against the specified cluster and database
    /// </summary>
    /// <param name="query">The KQL query to execute</param>
    /// <param name="clusterUri">The Kusto cluster URI</param>
    /// <param name="database">The database name</param>
    /// <returns>Query results as formatted string</returns>
    public static async Task<string> ExecuteKustoQueryAsync(string query, string clusterUri, string database)
    {
        try
        {
            // Clean and validate the query before execution
            string cleanedQuery = CleanAndValidateQuery(query);
            if (string.IsNullOrWhiteSpace(cleanedQuery))
            {
                return "Error executing Kusto query: Query is empty or invalid after cleaning";
            }

            // Create Kusto connection string builder
            var kcsb = new KustoConnectionStringBuilder(clusterUri, database)
                .WithAadUserPromptAuthentication();

            // Create query provider and execute query
            using var queryProvider = KustoClientFactory.CreateCslQueryProvider(kcsb);
            using var reader = await queryProvider.ExecuteQueryAsync(database, cleanedQuery, null);
            
            // Use StringBuilder for efficient string building
            var resultBuilder = new StringBuilder();
            
            // Build header row
            var headers = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                headers[i] = reader.GetName(i);
            }
            resultBuilder.AppendLine(string.Join("\t", headers));
            
            // Process data rows with single pass
            int rowCount = 0;
            var rowData = new string[reader.FieldCount];
            
            while (reader.Read() && rowCount < DEFAULT_ROW_LIMIT)
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    rowData[i] = reader.GetValue(i)?.ToString() ?? "null";
                }
                resultBuilder.AppendLine(string.Join("\t", rowData));
                rowCount++;
            }
            
            if (rowCount == DEFAULT_ROW_LIMIT)
            {
                resultBuilder.AppendLine("... (results truncated to first 100 rows)");
            }
            
            return resultBuilder.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error executing Kusto query: {ex.Message}";
        }
    }

    /// <summary>
    /// Cleans and validates a KQL query string
    /// </summary>
    /// <param name="query">Raw query string</param>
    /// <returns>Cleaned and validated query string</returns>
    private static string CleanAndValidateQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";

        // Remove common markdown artifacts
        query = query.Replace("```kql", "").Replace("```kusto", "").Replace("```", "");
        
        // Split into lines and clean each line
        var lines = query.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(line => line.Trim())
                         .Where(line => !string.IsNullOrWhiteSpace(line))
                         .Where(line => !line.StartsWith("//") && !line.StartsWith("#")) // Remove comments
                         .ToList();

        if (lines.Count == 0)
            return "";

        // Join lines back together
        string cleanedQuery = string.Join("\n", lines);

        // Basic validation: must contain table name and have valid KQL structure
        if (!cleanedQuery.Contains("LogExecutionClusterInfo"))
            return "";

        // Must have at least one pipe operator or be a simple table reference
        if (!cleanedQuery.Contains("|") && !cleanedQuery.Trim().Equals("LogExecutionClusterInfo"))
            return "";

        return cleanedQuery;
    }


    /// <summary>
    /// Extracts multiple Kusto queries from an AI agent response
    /// </summary>
    /// <param name="response">The AI agent response containing multiple KQL queries</param>
    /// <returns>List of extracted KQL query strings</returns>
    public static List<string> ExtractMultipleKustoQueries(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new List<string>();

        var queries = new List<string>();
        
        try
        {
            // Primary method: Extract from code blocks using regex
            var codeBlockMatches = Regex.Matches(response, @"```(?:kql|kusto)?\s*(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            foreach (Match match in codeBlockMatches)
            {
                string cleanedQuery = CleanAndValidateQuery(match.Groups[1].Value.Trim());
                if (!string.IsNullOrWhiteSpace(cleanedQuery))
                {
                    queries.Add(cleanedQuery);
                }
            }

            // Fallback method: Line-by-line extraction if no code blocks found
            if (queries.Count == 0)
            {
                queries.AddRange(ExtractQueriesLineByLine(response));
            }

            // Remove duplicates and return
            return queries.Distinct().ToList();
        }
        catch (Exception)
        {
            // Return empty list on any extraction error
            return new List<string>();
        }
    }

    /// <summary>
    /// Fallback method to extract queries line by line
    /// </summary>
    /// <param name="response">The AI agent response</param>
    /// <returns>List of extracted queries</returns>
    private static List<string> ExtractQueriesLineByLine(string response)
    {
        var queries = new List<string>();
        var lines = response.Split('\n');
        var currentQuery = new List<string>();
        bool inQuery = false;
        
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            
            if (trimmed.StartsWith("LogExecutionClusterInfo"))
            {
                // Save previous query if exists
                if (currentQuery.Count > 0)
                {
                    string prevQuery = CleanAndValidateQuery(string.Join("\n", currentQuery));
                    if (!string.IsNullOrWhiteSpace(prevQuery))
                    {
                        queries.Add(prevQuery);
                    }
                    currentQuery.Clear();
                }
                
                inQuery = true;
                currentQuery.Add(trimmed);
            }
            else if (inQuery && IsValidKqlLine(trimmed))
            {
                currentQuery.Add(trimmed);
            }
            else if (inQuery)
            {
                // End current query
                if (currentQuery.Count > 0)
                {
                    string query = CleanAndValidateQuery(string.Join("\n", currentQuery));
                    if (!string.IsNullOrWhiteSpace(query))
                    {
                        queries.Add(query);
                    }
                    currentQuery.Clear();
                }
                inQuery = false;
            }
        }
        
        // Handle final query
        if (currentQuery.Count > 0)
        {
            string finalQuery = CleanAndValidateQuery(string.Join("\n", currentQuery));
            if (!string.IsNullOrWhiteSpace(finalQuery))
            {
                queries.Add(finalQuery);
            }
        }
        
        return queries;
    }

    /// <summary>
    /// Checks if a line is a valid KQL operator line
    /// </summary>
    /// <param name="line">The line to check</param>
    /// <returns>True if the line is a valid KQL operator</returns>
    private static bool IsValidKqlLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        // Valid KQL operators and keywords - using static readonly for performance
        return ValidKqlStarts.Any(start => line.StartsWith(start, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] ValidKqlStarts = {
        "|", "where", "summarize", "extend", "project", "order", "sort", 
        "take", "top", "limit", "join", "union", "let", "datatable"
    };
}