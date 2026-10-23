// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Collections.Generic;
using System.Threading;

namespace CsharpcCoreLib.RegularExpressions
{
    internal static class RegexTreeAnalyzer
    {
        public static AnalysisResults Analyze(RegexTree regexTree)
        {
            var results = new AnalysisResults(regexTree);
            results._complete = TryAnalyze(regexTree.Root, results, isAtomicByAncestor: true, isInLoop: false);
            return results;

            static bool TryAnalyze(RegexNode node, AnalysisResults results, bool isAtomicByAncestor, bool isInLoop)
            {
                if (!StackHelper.TryEnsureSufficientExecutionStack())
                {
                    return false;
                }

                // Track whether we've seen any nodes with various options set.
                results._hasIgnoreCase |= (node.Options & RegexOptions.IgnoreCase) != 0;
                results._hasRightToLeft |= (node.Options & RegexOptions.RightToLeft) != 0;

                // Track whether this node is inside of a loop.
                if (isInLoop)
                {
                    (results._inLoops ??= new HashSet<RegexNode>()).Add(node);
                }

                if (isAtomicByAncestor)
                {
                    // We've been told by our parent that we should be considered atomic, so add ourselves
                    // to the atomic collection.
                    results._isAtomicByAncestor.Add(node);
                }
                else
                {
                    // Certain kinds of nodes incur backtracking logic themselves: add them to the backtracking collection.
                    // We may later find that a node contains another that has backtracking; we'll add nodes based on that
                    // after examining the children.
                    if (node.IsBacktrackingConstruct)
                    {
                        (results._mayBacktrack ??= new HashSet<RegexNode>()).Add(node);
                    }
                }

                // Update state for certain node types.
                bool isAtomicBySelf = false;
                switch (node.Kind)
                {
                    // Some node types add atomicity around what they wrap.  Set isAtomicBySelfOrParent to true for such nodes
                    // even if it was false upon entering the method.
                    case RegexNodeKind.Atomic:
                    case RegexNodeKind.NegativeLookaround:
                    case RegexNodeKind.PositiveLookaround:
                        isAtomicBySelf = true;
                        break;

                    // Track any nodes that are themselves captures.
                    case RegexNodeKind.Capture:
                        results._containsCapture.Add(node);
                        break;

                    // Track whether we've recurred into a loop
                    case RegexNodeKind.Loop:
                    case RegexNodeKind.Lazyloop:
                        isInLoop = true;
                        break;
                }

                // Process each child.
                int childCount = node.ChildCount();
                for (int i = 0; i < childCount; i++)
                {
                    RegexNode child = node.Child(i);

                    // Determine whether the child should be treated as atomic (whether anything
                    // can backtrack into it), which is influenced by whether this node (the child's
                    // parent) is considered atomic by itself or by its parent.
                    bool treatChildAsAtomic = (isAtomicByAncestor | isAtomicBySelf) && node.Kind switch
                    {
                        // If the parent is atomic, so is the child.  That's the whole purpose
                        // of the Atomic node, and lookarounds are also implicitly atomic.
                        RegexNodeKind.Atomic or RegexNodeKind.NegativeLookaround or RegexNodeKind.PositiveLookaround => true,

                        // Each branch is considered independently, so any atomicity applied to the alternation also applies
                        // to each individual branch.  This is true as well for conditionals.
                        RegexNodeKind.Alternate or RegexNodeKind.BackreferenceConditional or RegexNodeKind.ExpressionConditional => true,

                        // Captures don't impact atomicity: if the parent of a capture is atomic, the capture is also atomic.
                        RegexNodeKind.Capture => true,

                        // If the parent is a concatenation and this is the last node, any atomicity
                        // applying to the concatenation applies to this node, too.
                        RegexNodeKind.Concatenate => i == childCount - 1,

                        // For loops with a max iteration count of 1, they themselves can be considered
                        // atomic as can whatever they wrap, as they won't ever iterate more than once
                        // and thus we don't need to worry about one iteration consuming input destined
                        // for a subsequent iteration.
                        RegexNodeKind.Loop or RegexNodeKind.Lazyloop when node.N == 1 => true,

                        // For any other parent type, give up on trying to prove atomicity.
                        _ => false,
                    };

                    // Now analyze the child.
                    if (!TryAnalyze(child, results, treatChildAsAtomic, isInLoop))
                    {
                        return false;
                    }

                    // If the child contains captures, so too does this parent.
                    if (results._containsCapture.Contains(child))
                    {
                        results._containsCapture.Add(node);
                    }

                    // If the child might require backtracking into it, so too might the parent,
                    // unless the parent is itself considered atomic.  Here we don't consider parental
                    // atomicity, as we need to surface upwards to the parent whether any backtracking
                    // will be visible from this node to it.
                    if (!isAtomicBySelf && (results._mayBacktrack?.Contains(child) == true))
                    {
                        (results._mayBacktrack ??= new HashSet<RegexNode>()).Add(node);
                    }
                }

                // Successfully analyzed the node.
                return true;
            }
        }
    }

    internal sealed class AnalysisResults
    {
        internal bool _complete;

        internal readonly HashSet<RegexNode> _isAtomicByAncestor = new(); // since the root is implicitly atomic, every tree will contain atomic-by-ancestor nodes
        internal readonly HashSet<RegexNode> _containsCapture = new(); // the root is a capture, so this will always contain at least the root node
        internal HashSet<RegexNode>? _mayBacktrack;
        internal HashSet<RegexNode>? _inLoops;
        internal bool _hasIgnoreCase;
        internal bool _hasRightToLeft;

        internal AnalysisResults(RegexTree regexTree) => RegexTree = regexTree;

        public RegexTree RegexTree { get; }

        public bool IsAtomicByAncestor(RegexNode node) => _isAtomicByAncestor.Contains(node);

        public bool MayContainCapture(RegexNode node) => !_complete || _containsCapture.Contains(node);

        public bool MayBacktrack(RegexNode node) => !_complete || (_mayBacktrack?.Contains(node) ?? false);

        public bool IsInLoop(RegexNode node) => !_complete || (_inLoops?.Contains(node) ?? false);

        public bool HasIgnoreCase => !_complete || _hasIgnoreCase;

        public bool HasRightToLeft => !_complete || _hasRightToLeft;
    }
}
