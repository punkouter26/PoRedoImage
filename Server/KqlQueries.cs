namespace Server;

/// <summary>
/// KQL (Kusto Query Language) queries for Application Insights analytics.
/// These queries can be run in the Azure Portal > Application Insights > Logs section.
/// </summary>
public static class KqlQueries
{
    /// <summary>
    /// Query 1: User Activity - Count active users and sessions over the last 7 days
    /// 
    /// This query helps answer:
    /// - How many unique users are actively using the image analysis feature?
    /// - What is the total session count (number of distinct analysis workflows)?
    /// - What is the trend of daily active users over the past week?
    /// 
    /// KQL Query:
    /// 
    /// // User Activity Report - Last 7 Days
    /// let startTime = ago(7d);
    /// let endTime = now();
    /// customEvents
    /// | where timestamp between (startTime .. endTime)
    /// | where name == "ImageAnalysisStarted"
    /// | extend UserId = tostring(customDimensions.UserId)
    /// | extend UserName = tostring(customDimensions.UserName)
    /// | extend SessionId = session_Id
    /// | summarize 
    ///     TotalSessions = dcount(SessionId),
    ///     UniqueUsers = dcount(UserId),
    ///     AnalysisCount = count()
    ///     by bin(timestamp, 1d)
    /// | project 
    ///     Date = format_datetime(timestamp, 'yyyy-MM-dd'),
    ///     UniqueUsers,
    ///     TotalSessions,
    ///     AnalysisCount,
    ///     AvgAnalysisPerUser = round(toreal(AnalysisCount) / toreal(UniqueUsers), 2)
    /// | order by Date desc
    /// 
    /// // Summary Totals
    /// customEvents
    /// | where timestamp between (startTime .. endTime)
    /// | where name == "ImageAnalysisStarted"
    /// | extend UserId = tostring(customDimensions.UserId)
    /// | summarize 
    ///     TotalUniqueUsers = dcount(UserId),
    ///     TotalSessions = dcount(session_Id),
    ///     TotalAnalyses = count()
    /// | project 
    ///     Period = "Last 7 Days",
    ///     TotalUniqueUsers,
    ///     TotalSessions,
    ///     TotalAnalyses,
    ///     AvgAnalysisPerUser = round(toreal(TotalAnalyses) / toreal(TotalUniqueUsers), 2)
    /// 
    /// </summary>
    public const string UserActivityQuery = @"
        // User Activity Report - Last 7 Days
        let startTime = ago(7d);
        let endTime = now();
        customEvents
        | where timestamp between (startTime .. endTime)
        | where name == 'ImageAnalysisStarted'
        | extend UserId = tostring(customDimensions.UserId)
        | extend UserName = tostring(customDimensions.UserName)
        | extend SessionId = session_Id
        | summarize 
            TotalSessions = dcount(SessionId),
            UniqueUsers = dcount(UserId),
            AnalysisCount = count()
            by bin(timestamp, 1d)
        | project 
            Date = format_datetime(timestamp, 'yyyy-MM-dd'),
            UniqueUsers,
            TotalSessions,
            AnalysisCount,
            AvgAnalysisPerUser = round(toreal(AnalysisCount) / toreal(UniqueUsers), 2)
        | order by Date desc
    ";

