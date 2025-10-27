# Application Insights Monitoring Guide
# PoRedoImage - Production Monitoring

## Quick Links

**Application Insights**: https://portal.azure.com/#@/resource/subscriptions/f0504e26-451a-4249-8fb3-46270defdd5b/resourceGroups/poredoimage-uksouth/providers/microsoft.insights/components/appi-PoRedoImage-cqevadpy77pvi/overview

**Application URL**: https://app-poredoimage-cqevadpy77pvi.azurewebsites.net

**Resource Group**: https://portal.azure.com/#@/resource/subscriptions/f0504e26-451a-4249-8fb3-46270defdd5b/resourceGroups/poredoimage-uksouth/overview

## KQL Queries for Application Insights

### 1. User Activity Analysis (Last 7 Days)
```kql
requests
| where timestamp > ago(7d)
| summarize 
    RequestCount = count(),
    AvgDuration = avg(duration),
    Users = dcount(user_Id)
by bin(timestamp, 1h)
| order by timestamp desc
```

### 2. Top 10 Slowest Requests
```kql
requests
| where timestamp > ago(24h)
| top 10 by duration desc
| project 
    timestamp,
    name,
    duration,
    resultCode,
    url,
    operation_Id
```

### 3. Error Rate Monitoring (Last 24 Hours)
```kql
requests
| where timestamp > ago(24h)
| summarize 
    TotalRequests = count(),
    FailedRequests = countif(success == false),
    ErrorRate = round(100.0 * countif(success == false) / count(), 2)
by bin(timestamp, 1h)
| order by timestamp desc
```

### 4. Token Usage Tracking
```kql
customMetrics
| where name contains "Token"
| where timestamp > ago(7d)
| project 
    timestamp,
    name,
    value,
    operation_Id
| order by timestamp desc
```

### 5. Image Analysis Performance
```kql
customEvents
| where name in ("ImageAnalysisStarted", "ImageAnalysisCompleted")
| where timestamp > ago(24h)
| order by timestamp desc
| project 
    timestamp,
    name,
    duration = customMeasurements["Duration"],
    totalTokens = customMeasurements["TotalTokens"],
    operation_Id
```

### 6. Recent Exceptions
```kql
exceptions
| where timestamp > ago(24h)
| project 
    timestamp,
    type,
    outerMessage,
    operation_Name,
    problemId
| order by timestamp desc
```

### 7. Dependencies Performance
```kql
dependencies
| where timestamp > ago(24h)
| summarize 
    Count = count(),
    AvgDuration = avg(duration),
    FailureRate = round(100.0 * countif(success == false) / count(), 2)
by name
| order by AvgDuration desc
```

### 8. Page View Analytics
```kql
pageViews
| where timestamp > ago(7d)
| summarize Count = count() by name
| order by Count desc
```

### 9. Custom Events Timeline
```kql
customEvents
| where timestamp > ago(24h)
| summarize Count = count() by name, bin(timestamp, 1h)
| order by timestamp desc
```

### 10. Live Metrics (Real-time)
```kql
requests
| where timestamp > ago(5m)
| order by timestamp desc
| take 20
```

## How to Use KQL Queries

1. **Open Application Insights**
   - Navigate to Azure Portal
   - Go to your Application Insights resource: `appi-PoRedoImage-cqevadpy77pvi`

2. **Open Logs (Analytics)**
   - In the left menu, click "Logs"
   - You'll see the KQL query editor

3. **Run a Query**
   - Copy one of the queries above
   - Paste into the query editor
   - Click "Run" button (or press Shift+Enter)

4. **Customize Queries**
   - Adjust time ranges: Change `ago(24h)` to `ago(7d)`, `ago(1h)`, etc.
   - Add filters: `| where operation_Name == "POST /api/imageanalysis/analyze"`
   - Export results: Click "Export" to save as CSV or Excel

## Monitoring Dashboards

### Create a Custom Dashboard

1. Go to Application Insights
2. Click "Dashboard" or "New Dashboard"
3. Add tiles with the KQL queries above
4. Pin frequently used charts for quick access

### Recommended Tiles

- **Request Rate**: Real-time incoming requests
- **Response Time**: Average API response times
- **Failure Rate**: Percentage of failed requests
- **Active Users**: Number of concurrent users
- **Token Usage**: AI service token consumption
- **Exceptions**: Error count and trends

## Alerts Setup

### Recommended Alerts

1. **High Error Rate**
   - Threshold: > 5% error rate over 5 minutes
   - Action: Send email notification

2. **Slow Response Time**
   - Threshold: Average > 5 seconds over 5 minutes
   - Action: Send email notification

3. **High Token Usage**
   - Threshold: > 100,000 tokens per hour
   - Action: Send email notification (cost monitoring)

4. **Application Downtime**
   - Threshold: No requests for 5 minutes
   - Action: Send email + SMS notification

## Performance Baselines

Based on current deployment (F1 Free tier):

- **Expected Response Time**: 500ms - 2000ms
- **Concurrent Users**: Up to 10-20 (F1 limitations)
- **Request Rate**: 100-500 requests/hour
- **Token Usage**: Varies by image complexity

## Troubleshooting

### Common Issues

1. **No Telemetry Data**
   - Check if Application Insights connection string is configured
   - Verify `APPLICATIONINSIGHTS_CONNECTION_STRING` in App Service settings
   - Restart the web app

2. **Missing Custom Events**
   - Ensure TelemetryClient is properly injected
   - Check for exceptions in Application Insights logs
   - Verify event names match between code and queries

3. **High Latency**
   - Check Dependencies tab for slow external calls
   - Review Computer Vision and OpenAI response times
   - Consider scaling up App Service plan

## Cost Monitoring

### Application Insights Costs

- **Free Tier**: 5 GB/month data ingestion (included)
- **Overage**: ~$2.30 per GB
- **Retention**: 90 days (default, free)

### OpenAI Costs

Monitor token usage with Query #4 above to estimate costs:
- **GPT-4o**: ~$5-15 per 1M tokens
- **DALL-E 3**: ~$0.04-0.12 per image

---

*For more information, see [KqlQueries.cs](./Server/KqlQueries.cs) in the codebase.*
