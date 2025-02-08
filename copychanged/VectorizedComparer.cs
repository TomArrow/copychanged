using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using size_t = System.Int32; // update if this ever changes *shrug*
using System.IO;
using System.Threading;
using System.Diagnostics;

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
        
        public static bool SameStreamWholeTest(Stream stream, Stream stream2, size_t chunkLength = chunkLengthDefault)
        {
            if (stream.Length != stream2.Length)
            {
                return false;
            }
            Stopwatch sw = new Stopwatch();
            sw.Start();
            byte[] data = new byte[stream.Length];
            byte[] data2 = new byte[stream2.Length];
            stream.Read(data, 0, data.Length);
            stream2.Read(data2, 0, data2.Length);
            sw.Stop();
            double secs = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
#if DEBUG
            Trace.WriteLine("DEBUG MODE");
#endif
            Trace.WriteLine($"Reading 2x{stream.Length}byte streams into byte[] took {secs} secs");
            if (data.Length <= chunkLength)
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

        static private Task StartReadingStream(Stream stream, CancellationToken ct, Queue<byte[]> chunks, object chunksLock, ReferenceBool unequal, ReferenceBool finished, int streamChunkLength)
        {

            if (streamChunkLength == 0)
            {
                streamChunkLength = streamChunkLengthDefault;
            }
            Task task = Task.Factory.StartNew(() =>
            {
                Int64 dataRead = 0;

                while (stream.Length > dataRead && !unequal.b)
                {
                    if (ct.IsCancellationRequested || unequal.b)
                    {
                        unequal.b = true;
                        return;
                    }

                    lock (chunks)
                    {
                        while (chunks.Count > 4 && !unequal.b)
                        {
                            // avoid crazy RAM overflow if one stream is reading much faster than the other
                            System.Threading.Monitor.Wait(chunks);
                        }
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
                        //Debug.WriteLine($"StartReadingStream(s,s): adding chunk ({buff.Length} bytes)");
                        chunks.Enqueue(buff);
                        System.Threading.Monitor.Pulse(chunksLock);
                    }

                }


            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith((t) =>
            {
                lock (chunksLock)
                {
                    finished.b = true;
                    System.Threading.Monitor.Pulse(chunksLock);
                }
                throw new Exception("Stream reading failed", t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted).ContinueWith((t)=> {
                lock (chunksLock)
                {
                    finished.b = true;
                    System.Threading.Monitor.Pulse(chunksLock);
                }
                //throw new Exception("Test", t.Exception);
            });

            return task;
        }

        const int streamChunkLengthDefault = 1 << 26;  //chunkLengthDefault * 32; // could instead use amount of cpu cores or sth?
        static public bool Same(Stream in1, Stream in2, CancellationToken ct, int streamChunkLength = streamChunkLengthDefault, bool forcefinish=false)
        {
            if (in1.Length != in2.Length)
            {
                return false;
            }

            if(streamChunkLength == 0)
            {
                streamChunkLength = streamChunkLengthDefault;
            }

            in1.Seek(0, SeekOrigin.Begin);
            in2.Seek(0, SeekOrigin.Begin);

#if DEBUGSTREAM
            Debug.WriteLine($"Same(s,s): starting compare; chunkLength: {streamChunkLength}, forceFinish: {forcefinish}");
#endif
            object chunksLock = new object();

            Queue<byte[]> compareChunks1 = new Queue<byte[]>();
            Queue<byte[]> compareChunks2 = new Queue<byte[]>();

            UInt64 totalCompared = 0;

            ReferenceBool finished1 = new ReferenceBool { b=false};
            ReferenceBool finished2 = new ReferenceBool { b=false};
            ReferenceBool unequal = new ReferenceBool { b=false};

            Task read1 = StartReadingStream(in1, ct, compareChunks1, chunksLock, unequal, finished1, streamChunkLength);
            Task read2 = StartReadingStream(in2, ct, compareChunks2, chunksLock, unequal, finished2, streamChunkLength);

            while (true)
            {
                if (unequal.b)
                {
#if DEBUGSTREAM
                    Debug.WriteLine("Same(s,s): unequal,breaking.");
#endif
                    break;
                }
                byte[] chunk1, chunk2;
                lock (chunksLock)
                {
                    while ((compareChunks1.Count == 0 || compareChunks2.Count == 0) && (!finished1.b || !finished2.b))
                    {
#if DEBUGSTREAM
                        Debug.WriteLine("Same(s,s): waiting for input");
#endif
                        System.Threading.Monitor.Wait(chunksLock);
                    }
#if DEBUGSTREAM
                    Debug.WriteLine("Same(s,s): processing status update");
#endif
                    if (compareChunks1.Count == 0 || compareChunks2.Count == 0)
                    {
#if DEBUGSTREAM
                        Debug.WriteLine("Same(s,s): finished");
#endif
                        break; 
                    }
#if DEBUGSTREAM
                    Debug.WriteLine("Same(s,s): dequeueing");
#endif
                    lock (compareChunks1)
                    {
                        chunk1 = compareChunks1.Dequeue();
                        System.Threading.Monitor.Pulse(compareChunks1);
                    }
                    lock (compareChunks2)
                    {
                        chunk2 = compareChunks2.Dequeue();
                        System.Threading.Monitor.Pulse(compareChunks2);
                    }
                }

                totalCompared += (UInt64)chunk1.Length;
#if DEBUGSTREAM
                Debug.WriteLine($"Same(s,s): comparing. progress: {totalCompared}/{in1.Length}/{in2.Length}");
#endif
                if (!SameMultiThread(chunk1, chunk2))
                {
#if DEBUGSTREAM
                    Debug.WriteLine("Same(s,s): unequal,done.");
#endif
                    unequal.b = true;
                    break;
                }
            }

            if (forcefinish)
            {
                read1.Wait();
                read2.Wait();
            }


            return !unequal.b;
        }
    }
}
