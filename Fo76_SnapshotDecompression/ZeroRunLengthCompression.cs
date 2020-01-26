using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fo76_SnapshotDecompression
{
    class ZeroRunLengthCompression
    {
        int zeroCount;      // Number of pending zeroes
        MemoryStream comp;
        byte[] destStart;
        MemoryStream dest;
        int compressed;     // Compressed size
        int maxSize;

        public void Start(byte[] dest_, byte[] comp_, int maxSize_)
        {
            zeroCount = 0;
            dest = new MemoryStream(dest_);
            comp = new MemoryStream(comp_);
            compressed = 0;
            maxSize = maxSize_;
        }

        bool WriteRun()
        {
            if (zeroCount > 0)
            {
                //assert(zeroCount <= 255);
                if (compressed + 2 > maxSize)
                {
                    maxSize = -1;
                    return false;
                }
                if (comp != null)
                {
                    comp.WriteByte(0);
                    comp.WriteByte((byte)zeroCount);
                }
                else
                {
                    dest.WriteByte(0);
                    //*dest++ = 0;
                    dest.WriteByte((byte)zeroCount);
                    //*dest++ = (uint8)zeroCount;
                }
                compressed += 2;
                zeroCount = 0;
            }
            return true;
        }

        bool WriteByte(byte value)
        {
            if (value != 0 || zeroCount >= 255)
            {
                if (!WriteRun())
                {
                    maxSize = -1;
                    return false;
                }
            }

            if (value != 0)
            {
                if (compressed + 1 > maxSize)
                {
                    maxSize = -1;
                    return false;
                }
                if (comp != null)
                {
                    comp.WriteByte(value);
                }
                else
                {
                    dest.WriteByte(value);
                    //*dest++ = value;
                }
                compressed++;
            }
            else
            {
                zeroCount++;
            }

            return true;
        }

        byte ReadByte()
        {
            // See if we need to possibly read more data
            if (zeroCount == 0)
            {
                int value = ReadInternal();
                if (value == -1)
                {
                    //assert(0);
                }
                if (value != 0)
                {
                    return (byte)value; // Return non zero values immediately
                }
                // Read the number of zeroes
                zeroCount = ReadInternal();
            }

            //assert(zeroCount > 0);

            zeroCount--;
            return 0;
        }

        public void ReadBytes(byte[] dest, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dest[i] = this.ReadByte();
                //*dest++ = ReadByte();
            }
        }

        public void WriteBytes(byte[] src, int count)
        {
            for (int i = 0; i < count; i++)
            {
                this.WriteByte(src[i]);
                //WriteByte(*src++);
            }
        }

        int End()
        {
            WriteRun();
            if (maxSize == -1)
            {
                return -1;
            }
            return compressed;
        }

        int ReadInternal()
        {
            compressed++;
            if (comp != null)
            {
                return comp.ReadByte();
            }
            return comp.ReadByte();
        }
    }
}
