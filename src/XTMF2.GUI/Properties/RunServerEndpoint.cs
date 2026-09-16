using System;

namespace XTMF2.GUI.Properties;

/// <summary>
/// A RunServer endpoint configured for use by the GUI.
/// </summary>
public sealed class RunServerEndpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "RunServer";
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public bool IsLocal { get; set; }

    public static RunServerEndpoint CreateLocal()
        => new()
        {
            Id = "local",
            Name = "Local RunServer",
            Address = "127.0.0.1",
            Port = 0,
            IsLocal = true
        };

    public RunServerEndpoint Clone()
        => new()
        {
            Id = Id,
            Name = Name,
            Address = Address,
            Port = Port,
            IsLocal = IsLocal
        };
}
