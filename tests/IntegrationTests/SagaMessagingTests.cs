using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions.Messaging;
using Application.Budgets.Queries;
using Application.Payments.Queries;
using Domain.Enums;
using Infrastructure.Messaging;
using RabbitMQ.Client;

namespace IntegrationTests;

[Collection(BillingCollection.Name)]
public sealed class SagaMessagingTests(BillingFixture fixture)
{
    [Fact]
    public async Task SagaCommands_ShouldCreateChargeAndCompensate()
    {
        using var probe = new EventsProbe(fixture);
        using var client = fixture.Factory.CreateClient();
        var serviceOrderId = Guid.NewGuid();
        var correlationId = serviceOrderId.ToString("D");
        await WaitForWorkerAsync(SagaCommandsWorker.CancelBudgetCommandType);

        var context = new JsonObject
        {
            ["serviceOrderId"] = serviceOrderId,
            ["customerName"] = "João",
            ["items"] = new JsonArray(new JsonObject { ["description"] = "Revisão", ["unitPrice"] = 300m, ["quantity"] = 1 })
        };
        PublishCommand(SagaCommandsWorker.CreateBudgetCommandType, correlationId, context);
        PublishCommand(SagaCommandsWorker.CreateBudgetCommandType, correlationId, context);

        var budgetCreated = await probe.WaitForAsync("BudgetCreated", correlationId);
        var budgetId = budgetCreated.Payload!["budgetId"]!.GetValue<Guid>();
        await client.PostAsync($"/api/budgets/{budgetId}/approve", null);

        context["budgetId"] = budgetId;
        context["simulatePaymentFailure"] = false;
        PublishCommand(SagaCommandsWorker.ProcessPaymentCommandType, correlationId, context);
        var paymentApproved = await probe.WaitForAsync("PaymentApproved", correlationId);
        Assert.Equal(300m, paymentApproved.Payload!["amount"]!.GetValue<decimal>());

        PublishCommand(SagaCommandsWorker.CancelBudgetCommandType, correlationId, new JsonObject());
        await probe.WaitForAsync("PaymentRefunded", correlationId);
        await probe.WaitForAsync("BudgetCancelled", correlationId);

        var budgets = await client.GetFromJsonAsync<List<BudgetSummaryResponse>>("/api/budgets");
        var budget = Assert.Single(budgets!, item => item.ServiceOrderId == serviceOrderId);
        Assert.Equal(BudgetStatus.Cancelled, budget.Status);
        Assert.Equal("João", budget.CustomerName);

        var payments = await client.GetFromJsonAsync<List<PaymentSummaryResponse>>("/api/payments");
        var payment = Assert.Single(payments!, item => item.ServiceOrderId == serviceOrderId);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public async Task SagaCommand_WithoutItems_ShouldGoStraightToDlq()
    {
        await WaitForWorkerAsync(SagaCommandsWorker.CreateBudgetCommandType);
        var correlationId = Guid.NewGuid().ToString("D");

        PublishCommand(SagaCommandsWorker.CreateBudgetCommandType, correlationId, new JsonObject { ["items"] = new JsonArray() });

        var dead = await GetFromDlqAsync(SagaCommandsWorker.QueuePrefix + SagaCommandsWorker.CreateBudgetCommandType, correlationId);
        Assert.Equal(correlationId, dead.BasicProperties.CorrelationId);
    }

    [Fact]
    public async Task Consumer_ShouldRetryAndThenDeadLetter()
    {
        var commandType = $"RetryCommand.{Guid.NewGuid():N}";
        var queue = $"billing-tests.{commandType}";
        var attempts = 0;
        await using var consumer = fixture.CreateConsumer(fixture.RabbitOptions(retryCount: 2));

        await consumer.SubscribeAsync(queue, commandType, (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("Postgres fora do ar");
        });
        await consumer.SubscribeAsync(queue, commandType, (_, _) => Task.CompletedTask);

        var correlationId = Guid.NewGuid().ToString("D");
        PublishCommand(commandType, correlationId, new JsonObject());

        var dead = await GetFromDlqAsync(queue, correlationId);
        Assert.Equal(3, attempts);
        Assert.Equal(2, Convert.ToInt32(dead.BasicProperties.Headers["x-retry-count"]));
    }

    [Fact]
    public async Task Consumer_ShouldRecoverAfterTransientFailure()
    {
        var commandType = $"FlakyCommand.{Guid.NewGuid():N}";
        var attempts = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var consumer = fixture.CreateConsumer(fixture.RabbitOptions());

        await consumer.SubscribeAsync($"billing-tests.{commandType}", commandType, (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new TimeoutException("instável");
            }

            done.TrySetResult();
            return Task.CompletedTask;
        });

        PublishCommand(commandType, Guid.NewGuid().ToString("D"), new JsonObject());

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Consumer_ShouldDeadLetterInvalidEnvelopesWithoutRetry()
    {
        var commandType = $"InvalidCommand.{Guid.NewGuid():N}";
        var queue = $"billing-tests.{commandType}";
        var attempts = 0;
        await using var consumer = fixture.CreateConsumer(fixture.RabbitOptions());

        await consumer.SubscribeAsync(queue, commandType, (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            return Task.CompletedTask;
        });

        PublishRaw(commandType, Encoding.UTF8.GetBytes("{ quebrado"), "json-quebrado");
        PublishRaw(commandType, Encoding.UTF8.GetBytes("{\"type\":\"X\"}"), "sem-correlation");

        await GetFromDlqAsync(queue, "json-quebrado", "sem-correlation");
        Assert.Equal(0, attempts);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            consumer.SubscribeAsync(queue, commandType, (_, _) => Task.CompletedTask, new CancellationToken(canceled: true)));
    }

    private void PublishCommand(string type, string correlationId, JsonObject payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new IntegrationMessage { Type = type, CorrelationId = correlationId, Payload = payload },
            IntegrationMessage.SerializerOptions);
        PublishRaw(type, body, correlationId);
    }

    private void PublishRaw(string routingKey, byte[] body, string correlationId)
    {
        using var connection = fixture.CreateRawConnection();
        using var channel = connection.CreateModel();
        var properties = channel.CreateBasicProperties();
        properties.CorrelationId = correlationId;
        channel.BasicPublish("car-repair.commands", routingKey, properties, body);
    }

    private async Task<BasicGetResult> GetFromDlqAsync(string queue, string correlationId, params string[] others)
    {
        using var connection = fixture.CreateRawConnection();
        using var channel = connection.CreateModel();
        var seen = new List<BasicGetResult>();
        return await BillingFixture.WaitForAsync(() =>
        {
            while (channel.BasicGet($"{queue}.dlq", true) is { } delivery)
            {
                seen.Add(delivery);
            }

            var all = others.Append(correlationId).All(id => seen.Any(item => item.BasicProperties.CorrelationId == id));
            return Task.FromResult(all ? seen.First(item => item.BasicProperties.CorrelationId == correlationId) : null);
        });
    }

    private async Task WaitForWorkerAsync(string commandType)
    {
        _ = fixture.Factory.Services;
        using var connection = fixture.CreateRawConnection();
        await BillingFixture.WaitForAsync(() =>
        {
            try
            {
                using var channel = connection.CreateModel();
                return Task.FromResult(channel.QueueDeclarePassive(SagaCommandsWorker.QueuePrefix + commandType).ConsumerCount > 0 ? "ok" : null);
            }
            catch
            {
                return Task.FromResult<string?>(null);
            }
        }, TimeSpan.FromSeconds(30));
    }
}
