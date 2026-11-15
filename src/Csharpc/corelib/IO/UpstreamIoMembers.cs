// CoreLib-internal members the framework's stream, reader and writer IL calls (upstream-types/System.IO.txt) on types csharpc serves
// itself: Task, ValueTask, Decimal and Encoding (the reference pack does not declare them). Each does what CoreLib's does.
#nullable disable
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Threading.Tasks.Task")]
    internal static class TaskCanceledWith
    {
        /// <summary>
        /// Task.FromCanceled(OperationCanceledException): a task canceled with that exception's token, which `await` rethrows itself
        /// (MemoryStream.ReadAsync's `catch (OperationCanceledException oce) { return Task.FromCanceled&lt;int&gt;(oce); }`).
        /// </summary>
        public static Task FromCanceled(OperationCanceledException exception)
        {
            var source = new TaskCompletionSource();
            CoreIntrinsics.TryFaultAsyncTask(source.Task, exception);
            return source.Task;
        }

        public static Task<TResult> FromCanceled<TResult>(OperationCanceledException exception)
        {
            var source = new TaskCompletionSource<TResult>();
            CoreIntrinsics.TryFaultAsyncTask(source.Task, exception);
            return source.Task;
        }
    }

    [ShadowMembers("System.Threading.Tasks.ValueTask")]
    internal static class ValueTaskFromTyped
    {
        /// <summary>
        /// ValueTask.DangerousCreateFromTypedValueTask: the untyped view of a ValueTask&lt;TResult&gt; (Stream.WriteAsync over a
        /// ReadOnlyMemory, TextWriter.WriteAsync). It consumes the typed one, as CoreLib's reuses its object and token.
        /// </summary>
        public static ValueTask DangerousCreateFromTypedValueTask<TResult>(ValueTask<TResult> valueTask) =>
            valueTask.IsCompletedSuccessfully ? default : new ValueTask(valueTask.AsTask());
    }

    [ShadowMembers("System.Decimal")]
    internal static class DecimalBytes
    {
        /// <summary>Decimal.GetBytes(in decimal, Span&lt;byte&gt;): lo, mid, hi and flags as little-endian Int32s (BinaryWriter.Write(decimal)).</summary>
        public static void GetBytes(in decimal d, Span<byte> buffer)
        {
            int[] bits = decimal.GetBits(d);
            BinaryPrimitives.WriteInt32LittleEndian(buffer, bits[0]);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), bits[1]);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), bits[2]);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(12), bits[3]);
        }

        /// <summary>
        /// Decimal.ToDecimal(ReadOnlySpan&lt;byte&gt;): the inverse (BinaryReader.ReadDecimal). Invalid flags are the ArgumentException of
        /// decimal(int[]), which BinaryReader turns into IOException("Decimal ... ") as CoreLib's does.
        /// </summary>
        public static decimal ToDecimal(ReadOnlySpan<byte> span) => new decimal(new[]
        {
            BinaryPrimitives.ReadInt32LittleEndian(span), BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4)),
            BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8)), BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12)),
        });
    }

    [ShadowMembers("System.Runtime.CompilerServices.RuntimeHelpers")]
    internal static class RuntimeHelpersAsyncMembers
    {
        /// <summary>
        /// RuntimeHelpers.IsRuntimeAsync: CoreLib's IL body is `return false`, which a runtime that compiles the calling method as runtime-async
        /// replaces with true. StreamReader/StreamWriter choose their task-tracking path with it; csharpc takes the IL's own answer.
        /// </summary>
        public static bool IsRuntimeAsync() => false;
    }

    [ShadowMembers("System.Boolean")]
    internal static class BooleanTrueFalseText
    {
        /// <summary>bool.IsTrueStringIgnoreCase (AppContextConfigHelper's switches): "true" in ASCII letters of either case, as upstream's test.</summary>
        public static bool IsTrueStringIgnoreCase(ReadOnlySpan<char> value) =>
            value.Length == 4 && (value[0] | 0x20) == 't' && (value[1] | 0x20) == 'r' && (value[2] | 0x20) == 'u' && (value[3] | 0x20) == 'e';

        public static bool IsFalseStringIgnoreCase(ReadOnlySpan<char> value) =>
            value.Length == 5 && (value[0] | 0x20) == 'f' && (value[1] | 0x20) == 'a' && (value[2] | 0x20) == 'l' && (value[3] | 0x20) == 's'
            && (value[4] | 0x20) == 'e';
    }

    [ShadowMembers("System.Runtime.Serialization.SerializationInfo")]
    internal static class SerializationGuardMembers
    {
        /// <summary>
        /// SerializationInfo.ThrowIfDeserializationInProgress (FileStream's guard against writing files while deserializing), as upstream:
        /// throws while SerializationInfo.StartDeserialization is in effect (framework IL such as System.Data's DataSet starts it) unless the
        /// AppContext switch Switch.System.Runtime.Serialization.SerializationGuard.{switchSuffix} is true. cachedValue: 0 not read yet,
        /// 1 the switch is true, -1 it is not.
        /// </summary>
        public static void ThrowIfDeserializationInProgress(string switchSuffix, ref int cachedValue)
        {
            const string SwitchPrefix = "Switch.System.Runtime.Serialization.SerializationGuard.";
            if (cachedValue == 0)
                cachedValue = AppContext.TryGetSwitch(SwitchPrefix + switchSuffix, out bool isEnabled) && isEnabled ? 1 : -1;
            if (cachedValue == 1) return;
            if (cachedValue != -1) throw new ArgumentOutOfRangeException(nameof(cachedValue));
            if (SerializationInfoImpl.DeserializationInProgress)
                throw new System.Runtime.Serialization.SerializationException("An action was attempted during deserialization that could lead to a "
                    + "security vulnerability. The action has been aborted. To allow the action, set the '" + SwitchPrefix + switchSuffix
                    + "' AppContext switch to true.");
        }
    }

    [ShadowMembers("System.Text.Encoding")]
    internal static class EncodingCodePageMembers
    {
        /// <summary>Encoding.IsUTF8CodePage: BinaryWriter and BinaryReader take their UTF-8 fast paths for it.</summary>
        [ShadowInstance] public static bool get_IsUTF8CodePage(System.Text.Encoding encoding) => encoding.CodePage == 65001;
    }
}