    /// <summary>
    /// Query 2: Performance Analysis - Top 10 Slowest Requests
    /// 
    /// This query helps identify:
    /// - Which image analysis requests are taking the longest to complete?
    /// - What are the bottlenecks in the processing pipeline (Computer Vision, OpenAI, DALL-E)?
    /// - Which operations need optimization?
    /// 
    /// KQL Query:
    /// 
    /// // Top 10 Slowest Image Analysis Requests
    /// requests
    /// | where timestamp > ago(24h)
    /// | where name == "ImageAnalysisWorkflow" or url contains "/api/imageanalysis/analyze"
    /// | extend TotalTimeMs = toreal(customDimensions.TotalTimeMs)
    /// | extend TagCount = toint(customDimensions.TagCount)
    /// | extend DescriptionTokensUsed = toint(customDimensions.DescriptionTokensUsed)
    /// | extend RegenerationTokensUsed = toint(customDimensions.RegenerationTokensUsed)
    /// | extend UserId = tostring(customDimensions.UserId)
    /// | extend FileName = tostring(customDimensions.FileName)
    /// | project 
    ///     Timestamp = format_datetime(timestamp, 'yyyy-MM-dd HH:mm:ss'),
    ///     TotalTimeMs = coalesce(TotalTimeMs, duration),
    ///     UserId,
    ///     FileName,
    ///     TagCount,
    ///     DescriptionTokensUsed,
    ///     RegenerationTokensUsed,
    ///     Success = success,
    ///     ResultCode = resultCode
    /// | top 10 by TotalTimeMs desc
    /// 
    /// // Performance Metrics Summary
    /// customMetrics
    /// | where timestamp > ago(24h)
    /// | where name in ("ImageAnalysisTotalTimeMs", "ComputerVisionProcessingTimeMs", 
    ///                  "OpenAIDescriptionProcessingTimeMs", "DALLEProcessingTimeMs")
    /// | summarize 
    ///     AvgTime = round(avg(value), 2),
    ///     MaxTime = round(max(value), 2),
    ///     MinTime = round(min(value), 2),
    ///     P95Time = round(percentile(value, 95), 2)
    ///     by name
    /// | project 
    ///     Metric = name,
    ///     AvgTimeMs = AvgTime,
    ///     P95TimeMs = P95Time,
    ///     MaxTimeMs = MaxTime,
    ///     MinTimeMs = MinTime
    /// | order by AvgTimeMs desc
    /// 
    /// </summary>
    public const string PerformanceQuery = @"
        // Top 10 Slowest Image Analysis Requests
        requests
        | where timestamp > ago(24h)
        | where name == 'ImageAnalysisWorkflow' or url contains '/api/imageanalysis/analyze'
        | extend TotalTimeMs = toreal(customDimensions.TotalTimeMs)
        | extend TagCount = toint(customDimensions.TagCount)
        | extend UserId = tostring(customDimensions.UserId)
        | extend FileName = tostring(customDimensions.FileName)
        | project 
            Timestamp = format_datetime(timestamp, 'yyyy-MM-dd HH:mm:ss'),
            TotalTimeMs = coalesce(TotalTimeMs, duration),
            UserId,
            FileName,
            TagCount,
            Success = success,
            ResultCode = resultCode
        | top 10 by TotalTimeMs desc
    ";

    /// <summary>
    /// Query 3: Error Rate Analysis - Calculate failed request percentage over 24 hours
    /// 
    /// This query helps monitor:
    /// - What percentage of image analysis requests are failing?
    /// - What are the most common error types?
    /// - What is the error trend over time?
    /// 
    /// KQL Query:
    /// 
    /// // Error Rate Calculation - Last 24 Hours
    /// let startTime = ago(24h);
    /// let endTime = now();
    /// requests
    /// | where timestamp between (startTime .. endTime)
    /// | where name == "ImageAnalysisWorkflow" or url contains "/api/imageanalysis/analyze"
    /// | extend IsSuccess = tobool(customDimensions.Success) or success
    /// | summarize 
    ///     TotalRequests = count(),
    ///     SuccessfulRequests = countif(IsSuccess == true),
    ///     FailedRequests = countif(IsSuccess == false or success == false)
    /// | extend 
    ///     SuccessRate = round(toreal(SuccessfulRequests) * 100.0 / toreal(TotalRequests), 2),
    ///     ErrorRate = round(toreal(FailedRequests) * 100.0 / toreal(TotalRequests), 2)
    /// | project 
    ///     Period = "Last 24 Hours",
    ///     TotalRequests,
    ///     SuccessfulRequests,
    ///     FailedRequests,
    ///     SuccessRate_Percent = strcat(SuccessRate, "%"),
    ///     ErrorRate_Percent = strcat(ErrorRate, "%")
    /// 
    /// // Error Breakdown by Type
    /// exceptions
    /// | where timestamp > ago(24h)
    /// | extend ErrorType = tostring(customDimensions.ErrorType)
    /// | where isnotempty(ErrorType)
    /// | summarize ErrorCount = count() by ErrorType
    /// | order by ErrorCount desc
    /// 
    /// // Error Trend by Hour
    /// requests
    /// | where timestamp > ago(24h)
    /// | where name == "ImageAnalysisWorkflow" or url contains "/api/imageanalysis/analyze"
    /// | extend IsSuccess = tobool(customDimensions.Success) or success
    /// | summarize 
    ///     TotalRequests = count(),
    ///     FailedRequests = countif(IsSuccess == false or success == false)
    ///     by bin(timestamp, 1h)
    /// | extend ErrorRate = round(toreal(FailedRequests) * 100.0 / toreal(TotalRequests), 2)
    /// | project 
    ///     Hour = format_datetime(timestamp, 'yyyy-MM-dd HH:00'),
    ///     TotalRequests,
    ///     FailedRequests,
    ///     ErrorRate_Percent = strcat(ErrorRate, "%")
    /// | order by Hour desc
    /// 
    /// </summary>
    public const string ErrorRateQuery = @"
        // Error Rate Calculation - Last 24 Hours
        let startTime = ago(24h);
        let endTime = now();
        requests
        | where timestamp between (startTime .. endTime)
        | where name == 'ImageAnalysisWorkflow' or url contains '/api/imageanalysis/analyze'
        | extend IsSuccess = tobool(customDimensions.Success) or success
        | summarize 
            TotalRequests = count(),
            SuccessfulRequests = countif(IsSuccess == true),
            FailedRequests = countif(IsSuccess == false or success == false)
        | extend 
            SuccessRate = round(toreal(SuccessfulRequests) * 100.0 / toreal(TotalRequests), 2),
            ErrorRate = round(toreal(FailedRequests) * 100.0 / toreal(TotalRequests), 2)
        | project 
            Period = 'Last 24 Hours',
            TotalRequests,
            SuccessfulRequests,
            FailedRequests,
            SuccessRate_Percent = strcat(SuccessRate, '%'),
            ErrorRate_Percent = strcat(ErrorRate, '%')
    ";

