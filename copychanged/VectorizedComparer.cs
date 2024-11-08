using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using size_t = System.Int32; // update if this ever changes *shrug*
using System.IO;
using System.Threading;

namespace copychanged
{
    internal class ReferenceBool {
        public bool b = false;
    }

    public static class VectorizedComparer
    {


        static readonly int vectorByteCount = Vector<byte>.Count;
        public static bool Same(byte[] data, byte[] data2, size_t from = 0, size_t to = size_t.MaxValue)
        {
            if (data.Length != data2.Length) { 
                return false; 
            }
            size_t compareUpTo = Math.Min(to,data.Length-1); 
            size_t position = from;
            Vector<byte> a, b;
            while((compareUpTo - position + 1) > vectorByteCount)
            {
                a = new Vector<byte>(data,position);
                b = new Vector<byte>(data2,position);
                if (!a.Equals(b)) {
                    return false;
                }
                position += vectorByteCount;
            }

            while(position <= compareUpTo)
            {
                if (data[position] != data2[position])
                {
                    return false;
                }
                position++;
            }

            return true;
        }
        public static bool Same(byte[] data, byte[] data2,ref bool cancel, size_t from = 0, size_t to = size_t.MaxValue)
        {
            if (data.Length != data2.Length) { 
                return false; 
            }
            size_t compareUpTo = Math.Min(to,data.Length-1); 
            size_t position = from;
            Vector<byte> a, b;
            while((compareUpTo - position + 1) > vectorByteCount)
            {
                a = new Vector<byte>(data,position);
                b = new Vector<byte>(data2,position);
                if (!a.Equals(b) || cancel) {
                    return false;
                }
                position += vectorByteCount;
            }

            while(position <= compareUpTo)
            {
                if (data[position] != data2[position] || cancel)
                {
                    return false;
                }
                position++;
            }

            return true;
        }

        const size_t chunkLengthDefault = (size_t)(1<<20); // seems to be roughly a sweet spot. Sometimes a bit less, sometimes a bit more is better for short times but this always seems somewhat decent. for always bigger compare sizes, 23 might be a tiny bit better, but 20 is still fine
        public static bool SameMultiThread(byte[] data, byte[] data2, size_t chunkLength = chunkLengthDefault)
        {
            if (data.Length != data2.Length)
            {
                return false;
            }
            if(data.Length <= chunkLength)
            {
                return Same(data, data2);
            }
            size_t chunkCount = data.Length / chunkLength;
            if(data.Length % chunkLength != 0)
            {
                chunkCount++;
            }
            bool anyFalse = false;
            Parallel.For(0,chunkCount,(i,state)=>
            {
                if (!Same(data,data2,ref anyFalse,chunkLength * i,chunkLength*i+chunkLength-1))
                {
                    anyFalse = true;
                    state.Break();
                }
            });
            return !anyFalse;
        }

        static private Task StartReadingStream(Stream stream, CancellationToken ct, Queue<byte[]> chunks, object chunksLock, ReferenceBool unequal, int streamChunkLength)
        {
            Task task = Task.Factory.StartNew(() =>
            {
                Int64 dataRead = 0;

                while (stream.Length > dataRead && !unequal.b)
                {
                    if (ct.IsCancellationRequested)
                    {
                        unequal.b = true;
                        return;
                    }
                    byte[] buff = new byte[Math.Min(streamChunkLength, stream.Length - dataRead)];
                    size_t amountRead = 0;
                    while (amountRead < buff.Length && !unequal.b)
                    {
                        size_t readCount = stream.Read(buff, amountRead, buff.Length - amountRead);
                        amountRead += readCount;
                        dataRead += readCount;
                        if (readCount == 0)
                        {
                            if (readCount < buff.Length)
                            {
                                unequal.b = true; // technically this is just some weird read error but lets just say its unequal for now *shrug*
                            }
                            break;
                        }
                    }
                    if (unequal.b) return;

                    lock (chunksLock)
                    {
                        chunks.Enqueue(buff);
                        System.Threading.Monitor.Pulse(chunksLock);
                    }

                }


            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith((t) =>
            {
                throw new Exception("Stream reading failed", t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);

            return task;
        }

        const int streamChunkLengthDefault = 1 << 28;  //chunkLengthDefault * 32; // could instead use amount of cpu cores or sth?
        static public bool Same(Stream in1, Stream in2, CancellationToken ct, int streamChunkLength = streamChunkLengthDefault)
        {
            if (in1.Length != in2.Length)
            {
                return false;
            }

            object chunksLock = new object();

            Queue<byte[]> compareChunks1 = new Queue<byte[]>();
            Queue<byte[]> compareChunks2 = new Queue<byte[]>();

            ReferenceBool unequal = new ReferenceBool { b=false};

            Task read1 = StartReadingStream(in1, ct, compareChunks1, chunksLock, unequal, streamChunkLength);
            Task read2 = StartReadingStream(in2, ct, compareChunks2, chunksLock, unequal, streamChunkLength);

            while (true)
            {
                if (unequal.b)
                {
                    break;
                }
                bool allDone = read1.IsCompleted && read2.IsCompleted;
                int count1 = 0;
                int count2 = 0;
                lock (compareChunks1)
                {
                    count1 = compareChunks1.Count;
                }
                lock (compareChunks2)
                {
                    count2 = compareChunks2.Count;
                }
                if (count1 == 0 || count2 == 0)
                {
                    if (allDone)
                    {
                        break;
                    }
                    lock (chunksLock)
                    {
                        System.Threading.Monitor.Wait(chunksLock);
                    }
                    continue;
                }

                byte[] chunk1, chunk2;
                lock (chunksLock)
                {
                    chunk1 = compareChunks1.Dequeue();
                    chunk2 = compareChunks2.Dequeue();
                }
                if (!SameMultiThread(chunk1, chunk2))
                {
                    unequal.b = true;
                    //read1.Wait();
                    //read2.Wait();
                    return false;
                }
            }

            //Task.Factory.StartNew(() =>
            //{
            //    Int64 dataRead = 0;

            //    while(in1.Length > dataRead && !unequal.b)
            //    {
            //        byte[] buff = new byte[Math.Min(streamChunkLength, in1.Length - dataRead)];
            //        size_t amountRead = 0;
            //        while(amountRead < buff.Length && !unequal.b)
            //        {
            //            size_t readCount = in1.Read(buff, amountRead, buff.Length - amountRead);
            //            amountRead += readCount;
            //            if(readCount == 0)
            //            {
            //                if(readCount < buff.Length)
            //                {
            //                    unequal.b = true; // technically this is just some weird read error but lets just say its unequal for now *shrug*
            //                }
            //                break;
            //            }
            //        }
            //        if (unequal.b) return;

            //        compareChunks1.Enqueue(buff);

            //    }


            //}, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith((t) =>
            //{
            //    throw new Exception("Stream reading failed",t.Exception);
            //}, TaskContinuationOptions.OnlyOnFaulted);

            return !unequal.b;
        }
    }
}
