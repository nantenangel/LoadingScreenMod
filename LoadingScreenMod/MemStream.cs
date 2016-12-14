using System;
using System.IO;
using System.Reflection;
using System.Text;
using ColossalFramework.Packaging;

namespace LoadingScreenMod
{
    internal sealed class MemStream : Stream
    {
        byte[] buf;
        int pos;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Position { get { return pos; } set { pos = (int) value; } }
        internal int Pos => pos;
        internal byte[] Buf => buf;
        protected override void Dispose(bool b) { buf = null; base.Dispose(b); }

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

        int B() => buf[pos++];
        internal void Skip(int count) => pos += count;

        internal int ReadInt32()
        {
            // Trace.Tra(MethodBase.GetCurrentMethod().Name);
            return B() | B() << 8 | B() << 16 | B() << 24;
        }

        internal float ReadSingle()
        {
            // Trace.Tra(MethodBase.GetCurrentMethod().Name);
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
        Decoder decoder;
        char[] charBuf = new char[128];

        protected override void Dispose(bool b)
        {
            charBuf = null; decoder = null; stream = null;
            base.Dispose(b);
        }

        internal MemReader(MemStream stream) : base(stream)
        {
            this.stream = stream;
            this.decoder = (Decoder) typeof(BinaryReader).GetField("decoder", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this);
        }

        public override float ReadSingle() => stream.ReadSingle();
        public override int ReadInt32() => stream.ReadInt32();

        public override string ReadString()
        {
            Trace.stringRead -= Profiling.Micros;
            int len = Read7BitEncodedInt();

            if (len == 0)
            {
                Trace.stringRead += Profiling.Micros;
                return string.Empty;
            }
            if (len < 0 || len > 32767)
                throw new IOException("Invalid binary file: string len " + len);

            if (charBuf.Length < len)
                charBuf = new char[len];

            int n = decoder.GetChars(stream.Buf, stream.Pos, len, charBuf, 0);
            stream.Skip(len);
            string s = new string(charBuf, 0, n);
            Trace.stringRead += Profiling.Micros;

            if (n > Trace.maxString)
            {
                Trace.maxString = n;
                Trace.Ind(20, "MAX STRING LEN", n, s);
            }

            return s;
        }
    }
}
