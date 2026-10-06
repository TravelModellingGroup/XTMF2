using System;
using System.Collections.Generic;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

public interface ISharedEstimationWorker : IDisposable
{
    string WorkerId { get; }

    event EventHandler<SharedEstimationEvaluationResult>? ResultReceived;

    event EventHandler? Disconnected;

    bool SendCandidate(SharedEstimationCandidate candidate, out string? error);
}
