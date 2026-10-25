// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Diagnostics.DebugProvider, after dotnet/runtime (release/11.0) DebugProvider.cs and
// DebugProvider.Unix.cs; see THIRD-PARTY-NOTICES.md. Public in System.Private.CoreLib but not in the
// reference pack, so it lives here under its own name, and the System.Private.CoreLib facade
// (Frontend/CoreLibFacade.cs) forwards to it: System.Diagnostics.TraceSource's TraceProvider derives
// from it, and Trace installs one with Debug.SetProvider so that Debug output reaches Trace's listeners.
// Debug output goes to the debugger in .NET (syslog on Unix, OutputDebugString on Windows), which a
// program can't observe; with DOTNET_DebugWriteToStdErr=1 it is also written to stderr, ASCII only.
#nullable disable
using System;

namespace System.Diagnostics
{
    public class DebugProvider
    {
        private static readonly object s_lock = new object();
        private static readonly bool s_shouldWriteToStdErr = Environment.GetEnvironmentVariable("DOTNET_DebugWriteToStdErr") == "1";
        private bool _needIndent = true;
        private string _indentString;

        public virtual void Fail(string message, string detailMessage) => CsharpcCoreLib.DebugImpl.FailDefault(message, detailMessage);

        public virtual void Write(string message)
        {
            lock (s_lock)
            {
                if (message == null)
                {
                    WriteCore(string.Empty);
                    return;
                }
                if (_needIndent)
                {
                    message = GetIndentString() + message;
                    _needIndent = false;
                }
                WriteCore(message);
                if (message.EndsWith(Environment.NewLine, StringComparison.Ordinal)) _needIndent = true;
            }
        }

        public virtual void WriteLine(string message) => Write(message + Environment.NewLine);

        public virtual void OnIndentLevelChanged(int indentLevel) { }

        public virtual void OnIndentSizeChanged(int indentSize) { }

        public static void FailCore(string stackTrace, string message, string detailMessage, string errorSource) =>
            CsharpcCoreLib.DebugImpl.FailDefault(message, detailMessage);

        public static void WriteCore(string message)
        {
            if (!s_shouldWriteToStdErr || string.IsNullOrEmpty(message)) return;
            var ascii = new System.Text.StringBuilder(message.Length);
            foreach (char c in message) if (c <= 0x7F) ascii.Append(c);
            Console.Error.Write(ascii.ToString());
            Console.Error.Flush();
        }

        private string GetIndentString()
        {
            int indentCount = Debug.IndentSize * Debug.IndentLevel;
            if (_indentString?.Length == indentCount) return _indentString;
            return _indentString = new string(' ', indentCount);
        }
    }
}
