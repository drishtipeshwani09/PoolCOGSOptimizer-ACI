using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace PoolCOGSOptimizer;

/// <summary>
/// Service class for managing AI agent interactions and prompt generation
/// </summary>
public static class AgentService
{
    /// <summary>
    /// Generates a comprehensive prompt for pool-level analysis to determine optimal cluster count
    /// </summary>
    /// <param name="tableName">The Kusto table name</param>
    /// <param name="columns">Available columns in the table</param>
    /// <param name="regionName">The region name for filtering</param>
    /// <param name="poolName">The pool name for filtering</param>
    /// <returns>Formatted prompt string for the AI agent</returns>
    public static string CreateMultiQueryPrompt(string tableName, string columns, string regionName, string poolName)
    {
        return $@"
You are a Kusto Query Language expert specializing in pool-level capacity optimization for determining optimal cluster counts.

OBJECTIVE: Design exactly 2 separate KQL queries to analyze pool {poolName} in region {regionName} and determine the optimal target cluster count for cost optimization.

TABLE: '{tableName}'
COLUMNS: [{columns}]

FILTER REQUIREMENTS FOR ALL QUERIES:
- Tenant contains '{regionName}' (region should be part of tenant name)
- poolId == '{poolName}' (exact match)  
- currentState == 'Ready'
- Make sure to use the 'PreciseTimeStamp' column for time-based filtering
- Use 'cpuLoad' and 'memoryLoad' for utilization metrics
- Use 'cpuCapacity' and 'memoryCapacity' for capacity metrics

REQUIRED QUERIES FOCUSED ON POOL-LEVEL OPTIMIZATION:

QUERY 1 - POOL CAPACITY AND UTILIZATION TRENDS:
Analyze pool-wide capacity utilization patterns over the last 15 days to understand demand
- Summarize the daily CPU and Memory Utilization Percentage for the whole pool.

QUERY 2 - CURRENT POOL SNAPSHOT:
Look for the last one hour of data to understand current state of the pool
- Calculate the current cluster count - Distinct count of clusters
- Calculate the current CPU and Memory Utilization Percentages for the whole pool

FORMAT REQUIREMENTS:
- Start each query with 'LogExecutionClusterInfo'
- Focus on pool-level aggregations, not individual clusters
- Use proper KQL syntax with pipe operators
- Include detailed comments explaining capacity planning logic
- Ensure queries provide data for cluster count optimization decisions

Return all 2 queries in separate code blocks with clear labels.
";
    }

    /// <summary>
    /// Creates a function-calling enabled analysis prompt that uses actual data
    /// </summary>
    /// <param name="poolName">The pool name being analyzed</param>
    /// <param name="regionName">The region name being analyzed</param>
    /// <returns>Formatted analysis prompt</returns>
    public static string CreateDataDrivenAnalysisPrompt(string poolName, string regionName, int maxClusterUsageThreshold)
    {
        return $@"
You are a cloud infrastructure capacity planning expert analyzing pool {poolName} in region {regionName} to determine the OPTIMAL TARGET CLUSTER COUNT for cost optimization.

CRITICAL INSTRUCTION: You MUST use the available functions to access and analyze the ACTUAL query results. Do NOT make assumptions about data values.

ANALYSIS WORKFLOW:
1. First, use GetAvailableQueries() to see what data is available
2. For each query, use ValidateDataExistence() to confirm the data structure
3. Use ExtractNumericValues() and CalculateStatistics() to get actual numbers
4. Base ALL your analysis and recommendations on the ACTUAL extracted data

PRIMARY OBJECTIVE: Determine the optimal target cluster count for pool {poolName} that:
- Maintains performance and availability
- Handles peak demand with appropriate safety margins  
- Minimizes cost through right-sizing

ANALYSIS STEPS (use functions for each):

1.DATA VALIDATION:
   - Check what query results are available
   - Validate that expected columns exist
   - Confirm data quality and completeness

2.EXTRACT ACTUAL METRICS:
   - Extract current cluster count from recent data
   - Extract CPU utilization values and calculate statistics
   - Extract memory utilization values and calculate statistics
   - Extract historical cluster count trends

3.CALCULATE OPTIMAL COUNT:
   - Use ACTUAL 95th percentile values from the data
   - Apply {maxClusterUsageThreshold}% target utilization to ACTUAL capacity data
   - Calculate difference between ACTUAL current count and optimal count

4.PROVIDE EVIDENCE-BASED RECOMMENDATION:
   **Base your recommendation on ACTUAL extracted data:**
   - **Current Cluster Count**: [Use actual data from queries]
   - **Recommended Target Count**: [Based on actual utilization statistics]
   - **Reduction Opportunity**: [Calculated from actual numbers]
   - **Expected Cost Savings**: [Based on actual cluster count difference]
   - **Implementation Risk**: [Based on actual utilization patterns]
   - **Safety Margin**: [Based on actual peak vs average data]

CRITICAL: All numbers in your response must come from the actual query results accessed through the functions. Do not estimate or assume any values.
";
    }
}