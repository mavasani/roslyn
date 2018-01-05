// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    [DebuggerDisplay("CFG ({_blocks.Count} blocks)")]
    internal class ControlFlowGraph
    {
        private ImmutableHashSet<BasicBlock>.Builder _blocks;

        public static ControlFlowGraph Create(IOperation body, ISymbol containerSymbol, CancellationToken cancellationToken)
        {
            var semanticModel = ((Operation)body).SemanticModel;
            var generator = new ControlFlowGraphGenerator(semanticModel);
            var result = generator.Generate(body, containerSymbol, cancellationToken);
            return result;
        }

        public ControlFlowGraph()
        {
            _blocks = ImmutableHashSet.CreateBuilder<BasicBlock>();
            Entry = new BasicBlock(BasicBlockKind.Entry);
            Exit = new BasicBlock(BasicBlockKind.Exit);

            AddBlock(Entry);
            AddBlock(Exit);
        }

        public BasicBlock Entry { get; private set; }
        public BasicBlock Exit { get; private set; }
        public ImmutableHashSet<BasicBlock> Blocks => _blocks.ToImmutable();

        internal void AddBlock(BasicBlock block)
        {
            _blocks.Add(block);
        }

        internal void ConnectBlocks(BasicBlock from, BasicBlock to, bool unconditionalJump)
        {
            from.AddSuccessor(to, unconditionalJump);
            to.AddPredecessor(from, unconditionalJump);
            _blocks.Add(from);
            _blocks.Add(to);
        }
    }
}

