namespace Atomizer;

/// <summary>
/// Describes a registered Atomizer job handler and the payload type it handles.
/// </summary>
public sealed class AtomizerJobHandlerDescriptor
{
    internal AtomizerJobHandlerDescriptor(Type payloadType, Type handlerType)
    {
        PayloadType = payloadType;
        HandlerType = handlerType;
    }

    /// <summary>
    /// Gets the payload type accepted by the handler.
    /// </summary>
    public Type PayloadType { get; }

    /// <summary>
    /// Gets the concrete handler type registered for the payload.
    /// </summary>
    public Type HandlerType { get; }
}
