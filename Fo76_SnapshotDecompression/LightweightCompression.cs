using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fo76_SnapshotDecompression
{
    class lzwCompressionData_t
    {
        public const int LZW_DICT_BITS = 12;
        public const int LZW_DICT_SIZE = 1 << LZW_DICT_BITS;

        public byte[] dictionaryK = new byte[LZW_DICT_SIZE];
        public ushort[] dictionaryW = new ushort[LZW_DICT_SIZE];

        public int nextCode;
        public int codeBits;

        public int codeWord;

        public ulong tempValue;
        public int tempBits;
        public int bytesWritten;
    };

    class LightweightCompression
    {
        public const int LZW_BLOCK_SIZE = (1 << 15);
        public const int LZW_START_BITS = 9;
        public const int LZW_FIRST_CODE = (1 << (LZW_START_BITS - 1));
        public const int DICTIONARY_HASH_BITS = 10;
        public const int MAX_DICTIONARY_HASH = 1 << DICTIONARY_HASH_BITS;
        public const int HASH_MASK = MAX_DICTIONARY_HASH - 1;

        lzwCompressionData_t lzwData = new lzwCompressionData_t();

        ushort[] hash = new ushort[MAX_DICTIONARY_HASH];
        ushort[] nextHash = new ushort[lzwCompressionData_t.LZW_DICT_SIZE];

        // Used by DecompressBlock
        int oldCode;

        byte[] data;        // Read/write
        int maxSize;
        bool overflowed;

        // For reading
        int bytesRead;
        byte[] block = new byte[LZW_BLOCK_SIZE];
        int blockSize;
        int blockIndex;

        // saving/restoring when overflow (when writing). 
        // Must call End directly after restoring (dictionary is bad so can't keep writing)
        int savedBytesWritten;
        int savedCodeWord;
        int saveCodeBits;
        ulong savedTempValue;
        int savedTempBits;



        public void Start(byte[] data_, int maxSize_, bool append)
        {
            // Clear hash
            ClearHash();

            if (append)
            {
                //assert(lzwData->nextCode > LZW_FIRST_CODE);

                int originalNextCode = lzwData.nextCode;

                lzwData.nextCode = LZW_FIRST_CODE;

                // If we are appending, then fill up the hash
                for (int i = LZW_FIRST_CODE; i < originalNextCode; i++)
                {
                    AddToDict(lzwData.dictionaryW[i], lzwData.dictionaryK[i]);
                }

                //assert(originalNextCode == lzwData->nextCode);
            }
            else
            {
                for (int i = 0; i < LZW_FIRST_CODE; i++)
                {
                    lzwData.dictionaryK[i] = (byte)i;
                    lzwData.dictionaryW[i] = 0xFFFF;
                }

                lzwData.nextCode = LZW_FIRST_CODE;
                lzwData.codeBits = LZW_START_BITS;
                lzwData.codeWord = -1;
                lzwData.tempValue = 0;
                lzwData.tempBits = 0;
                lzwData.bytesWritten = 0;
            }

            oldCode = -1;       // Used by DecompressBlock
            data = data_;

            blockSize = 0;
            blockIndex = 0;

            bytesRead = 0;

            maxSize = maxSize_;
            overflowed = false;

            savedBytesWritten = 0;
            savedCodeWord = 0;
            saveCodeBits = 0;
            savedTempValue = 0;
            savedTempBits = 0;
        }

        public int ReadShort()
        {
            byte[] tempBuff = new byte[2];
            this.Read(tempBuff, tempBuff.Length, false);
            return BitConverter.ToInt16(tempBuff, 0);
        }

        public int ReadInt() {
            byte[] tempBuff = new byte[4];
            this.Read(tempBuff, tempBuff.Length, false);
            return BitConverter.ToInt32(tempBuff, 0);
        }

        public int Read(byte[] data, int length, bool ignoreOverflow = false)
        {
            byte[] src = data;

            for (int i = 0; i < length; i++)
            {
                int bByte = ReadByte(ignoreOverflow);

                if (bByte == -1)
                {
                    return i;
                }

                src[i] = (byte)bByte;
            }

            return length;
        }

        int ReadByte(bool ignoreOverflow)
        {
            if (blockIndex == blockSize)
            {
                DecompressBlock();
            }

            if (blockIndex == blockSize)
            { //-V581 DecompressBlock() updates these values, the if() isn't redundant
                if (!ignoreOverflow)
                {
                    overflowed = true;
                    //assert(!"idLZWCompressor::ReadByte overflowed!");
                }
                return -1;
            }

            return block[blockIndex++];
        }

        int ReadBits(int bits)
        {
            int bitsToRead = bits - lzwData.tempBits;

            while (bitsToRead > 0)
            {
                if (bytesRead >= maxSize)
                {
                    return -1;
                }
                //lzwData.tempValue |= (uint64)data[bytesRead++] << lzwData.tempBits;
                ulong tempUlong = BitConverter.ToUInt64(data, bytesRead++);
                lzwData.tempValue |= tempUlong << lzwData.tempBits;
                lzwData.tempBits += 8;
                bitsToRead -= 8;
            }

            int value = (int)lzwData.tempValue & ((1 << bits) - 1);
            lzwData.tempValue >>= bits;
            lzwData.tempBits -= bits;

            return value;
        }

        void WriteBits(uint value, int bits)
        {

            // Queue up bits into temp value
            lzwData.tempValue |= (ulong)value << lzwData.tempBits;
            lzwData.tempBits += bits;

            // Flush 8 bits (1 byte) at a time ( leftovers will get caught in idLZWCompressor::End() )
            while (lzwData.tempBits >= 8)
            {
                if (lzwData.bytesWritten >= maxSize)
                {
                    overflowed = true;
                    return;
                }

                data[lzwData.bytesWritten++] = (byte)(lzwData.tempValue & 255);
                lzwData.tempValue >>= 8;
                lzwData.tempBits -= 8;
            }
        }

        int WriteChain(int code)
        {
            byte[] chain = new byte[lzwCompressionData_t.LZW_DICT_SIZE];
            int firstChar = 0;
            int i = 0;
            do
            {
                //assert(i < lzwCompressionData_t::LZW_DICT_SIZE && code < lzwCompressionData_t::LZW_DICT_SIZE && code >= 0);
                chain[i++] = lzwData.dictionaryK[code];
                code = lzwData.dictionaryW[code];
            } while (code != 0xFFFF);
            firstChar = chain[--i];
            for (; i >= 0; i--)
            {
                block[blockSize++] = chain[i];
            }
            return firstChar;
        }

        int AddToDict(int w, int k)
        {
            //assert(w < 0xFFFF - 1);
            //assert(k < 256);
            //assert(lzwData->nextCode < lzwCompressionData_t::LZW_DICT_SIZE);
            //Console.WriteLine(k.ToString("X"));
            Console.WriteLine(w.ToString("X"));
            lzwData.dictionaryK[lzwData.nextCode] = (byte)k;
            lzwData.dictionaryW[lzwData.nextCode] = (ushort)w;
            int i = HashIndex(w, k);
            nextHash[lzwData.nextCode] = hash[i];
            hash[i] = (ushort)lzwData.nextCode;
            return lzwData.nextCode++;
        }

        static int HashIndex(int w, int k)
        {
            return (w ^ k) & HASH_MASK;
        }

        void DecompressBlock()
        {
            //assert(blockIndex == blockSize);        // Make sure we've read all we can

            blockIndex = 0;
            blockSize = 0;

            int firstChar = -1;
            while (blockSize < LZW_BLOCK_SIZE - lzwCompressionData_t.LZW_DICT_SIZE)
            {
                //assert(lzwData->codeBits <= lzwCompressionData_t.LZW_DICT_BITS);

                int code = ReadBits(lzwData.codeBits);
                if (code == -1)
                {
                    break;
                }

                if (oldCode == -1)
                {
                    //assert(code < 256);
                    block[blockSize++] = (byte)code;
                    oldCode = code;
                    firstChar = code;
                    continue;
                }

                if (code >= lzwData.nextCode)
                {
                    //assert(code == lzwData->nextCode);
                    firstChar = WriteChain(oldCode);
                    block[blockSize++] = (byte)firstChar;
                }
                else
                {
                    firstChar = WriteChain(code);
                }
                AddToDict(oldCode, firstChar);
                if (BumpBits())
                {
                    oldCode = -1;
                }
                else
                {
                    oldCode = code;
                }
            }
        }

        bool BumpBits()
        {
            if (lzwData.nextCode == (1 << lzwData.codeBits))
            {
                lzwData.codeBits++;
                if (lzwData.codeBits > lzwCompressionData_t.LZW_DICT_BITS)
                {
                    lzwData.nextCode = LZW_FIRST_CODE;
                    lzwData.codeBits = LZW_START_BITS;
                    ClearHash();
                    return true;
                }
            }
            return false;
        }

        void ClearHash()
        {
            //memset(hash, 0xFF, sizeof(hash));
            for(var i = 0; i < hash.Length; i++) {
                hash[i] = 0xFF;
            }
        }
    }
}
