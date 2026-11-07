using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using static Csharpc.Llvm.Native;

namespace Csharpc.CodeGen;

/// <summary>
/// `-g` for code lowered from IL (docs/13-debugging.md, "From the assembly's PDB"): the DISubprogram, the
/// line table and the local names come from the portable PDB csc wrote — embedded in the assembly or beside
/// it — the same one StackTraceInfo reads for stack traces. Methods without sequence points (an assembly
/// without a PDB, the precompiled corelib, shared-framework assemblies) get no debug info.
/// </summary>
public sealed partial class DebugInfo
{
    private readonly Dictionary<IlAssembly, IlPdb?> _pdbs = [];

    private IlPdb? PdbOf(IlAssembly assembly)
    {
        if (_pdbs.TryGetValue(assembly, out var known)) return known;
        IlPdb? pdb = null;
        if (!assembly.IsCoreLib && !assembly.IsFramework)
        {
            try
            {
                if (StackTraceInfo.PdbOf(assembly, sibling: true) is { } provider) pdb = new IlPdb(provider);
            }
            catch (Exception e) when (e is BadImageFormatException or IOException or InvalidOperationException)
            {
                // Unreadable PDB: the same as none (StackTraceInfo reports it when stack traces need it).
            }
        }
        return _pdbs[assembly] = pdb;
    }

    private void DisposePdbs()
    {
        foreach (var pdb in _pdbs.Values) pdb?.Dispose();
        _pdbs.Clear();
    }

    internal LLVMMetadataRef DocumentScope(LLVMMetadataRef scope, string document) =>
        LLVMDIBuilderCreateLexicalBlockFile(_di, scope, File(document), 0);

    private FunctionDebug? BeginIlFunction(LLVMValueRef fn, IMethodSymbol method)
    {
        if (_m.Il.OwnerOf(method) is not { } owner || PdbOf(owner) is not { } pdb) return null;
        if (pdb.Lines(method.OriginalDefinition) is not { } lines) return null;
        var file = File(lines.Document);
        var line = (uint)lines.Line;
        if (LLVMGetSubprogram(fn) is { IsNull: false } existing) return new FunctionDebug(this, existing, file, (int)line) { Il = lines };
        var name = ReadableName(method);
        _emptySubroutine = _emptySubroutine.IsNull ? LLVMDIBuilderCreateSubroutineType(_di, _unitFile, [], 0, 0) : _emptySubroutine;
        var sp = LLVMDIBuilderCreateFunction(_di, file, name, Len(name), "", 0, file, line, _emptySubroutine, 1, 1, line, FlagPrototyped, 0);
        LLVMSetSubprogram(fn, sp);
        return new FunctionDebug(this, sp, file, (int)line) { Il = lines };
    }
}

/// <summary>One assembly's portable PDB: sequence points and local names per method.</summary>
internal sealed class IlPdb(MetadataReaderProvider provider) : IDisposable
{
    private readonly MetadataReader _pdb = provider.GetMetadataReader();
    private readonly Dictionary<DocumentHandle, string> _documents = [];
    private readonly Dictionary<int, IlMethodLines?> _methods = [];

    public IlMethodLines? Lines(IMethodSymbol method)
    {
        var token = method.MetadataToken;
        if (_methods.TryGetValue(token, out var known)) return known;
        return _methods[token] = Read(token);
    }

    private IlMethodLines? Read(int token)
    {
        if (MetadataTokens.EntityHandle(token) is not { Kind: HandleKind.MethodDefinition } entity) return null;
        var handle = (MethodDefinitionHandle)entity;
        List<IlSequencePoint> points = [];
        foreach (var sp in _pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle()).GetSequencePoints())
            points.Add(sp.IsHidden ? new IlSequencePoint(sp.Offset, 0, 0, null) : new IlSequencePoint(sp.Offset, sp.StartLine, sp.StartColumn, Document(sp.Document)));
        var first = points.FirstOrDefault(p => p.Document is not null);
        if (first.Document is null) return null;
        Dictionary<int, string> locals = [];
        foreach (var scopeHandle in _pdb.GetLocalScopes(handle.ToDebugInformationHandle()))
            foreach (var variableHandle in _pdb.GetLocalScope(scopeHandle).GetLocalVariables())
            {
                var variable = _pdb.GetLocalVariable(variableHandle);
                if ((variable.Attributes & LocalVariableAttributes.DebuggerHidden) == 0)
                    locals.TryAdd(variable.Index, _pdb.GetString(variable.Name));
            }
        return new IlMethodLines(first.Document, first.Line, [.. points], locals);
    }

    private string Document(DocumentHandle handle)
    {
        if (!_documents.TryGetValue(handle, out var name))
            _documents[handle] = name = Path.GetFullPath(_pdb.GetString(_pdb.GetDocument(handle).Name));
        return name;
    }

    public void Dispose() => provider.Dispose();
}

internal readonly record struct IlSequencePoint(int Offset, int Line, int Column, string? Document);

internal sealed class IlMethodLines(string document, int line, IlSequencePoint[] points, Dictionary<int, string> localNames)
{
    public string Document { get; } = document;
    public int Line { get; } = line;
    public Dictionary<int, string> LocalNames { get; } = localNames;

    /// <summary>The sequence point in effect at an IL offset: the last one at or before it (hidden when none).</summary>
    public IlSequencePoint Find(int offset)
    {
        int lo = 0, hi = points.Length - 1, best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (points[mid].Offset <= offset) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return best < 0 ? default : points[best];
    }
}
