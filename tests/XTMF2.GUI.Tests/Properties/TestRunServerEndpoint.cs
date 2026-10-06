using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI.Tests.Properties;

[TestClass]
public class TestRunServerEndpoint
{
    [TestMethod]
    public void BasicParameterOverrides_RoundTripThroughJson()
    {
        var endpoint = new RunServerEndpoint
        {
            Id = "worker-1",
            Name = "Worker 1",
            BasicParameterOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["11111111-1111-1111-1111-111111111111"] = "/data/input",
                ["22222222-2222-2222-2222-222222222222"] = "/data/output"
            }
        };

        var restored = JsonSerializer.Deserialize<RunServerEndpoint>(JsonSerializer.Serialize(endpoint));

        Assert.IsNotNull(restored);
        Assert.AreEqual("/data/input", restored.BasicParameterOverrides["11111111-1111-1111-1111-111111111111"]);
        Assert.AreEqual("/data/output", restored.BasicParameterOverrides["22222222-2222-2222-2222-222222222222"]);
    }

    [TestMethod]
    public void Clone_IsolatesBasicParameterOverrides()
    {
        var endpoint = new RunServerEndpoint
        {
            BasicParameterOverrides = new Dictionary<string, string>
            {
                ["node-1"] = "/original"
            }
        };

        var clone = endpoint.Clone();
        clone.BasicParameterOverrides["node-1"] = "/clone";
        clone.BasicParameterOverrides["node-2"] = "/clone-only";

        Assert.AreEqual("/original", endpoint.BasicParameterOverrides["node-1"]);
        Assert.IsFalse(endpoint.BasicParameterOverrides.ContainsKey("node-2"));
    }

    [TestMethod]
    public void Clone_HandlesLegacyNullOverrideMap()
    {
        var endpoint = JsonSerializer.Deserialize<RunServerEndpoint>(
            "{\"Id\":\"worker-1\",\"BasicParameterOverrides\":null}");

        Assert.IsNotNull(endpoint);
        var clone = endpoint.Clone();

        Assert.IsNotNull(clone.BasicParameterOverrides);
        Assert.IsEmpty(clone.BasicParameterOverrides);
    }
}