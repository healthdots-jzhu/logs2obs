# Graph Visualization Guide

## Overview

logs2obs provides a powerful graph visualization engine that automatically suggests appropriate chart types based on query results and renders them using industry-standard visualization libraries (Vega-Lite and Chart.js).

---

## Supported Graph Types

| Graph Type | Description | Best Used For |
|------------|-------------|---------------|
| **LineChart** | Continuous line connecting data points over time | Time series trends, metrics over time |
| **BarChart** | Vertical bars comparing categorical values | Comparing counts/values across categories |
| **AreaChart** | Filled area under a line, emphasizing volume | Cumulative trends, error rates over time |
| **PieChart** | Circular chart divided into slices | Distribution of categorical data (status codes, log levels) |
| **HeatMap** | Color-coded matrix showing density | Error patterns by hour/day, correlation matrices |
| **Scatter** | Individual points on X/Y axes | Correlation analysis (latency vs error count) |
| **Stat** | Large single numeric value | Current metric value, KPI display |
| **Gauge** | Arc/radial gauge showing value within range | Current rate/percentage (error rate, CPU usage) |
| **StackedAreaChart** | Multiple layered areas showing composition over time | Log volume by type, service breakdown over time |

---

## Graph Type Selection Logic

### Rule-Based Selection

The `GraphSuggestionEngine` automatically analyzes your query result schema and suggests appropriate graph types using these rules:

```csharp
// Time series with counts → AreaChart (95% confidence)
SELECT timestamp, COUNT(*) AS error_count
FROM logs WHERE level = 'error' ...
// Suggested: AreaChart

// Time series without counts → LineChart (90% confidence)
SELECT timestamp, AVG(duration_ms) AS avg_latency
FROM logs ...
// Suggested: LineChart

// Hour/day matrix with counts → HeatMap (88% confidence)
SELECT hour_of_day, day_of_week, COUNT(*) AS error_count
FROM logs WHERE level = 'error' ...
// Suggested: HeatMap

// Single row, single numeric → Gauge (92% confidence)
SELECT COUNT(*) AS error_count FROM logs ...
// Suggested: Gauge

// Category + count (no time) → BarChart (80% confidence)
SELECT service, COUNT(*) AS error_count
FROM logs GROUP BY service
// Suggested: BarChart

// Two numeric columns → Scatter (75% confidence)
SELECT error_count, avg_latency_ms
FROM logs GROUP BY service
// Suggested: Scatter

// Time + category + count → StackedAreaChart (70% confidence)
SELECT timestamp, log_type, COUNT(*) AS count
FROM logs GROUP BY timestamp, log_type
// Suggested: StackedAreaChart
```

### AI-Enriched Selection

You can override the rule-based suggestion with natural language hints:

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -H "Content-Type: application/json" \
  -d '{
    "queryId": "qry_01HZ...",
    "graphType": "Auto",
    "hint": "show as a heatmap"
  }'
```

The AI will interpret your hint and override the rule-based suggestion if appropriate.

---

## Prebuilt Graph Templates

logs2obs includes 8 standard graph templates ready to use out-of-the-box:

### 1. Error Rate Heatmap (`error-rate-heatmap`)

**Description:** Error density by hour of day and day of week.  
**Graph Type:** HeatMap  
**Recommended Time Range:** 7 days

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -H "Content-Type: application/json" \
  -d '{
    "templateId": "error-rate-heatmap",
    "parameters": {
      "year": "2026",
      "month": "03",
      "day_start": "17",
      "day_end": "23"
    }
  }'
```

---

### 2. Latency P99 Trend (`latency-p99-trend`)

**Description:** P99 latency per service over time.  
**Graph Type:** LineChart  
**Recommended Time Range:** 24 hours

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -d '{
    "templateId": "latency-p99-trend",
    "parameters": {
      "year": "2026",
      "month": "03",
      "day": "23"
    }
  }'
