using System.Text.Json.Nodes;
using Application.Abstractions.Messaging;
using Application.Abstractions.Payments;
using Application.Budgets.Commands;
using Application.Payments.Commands;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTests;

public sealed class SagaCommandsTests
{
    private static readonly Guid ServiceOrderId = Guid.NewGuid();

    [Fact]
    public void MapCreateBudget_ShouldReadSagaContext()
    {
        var message = new IntegrationMessage
        {
            Type = SagaCommandsWorker.CreateBudgetCommandType,
            CorrelationId = ServiceOrderId.ToString(),
            Payload = new JsonObject
            {
                ["customerName"] = "Maria",
                ["items"] = new JsonArray(
                    new JsonObject { ["description"] = "Troca de óleo", ["unitPrice"] = 150.5m, ["quantity"] = 1 },
                    new JsonObject { ["description"] = "Filtro", ["unitPrice"] = 40m })
            }
        };

        var command = SagaCommandsWorker.MapCreateBudget(message);

        Assert.Equal(ServiceOrderId, command.ServiceOrderId);
        Assert.Equal("Maria", command.CustomerName);
        Assert.Collection(
            command.Items,
            item => Assert.Equal(("Troca de óleo", 150.5m, 1), (item.Description, item.UnitPrice, item.Quantity)),
            item => Assert.Equal(("Filtro", 40m, 1), (item.Description, item.UnitPrice, item.Quantity)));
    }

    [Fact]
    public void MapCreateBudget_ShouldUseDefaultCustomerAndRejectBadCorrelation()
    {
        var command = SagaCommandsWorker.MapCreateBudget(new IntegrationMessage { Type = "CreateBudgetCommand", CorrelationId = ServiceOrderId.ToString() });

        Assert.Equal(SagaCommandsWorker.DefaultCustomerName, command.CustomerName);
        Assert.Empty(command.Items);
        Assert.Throws<InvalidMessageException>(() =>
            SagaCommandsWorker.MapCreateBudget(new IntegrationMessage { Type = "CreateBudgetCommand", CorrelationId = "x" }));
    }

    [Fact]
    public void IntegrationMessage_ShouldReadOptionalFields()
    {
        var budgetId = Guid.NewGuid();
        var message = new IntegrationMessage
        {
            Payload = new JsonObject { ["budgetId"] = budgetId.ToString(), ["simulatePaymentFailure"] = true, ["other"] = 1 }
        };

        Assert.Equal(budgetId, message.GetGuid("budgetId"));
        Assert.Null(message.GetGuid("other"));
        Assert.Null(message.GetGuid("missing"));
        Assert.True(message.GetBoolean("simulatePaymentFailure"));
        Assert.False(message.GetBoolean("other"));
    }

    [Fact]
    public async Task CreateBudget_ShouldBeIdempotentPerServiceOrder()
    {
        var repository = new InMemoryBudgetRepository();
        var handler = new CreateBudgetCommandHandler(repository, new NoOpUnitOfWork());
        var command = new CreateBudgetCommand(ServiceOrderId, "Maria", [new CreateBudgetItemModel("Troca de óleo", 150m, 1)]);

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task ProcessPayment_ShouldChargeBudgetTotalAndApprove()
    {
        var budget = Builders.ApprovedBudget(ServiceOrderId, 180m);
        var payments = new InMemoryPaymentRepository();
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(true, "charge-ref", "approved"));
        var handler = new ProcessServiceOrderPaymentCommandHandler(new InMemoryBudgetRepository(budget), payments, gateway, new NoOpUnitOfWork());

        await handler.Handle(new ProcessServiceOrderPaymentCommand(ServiceOrderId, budget.Id, false), CancellationToken.None);

        var payment = Assert.Single(payments.Items);
        Assert.Equal(180m, payment.Amount);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(ServiceOrderId, payment.ServiceOrderId);
    }

    [Fact]
    public async Task ProcessPayment_ShouldRejectWhenGatewayRefuses()
    {
        var budget = Builders.ApprovedBudget(ServiceOrderId);
        var payments = new InMemoryPaymentRepository();
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(false, "charge-ref", "Cartão recusado"));
        var handler = new ProcessServiceOrderPaymentCommandHandler(new InMemoryBudgetRepository(budget), payments, gateway, new NoOpUnitOfWork());

        await handler.Handle(new ProcessServiceOrderPaymentCommand(ServiceOrderId, null, true), CancellationToken.None);

