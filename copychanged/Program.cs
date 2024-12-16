using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace copychanged
{
    class Program
    {
        static void Main(string[] args)
        {
            for(int i = 0; i < 10; i++)
            {
                try
                {
                    string file1 = args[0];
                    string file2 = args[1];
                    CancellationTokenSource cts = new CancellationTokenSource();

                    Stopwatch sw = new Stopwatch();
                    sw.Restart();
                    bool same;
                    using(FileStream fs1 = File.OpenRead(file1))
                    {
                        using(FileStream fs2 = File.OpenRead(file2))
                        {
                            same = VectorizedComparer.Same(fs1, fs2, cts.Token, default, true);
                        }
                    }
                    sw.Stop();
                    double time = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                    Console.WriteLine($"Comparison took {time} seconds. Same: {same}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
            Console.ReadKey();
        }
    }
}
