using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus.Optimization;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public sealed class TestParticleSwarmAlgorithm
{
    [TestMethod]
    public void BoundaryStrategies_AbsorbReflectAndRandomRelocate()
    {
        var (seed, initialPosition, initialVelocity) = FindSeedWithUpperBoundaryCrossing();
        double proposedPosition = initialPosition + initialVelocity;

        var absorb = Run(seed, ParticleSwarmBoundaryStrategy.Absorb, 0, 2, [0.0], [1.0]);
        Assert.AreEqual(1.0, absorb[1][1][0]);
        Assert.AreEqual(1.0, absorb[2][1][0]);

        var reflect = Run(seed, ParticleSwarmBoundaryStrategy.Reflect, 0, 2, [0.0], [1.0]);
        double reflectedPosition = 2.0 - proposedPosition;
        Assert.AreEqual(reflectedPosition, reflect[1][1][0], 1e-12);
        Assert.AreEqual(reflectedPosition - initialVelocity, reflect[2][1][0], 1e-12);

        var relocated = Run(seed, ParticleSwarmBoundaryStrategy.RandomRelocate, 0, 2, [0.0], [1.0]);
        Assert.IsTrue(relocated[1][1][0] >= 0.0 && relocated[1][1][0] <= 1.0);
        Assert.AreEqual(relocated[1][1][0], relocated[2][1][0],
            "Random relocation should clear momentum so the particle stays put when there are no other forces.");
        var repeated = Run(seed, ParticleSwarmBoundaryStrategy.RandomRelocate, 0, 2, [0.0], [1.0]);
        CollectionAssert.AreEqual(
            relocated.SelectMany(batch => batch).SelectMany(candidate => candidate).ToArray(),
            repeated.SelectMany(batch => batch).SelectMany(candidate => candidate).ToArray());
    }

    [TestMethod]
    public void MovementReduction_InterpolatesInertiaAndZeroPreservesLegacySchedule()
    {
        const double lower = -1_000_000;
        const double upper = 1_000_000;
        var (seed, initialPosition, initialVelocity) = FindSeedWithoutBoundaryCrossing(lower, upper, 3);

        var reduced = Run(seed, ParticleSwarmBoundaryStrategy.Absorb, 1, 3, [lower], [upper]);
        Assert.AreEqual(initialPosition + initialVelocity, reduced[1][1][0], 1e-8);
        Assert.AreEqual(initialPosition + initialVelocity * 1.5, reduced[2][1][0], 1e-8);
        Assert.AreEqual(reduced[2][1][0], reduced[3][1][0], 1e-8);

        var unchanged = Run(seed, ParticleSwarmBoundaryStrategy.Absorb, 0, 3, [lower], [upper]);
        Assert.AreEqual(initialPosition + initialVelocity * 3, unchanged[3][1][0], 1e-8);
    }

    [TestMethod]
    public void ParticleSwarmConfig_ExposesAndValidatesNewParameters()
    {
        var config = new ParticleSwarmConfig();
        Assert.AreEqual(ParticleSwarmBoundaryStrategy.Absorb, config.BoundaryStrategy);
        Assert.AreEqual(0.0, config.MovementReduction);

        var parameters = config.GetParameters();
        var boundary = parameters.Single(parameter => parameter.Key == "BoundaryStrategy");
        CollectionAssert.AreEqual(new[] { "Absorb", "Reflect", "Random Relocate" }, boundary.Options.ToArray());
        Assert.IsTrue(boundary.HasOptions);
        boundary.Value = "Random Relocate";
        parameters.Single(parameter => parameter.Key == "MovementReduction").Value = "0.75";

        Assert.IsNull(config.ApplyParameters(parameters));
        Assert.AreEqual(ParticleSwarmBoundaryStrategy.RandomRelocate, config.BoundaryStrategy);
        Assert.AreEqual(0.75, config.MovementReduction);

        boundary.Value = "Unknown";
        Assert.IsNotNull(config.ApplyParameters(parameters));
        boundary.Value = "Reflect";
        parameters.Single(parameter => parameter.Key == "MovementReduction").Value = "1.01";
        Assert.IsNotNull(config.ApplyParameters(parameters));
    }

    private static List<double[][]> Run(
        int seed,
        ParticleSwarmBoundaryStrategy strategy,
        double movementReduction,
        int maxIterations,
        double[] lower,
        double[] upper)
    {
        var batches = new List<double[][]>();
        var algorithm = new ParticleSwarmAlgorithm(
            swarmSize: 2,
            inertia: 1,
            cognitiveCoeff: 0,
            socialCoeff: 0,
            noImprovementLimit: 0,
            randomSeed: seed,
            boundaryStrategy: strategy,
            movementReduction: movementReduction);
        algorithm.Initialize(1, lower, upper, [0.5], maxIterations, 0);

        IReadOnlyList<double> EvaluateBatch(IReadOnlyList<double[]> candidates)
        {
            batches.Add(candidates.Select(candidate => (double[])candidate.Clone()).ToArray());
            return new double[candidates.Count];
        }

        algorithm.RunBatch(_ => 0, EvaluateBatch);
        return batches;
    }

    private static (int Seed, double Position, double Velocity) FindSeedWithUpperBoundaryCrossing()
    {
        for (int seed = 0; seed < 100_000; seed++)
        {
            var random = new Random(seed);
            double position = random.NextDouble();
            double velocity = (random.NextDouble() * 2.0 - 1.0) * 0.5;
            if (position + velocity > 1.0)
                return (seed, position, velocity);
        }
        Assert.Fail("Could not find a deterministic seed that crosses the upper bound.");
        return default;
    }

    private static (int Seed, double Position, double Velocity) FindSeedWithoutBoundaryCrossing(
        double lower, double upper, int iterations)
    {
        double velocityLimit = (upper - lower) / 2.0;
        for (int seed = 0; seed < 100_000; seed++)
        {
            var random = new Random(seed);
            double position = lower + random.NextDouble() * (upper - lower);
            double velocity = (random.NextDouble() * 2.0 - 1.0) * velocityLimit;
            if (Enumerable.Range(1, iterations)
                .All(step => position + velocity * step > lower && position + velocity * step < upper))
                return (seed, position, velocity);
        }
        Assert.Fail("Could not find a deterministic seed whose particle remains within bounds.");
        return default;
    }
}
