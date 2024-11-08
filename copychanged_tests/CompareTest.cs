using Microsoft.VisualStudio.TestTools.UnitTesting;
using copychanged;
using System.Numerics;
using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;

namespace copychanged_tests
{
    [TestClass]
    public class CompareTest
    {
        [TestMethod]
        public void TestVectorComparer()
        {
            Random rnd = new Random();

            Int64 totalCompared = 0;
            double secondsSpent = 0;
            double secondsSpentSequenceEqual = 0;

            Stopwatch sw = new Stopwatch();
            for (int t = 0; t < 10; t++)
            {
                byte[] data = new byte[rnd.Next(50_000_000, 100_000_0001)];

                //for (int i = 0; i < data.Length; i++)
                //{
                 //   data[i] = (byte)rnd.Next(0, 256);
                //}

                byte[] data2 = (byte[])data.Clone();

                bool changed = rnd.Next(0, 2) > 0;
                if (changed)
                {
                    int changedposition = rnd.Next(0, data2.Length);
                    totalCompared += changedposition;
                    data2[changedposition]++;
                }
                else
                {
                    totalCompared += data.Length;
                }

                sw.Restart();
                bool same = VectorizedComparer.Same(data, data2);
                sw.Stop();
                secondsSpent += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                Assert.AreEqual(same, !changed);
                sw.Restart();
                bool test = data.SequenceEqual(data2);
                sw.Stop();
                Assert.AreEqual(test, !changed);
                secondsSpentSequenceEqual += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
            }
            Trace.WriteLine($"{secondsSpent} seconds to compare {totalCompared} bytes");
            Trace.WriteLine($"{secondsSpentSequenceEqual} seconds to compare {totalCompared} bytes (SequenceEqual)");

        }
        [TestMethod]
        public void TestVectorComparerMultiThread()
        {
            Random rnd = new Random();

            Int64 totalCompared = 0;
            double secondsSpent = 0;
            double[] secondsSpentMultiThread = new double[100];
            int minBitshift = 10;
            int maxBitshift = 48;

            Stopwatch sw = new Stopwatch();
            for (int t = 0; t < 10; t++)
            {
                byte[] data = new byte[rnd.Next(1_000_000, 100_000_0001)];

                //for (int i = 0; i < data.Length; i++)
                //{
                //   data[i] = (byte)rnd.Next(0, 256);
                //}

                byte[] data2 = (byte[])data.Clone();

                bool changed = rnd.Next(0, 2) > 0;
                if (changed)
                {
                    int changedposition = rnd.Next(0, data2.Length);
                    totalCompared += changedposition;
                    data2[changedposition]++;
                }
                else
                {
                    totalCompared += data.Length;
                }

                sw.Restart();
                bool same = VectorizedComparer.Same(data, data2);
                sw.Stop();
                secondsSpent += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                Assert.AreEqual(same, !changed);
                for(int bitshift = minBitshift; bitshift <= maxBitshift; bitshift++)
                {
                    sw.Restart();
                    bool test = VectorizedComparer.SameMultiThread(data, data2, 1<<bitshift);
                    sw.Stop();
                    Assert.AreEqual(test, !changed);
                    secondsSpentMultiThread[bitshift] += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                }
            }
            Trace.WriteLine($"{secondsSpent} seconds to compare {totalCompared} bytes");
            for (int bitshift = minBitshift; bitshift <= maxBitshift; bitshift++)
            {

                Trace.WriteLine($"{secondsSpentMultiThread[bitshift]} seconds to compare {totalCompared} bytes (MultiThread {bitshift} bit shift ({1<<bitshift} byte chunks))");
            }

        }
        [TestMethod]
        public void TestVectorComparerStreamMultiThread()
        {
            Random rnd = new Random();

            Int64 totalCompared = 0;
            double secondsSpent = 0;
            double secondsSpentMultiThread = 0;

            Stopwatch sw = new Stopwatch();
            for (int t = 0; t < 30; t++)
            {
                byte[] data = new byte[rnd.Next(1_000_000, 100_000_0001)];

                //for (int i = 0; i < data.Length; i++)
                //{
                //   data[i] = (byte)rnd.Next(0, 256);
                //}

                byte[] data2 = (byte[])data.Clone();

                bool changed = rnd.Next(0, 2) > 0;
                if (changed)
                {
                    int changedposition = rnd.Next(0, data2.Length);
                    totalCompared += changedposition;
                    data2[changedposition]++;
                }
                else
                {
                    totalCompared += data.Length;
                }

                MemoryStream ms = new MemoryStream(data);
                MemoryStream ms2 = new MemoryStream(data2);

                CancellationTokenSource cts = new CancellationTokenSource();
                CancellationToken ct = cts.Token;

                sw.Restart();
                bool same = VectorizedComparer.Same((Stream)ms, (Stream)ms2,ct);
                sw.Stop();
                secondsSpent += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                Assert.AreEqual(same, !changed);

                sw.Restart();
                bool test = VectorizedComparer.SameMultiThread(data, data2);
                sw.Stop();
                Assert.AreEqual(test, !changed);
                secondsSpentMultiThread += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
            }
            Trace.WriteLine($"{secondsSpent} seconds to compare {totalCompared} bytes (stream)"); 
            Trace.WriteLine($"{secondsSpentMultiThread} seconds to compare {totalCompared} bytes (multithread)");

        }
        [TestMethod]
        public void TestVectorComparerMultiThreadSmallMany()
        {
            Random rnd = new Random();

            Int64 totalCompared = 0;
            double secondsSpent = 0;
            double[] secondsSpentMultiThread = new double[100];
            int minBitshift = 10;
            int maxBitshift = 48;

            Stopwatch sw = new Stopwatch();
            for (int t = 0; t < 1000; t++)
            {
                byte[] data = new byte[rnd.Next(1, 10_000_000)];

                //for (int i = 0; i < data.Length; i++)
                //{
                //   data[i] = (byte)rnd.Next(0, 256);
                //}

                byte[] data2 = (byte[])data.Clone();

                bool changed = rnd.Next(0, 2) > 0;
                if (changed)
                {
                    int changedposition = rnd.Next(0, data2.Length);
                    totalCompared += changedposition;
                    data2[changedposition]++;
                }
                else
                {
                    totalCompared += data.Length;
                }

                sw.Restart();
                bool same = VectorizedComparer.Same(data, data2);
                sw.Stop();
                secondsSpent += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                Assert.AreEqual(same, !changed);
                for(int bitshift = minBitshift; bitshift <= maxBitshift; bitshift++)
                {
                    sw.Restart();
                    bool test = VectorizedComparer.SameMultiThread(data, data2, 1<<bitshift);
                    sw.Stop();
                    Assert.AreEqual(test, !changed);
                    secondsSpentMultiThread[bitshift] += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                }
            }
            Trace.WriteLine($"{secondsSpent} seconds to compare {totalCompared} bytes");
            for (int bitshift = minBitshift; bitshift <= maxBitshift; bitshift++)
            {

                Trace.WriteLine($"{secondsSpentMultiThread[bitshift]} seconds to compare {totalCompared} bytes (MultiThread {bitshift} bit shift ({1<<bitshift} byte chunks))");
            }

        }

