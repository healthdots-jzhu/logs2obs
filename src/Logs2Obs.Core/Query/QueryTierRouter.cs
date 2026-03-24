namespace Logs2Obs.Core.Query;

using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>Routes queries to the appropriate storage tier based on time range and query characteristics.</summary>
public class QueryTierRouter(ILogger<QueryTierRouter> logger)
{
    private readonly ILogger<QueryTierRouter> _logger = logger;

    /// <summary>
    /// Determines the optimal query tier for the given parsed query and tenant settings.
    /// Applies 6 routing rules in order: full-text search, hot-only, warm-only,
    /// cold-only, warm+cold cross-tier, and hot+warm cross-tier.
    /// </summary>
    public QueryTierDecision Route(ParsedQuery query, TenantSettings tenant)
    {
        var now        = DateTimeOffset.UtcNow;
        var hotCutoff  = now.AddDays(-tenant.HotRetentionDays);
        var warmCutoff = now.AddDays(-tenant.WarmRetentionDays);

        // Rule 1: Full-text search always goes to Hot (OpenSearch only supports full-text)
        if (query.HasFullTextSearch)
        {
            _logger.LogDebug("Query {QueryId} → Hot (full-text search)", query.QueryId);
            return new QueryTierDecision { Tier = QueryTier.Hot, Reason = "Full-text search requires OpenSearch" };
        }

        var earliest = query.EarliestTimestamp;
        var latest   = query.LatestTimestamp;

        if (earliest is null || latest is null)
        {
            _logger.LogDebug("Query {QueryId} → Hot (no time range specified)", query.QueryId);
            return new QueryTierDecision { Tier = QueryTier.Hot, Reason = "No time range specified — defaulting to hot tier" };
        }

        // Rule 2: Entirely within hot window → Hot
        if (earliest >= hotCutoff && latest <= now)
        {
            _logger.LogDebug("Query {QueryId} → Hot (within hot window)", query.QueryId);
            return new QueryTierDecision
            {
                Tier   = QueryTier.Hot,
                Reason = $"Data within {tenant.HotRetentionDays}-day hot window"
            };
        }

        // Rule 3: Entirely within warm window → Warm
        if (earliest >= warmCutoff && latest < hotCutoff)
        {
            _logger.LogDebug("Query {QueryId} → Warm", query.QueryId);
            return new QueryTierDecision
            {
                Tier   = QueryTier.Warm,
                Reason = $"Data in warm window ({tenant.HotRetentionDays}–{tenant.WarmRetentionDays} days)"
            };
        }

        // Rule 4: Entirely in cold storage → Cold (with warning)
        if (earliest < warmCutoff)
        {
            if (latest < warmCutoff)
            {
                _logger.LogDebug("Query {QueryId} → Cold", query.QueryId);
                return new QueryTierDecision
                {
                    Tier    = QueryTier.Cold,
                    Reason  = "Data in cold storage (>90 days)",
                    Warning = "Query may take 30–120s"
                };
            }

            // Rule 5: Spans warm + cold → Cross-tier
            _logger.LogDebug("Query {QueryId} → CrossTier (warm + cold)", query.QueryId);
            return new QueryTierDecision
            {
                Tier       = QueryTier.CrossTier,
                Reason     = "Query spans warm and cold tiers — fan-out required",
                SubQueries =
                [
                    new SubQuery { Tier = QueryTier.Warm, From = warmCutoff,          To = latest.Value },
                    new SubQuery { Tier = QueryTier.Cold, From = earliest.Value,      To = warmCutoff   }
                ]
            };
        }

        // Rule 6: Spans hot + warm → Cross-tier fan-out
        _logger.LogDebug("Query {QueryId} → CrossTier (hot + warm)", query.QueryId);
        return new QueryTierDecision
        {
            Tier       = QueryTier.CrossTier,
            Reason     = "Query spans hot and warm tiers — fan-out required",
            SubQueries =
            [
                new SubQuery { Tier = QueryTier.Hot,  From = hotCutoff,          To = latest.Value   },
                new SubQuery { Tier = QueryTier.Warm, From = earliest.Value,     To = hotCutoff       }
            ]
        };
    }
}
