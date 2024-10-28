using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using size_t = System.Int32; // update if this ever changes *shrug*

namespace copychanged
{
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
    }
}
