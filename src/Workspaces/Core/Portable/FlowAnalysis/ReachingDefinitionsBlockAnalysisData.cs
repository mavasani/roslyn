// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal sealed class ReachingDefinitionsBlockAnalysisData : IEquatable<ReachingDefinitionsBlockAnalysisData>
    {
        private static readonly ObjectPool<ReachingDefinitionsBlockAnalysisData> s_pool =
            new ObjectPool<ReachingDefinitionsBlockAnalysisData>(() => new ReachingDefinitionsBlockAnalysisData());

        private readonly Dictionary<ISymbol, PooledHashSet<IOperation>> _reachingDefinitions;
        private ReachingDefinitionsBlockAnalysisData()
        {
            _reachingDefinitions = new Dictionary<ISymbol, PooledHashSet<IOperation>>();
        }

        public static ReachingDefinitionsBlockAnalysisData GetInstance() => s_pool.Allocate();
        public void Free()
        {
            Clear();
            s_pool.Free(this);
        }

        private void Clear()
        {
            foreach (var value in _reachingDefinitions.Values)
            {
                value.Free();
            }

            _reachingDefinitions.Clear();
        }

        public void SetAnalysisDataFrom(ReachingDefinitionsBlockAnalysisData other)
        {
            Debug.Assert(other != null);

            Clear();
            AddEntries(_reachingDefinitions, other);
        }

        public IEnumerable<IOperation> GetCurrentDefinitions(ISymbol symbol)
        {
            if (_reachingDefinitions.TryGetValue(symbol, out var values))
            {
                foreach (var value in values)
                {
                    yield return value;
                }
            }
        }

        public void OnWriteReferenceFound(ISymbol symbol, IOperation operation, bool maybeWritten)
        {
            if (!_reachingDefinitions.TryGetValue(symbol, out var values))
            {
                values = PooledHashSet<IOperation>.GetInstance();
                _reachingDefinitions.Add(symbol, values);
            }
            else if (!maybeWritten)
            {
                values.Clear();
            }

            values.Add(operation);
        }

        public bool Equals(ReachingDefinitionsBlockAnalysisData other)
            => this.GetHashCode() == (other?.GetHashCode() ?? 0);

        public override bool Equals(object obj)
        {
            return Equals(obj as ReachingDefinitionsBlockAnalysisData);
        }

        public override int GetHashCode()
        {
            var hashCode = _reachingDefinitions.Count;
            foreach ((int keyHash, int valueHash) in _reachingDefinitions.Select(GetKeyValueHashCodeTuple).OrderBy(hashTuple => hashTuple.keyHash))
            {
                hashCode = Hash.Combine(Hash.Combine(keyHash, valueHash), hashCode);
            }

            return hashCode;

            // Local functions.
            (int keyHash, int valueHash) GetKeyValueHashCodeTuple(KeyValuePair<ISymbol, PooledHashSet<IOperation>> keyValuePair)
                => (keyValuePair.Key.GetHashCode(), Hash.Combine(keyValuePair.Value.Count, Hash.CombineValues(keyValuePair.Value)));
        }

        private bool IsEmpty => _reachingDefinitions.Count == 0;

        public static ReachingDefinitionsBlockAnalysisData Merge(ReachingDefinitionsBlockAnalysisData data1, ReachingDefinitionsBlockAnalysisData data2)
        {
            if (data1 == null)
            {
                Debug.Assert(data2 != null);
                return data2;
            }
            else if (data2 == null)
            {
                Debug.Assert(data1 != null);
                return data1;
            }
            else if (data1.IsEmpty)
            {
                return data2;
            }
            else if (data2.IsEmpty)
            {
                return data1;
            }

            var mergedData = GetInstance();
            AddEntries(mergedData._reachingDefinitions, data1);
            AddEntries(mergedData._reachingDefinitions, data2);

            if (mergedData.Equals(data1))
            {
                mergedData.Free();
                return data1;
            }
            else if (mergedData.Equals(data2))
            {
                mergedData.Free();
                return data2;
            }

            return mergedData;
        }

        private static void AddEntries(Dictionary<ISymbol, PooledHashSet<IOperation>> result, ReachingDefinitionsBlockAnalysisData source)
        {
            if (source != null)
            {
                foreach (var kvp in source._reachingDefinitions)
                {
                    if (!result.TryGetValue(kvp.Key, out var values))
                    {
                        values = PooledHashSet<IOperation>.GetInstance();
                        result.Add(kvp.Key, values);
                    }

                    values.AddRange(kvp.Value);
                }
            }
        }
    }
}
