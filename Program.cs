// Import Packages
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using PoolCOGSOptimizer;

// Get user inputs
Console.WriteLine("Welcome to ACI Pool COGS Optimizer!");
Console.WriteLine("\nPlease provide the following information:");

Console.Write("Region Name: ");
TenantName regionName = Enum.TryParse(Console.ReadLine(), out TenantName parsedRegion) ? parsedRegion : TenantName.None;

Console.Write("Pool Name: ");
PoolName poolName = Enum.TryParse(Console.ReadLine(), out PoolName parsedPool) ? parsedPool : PoolName.None;

// Default thresholds for cluster usage is 85%
Console.WriteLine("Maximum Cluster Usage Percentage Threshold (0-100): ");
int maxClusterUsageThreshold = int.TryParse(Console.ReadLine(), out int parsedThreshold) && parsedThreshold >= 0 && parsedThreshold <= 100 ? parsedThreshold : 85;

Console.WriteLine("\n--- Collected Parameters ---");
Console.WriteLine($"Region Name: {regionName}");
Console.WriteLine($"Pool Name: {poolName}");
Console.WriteLine($"Maximum Cluster Usage Percentage Threshold: {maxClusterUsageThreshold}%");

if(poolName == PoolName.None)
{
    Console.WriteLine("Invalid pool or region name provided. Please ensure you enter valid names.");
    return;
}

// Configure OpenAI deployment with function calling enabled
var modelId = "o4-mini";
var endpoint = "https://aciaihackathon-resource.openai.azure.com/";
var apiKey = "";

// Create kernel with Azure OpenAI chat completion and function calling
var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);
builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

Kernel kernel = builder.Build();
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
var history = new ChatHistory();

Console.WriteLine("\n=== POOL-LEVEL CAPACITY ANALYSIS ===");
Console.WriteLine($"Analyzing pool {poolName} in region {regionName} to determine optimal cluster target count...");

// We will first start by designing the queries needed for pool-level analysis.
// Kusto connection parameters
string kustoClusterUri = "https://atlaslogscp.eastus.kusto.windows.net";
string kustoDatabase = "telemetry";

// Table and column configuration
string tableName = "LogExecutionClusterInfo";
string columns = "PreciseTimeStamp,Tenant,poolId,clusterId,cpuLoad,cpuCapacity,memoryLoad,memoryCapacity,appCapacity,currentState";

// Generate pool-focused multi-query prompt using AgentService
string agentQueryPrompt = AgentService.CreateMultiQueryPrompt(tableName, columns, regionName.ToString(), poolName.ToString());

// Get queries from AI agent
history.AddUserMessage(agentQueryPrompt);
var agentQueryResponse = await chatCompletionService.GetChatMessageContentAsync(history, kernel: kernel);
Console.WriteLine("\n=== AGENT-DESIGNED POOL ANALYSIS QUERIES ===");
Console.WriteLine(agentQueryResponse.Content);
history.AddAssistantMessage(agentQueryResponse.Content ?? string.Empty);

// Extract and execute multiple queries
var extractedQueries = KustoService.ExtractMultipleKustoQueries(agentQueryResponse.Content ?? "");

if (extractedQueries.Count > 0)
{
    Console.WriteLine($"\n=== EXECUTING {extractedQueries.Count} POOL ANALYSIS QUERIES ===");

    var queryResults = new Dictionary<string, string>();

    // Execute all queries
    for (int i = 0; i < extractedQueries.Count; i++)
    {
        string query = extractedQueries[i];
        string queryName = $"Query_{i + 1}";

        Console.WriteLine($"\nExecuting {queryName} (Pool Capacity Analysis):");
        try
        {
            string results = await KustoService.ExecuteKustoQueryAsync(query, kustoClusterUri, kustoDatabase);
            queryResults[queryName] = results;
            
            if (results.StartsWith("Error:"))
            {
                Console.WriteLine($"{queryName} failed: {results}");
            }
            else
            {
                Console.WriteLine($"{queryName} completed successfully");
            }
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error: {ex.Message}";
            Console.WriteLine($"{queryName} failed with exception: {errorMessage}");
            queryResults[queryName] = errorMessage;
        }
    }

    // Check that query results are not empty
    bool allQueriesValid = queryResults.Values.All(result => !string.IsNullOrWhiteSpace(result) && !result.StartsWith("Error:"));
    
    Console.WriteLine($"\n=== QUERY EXECUTION SUMMARY ===");
    Console.WriteLine($"Total queries executed: {queryResults.Count}");
    int successCount = queryResults.Values.Count(result => !result.StartsWith("Error:"));
    int errorCount = queryResults.Values.Count(result => result.StartsWith("Error:"));
    Console.WriteLine($"Successful queries: {successCount}");
    Console.WriteLine($"Failed queries: {errorCount}");
    
    if (allQueriesValid)
    {
        // Create the Kusto Analysis Plugin with actual query results
        Console.WriteLine("\n=== SETTING UP DATA ANALYSIS PLUGIN ===");
        var kustoPlugin = new KustoAnalysisPlugin(queryResults);
        kernel.Plugins.AddFromObject(kustoPlugin, "KustoAnalysis");

        // Enable function calling for data-driven analysis
        OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        // Perform data-driven analysis using function calls
        Console.WriteLine("\n=== DATA-DRIVEN CLUSTER COUNT ANALYSIS ===");
        Console.WriteLine("Using function calling to ensure analysis is based on actual query results...");

        string dataDrivenPrompt = AgentService.CreateDataDrivenAnalysisPrompt(poolName.ToString(), regionName.ToString(), maxClusterUsageThreshold);

        history.AddUserMessage(dataDrivenPrompt);
        var dataDrivenAnalysis = await chatCompletionService.GetChatMessageContentAsync(
            history,
            openAIPromptExecutionSettings,
            kernel);

        Console.WriteLine("\n=== DATA-VALIDATED CLUSTER COUNT RECOMMENDATIONS ===");
        Console.WriteLine(dataDrivenAnalysis.Content);
        Console.WriteLine();

        history.AddAssistantMessage(dataDrivenAnalysis.Content ?? string.Empty);
    }
    else
    {
        Console.WriteLine("\nNot all queries executed successfully. Analyzing errors...");
        foreach (var kvp in queryResults.Where(r => r.Value.StartsWith("Error:")))
        {
            Console.WriteLine($"{kvp.Key}: {kvp.Value}");
        }
    }
}
else
{
    Console.WriteLine("No valid queries were extracted from the agent's response.");
    Console.WriteLine("The agent response might need to be refined or the query extraction logic adjusted.");
    Console.WriteLine("\nAgent response preview:");
    string responseContent = agentQueryResponse.Content ?? "";
    Console.WriteLine(responseContent.Length > 500 ? responseContent.Substring(0, 500) + "..." : responseContent);
}