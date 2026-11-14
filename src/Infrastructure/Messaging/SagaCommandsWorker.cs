using System.Text.Json.Nodes;
using Application.Abstractions.Messaging;
using Application.Budgets.Commands;
using Application.Payments.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging;

/// <summary>
/// Assina os comandos da saga destinados ao billing-service (docs/contrato-mensageria.md).
/// </summary>
public sealed class SagaCommandsWorker(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<SagaCommandsWorker> logger) : BackgroundService
{
    public const string QueuePrefix = "billing-service.";
    public const string CreateBudgetCommandType = "CreateBudgetCommand";
    public const string ProcessPaymentCommandType = "ProcessPaymentCommand";
    public const string CancelBudgetCommandType = "CancelBudgetCommand";
    public const string DefaultCustomerName = "Cliente";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Resolvido aqui (e não no construtor) para o serviço subir mesmo com o RabbitMQ fora do ar.
                var consumer = serviceProvider.GetRequiredService<ICommandConsumer>();

                await consumer.SubscribeAsync(QueuePrefix + CreateBudgetCommandType, CreateBudgetCommandType, HandleCreateBudgetAsync, stoppingToken);
                await consumer.SubscribeAsync(QueuePrefix + ProcessPaymentCommandType, ProcessPaymentCommandType, HandleProcessPaymentAsync, stoppingToken);
                await consumer.SubscribeAsync(QueuePrefix + CancelBudgetCommandType, CancelBudgetCommandType, HandleCancelBudgetAsync, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not subscribe to saga commands. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public Task HandleCreateBudgetAsync(IntegrationMessage message, CancellationToken cancellationToken)
    {
        return SendAsync(MapCreateBudget(message), cancellationToken);
    }

    public Task HandleProcessPaymentAsync(IntegrationMessage message, CancellationToken cancellationToken)
    {
        return SendAsync(
            new ProcessServiceOrderPaymentCommand(
                message.GetServiceOrderId(),
                message.GetGuid("budgetId"),
                message.GetBoolean("simulatePaymentFailure")),
            cancellationToken);
    }

    public Task HandleCancelBudgetAsync(IntegrationMessage message, CancellationToken cancellationToken)
    {
        return SendAsync(
            new CancelServiceOrderBudgetCommand(message.GetServiceOrderId(), message.GetGuid("budgetId")),
            cancellationToken);
    }

    public static CreateBudgetCommand MapCreateBudget(IntegrationMessage message)
    {
        var items = (message.Payload?["items"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(item => new CreateBudgetItemModel(
                item["description"]?.GetValue<string>() ?? string.Empty,
                item["unitPrice"] is JsonValue price && price.TryGetValue<decimal>(out var unitPrice) ? unitPrice : 0m,
                item["quantity"] is JsonValue qty && qty.TryGetValue<int>(out var quantity) ? quantity : 1))
            .ToList();

        var customerName = message.Payload?["customerName"] is JsonValue name && name.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : DefaultCustomerName;

        return new CreateBudgetCommand(message.GetServiceOrderId(), customerName, items);
    }

    private async Task SendAsync(IBaseRequest request, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        try
        {
            await sender.Send(request, cancellationToken);
        }
        catch (FluentValidation.ValidationException exception)
        {
            // Payload inválido não melhora com retry: vai direto para a DLQ.
            throw new InvalidMessageException(exception.Message);
        }
    }
}