        [TestMethod]
        public void TestVectorComparerMultiThreadChangeNearEnd()
        {
            Random rnd = new Random();

            Int64 totalCompared = 0;
            double secondsSpent = 0;
            double[] secondsSpentMultiThread = new double[100];
            int minBitshift = 10;
            int maxBitshift = 48;

            Stopwatch sw = new Stopwatch();
            for (int t = 0; t < 10; t++)
            {
                byte[] data = new byte[rnd.Next(1_000_000, 100_000_0001)];

                //for (int i = 0; i < data.Length; i++)
                //{
                //   data[i] = (byte)rnd.Next(0, 256);
                //}

                byte[] data2 = (byte[])data.Clone();

                bool changed = rnd.Next(0, 2) > 0;
                if (changed)
                {
                    int changedposition = rnd.Next(data2.Length-Vector<byte>.Count, data2.Length);
                    totalCompared += changedposition;
                    data2[changedposition]++;
                }
                else
                {
                    totalCompared += data.Length;
                }

                sw.Restart();
                bool same = VectorizedComparer.Same(data, data2);
                sw.Stop();
                secondsSpent += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                Assert.AreEqual(same, !changed);
                for(int bitshift = minBitshift; bitshift <= maxBitshift; bitshift++)
                {
                    sw.Restart();
                    bool test = VectorizedComparer.SameMultiThread(data, data2, 1<<bitshift);
                    sw.Stop();
                    Assert.AreEqual(test, !changed);
                    secondsSpentMultiThread[bitshift] += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                }
            }
            Trace.WriteLine($"{secondsSpent} seconds to compare {totalCompared} bytes");
            for (int bitshift = minBitshift; bitshift <= maxBitshift; bitshift++)
            {

                Trace.WriteLine($"{secondsSpentMultiThread[bitshift]} seconds to compare {totalCompared} bytes (MultiThread {bitshift} bit shift ({1<<bitshift} byte chunks))");
            }

        }
        [TestMethod]
        public void TestVectorComparerMultiThreadChangeActualEnd()
        {
            Random rnd = new Random();

            Int64 totalCompared = 0;
            double secondsSpent = 0;
            double[] secondsSpentMultiThread = new double[100];
            int minBitshift = 10;
            int maxBitshift = 48;

            Stopwatch sw = new Stopwatch();
            for (int t = 0; t < 10; t++)
            {
                byte[] data = new byte[rnd.Next(1_000_000, 100_000_0001)];

                //for (int i = 0; i < data.Length; i++)
                //{
                //   data[i] = (byte)rnd.Next(0, 256);
                //}

                byte[] data2 = (byte[])data.Clone();

                bool changed = rnd.Next(0, 2) > 0;
                if (changed)
                {
                    int changedposition = data2.Length-1;
                    totalCompared += changedposition;
                    data2[changedposition]++;
                }
                else
                {
                    totalCompared += data.Length;
                }

                sw.Restart();
                bool same = VectorizedComparer.Same(data, data2);
                sw.Stop();
                secondsSpent += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                Assert.AreEqual(same, !changed);
                for(int bitshift = minBitshift; bitshift <= maxBitshift; bitshift++)
                {
                    sw.Restart();
                    bool test = VectorizedComparer.SameMultiThread(data, data2, 1<<bitshift);
                    sw.Stop();
                    Assert.AreEqual(test, !changed);
                    secondsSpentMultiThread[bitshift] += (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                }
            }
            Trace.WriteLine($"{secondsSpent} seconds to compare {totalCompared} bytes");
            for (int bitshift = minBitshift; bitshift <= maxBitshift; bitshift++)
            {

                Trace.WriteLine($"{secondsSpentMultiThread[bitshift]} seconds to compare {totalCompared} bytes (MultiThread {bitshift} bit shift ({1<<bitshift} byte chunks))");
            }

        }
    }
}
