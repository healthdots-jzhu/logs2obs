using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Amazon.CDK.AWS.SQS;
using Constructs;

namespace Logs2Obs.Cdk.Stacks;

public class MessagingStack : Stack
{
    public Topic IngestTopic { get; }
    public Topic EventsTopic { get; }

    public Queue WorkerIngestQueue { get; }
    public Queue WorkerAlertsQueue { get; }
    public Queue WorkerMatviewQueue { get; }
    public Queue WorkerReplayQueue { get; }
    public Queue PullerScheduleQueue { get; }
    public Queue QueryAsyncQueue { get; }
    public Queue ApiWebhookQueue { get; }
    public Queue DeadletterAuditQueue { get; }

    public MessagingStack(Construct scope, string id, StackProps props)
        : base(scope, id, props)
    {
        IngestTopic = new Topic(this, "IngestTopic", new TopicProps
        {
            TopicName = "logs2obs-ingest"
        });

        EventsTopic = new Topic(this, "EventsTopic", new TopicProps
        {
            TopicName = "logs2obs-events"
        });

        WorkerIngestQueue = CreateQueueWithDlq("WorkerIngestQueue", "logs2obs-worker-ingest", out _);
        WorkerAlertsQueue = CreateQueueWithDlq("WorkerAlertsQueue", "logs2obs-worker-alerts", out _);
        WorkerMatviewQueue = CreateQueueWithDlq("WorkerMatviewQueue", "logs2obs-worker-matview", out _);
        WorkerReplayQueue = CreateQueueWithDlq("WorkerReplayQueue", "logs2obs-worker-replay", out _);
        PullerScheduleQueue = CreateQueueWithDlq("PullerScheduleQueue", "logs2obs-puller-schedule", out _);
        QueryAsyncQueue = CreateQueueWithDlq("QueryAsyncQueue", "logs2obs-query-async", out _);
        ApiWebhookQueue = CreateQueueWithDlq("ApiWebhookQueue", "logs2obs-api-webhook", out _);
        DeadletterAuditQueue = CreateQueueWithDlq("DeadletterAuditQueue", "logs2obs-deadletter-audit", out _);

        IngestTopic.AddSubscription(new SqsSubscription(WorkerIngestQueue));

        EventsTopic.AddSubscription(CreateFilteredSubscription(WorkerAlertsQueue, "alert-check"));
        EventsTopic.AddSubscription(CreateFilteredSubscription(WorkerMatviewQueue, "matview-refresh"));
        EventsTopic.AddSubscription(CreateFilteredSubscription(WorkerReplayQueue, "replay-start"));
        EventsTopic.AddSubscription(CreateFilteredSubscription(PullerScheduleQueue, "pull-job"));
        EventsTopic.AddSubscription(CreateFilteredSubscription(QueryAsyncQueue, "query-run"));
        EventsTopic.AddSubscription(CreateFilteredSubscription(ApiWebhookQueue, "webhook"));
    }

    private Queue CreateQueueWithDlq(string id, string queueName, out Queue dlq)
    {
        dlq = new Queue(this, $"{id}Dlq", new QueueProps
        {
            QueueName = $"{queueName}-dlq",
            VisibilityTimeout = Duration.Seconds(300),
            RetentionPeriod = Duration.Days(14)
        });

        return new Queue(this, id, new QueueProps
        {
            QueueName = queueName,
            VisibilityTimeout = Duration.Seconds(300),
            RetentionPeriod = Duration.Days(14),
            DeadLetterQueue = new DeadLetterQueue
            {
                Queue = dlq,
                MaxReceiveCount = 5
            }
        });
    }

    private SqsSubscription CreateFilteredSubscription(Queue queue, string eventType)
    {
        return new SqsSubscription(queue, new SqsSubscriptionProps
        {
            FilterPolicy = new Dictionary<string, SubscriptionFilter>
            {
                ["event_type"] = SubscriptionFilter.StringFilter(new StringConditions
                {
                    Allowlist = new[] { eventType }
                })
            }
        });
    }
}