```

---

### 3. Error Rate Gauge (`error-rate-gauge`)

**Description:** Current errors per minute.  
**Graph Type:** Gauge  
**Recommended Time Range:** 1 hour

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -d '{
    "templateId": "error-rate-gauge",
    "parameters": {
      "year": "2026",
      "month": "03",
      "day": "23",
      "hour": "14"
    }
  }'
```

---

### 4. Top Errors (`top-errors-bar`)

**Description:** Top 10 error messages by count.  
**Graph Type:** BarChart  
**Recommended Time Range:** 24 hours

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -d '{
    "templateId": "top-errors-bar",
    "parameters": {
      "year": "2026",
      "month": "03",
      "day": "23"
    }
  }'
```

---

### 5. Status Code Distribution (`status-code-donut`)

**Description:** HTTP status code distribution as pie chart.  
**Graph Type:** PieChart  
**Recommended Time Range:** 24 hours

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -d '{
    "templateId": "status-code-donut",
    "parameters": {
      "year": "2026",
      "month": "03",
      "day": "23"
    }
  }'
```

---

### 6. Service Error Scatter (`service-error-scatter`)

**Description:** Error count vs average latency by service.  
**Graph Type:** Scatter  
**Recommended Time Range:** 7 days

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -d '{
    "templateId": "service-error-scatter",
    "parameters": {
      "year": "2026",
      "month": "03",
      "day": "23"
    }
  }'
```

---

### 7. Log Volume by Type (`log-volume-stacked`)

**Description:** Ingestion volume by log type over time.  
**Graph Type:** StackedAreaChart  
**Recommended Time Range:** 24 hours

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -d '{
    "templateId": "log-volume-stacked",
    "parameters": {
      "year": "2026",
      "month": "03",
      "day": "23"
    }
  }'
```

---

### 8. Alert Firing Timeline (`alert-firing-timeline`)

**Description:** Alert firing events over time.  
**Graph Type:** LineChart  
**Recommended Time Range:** 24 hours

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_your_key" \
  -d '{
    "templateId": "alert-firing-timeline",
    "parameters": {
      "year": "2026",
      "month": "03",
      "day": "23"
    }
  }'
```

---

## Rendering in the Browser

### Using Vega-Embed (Vega-Lite)

The response from `/api/v1/graphs/render` includes a `vegaLiteSpec` field. Use this with Vega-Embed:

```html
<!DOCTYPE html>
<html>
<head>
  <script src="https://cdn.jsdelivr.net/npm/vega@5"></script>
  <script src="https://cdn.jsdelivr.net/npm/vega-lite@5"></script>
  <script src="https://cdn.jsdelivr.net/npm/vega-embed@6"></script>
</head>
<body>
  <div id="vis"></div>
  
  <script>
    // Fetch graph spec from logs2obs API
    fetch('http://localhost:8080/api/v1/graphs/render', {
      method: 'POST',
      headers: {
        'X-Api-Key': 'ls_your_key',
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        queryId: 'qry_01HZ...',
        graphType: 'Auto'
      })
    })
    .then(response => response.json())
    .then(data => {
      const spec = data.vegaLiteSpec;
      vegaEmbed('#vis', spec, {
        actions: {
          export: true,
          source: false,
          compiled: false,
          editor: false
        }
      });
    });
  </script>
</body>
</html>
```

### Using Chart.js

The response also includes a `chartJsConfig` field. Use this with Chart.js:

```html
<!DOCTYPE html>
<html>
<head>
  <script src="https://cdn.jsdelivr.net/npm/chart.js@4"></script>
</head>
<body>
  <canvas id="myChart" width="800" height="400"></canvas>
  
  <script>
    fetch('http://localhost:8080/api/v1/graphs/render', {
      method: 'POST',
      headers: {
        'X-Api-Key': 'ls_your_key',
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        queryId: 'qry_01HZ...',
        graphType: 'BarChart'
      })
    })
    .then(response => response.json())
    .then(data => {
      const ctx = document.getElementById('myChart').getContext('2d');
      new Chart(ctx, data.chartJsConfig);
    });
  </script>
