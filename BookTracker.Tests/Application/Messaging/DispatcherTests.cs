using BookTracker.Application;
using BookTracker.Application.Books;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookTracker.Tests;

// Direct tests of the command dispatcher — the primitive every command flows
// through. Pure unit tests (no EF / no container): fake commands + handlers
// registered in a throwaway ServiceCollection. Guards the result-returning
// path, exception propagation (incl. the reflection-wrapping trap the dynamic
// invoke fixes), and the unregistered-command failure mode.
[Trait("Category", TestCategories.Unit)]
public class DispatcherTests
{
    public sealed record Ping(string Tag) : ICommand;
    public sealed class PingHandler : ICommandHandler<Ping>
    {
        public string? Seen { get; private set; }
        public Task HandleAsync(Ping command, CancellationToken ct = default)
        {
            Seen = command.Tag;
            return Task.CompletedTask;
        }
    }

    public sealed record Doubled(int Value) : ICommand<int>;
    public sealed class DoubledHandler : ICommandHandler<Doubled, int>
    {
        public Task<int> HandleAsync(Doubled command, CancellationToken ct = default) =>
            Task.FromResult(command.Value * 2);
    }

    // Throws *synchronously* — before any await — which is exactly the case
    // MethodInfo.Invoke would wrap in TargetInvocationException. The dynamic
    // invoke must surface the original type. This test fails on the old
    // reflection-based dispatcher and passes on the dynamic one.
    public sealed record Boom() : ICommand;
    public sealed class BoomHandler : ICommandHandler<Boom>
    {
        public Task HandleAsync(Boom command, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    public sealed record Unregistered() : ICommand;

    private static (IDispatcher dispatcher, PingHandler ping) Build()
    {
        var ping = new PingHandler();
        var services = new ServiceCollection();
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddSingleton<ICommandHandler<Ping>>(ping);
        services.AddScoped<ICommandHandler<Doubled, int>, DoubledHandler>();
        services.AddScoped<ICommandHandler<Boom>, BoomHandler>();
        var dispatcher = services.BuildServiceProvider().GetRequiredService<IDispatcher>();
        return (dispatcher, ping);
    }

    [Fact]
    public async Task Send_resolvesAndInvokesTheVoidHandler()
    {
        var (dispatcher, ping) = Build();
        await dispatcher.Send(new Ping("hello"));
        Assert.Equal("hello", ping.Seen);
    }

    [Fact]
    public async Task Send_withResult_returnsTheHandlersValue()
    {
        var (dispatcher, _) = Build();
        var result = await dispatcher.Send(new Doubled(21));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Send_handlerThrowsSynchronously_propagatesOriginalException()
    {
        var (dispatcher, _) = Build();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.Send(new Boom()));
        Assert.Equal("boom", ex.Message); // not wrapped in TargetInvocationException
    }

    [Fact]
    public async Task Send_unregisteredCommand_throwsInvalidOperationException()
    {
        var (dispatcher, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.Send(new Unregistered()));
    }

    [Fact]
    public void AddApplicationLayer_registersHandlersByConvention()
    {
        var services = new ServiceCollection();
        services.AddApplicationLayer();

        // The convention scan must pick up both handler arities + the dispatcher.
        Assert.Contains(services, d => d.ServiceType == typeof(IDispatcher));
        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<RateBook>));              // void
        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<AddEditionToBook, int>)); // result
    }
}
