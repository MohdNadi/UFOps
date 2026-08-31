using System.Collections.Immutable;

namespace UFOps.Core;

public sealed record EngineCapability
{
    public CapabilityId Id { get; }
    public int ContractVersion { get; }
    public string Description { get; }

    public EngineCapability(CapabilityId id, int contractVersion, string description)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(contractVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Id = id;
        ContractVersion = contractVersion;
        Description = description;
    }
}

public sealed class EngineDescriptor
{
    public EngineId Id { get; }
    public string Name { get; }
    public Version Version { get; }
    public ImmutableArray<EngineCapability> Capabilities { get; }

    public EngineDescriptor(EngineId id, string name, Version version, IEnumerable<EngineCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (version.Major < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Engine versions must have a major version of at least 1.");
        }

        var materialized = capabilities.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An engine must declare at least one capability.", nameof(capabilities));
        }

        if (materialized.Any(capability => capability is null))
        {
            throw new ArgumentException("An engine cannot declare null capabilities.", nameof(capabilities));
        }

        if (materialized.Select(capability => capability.Id).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("An engine cannot declare duplicate capabilities.", nameof(capabilities));
        }

        Id = id;
        Name = name;
        Version = version;
        Capabilities = materialized;
    }
}
