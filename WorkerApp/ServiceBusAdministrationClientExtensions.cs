using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwissLife.Slkv.Framework.Extensions.ServiceBus.Administration;

public static class ServiceBusAdministrationClientExtensions
{
    public static async Task EnsureQueueExistsAsync(
        this ServiceBusAdministrationClient adminClient,
        string queue,
        ILogger log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminClient);
        ArgumentNullException.ThrowIfNull(log);

        if (await adminClient.QueueExistsAsync(queue, cancellationToken))
        {
            return;
        }

        log.LogInformation("Create queue {Queue}", queue);

        CreateQueueOptions options = new(queue);
        options.LockDuration = TimeSpan.FromMinutes(5);
        await adminClient.CreateQueueAsync(options, cancellationToken);
    }

    public static async Task EnsureTopicExistsAsync(
        this ServiceBusAdministrationClient adminClient,
        string topic,
        ILogger log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminClient);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrEmpty(topic);

        if (await adminClient.TopicExistsAsync(topic, cancellationToken))
        {
            return;
        }

        log.LogInformation("Create topic {Topic}", topic);

        await adminClient.CreateTopicAsync(topic, cancellationToken);
    }

    public static async Task EnsureSubscriptionAsync(
        this ServiceBusAdministrationClient adminClient,
        string topic,
        string subscription,
        string? forwardToQueueOrTopic,
        IDictionary<string, string>? rules,
        ILogger log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminClient);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrEmpty(topic);
        ArgumentException.ThrowIfNullOrEmpty(subscription);

        if (await adminClient.SubscriptionExistsAsync(topic, subscription, cancellationToken))
        {
            return;
        }

        if (rules == null || rules.Count < 1)
        {
            rules = new Dictionary<string, string>()
            {
                { "$default", "1=1" }
            };
        }

        log.LogInformation("Create subscription {Subscription} on topic {Topic}", subscription, topic);
        CreateSubscriptionOptions args = new(topic, subscription);
        args.ForwardTo = forwardToQueueOrTopic;
        args.LockDuration = TimeSpan.FromMinutes(5);

        await adminClient.CreateSubscriptionAsync(args, cancellationToken);

        Azure.AsyncPageable<RuleProperties> existingRules = adminClient.GetRulesAsync(
            topic,
            subscription,
            cancellationToken);

        await foreach (Azure.Page<RuleProperties> rulePage in existingRules.AsPages())
        {
            foreach (RuleProperties rule in rulePage.Values)
            {
                await adminClient.DeleteRuleAsync(
                    topic,
                    subscription,
                    rule.Name,
                    cancellationToken);
            }
        }

        foreach (KeyValuePair<string, string> rule in rules)
        {
            try
            {
                CreateRuleOptions createRuleOptions = new(rule.Key, new SqlRuleFilter(rule.Value));
                await adminClient.CreateRuleAsync(
                    topic,
                    subscription,
                    createRuleOptions,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not create filter {Name}: {Filter}: {Message}", rule.Key, rule.Value, ex.Message);
            }
        }
    }
}
