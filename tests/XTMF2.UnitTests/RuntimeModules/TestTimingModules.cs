using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.RuntimeModules;

namespace XTMF2.UnitTests.RuntimeModules;

[TestClass]
public class TestTimingModules
{
    [TestMethod]
    public void TimeFunction_PreservesResultAndRecordsElapsedTime()
    {
        var duration = new RecordingDuration();
        var timer = new TimeFunction<int>
        {
            ToTime = new TestFunction(42),
            ElapsedTime = duration
        };

        Assert.AreEqual(42, timer.Invoke());
        Assert.AreEqual(1, duration.SetCount);
        Assert.IsGreaterThanOrEqualTo(0.0, duration.Value);
    }

    [TestMethod]
    public void TimeContextFunction_ForwardsContextAndPreservesResult()
    {
        var context = new object();
        var target = new TestContextFunction();
        var duration = new RecordingDuration();
        var timer = new TimeFunction<object, int>
        {
            ToTime = target,
            ElapsedTime = duration
        };

        Assert.AreEqual(42, timer.Invoke(context));
        Assert.AreSame(context, target.ReceivedContext);
        Assert.AreEqual(1, duration.SetCount);
    }

    [TestMethod]
    public void TimeActions_InvokeTargetsAndRecordElapsedTime()
    {
        var actionDuration = new RecordingDuration();
        var action = new TestAction();
        new TimeAction { ToTime = action, ElapsedTime = actionDuration }.Invoke();
        Assert.AreEqual(1, action.InvocationCount);
        Assert.AreEqual(1, actionDuration.SetCount);

        var context = new object();
        var contextActionDuration = new RecordingDuration();
        var contextAction = new TestContextAction();
        new TimeAction<object> { ToTime = contextAction, ElapsedTime = contextActionDuration }.Invoke(context);
        Assert.AreSame(context, contextAction.ReceivedContext);
        Assert.AreEqual(1, contextActionDuration.SetCount);
    }

    [TestMethod]
    public void TimersRecordElapsedTimeWhenTargetsThrow()
    {
        var functionDuration = new RecordingDuration();
        var functionTimer = new TimeFunction<int>
        {
            ToTime = new ThrowingFunction(),
            ElapsedTime = functionDuration
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => functionTimer.Invoke());
        Assert.AreEqual(1, functionDuration.SetCount);

        var actionDuration = new RecordingDuration();
        var actionTimer = new TimeAction
        {
            ToTime = new ThrowingAction(),
            ElapsedTime = actionDuration
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => actionTimer.Invoke());
        Assert.AreEqual(1, actionDuration.SetCount);
    }

    private sealed class RecordingDuration : BaseModule, ISetableValue<float>
    {
        public float Value { get; private set; }
        public int SetCount { get; private set; }

        public float Get() => Value;

        public void Set(float value)
        {
            Value = value;
            SetCount++;
        }
    }

    private sealed class TestFunction(int result) : BaseFunction<int>
    {
        public override int Invoke() => result;
    }

    private sealed class TestContextFunction : BaseFunction<object, int>
    {
        public object ReceivedContext { get; private set; } = null!;

        public override int Invoke(object context)
        {
            ReceivedContext = context;
            return 42;
        }
    }

    private sealed class TestAction : BaseAction
    {
        public int InvocationCount { get; private set; }

        public override void Invoke() => InvocationCount++;
    }

    private sealed class TestContextAction : BaseAction<object>
    {
        public object ReceivedContext { get; private set; } = null!;

        public override void Invoke(object context) => ReceivedContext = context;
    }

    private sealed class ThrowingFunction : BaseFunction<int>
    {
        public override int Invoke() => throw new InvalidOperationException("Function failed.");
    }

    private sealed class ThrowingAction : BaseAction
    {
        public override void Invoke() => throw new InvalidOperationException("Action failed.");
    }
}
