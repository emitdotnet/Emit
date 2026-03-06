namespace BuildingSentinel.Common.Extensions;

using BuildingSentinel.Common.Handlers;
using Emit.Mediator.DependencyInjection;

public static class MediatorBuilderExtensions
{
    /// <summary>Registers <see cref="SubmitBuildingEventHandler"/> with the mediator.</summary>
    public static MediatorBuilder AddBuildingSentinelHandlers(this MediatorBuilder mediator)
    {
        mediator.AddHandler<SubmitBuildingEventHandler>();
        return mediator;
    }
}
