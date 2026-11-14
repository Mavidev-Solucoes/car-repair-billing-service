namespace Domain.Common;

/// <summary>
/// Evento ligado a uma ordem de serviço. O ServiceOrderId vira o correlationId da mensagem.
/// </summary>
public interface IServiceOrderEvent : IDomainEvent
{
    Guid ServiceOrderId { get; }
}
