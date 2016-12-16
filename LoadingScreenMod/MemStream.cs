using System;
using System.IO;
using System.Reflection;
using System.Text;
using ColossalFramework.Packaging;

namespace LoadingScreenMod
{
    internal sealed class MemStream : Stream
    {
        char[] charBuf = new char[96];
        byte[] buf;
        int pos;

        static readonly UTF8Encoding utf = new UTF8Encoding(false, false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Position { get { return pos; } set { pos = (int) value; } }
        protected override void Dispose(bool b) { buf = null; charBuf = null; base.Dispose(b); }

        public override long Length { get { throw new NotImplementedException(); } }
        public override void SetLength(long value) { throw new NotImplementedException(); }
        public override void Flush() { throw new NotImplementedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotImplementedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotImplementedException(); }

        internal MemStream(byte[] buf, int pos)
        {
            this.buf = buf;
            this.pos = pos;
        }

        internal byte B8() => buf[pos++];
        internal int I8() => buf[pos++];
        internal void Skip(int count) => pos += count;

        internal int ReadInt32()
        {
            Trace.Tra("ReadInt32");
            return I8() | I8() << 8 | I8() << 16 | I8() << 24;
        }

        internal ulong ReadUInt64()
        {
            Trace.Tra("ReadUInt64");
            uint i1 = (uint) ReadInt32(), i2 = (uint) ReadInt32();
            return i1 | (ulong) i2 << 32;
        }

        internal float ReadSingle()
        {
            Trace.Tra("ReadSingle");
            float f;

            unsafe
            {
                byte* p = (byte*) &f;
                byte[] b = buf;
                *p = b[pos++];
                p[1] = b[pos++];
                p[2] = b[pos++];
                p[3] = b[pos++];
            }

            return f;
        }

        internal string ReadString()
        {
            Trace.Tra("ReadString");
            Trace.stringRead -= Profiling.Micros;
            int len = ReadEncodedInt();

            if (len == 0)
            {
                Trace.stringRead += Profiling.Micros;
                return string.Empty;
            }
            if (len < 0 || len > 32767)
                throw new IOException("Invalid binary file: string len " + len);

            if (charBuf.Length < len)
                charBuf = new char[len];

            int n = utf.GetChars(buf, pos, len, charBuf, 0);
            Skip(len);
            string s = new string(charBuf, 0, n);
            Trace.stringRead += Profiling.Micros;
            return s;
        }

        int ReadEncodedInt()
        {
            int ret = 0, shift = 0, i;

            for (i = 0; i < 5; i++)
            {
                byte b = B8();
                ret |= (b & 127) << shift;
                shift += 7;

                if ((b & 128) == 0)
                    return ret;
            }

            throw new FormatException("Too many bytes in what should have been a 7 bit encoded Int32.");
        }

        internal byte[] ReadBytes(int count)
        {
            byte[] result = new byte[count];
            Buffer.BlockCopy(buf, pos, result, 0, count);
            Skip(count);
            return result;
        }

        public override int ReadByte() => buf[pos++];

        public override int Read(byte[] result, int offset, int count)
        {
            byte[] b = buf;

            for (int i = 0; i < count; i++)
                result[offset++] = b[pos++];

            return count;
        }
    }

    internal sealed class MemReader : PackageReader
    {
        MemStream stream;

        protected override void Dispose(bool b) { stream = null; base.Dispose(b); }
        public override float ReadSingle() => stream.ReadSingle();
        public override int ReadInt32() => stream.ReadInt32();
        public override ulong ReadUInt64() => stream.ReadUInt64();
        public override byte ReadByte() { Trace.Tra("ReadByte"); return stream.B8(); }
        public override bool ReadBoolean() { Trace.Tra("ReadBoolean"); return stream.B8() != 0; }
        public override byte[] ReadBytes(int count) { Trace.Tra("ReadBytes"); return stream.ReadBytes(count); }
        public override string ReadString() => stream.ReadString();

        internal MemReader(MemStream stream) : base(stream)
        {
            this.stream = stream;
        }
    }
}
