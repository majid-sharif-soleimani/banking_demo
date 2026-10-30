using System.Diagnostics;

namespace Bank.Testing;

/// <summary>Retries an assertion until it passes or the timeout ends, for effects that happen on another thread.</summary>
public static class Eventually
{
    public static async Task AssertAsync(Func<Task> assertion, TimeSpan? timeout = null, TimeSpan? interval = null)
    {
        var deadline = timeout ?? TimeSpan.FromSeconds(10);
        var pause = interval ?? TimeSpan.FromMilliseconds(50);
        var clock = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                await assertion();
                return;
            }
            catch (Exception) when (clock.Elapsed < deadline)
            {
                await Task.Delay(pause);
            }
        }
    }
}
