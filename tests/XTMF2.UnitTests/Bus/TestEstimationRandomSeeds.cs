using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus.Optimization;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public sealed class TestEstimationRandomSeeds
{
    [TestMethod]
    public void AllEstimationConfigsExposeRandomSeedParameter()
    {
        EstimationAlgorithmConfig[] configs =
        [
            new NelderMeadConfig(),
            new ParticleSwarmConfig(),
            new GeneticAlgorithmConfig(),
            new StochasticGradientConfig()
        ];

        foreach (var config in configs)
        {
            var parameters = config.GetParameters();
            var seed = parameters.Single(parameter => parameter.Key == "RandomSeed");
            seed.Value = "-123456";

            Assert.IsNull(config.ApplyParameters(parameters));
            Assert.AreEqual(-123456, config.RandomSeed);
        }
    }

    [TestMethod]
    public void StochasticEstimators_ReproduceCandidatesForConfiguredSeed()
    {
        Func<int, EstimationAlgorithmConfig>[] createConfigs =
        [
            seed => new ParticleSwarmConfig
            {
                RandomSeed = seed,
                SwarmSize = 8,
                MaxIterations = 4,
                ConvergenceTolerance = 0,
                NoImprovementLimit = 0
            },
            seed => new GeneticAlgorithmConfig
            {
                RandomSeed = seed,
                PopulationSize = 8,
                MaxGenerations = 4,
                ConvergenceTolerance = 0,
                MutationRate = 1
            },
            seed => new StochasticGradientConfig
            {
                RandomSeed = seed,
                MaxIterations = 4,
                ConvergenceTolerance = 0
            }
        ];

        foreach (var createConfig in createConfigs)
        {
            var first = CaptureCandidates(createConfig(731));
            var repeated = CaptureCandidates(createConfig(731));
            var changedSeed = CaptureCandidates(createConfig(732));

            CollectionAssert.AreEqual(first, repeated);
            Assert.IsFalse(first.SequenceEqual(changedSeed),
                "Changing the configured seed should change the stochastic candidate sequence.");
        }
    }

    private static string[] CaptureCandidates(EstimationAlgorithmConfig config)
    {
        var algorithm = config.CreateAlgorithm(2, [-2, -2], [2, 2], [0.25, -0.5]);
        var candidates = new List<string>();

        double Evaluate(double[] values)
        {
            candidates.Add(string.Join(",", values.Select(value =>
                value.ToString("R", CultureInfo.InvariantCulture))));
            return values.Sum(value => value * value);
        }

        IReadOnlyList<double> EvaluateBatch(IReadOnlyList<double[]> values)
        {
            var fitnesses = new double[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                candidates.Add(string.Join(",", values[index].Select(value =>
                    value.ToString("R", CultureInfo.InvariantCulture))));
                fitnesses[index] = values[index].Sum(value => value * value);
            }
            return fitnesses;
        }

        algorithm.RunBatch(Evaluate, EvaluateBatch);
        return candidates.ToArray();
    }
}