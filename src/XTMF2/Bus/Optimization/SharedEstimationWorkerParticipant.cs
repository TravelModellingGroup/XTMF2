using System;
using System.Diagnostics.CodeAnalysis;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

public sealed class SharedEstimationWorkerParticipant
{
    private readonly RunContext _context;

    private SharedEstimationWorkerParticipant(RunContext context)
    {
        _context = context;
    }

    public static bool TryCreate(
        XTMFRuntime runtime,
        SharedEstimationRunRequest request,
        [NotNullWhen(true)] out SharedEstimationWorkerParticipant? participant,
        [NotNullWhen(false)] out RunError? error)
    {
        participant = null;
        if (!RunContext.CreateRunContext(runtime, request.RunId, request.ModelSystem,
            request.WorkingDirectory, request.StartToExecute, RunMode.Estimation, out var context,
            request.BasicParameterOverrides))
        {
            error = new RunError(RunErrorType.Validation,
                "Unable to create the shared-estimation run context.", null, string.Empty);
            return false;
        }

        if (!context.PrepareSharedEstimationWorker(out error))
            return false;

        participant = new SharedEstimationWorkerParticipant(context);
        return true;
    }

    public SharedEstimationEvaluationResult Evaluate(SharedEstimationCandidate candidate)
        => _context.EvaluateSharedEstimationCandidate(candidate);

}
