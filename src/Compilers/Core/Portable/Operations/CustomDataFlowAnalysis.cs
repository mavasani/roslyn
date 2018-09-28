﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
#if WORKSPACE
    using TBasicBlock = BasicBlock;
    using TControlFlowBranch = ControlFlowBranch;
#else
    using TBasicBlock = BasicBlockBuilder;
    using TControlFlowBranch = BasicBlockBuilder.Branch;
#endif

    internal static class CustomDataFlowAnalysis<TDataFlowAnalyzer, TBlockAnalysisData>
        where TDataFlowAnalyzer: IDataFlowAnalyzer<TBlockAnalysisData>
    {
        /// <summary>
        /// Runs dataflow analysis for the given <paramref name="analyzer"/> on the given <paramref name="blocks"/>.
        /// </summary>
        /// <param name="blocks">Blocks on which to execute analysis.</param>
        /// <param name="analyzer">Dataflow analyzer.</param>
        /// <returns>Block analysis data for the last block.</returns>
        internal static TBlockAnalysisData Run(ImmutableArray<TBasicBlock> blocks, TDataFlowAnalyzer analyzer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var continueDispatchAfterFinally = PooledDictionary<ControlFlowRegion, bool>.GetInstance();
            var dispatchedExceptionsFromRegions = PooledHashSet<ControlFlowRegion>.GetInstance();
            int firstBlockOrdinal = 0;
            int lastBlockOrdinal = blocks.Length - 1;

            var unreachableBlocksToVisit = ArrayBuilder<TBasicBlock>.GetInstance();
            if (analyzer.AnalyzeUnreachableBlocks)
            {
                for (int i = firstBlockOrdinal; i <= lastBlockOrdinal; i++)
                {
                    if (!blocks[i].IsReachable)
                    {
                        unreachableBlocksToVisit.Add(blocks[i]);
                    }
                }
            }

            var initialAnalysisData = analyzer.GetCurrentAnalysisData(blocks[0]);
            if (initialAnalysisData == default)
            {
                initialAnalysisData = analyzer.GetEmptyAnalysisData();
            }

            var result = RunCore(blocks, analyzer, firstBlockOrdinal, lastBlockOrdinal,
                                 initialAnalysisData,
                                 unreachableBlocksToVisit,
                                 outOfRangeBlocksToVisit: null,
                                 continueDispatchAfterFinally,
                                 dispatchedExceptionsFromRegions,
                                 cancellationToken);
            Debug.Assert(unreachableBlocksToVisit.Count == 0);
            unreachableBlocksToVisit.Free();
            continueDispatchAfterFinally.Free();
            dispatchedExceptionsFromRegions.Free();
            return result;
        }

        private static TBlockAnalysisData RunCore(
            ImmutableArray<TBasicBlock> blocks,
            TDataFlowAnalyzer analyzer,
            int firstBlockOrdinal,
            int lastBlockOrdinal,
            TBlockAnalysisData initialAnalysisData,
            ArrayBuilder<TBasicBlock> unreachableBlocksToVisit,
            ArrayBuilder<TBasicBlock> outOfRangeBlocksToVisit,
            PooledDictionary<ControlFlowRegion, bool> continueDispatchAfterFinally,
            PooledHashSet<ControlFlowRegion> dispatchedExceptionsFromRegions,
            CancellationToken cancellationToken)
        {
            var toVisit = ArrayBuilder<TBasicBlock>.GetInstance();

            var firstBlock = blocks[firstBlockOrdinal];
            analyzer.SetCurrentAnalysisData(firstBlock, initialAnalysisData);
            toVisit.Push(firstBlock);

            var processedBlocks = PooledHashSet<TBasicBlock>.GetInstance();
            TBlockAnalysisData resultAnalysisData = default;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                TBasicBlock current;
                if (toVisit.Count > 0)
                {
                    current = toVisit.Pop();
                }
                else
                {
                    int index;
                    current = null;
                    for (index = 0; index < unreachableBlocksToVisit.Count; index++)
                    {
                        var unreachableBlock = unreachableBlocksToVisit[index];
                        if (unreachableBlock.Ordinal >= firstBlockOrdinal && unreachableBlock.Ordinal <= lastBlockOrdinal)
                        {
                            current = unreachableBlock;
                            break;
                        }
                    }

                    if (current == null)
                    {
                        continue;
                    }

                    unreachableBlocksToVisit.RemoveAt(index);
                    if (processedBlocks.Contains(current))
                    {
                        // Already processed from a branch from another unreachable block.
                        continue;
                    }

                    analyzer.SetCurrentAnalysisData(current, analyzer.GetEmptyAnalysisData());
                }

                if (current.Ordinal < firstBlockOrdinal || current.Ordinal > lastBlockOrdinal)
                {
                    outOfRangeBlocksToVisit.Push(current);
                    continue;
                }

                if (current.Ordinal == current.EnclosingRegion.FirstBlockOrdinal)
                {
                    // We are revisiting first block of a region, so we need to again dispatch exceptions from region.
                    dispatchedExceptionsFromRegions.Remove(current.EnclosingRegion);
                }

                TBlockAnalysisData fallThroughAnalysisData = analyzer.AnalyzeBlock(current, cancellationToken);
                bool fallThroughSuccessorIsReachable = true;

                if (current.ConditionKind != ControlFlowConditionKind.None)
                {
                    TBlockAnalysisData conditionalSuccessorAnalysisData;
                    (fallThroughAnalysisData, conditionalSuccessorAnalysisData) = analyzer.AnalyzeConditionalBranch(current, fallThroughAnalysisData, cancellationToken);

                    bool conditionalSuccesorIsReachable = true;
                    if (current.BranchValue.ConstantValue.HasValue && current.BranchValue.ConstantValue.Value is bool constant)
                    {
                        if (constant == (current.ConditionKind == ControlFlowConditionKind.WhenTrue))
                        {
                            fallThroughSuccessorIsReachable = false;
                        }
                        else
                        {
                            conditionalSuccesorIsReachable = false;
                        }
                    }

                    if (conditionalSuccesorIsReachable || analyzer.AnalyzeUnreachableBlocks)
                    {
                        followBranch(current, current.ConditionalSuccessor, conditionalSuccessorAnalysisData);
                    }
                }
                else
                {
                    fallThroughAnalysisData = analyzer.AnalyzeNonConditionalBranch(current, fallThroughAnalysisData, cancellationToken);
                }

                if (fallThroughSuccessorIsReachable || analyzer.AnalyzeUnreachableBlocks)
                {

                    TControlFlowBranch branch = current.FallThroughSuccessor;
                    followBranch(current, branch, fallThroughAnalysisData);

                    if (current.EnclosingRegion.Kind == ControlFlowRegionKind.Finally &&
                        current.Ordinal == lastBlockOrdinal)
                    {
                        continueDispatchAfterFinally[current.EnclosingRegion] = branch.Semantics != ControlFlowBranchSemantics.Throw &&
                            branch.Semantics != ControlFlowBranchSemantics.Rethrow &&
                            current.FallThroughSuccessor.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling;
                    }
                }

                if (current.Ordinal == lastBlockOrdinal)
                {
                    resultAnalysisData = fallThroughAnalysisData;
                }

                // We are using very simple approach: 
                // If try block is reachable, we should dispatch an exception from it, even if it is empty.
                // To simplify implementation, we dispatch exception from every reachable basic block and rely
                // on dispatchedExceptionsFromRegions cache to avoid doing duplicate work.
                dispatchException(current.EnclosingRegion);

                processedBlocks.Add(current);
            }
            while (toVisit.Count != 0 || unreachableBlocksToVisit.Count != 0);

            toVisit.Free();
            return resultAnalysisData;

            void followBranch(TBasicBlock current, TControlFlowBranch branch, TBlockAnalysisData currentAnalsisData)
            {
#if WORKSPACE
                if (branch == null)
                {
                    return;
                }
#endif
                switch (branch.Semantics)
                {
                    case ControlFlowBranchSemantics.None:
                    case ControlFlowBranchSemantics.ProgramTermination:
                    case ControlFlowBranchSemantics.StructuredExceptionHandling:
                    case ControlFlowBranchSemantics.Throw:
                    case ControlFlowBranchSemantics.Rethrow:
                    case ControlFlowBranchSemantics.Error:
                        Debug.Assert(branch.Destination == null);
                        return;

                    case ControlFlowBranchSemantics.Regular:
                    case ControlFlowBranchSemantics.Return:
                        Debug.Assert(branch.Destination != null);

                        if (stepThroughFinally(current.EnclosingRegion, branch.Destination, ref currentAnalsisData))
                        {
                            var destination = branch.Destination;
                            var currentDestinationData = analyzer.GetCurrentAnalysisData(destination);
                            var mergedAnalysisData = analyzer.Merge(currentDestinationData, currentAnalsisData, cancellationToken);
                            if (!analyzer.IsEqual(currentDestinationData, mergedAnalysisData) &&
                                (current.IsReachable || !destination.IsReachable))
                            {
                                analyzer.SetCurrentAnalysisData(destination, mergedAnalysisData);
                                toVisit.Add(branch.Destination);
                            }
                        }

                        return;

                    default:
                        throw ExceptionUtilities.UnexpectedValue(branch.Semantics);
                }
            }

            // Returns whether we should proceed to the destination after finallies were taken care of.
            bool stepThroughFinally(ControlFlowRegion region, TBasicBlock destination, ref TBlockAnalysisData currentAnalysisData)
            {
                int destinationOrdinal = destination.Ordinal;
                while (!region.ContainsBlock(destinationOrdinal))
                {
                    Debug.Assert(region.Kind != ControlFlowRegionKind.Root);
                    ControlFlowRegion enclosing = region.EnclosingRegion;
                    if (region.Kind == ControlFlowRegionKind.Try && enclosing.Kind == ControlFlowRegionKind.TryAndFinally)
                    {
                        Debug.Assert(enclosing.NestedRegions[0] == region);
                        Debug.Assert(enclosing.NestedRegions[1].Kind == ControlFlowRegionKind.Finally);
                        if (!stepThroughSingleFinally(enclosing.NestedRegions[1], ref currentAnalysisData))
                        {
                            // The point that continues dispatch is not reachable. Cancel the dispatch.
                            return false;
                        }
                    }

                    region = enclosing;
                }

                return true;
            }

            // Returns whether we should proceed with dispatch after finally was taken care of.
            bool stepThroughSingleFinally(ControlFlowRegion @finally, ref TBlockAnalysisData currentAnalysisData)
            {
                Debug.Assert(@finally.Kind == ControlFlowRegionKind.Finally);
                var previousAnalysisData = analyzer.GetCurrentAnalysisData(blocks[@finally.FirstBlockOrdinal]);
                var mergedAnalysisData = analyzer.Merge(previousAnalysisData, currentAnalysisData, cancellationToken);
                if (!analyzer.IsEqual(previousAnalysisData, mergedAnalysisData))
                {
                    // For simplicity, we do a complete walk of the finally/filter region in isolation
                    // to make sure that the resume dispatch point is reachable from its beginning.
                    // It could also be reachable through invalid branches into the finally and we don't want to consider 
                    // these cases for regular finally handling.
                    currentAnalysisData = RunCore(blocks,
                                                  analyzer,
                                                  @finally.FirstBlockOrdinal,
                                                  @finally.LastBlockOrdinal,
                                                  mergedAnalysisData,
                                                  unreachableBlocksToVisit,
                                                  outOfRangeBlocksToVisit: toVisit,
                                                  continueDispatchAfterFinally,
                                                  dispatchedExceptionsFromRegions,
                                                  cancellationToken);
                }

                if (!continueDispatchAfterFinally.TryGetValue(@finally, out bool dispatch))
                {
                    dispatch = false;
                    continueDispatchAfterFinally.Add(@finally, false);
                }

                return dispatch;
            }

            void dispatchException(ControlFlowRegion fromRegion)
            {
                do
                {
                    if (!dispatchedExceptionsFromRegions.Add(fromRegion))
                    {
                        return;
                    }

                    ControlFlowRegion enclosing = fromRegion.Kind == ControlFlowRegionKind.Root ? null : fromRegion.EnclosingRegion;
                    if (fromRegion.Kind == ControlFlowRegionKind.Try)
                    {
                        switch (enclosing.Kind)
                        {
                            case ControlFlowRegionKind.TryAndFinally:
                                Debug.Assert(enclosing.NestedRegions[0] == fromRegion);
                                Debug.Assert(enclosing.NestedRegions[1].Kind == ControlFlowRegionKind.Finally);
                                var currentAnalysisData = analyzer.GetCurrentAnalysisData(blocks[fromRegion.FirstBlockOrdinal]);
                                if (!stepThroughSingleFinally(enclosing.NestedRegions[1], ref currentAnalysisData))
                                {
                                    // The point that continues dispatch is not reachable. Cancel the dispatch.
                                    return;
                                }
                                break;

                            case ControlFlowRegionKind.TryAndCatch:
                                Debug.Assert(enclosing.NestedRegions[0] == fromRegion);
                                dispatchExceptionThroughCatches(enclosing, startAt: 1);
                                break;

                            default:
                                throw ExceptionUtilities.UnexpectedValue(enclosing.Kind);
                        }
                    }
                    else if (fromRegion.Kind == ControlFlowRegionKind.Filter)
                    {
                        // If filter throws, dispatch is resumed at the next catch with an original exception
                        Debug.Assert(enclosing.Kind == ControlFlowRegionKind.FilterAndHandler);
                        ControlFlowRegion tryAndCatch = enclosing.EnclosingRegion;
                        Debug.Assert(tryAndCatch.Kind == ControlFlowRegionKind.TryAndCatch);

                        int index = tryAndCatch.NestedRegions.IndexOf(enclosing, startIndex: 1);

                        if (index > 0)
                        {
                            dispatchExceptionThroughCatches(tryAndCatch, startAt: index + 1);
                            fromRegion = tryAndCatch;
                            continue;
                        }

                        throw ExceptionUtilities.Unreachable;
                    }

                    fromRegion = enclosing;
                }
                while (fromRegion != null);
            }

            void dispatchExceptionThroughCatches(ControlFlowRegion tryAndCatch, int startAt)
            {
                // For simplicity, we do not try to figure out whether a catch clause definitely
                // handles all exceptions.

                Debug.Assert(tryAndCatch.Kind == ControlFlowRegionKind.TryAndCatch);
                Debug.Assert(startAt > 0);
                Debug.Assert(startAt <= tryAndCatch.NestedRegions.Length);

                for (int i = startAt; i < tryAndCatch.NestedRegions.Length; i++)
                {
                    ControlFlowRegion @catch = tryAndCatch.NestedRegions[i];

                    switch (@catch.Kind)
                    {
                        case ControlFlowRegionKind.Catch:
                            toVisit.Add(blocks[@catch.FirstBlockOrdinal]);
                            break;

                        case ControlFlowRegionKind.FilterAndHandler:
                            TBasicBlock entryBlock = blocks[@catch.FirstBlockOrdinal];
                            Debug.Assert(@catch.NestedRegions[0].Kind == ControlFlowRegionKind.Filter);
                            Debug.Assert(entryBlock.Ordinal == @catch.NestedRegions[0].FirstBlockOrdinal);

                            toVisit.Add(entryBlock);
                            break;

                        default:
                            throw ExceptionUtilities.UnexpectedValue(@catch.Kind);
                    }
                }
            }
        }
    }
}
