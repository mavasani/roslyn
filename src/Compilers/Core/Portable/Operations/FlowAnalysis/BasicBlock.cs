// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    [DebuggerDisplay("{Kind} ({Statements.Length} statements)")]
    internal class BasicBlock
    {
        private ImmutableArray<IOperation>.Builder _statements;
        private ImmutableHashSet<BasicBlock>.Builder _successors;
        private ImmutableHashSet<BasicBlock>.Builder _predecessors;

        public BasicBlock(BasicBlockKind kind)
        {
            Kind = kind;
            _statements = ImmutableArray.CreateBuilder<IOperation>();
            _successors = ImmutableHashSet.CreateBuilder<BasicBlock>();
            _predecessors = ImmutableHashSet.CreateBuilder<BasicBlock>();
        }

        public BasicBlockKind Kind { get; private set; }
        public ImmutableArray<IOperation> Statements => _statements.ToImmutable();
        public BasicBlock FallThroughSuccessor { get; private set; }
        public BasicBlock ConditionalJumpSuccessor { get; private set; }
        public ImmutableHashSet<BasicBlock> Predecessors => _predecessors.ToImmutable();

        internal void AddStatement(IOperation statement)
        {
            _statements.Add(statement);
        }

        internal void AddSuccessor(BasicBlock block, bool unconditionalJump)
        {
            if (unconditionalJump)
            {
                FallThroughSuccessor = block;
            }
            else
            {
                ConditionalJumpSuccessor = block;
            }
        }

        internal void AddPredecessor(BasicBlock block, bool unconditionalJump)
        {
            _predecessors.Add(block);
        }
    }
}
