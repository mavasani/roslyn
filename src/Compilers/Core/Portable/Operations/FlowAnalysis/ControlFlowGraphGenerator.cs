// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal partial class ControlFlowGraphGenerator
    {
        private readonly SemanticModel _semanticModel;
        private IList<BasicBlock> _blocks;
        private IDictionary<ILabelSymbol, BasicBlock> _labeledBlocks;
        private BasicBlock _currentBlock;
        private ControlFlowGraph _graph;

        public ControlFlowGraphGenerator(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
            _blocks = new List<BasicBlock>();
            _labeledBlocks = new Dictionary<ILabelSymbol, BasicBlock>();
        }

        public ControlFlowGraph Generate(IOperation body, ISymbol containerSymbol, CancellationToken cancellationToken)
        {
            body = _semanticModel.RewriteOperationForFlowGraph(body, containerSymbol, cancellationToken);
            _graph = new ControlFlowGraph();

            CreateBlocks(body);
            ConnectBlocks();

            var result = _graph;

            _graph = null;
            _blocks.Clear();
            _labeledBlocks.Clear();

            return result;
        }

        private void CreateBlocks(IOperation body)
        {
            var collector = new StatementCollector();
            collector.Visit(body);

            foreach (var statement in collector.Statements)
            {
                Visit(statement);
            }
        }

        private void Visit(IOperation operation)
        {
            var isLastStatement = false;

            switch (operation.Kind)
            {
                case OperationKind.Labeled:
                    var label = (ILabeledOperation)operation;
                    _currentBlock = NewBlock();
                    _labeledBlocks.Add(label.Label, _currentBlock);
                    break;

                case OperationKind.Return:
                case OperationKind.Branch:
                    isLastStatement = true;
                    break;
            }

            if (_currentBlock == null)
            {
                _currentBlock = NewBlock();
            }

            _currentBlock.AddStatement(operation);

            if (isLastStatement)
            {
                _currentBlock = null;
            }
        }

        private BasicBlock NewBlock()
        {
            var block = new BasicBlock(BasicBlockKind.Block);

            _blocks.Add(block);
            _graph.AddBlock(block);
            return block;
        }

        private void ConnectBlocks()
        {
            var connectWithPrev = true;
            var prevBlock = _graph.Entry;

            _blocks.Add(_graph.Exit);

            foreach (var block in _blocks)
            {
                if (connectWithPrev)
                {
                    _graph.ConnectBlocks(prevBlock, block, unconditionalJump: true);
                }
                else
                {
                    connectWithPrev = true;
                }

                BasicBlock target = null;
                var lastStatement = block.Statements.LastOrDefault();

                switch (lastStatement)
                {
                    case IBranchOperation branch:
                        target = _labeledBlocks[branch.Target];
                        bool unconditionalJump = false;
                        if (branch.BranchKind != BranchKind.ConditionalGoTo)
                        {
                            connectWithPrev = false;
                            unconditionalJump = true;
                        }
                        _graph.ConnectBlocks(block, target, unconditionalJump);
                        break;

                    case IReturnOperation ret:
                        _graph.ConnectBlocks(block, _graph.Exit, unconditionalJump: true);
                        connectWithPrev = false;
                        break;
                }

                prevBlock = block;
            }
        }
    }
}
