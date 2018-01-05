// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations.FlowAnalysis;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Test.Utilities
{
    internal sealed class ControlFlowGraphDumper
    {
        private readonly Compilation _compilation;
        private readonly ControlFlowGraph _controlFlowGraph;
        private readonly StringBuilder _builder;
        private static readonly Func<CSharpSyntaxNode, bool> s_isMemberDeclarationFunction = IsMemberDeclaration;
        private readonly IBidirectionalMap<int, BasicBlock> _blockIndices;

        private const string indent = "  ";
        private string _currentIndent;
        private bool _pendingIndent;

        public ControlFlowGraphDumper(Compilation compilation, ControlFlowGraph controlFlowGraph, int initialIndent)
        {
            _compilation = compilation;
            _controlFlowGraph = controlFlowGraph;
            _builder = new StringBuilder();
            _blockIndices = CreateBasicBlockIndices(controlFlowGraph);

            _currentIndent = new string(' ', initialIndent);
            _pendingIndent = true;
        }

        private static IBidirectionalMap<int, BasicBlock> CreateBasicBlockIndices(ControlFlowGraph graph)
        {
            var map = BidirectionalMap<int, BasicBlock>.Empty;
            var queue = new Queue<BasicBlock>();
            queue.Enqueue(graph.Entry);
            int currentIndex = 0;
            while (queue.Count != 0)
            {
                BasicBlock currentBlock = queue.Dequeue();
                if (map.ContainsValue(currentBlock))
                {
                    continue;
                }

                map = map.Add(currentIndex++, currentBlock);
                if (currentBlock.FallThroughSuccessor != null)
                {
                    queue.Enqueue(currentBlock.FallThroughSuccessor);
                }

                if (currentBlock.ConditionalJumpSuccessor != null)
                {
                    queue.Enqueue(currentBlock.ConditionalJumpSuccessor);
                }
            }

            return map;
        }

        public static void Verify(Compilation compilation, IOperation operation, string expectedFlowGraph, int initialIndent = 0)
        {
            var actual = GetControlFlowGraph(compilation, operation, initialIndent);
            Assert.Equal(expectedFlowGraph, actual);
        }

        public static string GetControlFlowGraph(Compilation compilation, IOperation operation, int initialIndent = 0)
        {
            var containingMemberDecl = GetMemberDeclaration(operation.Syntax);
            var symbol = compilation.GetSemanticModel(operation.Syntax.SyntaxTree).GetDeclaredSymbol(containingMemberDecl);
            var controlFlowGraph = ControlFlowGraph.Create(operation, symbol, CancellationToken.None);

            var walker = new ControlFlowGraphDumper(compilation, controlFlowGraph, initialIndent);
            walker.Visit();
            return walker._builder.ToString();
        }

        private static CSharpSyntaxNode GetMemberDeclaration(SyntaxNode node)
        {
            return node.FirstAncestorOrSelf(s_isMemberDeclarationFunction);
        }

        private static bool IsMemberDeclaration(CSharpSyntaxNode node)
        {
            return (node is MemberDeclarationSyntax) || (node is AccessorDeclarationSyntax) ||
                   (node.Kind() == SyntaxKind.Attribute) || (node.Kind() == SyntaxKind.Parameter);
        }

        public static void Verify(string expectedOperationTree, string actualOperationTree)
        {
            char[] newLineChars = Environment.NewLine.ToCharArray();
            string actual = actualOperationTree.Trim(newLineChars);
            expectedOperationTree = expectedOperationTree.Trim(newLineChars);
            expectedOperationTree = Regex.Replace(expectedOperationTree, "([^\r])\n", "$1" + Environment.NewLine);

            AssertEx.AreEqual(expectedOperationTree, actual);
        }

        #region Logging helpers

        private void LogString(string str)
        {
            if (_pendingIndent)
            {
                str = _currentIndent + str;
                _pendingIndent = false;
            }

            _builder.Append(str);
        }

        private void LogNewLine()
        {
            LogString(Environment.NewLine);
            _pendingIndent = true;
        }

        private void Indent()
        {
            _currentIndent += indent;
        }

        private void Unindent()
        {
            _currentIndent = _currentIndent.Substring(indent.Length);
        }

        #endregion

        private void Visit()
        {
            for(int i = 0; i < _blockIndices.Keys.Count(); i++)
            {
                VisitBlock(_blockIndices.GetValueOrDefault(i));
            }
        }

        private string GetBlockDisplayString(BasicBlock block)
        {
            var str = $"BB[{_blockIndices.GetKeyOrDefault(block)}]";
            if (_controlFlowGraph.Entry == block)
            {
                str += "(Entry)";
            }
            else if (_controlFlowGraph.Exit == block)
            {
                str += "(Exit)";
            }

            return str;
        }

        private void VisitBlock(BasicBlock block)
        {
            LogString(GetBlockDisplayString(block));
            LogNewLine();
            foreach (var statement in block.Statements)
            {
                var tree = OperationTreeVerifier.GetOperationTree(_compilation, statement, _currentIndent.Length + indent.Length);
                LogString(tree);
            }

            LogNewLine();

            if (block.ConditionalJumpSuccessor != null)
            {
                Indent();
                LogString($"Conditional jump to {GetBlockDisplayString(block.ConditionalJumpSuccessor)}");
                LogNewLine();
                Unindent();
            }

            if (block.FallThroughSuccessor != null)
            {
                Indent();
                LogString($"Fall through to {GetBlockDisplayString(block.FallThroughSuccessor)}");
                LogNewLine();
                Unindent();
            }

            if (_controlFlowGraph.Exit != block)
            {
                LogNewLine();
            }
        }
    }
}
