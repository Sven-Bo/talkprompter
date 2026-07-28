using System;

namespace Teleprompter.Core.Scrolling;

/// <summary>
/// A critically-damped spring, ported from the well-known game-engine
/// SmoothDamp. It eases a value toward a target without overshoot, converting
/// the matcher's discrete position jumps into smooth, natural scrolling.
/// </summary>
public static class SmoothDamp
{
    /// <summary>
    /// Advances <paramref name="current"/> toward <paramref name="target"/>.
    /// </summary>
    /// <param name="current">Current value.</param>
    /// <param name="target">Desired value.</param>
    /// <param name="velocity">Carried between calls; pass the same variable each frame.</param>
    /// <param name="smoothTime">Approximate seconds to reach the target; larger is slower.</param>
    /// <param name="deltaTime">Seconds since the last call.</param>
    /// <param name="maxSpeed">Optional clamp on velocity.</param>
    /// <returns>The new value.</returns>
    public static double Step(
        double current,
        double target,
        ref double velocity,
        double smoothTime,
        double deltaTime,
        double maxSpeed = double.PositiveInfinity)
    {
        smoothTime = Math.Max(0.0001, smoothTime);
        double omega = 2.0 / smoothTime;

        double x = omega * deltaTime;
        double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);

        double change = current - target;
        double originalTo = target;

        double maxChange = maxSpeed * smoothTime;
        change = Math.Clamp(change, -maxChange, maxChange);
        double temp = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * temp) * exp;
        double result = (current - change) + (change + temp) * exp;

        // Prevent overshooting past the target.
        if (originalTo - current > 0.0 == result > originalTo)
        {
            result = originalTo;
            velocity = (result - originalTo) / deltaTime;
        }

        return result;
    }
}