    /// <summary>
    /// Bonus Query 4: Token Usage and Cost Analysis
    /// 
    /// This query helps track:
    /// - How many OpenAI tokens are being consumed?
    /// - What is the cost trend over time?
    /// - Which operations (description vs image generation) consume more tokens?
    /// 
    /// KQL Query:
    /// 
    /// // Token Usage Analysis - Last 7 Days
    /// customMetrics
    /// | where timestamp > ago(7d)
    /// | where name in ("OpenAIDescriptionTokensUsed", "DALLETokensUsed")
    /// | summarize 
    ///     TotalTokens = sum(value),
    ///     AvgTokens = round(avg(value), 2),
    ///     RequestCount = count()
    ///     by name, bin(timestamp, 1d)
    /// | project 
    ///     Date = format_datetime(timestamp, 'yyyy-MM-dd'),
    ///     Operation = name,
    ///     TotalTokens,
    ///     AvgTokensPerRequest = AvgTokens,
    ///     RequestCount
    /// | order by Date desc, Operation asc
    /// 
    /// // Total Token Summary
    /// customMetrics
    /// | where timestamp > ago(7d)
    /// | where name in ("OpenAIDescriptionTokensUsed", "DALLETokensUsed")
    /// | summarize 
    ///     TotalTokens = sum(value),
    ///     TotalRequests = count()
    ///     by name
    /// | extend EstimatedCost_USD = round(TotalTokens / 1000.0 * 0.002, 4) // Rough estimate
    /// | project 
    ///     Operation = name,
    ///     TotalTokens,
    ///     TotalRequests,
    ///     AvgTokensPerRequest = round(TotalTokens / TotalRequests, 2),
    ///     EstimatedCost_USD
    /// 
    /// </summary>
    public const string TokenUsageQuery = @"
        // Token Usage Analysis - Last 7 Days
        customMetrics
        | where timestamp > ago(7d)
        | where name in ('OpenAIDescriptionTokensUsed', 'DALLETokensUsed')
        | summarize 
            TotalTokens = sum(value),
            AvgTokens = round(avg(value), 2),
            RequestCount = count()
            by name, bin(timestamp, 1d)
        | project 
            Date = format_datetime(timestamp, 'yyyy-MM-dd'),
            Operation = name,
            TotalTokens,
            AvgTokensPerRequest = AvgTokens,
            RequestCount
        | order by Date desc, Operation asc
    ";
}
