using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.AI;

public sealed class AiProviderRegistry
{
    private readonly Dictionary<string, IAiProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AiProviderInfo> Providers =>
        _providers.Values
            .Select(provider => provider.Info)
            .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void Register(IAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.Info.Id))
        {
            throw new ArgumentException("An AI provider must have a non-empty id.", nameof(provider));
        }

        if (!_providers.TryAdd(provider.Info.Id, provider))
        {
            throw new InvalidOperationException(
                $"An AI provider with id '{provider.Info.Id}' is already registered.");
        }
    }

    public bool TryGet(string providerId, out IAiProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            provider = null;
            return false;
        }

        return _providers.TryGetValue(providerId, out provider);
    }

    public IAiProvider GetRequired(string providerId)
    {
        if (!TryGet(providerId, out var provider))
        {
            throw new KeyNotFoundException($"AI provider '{providerId}' is not registered.");
        }

        return provider!;
    }

    public Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        return GetRequired(providerId).GetModelsAsync(cancellationToken);
    }
}