</body>
</html>
```

---

## Vega-Lite Spec Schema Reference

A Vega-Lite spec returned by logs2obs follows this structure:

```json
{
  "$schema": "https://vega.github.io/schema/vega-lite/v5.json",
  "width": 800,
  "height": 400,
  "data": {
    "values": [ /* query result rows */ ]
  },
  "mark": "line",  // or "bar", "area", "arc", "rect", "point", "text"
  "encoding": {
    "x": { "field": "timestamp", "type": "temporal" },
    "y": { "field": "error_count", "type": "quantitative" }
  }
}
```

### Key Fields

- **`mark`**: Visual representation type (`line`, `bar`, `area`, `arc`, `rect`, `point`, `text`)
- **`encoding`**: Maps data fields to visual channels (x, y, color, size, opacity, etc.)
- **`type`**: Data type for encoding (`quantitative`, `temporal`, `nominal`, `ordinal`)
- **`transform`**: Data transformations (filter, aggregate, calculate, etc.) — applied before rendering

### Example: Custom Encoding

```json
{
  "mark": "bar",
  "encoding": {
    "x": { "field": "service", "type": "nominal", "axis": { "title": "Service Name" } },
    "y": { "field": "error_count", "type": "quantitative", "axis": { "title": "Errors" } },
    "color": { "field": "service", "type": "nominal", "legend": null }
  }
}
```

For full schema reference, see: [Vega-Lite Documentation](https://vega.github.io/vega-lite/docs/)

---

## Custom Graph Options

When calling `/api/v1/graphs/render`, you can customize the output with these options:

```json
{
  "queryId": "qry_01HZ...",
  "graphType": "LineChart",
  "options": {
    "theme": "dark",          // "light" or "dark" (default: "light")
    "height": 600,            // pixels (default: 400)
    "width": 1200,            // pixels (default: 800)
    "colorScheme": "tableau10" // Vega color scheme (default: "category10")
  }
}
```

### Available Color Schemes

- `category10` (default)
- `tableau10`
- `accent`
- `dark2`
- `paired`
- `pastel1`
- `set1`
- `set2`
- `set3`

### Responsive Design

To make charts responsive, omit `width` and use Vega-Embed's `autosize`:

```javascript
vegaEmbed('#vis', spec, {
  autosize: {
    type: 'fit',
    contains: 'padding'
  }
});
```

---

## Advanced: Combining Multiple Graphs

To create a dashboard with multiple graphs, make parallel API calls:

```javascript
const templates = [
  'error-rate-heatmap',
  'latency-p99-trend',
  'top-errors-bar',
  'error-rate-gauge'
];

const params = { year: '2026', month: '03', day: '23' };

Promise.all(
  templates.map(templateId =>
    fetch('http://localhost:8080/api/v1/graphs/render', {
      method: 'POST',
      headers: { 'X-Api-Key': 'ls_your_key', 'Content-Type': 'application/json' },
      body: JSON.stringify({ templateId, parameters: params })
    }).then(r => r.json())
  )
).then(results => {
  results.forEach((result, i) => {
    vegaEmbed(`#vis-${i}`, result.vegaLiteSpec);
  });
});
```

---

## Troubleshooting

### Graph Shows "No data"

- **Cause:** Query returned empty result set.
- **Fix:** Check query time range and partition filters.

### Heatmap Rendering Incorrectly

- **Cause:** Missing `hour_of_day` or `day_of_week` columns.
- **Fix:** Ensure query includes: `EXTRACT(HOUR FROM timestamp) AS hour_of_day, EXTRACT(DOW FROM timestamp) AS day_of_week`

### Scatter Plot Shows Single Point

- **Cause:** Query result has only one row.
- **Fix:** Add `GROUP BY` to create multiple points.

### Chart.js Config Errors

- **Cause:** Version mismatch (logs2obs targets Chart.js v4).
- **Fix:** Use Chart.js 4.x CDN: `https://cdn.jsdelivr.net/npm/chart.js@4`

---

## Next Steps

- See [Query Guide](./query-guide.md) for writing effective SQL queries
- See [API Reference](./api-reference.md) for full endpoint documentation
- See [Materialized Views](./materialized-views.md) for pre-aggregated dashboard data
