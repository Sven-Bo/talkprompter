using FluentAssertions;
using Teleprompter.Core.Scrolling;

namespace Teleprompter.Core.Tests;

public sealed class SmoothDampTests
{
    [Fact]
    public void Step_ConvergesTowardTarget_WithoutOvershoot()
    {
        double current = 0.0;
        double velocity = 0.0;
        double max = double.MinValue;

        for (int i = 0; i < 200; i++)
        {
            current = SmoothDamp.Step(current, target: 100.0, ref velocity, smoothTime: 0.3, deltaTime: 0.016);
            max = System.Math.Max(max, current);
        }

        current.Should().BeApproximately(100.0, 0.5);
        max.Should().BeLessThanOrEqualTo(100.0 + 1e-6);
    }

    [Fact]
    public void Step_MovesGraduallyNotInstantly()
    {
        double velocity = 0.0;
        double afterOneStep = SmoothDamp.Step(0.0, target: 100.0, ref velocity, smoothTime: 0.5, deltaTime: 0.016);

        afterOneStep.Should().BeGreaterThan(0.0);
        afterOneStep.Should().BeLessThan(50.0);
    }
}
