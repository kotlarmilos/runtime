using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class Program
{
    public static void Main()
    {
        CrashTest();
    }

    public static void CrashTest()
    {
        var rnd = new Random();
        // Use large dataset
        var rows = Enumerable.Range(1, 100000)
                             .Select(x => new object[] { $"T{rnd.Next()}" })
                             .ToArray();

        // Forever loop
        var forever = true;

        int iteration = 0;
        while (true)
        {
            iteration++;
            var cts = new CancellationTokenSource();

            var thread = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        // This could race with the cancellation
                        // https://github.com/dotnet/runtime/issues/83520
                        var list = rows.AsParallel()
                                       .AsOrdered()
                                       .WithCancellation(cts.Token)
                                       .OrderBy(row => row[0])
                                       .ToList();

                        if (!forever)
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                catch (ThreadInterruptedException)
                {
                    // Expected
                }
            });

            thread.Start();

            // Interrupt the thread at random intervals
            Thread.Sleep(rnd.Next(1, 20));
            thread.Interrupt();
            cts.Cancel();

            // Wait for the thread to finish
            thread.Join();

            Console.WriteLine($"Iteration {iteration}");
        }
    }
}
