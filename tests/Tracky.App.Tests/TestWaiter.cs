using System.Diagnostics;

namespace Tracky.App.Tests;

public static class TestWaiter
{
    public static async Task UntilAsync(Func<bool> condition, string failureMessage, int timeoutMilliseconds = 2000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMilliseconds)
            {
                throw new TimeoutException(failureMessage);
            }

            await Task.Delay(20);
        }
    }
}
