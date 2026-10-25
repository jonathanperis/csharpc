// System.IO.Pipes: not implemented by csharpc yet. The types exist so code that mentions them compiles
// (Kestrel's named-pipe transport, type tests); creating a pipe throws NotSupportedException.
#nullable disable
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.Pipes.PipeStream")]
    public abstract class PipeStreamImpl : Stream
    {
        internal static NotSupportedException NotSupported() => new NotSupportedException("System.IO.Pipes is not supported by csharpc yet.");

        protected PipeStreamImpl(PipeDirection direction, int bufferSize) => throw NotSupported();

        public bool IsConnected => false;
        public override bool CanRead => false;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw NotSupported();
        public override void Write(byte[] buffer, int offset, int count) => throw NotSupported();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Shadows("System.IO.Pipes.NamedPipeServerStream")]
    public sealed class NamedPipeServerStreamImpl : PipeStreamImpl
    {
        public const int MaxAllowedServerInstances = -1;

        public NamedPipeServerStreamImpl(string pipeName) : base(PipeDirection.InOut, 0) { }
        public NamedPipeServerStreamImpl(string pipeName, PipeDirection direction) : base(direction, 0) { }
        public NamedPipeServerStreamImpl(string pipeName, PipeDirection direction, int maxNumberOfServerInstances) : base(direction, 0) { }
        public NamedPipeServerStreamImpl(string pipeName, PipeDirection direction, int maxNumberOfServerInstances, PipeTransmissionMode transmissionMode)
            : base(direction, 0) { }
        public NamedPipeServerStreamImpl(string pipeName, PipeDirection direction, int maxNumberOfServerInstances, PipeTransmissionMode transmissionMode,
            PipeOptions options) : base(direction, 0) { }
        public NamedPipeServerStreamImpl(string pipeName, PipeDirection direction, int maxNumberOfServerInstances, PipeTransmissionMode transmissionMode,
            PipeOptions options, int inBufferSize, int outBufferSize) : base(direction, 0) { }

        public void WaitForConnection() => throw NotSupported();
        public Task WaitForConnectionAsync() => throw NotSupported();
        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => throw NotSupported();
        public void Disconnect() => throw NotSupported();
    }
}