        Assert.Equal(PaymentStatus.Rejected, Assert.Single(payments.Items).Status);
    }

    [Fact]
    public async Task ProcessPayment_ShouldNotChargeTwice()
    {
        var budget = Builders.ApprovedBudget(ServiceOrderId);
        var existing = Builders.ApprovedPayment(budget);
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(true, "charge-ref", "approved"));
        var handler = new ProcessServiceOrderPaymentCommandHandler(
            new InMemoryBudgetRepository(budget), new InMemoryPaymentRepository(existing), gateway, new NoOpUnitOfWork());

        var paymentId = await handler.Handle(new ProcessServiceOrderPaymentCommand(ServiceOrderId, budget.Id, false), CancellationToken.None);

        Assert.Equal(existing.Id, paymentId);
        Assert.Equal(0, gateway.ProcessCalls);
    }

    [Fact]
    public async Task ProcessPayment_ShouldReusePendingPayment()
    {
        var budget = Builders.ApprovedBudget(ServiceOrderId);
        var pending = new Payment(budget.Id, ServiceOrderId, budget.TotalAmount);
        var payments = new InMemoryPaymentRepository(pending);
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(true, "charge-ref", "approved"));
        var handler = new ProcessServiceOrderPaymentCommandHandler(new InMemoryBudgetRepository(budget), payments, gateway, new NoOpUnitOfWork());

        await handler.Handle(new ProcessServiceOrderPaymentCommand(ServiceOrderId, budget.Id, false), CancellationToken.None);

        Assert.Same(pending, Assert.Single(payments.Items));
        Assert.Equal(PaymentStatus.Approved, pending.Status);
    }

    [Fact]
    public async Task ProcessPayment_ShouldFailForMissingOrNotApprovedBudget()
    {
        var pending = new Budget(ServiceOrderId, "Maria", [new BudgetItem("Peça", 10m, 1)]);
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(true, "r", "ok"));
        var handler = new ProcessServiceOrderPaymentCommandHandler(
            new InMemoryBudgetRepository(pending), new InMemoryPaymentRepository(), gateway, new NoOpUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ProcessServiceOrderPaymentCommand(ServiceOrderId, null, false), CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new ProcessServiceOrderPaymentCommand(Guid.NewGuid(), pending.Id, false), CancellationToken.None));
    }

    [Fact]
    public async Task CancelBudget_ShouldRefundApprovedPaymentsAndCancelBudget()
    {
        var budget = Builders.ApprovedBudget(ServiceOrderId);
        var approved = Builders.ApprovedPayment(budget);
        var pending = new Payment(budget.Id, ServiceOrderId, 1m);
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(true, "r", "ok"));
        var handler = CreateCancelHandler(budget, gateway, approved, pending);

        await handler.Handle(new CancelServiceOrderBudgetCommand(ServiceOrderId, budget.Id), CancellationToken.None);

        Assert.Equal(BudgetStatus.Cancelled, budget.Status);
        Assert.Equal(PaymentStatus.Refunded, approved.Status);
        Assert.Equal(PaymentStatus.Rejected, pending.Status);
        Assert.Equal(["charge-ref"], gateway.RefundedReferences);
    }

    [Fact]
    public async Task CancelBudget_ShouldIgnoreRepeatedOrUnknownBudget()
    {
        var budget = Builders.ApprovedBudget(ServiceOrderId);
        budget.Cancel([]);
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(true, "r", "ok"));
        var handler = CreateCancelHandler(budget, gateway);

        await handler.Handle(new CancelServiceOrderBudgetCommand(ServiceOrderId, null), CancellationToken.None);
        await handler.Handle(new CancelServiceOrderBudgetCommand(Guid.NewGuid(), null), CancellationToken.None);

        Assert.Equal(BudgetStatus.Cancelled, budget.Status);
        Assert.Empty(gateway.RefundedReferences);
    }

    [Fact]
    public async Task CancelBudget_ShouldThrowWhenRefundFailsSoTheMessageIsRetried()
    {
        var budget = Builders.ApprovedBudget(ServiceOrderId);
        var approved = Builders.ApprovedPayment(budget);
        var gateway = new StubPaymentGateway(
            new PaymentGatewayResult(true, "r", "ok"),
            new PaymentGatewayResult(false, "r", "gateway indisponível"));
        var handler = CreateCancelHandler(budget, gateway, approved);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CancelServiceOrderBudgetCommand(ServiceOrderId, budget.Id), CancellationToken.None));

        Assert.Equal(BudgetStatus.Approved, budget.Status);
        Assert.Equal(PaymentStatus.Approved, approved.Status);
    }

    private static CancelServiceOrderBudgetCommandHandler CreateCancelHandler(Budget budget, IPaymentGateway gateway, params Payment[] payments)
    {
        return new CancelServiceOrderBudgetCommandHandler(
            new InMemoryBudgetRepository(budget),
            new InMemoryPaymentRepository(payments),
            gateway,
            new NoOpUnitOfWork(),
            NullLogger<CancelServiceOrderBudgetCommandHandler>.Instance);
    }
}
