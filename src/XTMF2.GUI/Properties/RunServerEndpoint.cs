using System;
using System.Collections.Generic;

namespace XTMF2.GUI.Properties;

/// <summary>
/// A RunServer endpoint configured for use by the GUI.
/// </summary>
public sealed class RunServerEndpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "RunServer";
    public bool Enabled { get; set; } = true;
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public bool IsLocal { get; set; }
    public string Token { get; set; } = string.Empty;
    public string CertificateFingerprint { get; set; } = string.Empty;
    public Dictionary<string, string> BasicParameterOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static RunServerEndpoint CreateLocal()
        => new()
        {
            Id = "local",
            Name = "Local RunServer",
            Enabled = true,
            Address = "127.0.0.1",
            Port = 0,
            IsLocal = true,
            Token = string.Empty,
            CertificateFingerprint = string.Empty,
            BasicParameterOverrides = new(StringComparer.OrdinalIgnoreCase)
        };

    public RunServerEndpoint Clone()
        => new()
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            Address = Address,
            Port = Port,
            IsLocal = IsLocal,
            Token = Token,
            CertificateFingerprint = CertificateFingerprint,
            BasicParameterOverrides = new Dictionary<string, string>(BasicParameterOverrides ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase)
        };
}
