using System.Runtime.CompilerServices;
using Nori.Plugin.Abstractions;
using Nori.Plugin.Arcade.Abstractions;
using Nori.Plugin.Games.Abstractions;
using Nori.Plugin.Harness.Abstractions;

namespace Nori.Plugin.Runtime;

internal static class PluginContributionProxies
{
	public static T Wrap<T>(PluginDescriptor descriptor, T contribution)
		where T : class, IPluginContribution => contribution switch
	{
		IGameProvider provider when typeof(T) == typeof(IGameProvider) => (T)(IPluginContribution)new SafeGameProvider(descriptor, provider),
		IArcadeCartridge cartridge when typeof(T) == typeof(IArcadeCartridge) => (T)(IPluginContribution)new SafeArcadeCartridge(descriptor, cartridge),
		IHarnessTool tool when typeof(T) == typeof(IHarnessTool) => (T)(IPluginContribution)new SafeHarnessTool(descriptor, tool),
		IHarnessResourceProvider resource when typeof(T) == typeof(IHarnessResourceProvider) => (T)(IPluginContribution)new SafeHarnessResourceProvider(descriptor, resource),
		IHarnessEventSource source when typeof(T) == typeof(IHarnessEventSource) => (T)(IPluginContribution)new SafeHarnessEventSource(descriptor, source),
		_ => contribution,
	};

	private static PluginException InvocationFailure(PluginDescriptor descriptor, string stage, Exception exception) =>
		exception is PluginException pluginException && pluginException.Code == PluginErrorCodes.InvocationFailed
			? pluginException
			: new PluginException(PluginErrorCodes.InvocationFailed, $"插件调用失败 [{descriptor.Id}@{descriptor.Version}] {stage}", exception);

	private sealed class SafeGameProvider(PluginDescriptor descriptor, IGameProvider inner) : IGameProvider
	{
		public GameDescriptor Descriptor
		{
			get
			{
				try { return inner.Descriptor; }
				catch (Exception exception) { throw InvocationFailure(descriptor, "game.descriptor", exception); }
			}
		}

		public async ValueTask<IGameSession> CreateSessionAsync(GameLaunchContext context, CancellationToken cancellationToken)
		{
			try
			{
				IGameSession session = await inner.CreateSessionAsync(context, cancellationToken).ConfigureAwait(false);
				return new SafeGameSession(descriptor, session);
			}
			catch (Exception exception) { throw InvocationFailure(descriptor, "game.create_session", exception); }
		}
	}

	private sealed class SafeGameSession(PluginDescriptor descriptor, IGameSession inner) : IGameSession
	{
		public async ValueTask StartAsync(CancellationToken cancellationToken)
		{
			try { await inner.StartAsync(cancellationToken).ConfigureAwait(false); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "game.start", exception); }
		}

		public async ValueTask StopAsync(CancellationToken cancellationToken)
		{
			try { await inner.StopAsync(cancellationToken).ConfigureAwait(false); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "game.stop", exception); }
		}

		public async ValueTask DisposeAsync()
		{
			try { await inner.DisposeAsync().ConfigureAwait(false); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "game.dispose", exception); }
		}
	}

	private sealed class SafeArcadeCartridge(PluginDescriptor descriptor, IArcadeCartridge inner) : IArcadeCartridge
	{
		public string Id
		{
			get
			{
				try { return inner.Id; }
				catch (Exception exception) { throw InvocationFailure(descriptor, "arcade.id", exception); }
			}
		}

		public System.Text.Json.Nodes.JsonNode CreateInitialState()
		{
			try { return inner.CreateInitialState(); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "arcade.initial_state", exception); }
		}

		public async ValueTask<ArcadeReduceResult> ReduceAsync(ArcadeReduceContext context, System.Text.Json.JsonElement state, System.Text.Json.JsonElement command, CancellationToken cancellationToken)
		{
			try { return await inner.ReduceAsync(context, state, command, cancellationToken).ConfigureAwait(false); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "arcade.reduce", exception); }
		}
	}

	private sealed class SafeHarnessTool(PluginDescriptor descriptor, IHarnessTool inner) : IHarnessTool
	{
		public HarnessToolDescriptor Descriptor
		{
			get
			{
				try { return inner.Descriptor; }
				catch (Exception exception) { throw InvocationFailure(descriptor, "harness.descriptor", exception); }
			}
		}

		public async ValueTask<HarnessToolResult> InvokeAsync(System.Text.Json.JsonElement arguments, HarnessInvocationContext context, CancellationToken cancellationToken)
		{
			try { return await inner.InvokeAsync(arguments, context, cancellationToken).ConfigureAwait(false); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "harness.invoke", exception); }
		}
	}

	private sealed class SafeHarnessResourceProvider(PluginDescriptor descriptor, IHarnessResourceProvider inner) : IHarnessResourceProvider
	{
		public string Id
		{
			get
			{
				try { return inner.Id; }
				catch (Exception exception) { throw InvocationFailure(descriptor, "harness.resource.id", exception); }
			}
		}

		public async ValueTask<IReadOnlyList<HarnessResourceDescriptor>> ListAsync(CancellationToken cancellationToken)
		{
			try { return await inner.ListAsync(cancellationToken).ConfigureAwait(false); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "harness.resource.list", exception); }
		}

		public async ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
		{
			try { return await inner.OpenReadAsync(relativePath, cancellationToken).ConfigureAwait(false); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "harness.resource.open", exception); }
		}
	}

	private sealed class SafeHarnessEventSource(PluginDescriptor descriptor, IHarnessEventSource inner) : IHarnessEventSource
	{
		public string Id
		{
			get
			{
				try { return inner.Id; }
				catch (Exception exception) { throw InvocationFailure(descriptor, "harness.event.id", exception); }
			}
		}

		public async IAsyncEnumerable<HarnessEvent> ListenAsync(
			HarnessEventSubscription subscription,
			[EnumeratorCancellation] CancellationToken cancellationToken)
		{
			IAsyncEnumerable<HarnessEvent> events;
			try { events = inner.ListenAsync(subscription, cancellationToken); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "harness.event.listen", exception); }
			IAsyncEnumerator<HarnessEvent> enumerator;
			try { enumerator = events.GetAsyncEnumerator(cancellationToken); }
			catch (Exception exception) { throw InvocationFailure(descriptor, "harness.event.listen", exception); }
			try
			{
				while (true)
				{
					bool hasNext;
					try { hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false); }
					catch (Exception exception) { throw InvocationFailure(descriptor, "harness.event.next", exception); }
					if (!hasNext) break;
					yield return enumerator.Current;
				}
			}
			finally
			{
				try { await enumerator.DisposeAsync().ConfigureAwait(false); }
				catch (Exception exception) { throw InvocationFailure(descriptor, "harness.event.dispose", exception); }
			}
		}
	}
}
