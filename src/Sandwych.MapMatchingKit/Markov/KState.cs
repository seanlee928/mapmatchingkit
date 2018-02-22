﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nito.Collections;
using Sandwych.MapMatchingKit.Utility;

namespace Sandwych.MapMatchingKit.Markov
{

    /// <summary>
    /// <i>k</i>-State data structure for organizing state memory in HMM inference.
    /// </summary>
    /// <typeparam name="TCandidate">Candidate inherits from {@link StateCandidate}.</typeparam>
    /// <typeparam name="TTransition">Transition inherits from {@link StateTransition}.</typeparam>
    /// <typeparam name="TSample">Sample inherits from {@link Sample}.</typeparam>
    public class KState<TCandidate, TTransition, TSample> :
        IStateMemory<TCandidate, TTransition, TSample>
        where TCandidate : class, IStateCandidate<TCandidate, TTransition, TSample>
        where TSample : ISample
    {
        private readonly struct SequenceEntry
        {
            public ICollection<TCandidate> Candidates { get; }
            public TSample Sample { get; }
            public TCandidate Estimated { get; }

            public SequenceEntry(ICollection<TCandidate> candidates, in TSample sample, in TCandidate estimated)
            {
                this.Candidates = candidates;
                this.Sample = sample;
                this.Estimated = estimated;
            }
        }

        private readonly int _k = -1;
        private readonly TimeSpan _t = TimeSpan.MaxValue;
        private readonly Deque<SequenceEntry> _sequence;
        private readonly IDictionary<TCandidate, int> _counters;

        /// <summary>
        /// Creates empty <see cref="KState{TCandidate, TTransition, TSample}"/> object with default parameters, i.e. capacity is unbounded.
        /// </summary>
        public KState() : this(-1, TimeSpan.MaxValue)
        {
        }


        /// <summary>
        /// Creates an empty <see cref="KState{TCandidate, TTransition, TSample}"/> object and sets <i>&kappa;</i> and <i>&tau;</i> parameters.
        /// </summary>
        /// <param name="k"><i>&kappa;</i> parameter bounds the length of the state sequence to at most <i>&kappa;+1</i> states, if <i>&kappa; &ge; 0</i>.</param>
        /// <param name="t"><i>&tau;</i> parameter bounds length of the state sequence to contain only states for the past <i>&tau;</i> milliseconds.</param>
        public KState(int k, TimeSpan t)
        {
            this._k = k;
            this._t = t;
            this._sequence = new Deque<SequenceEntry>();
            this._counters = new Dictionary<TCandidate, int>();
        }

        public bool IsEmpty => _counters.Count == 0;

        public int Count => _counters.Count;

        public TSample Sample
        {
            get
            {
                if (_sequence.Count == 0)
                {
                    return default(TSample);
                }
                else
                {
                    return _sequence.PeekLast().Sample;
                }
            }
        }

        public DateTimeOffset Time
        {
            get
            {
                if (_sequence.Count == 0)
                {
                    return DateTimeOffset.MaxValue;
                }
                else
                {
                    return this.Sample.Time;
                }
            }
        }

        /// <summary>
        /// Gets the sequence of measurements <i>z<sub>0</sub>, z<sub>1</sub>, ..., z<sub>t</sub></i>.
        /// </summary>
        /// <returns>List with the sequence of measurements.</returns>
        public IEnumerable<TSample> Samples()
        {
            foreach (var element in _sequence)
            {
                yield return element.Sample;
            }
        }

        public void Update(ICollection<TCandidate> vector, in TSample sample)
        {
            if (vector.Count == 0)
            {
                return;
            }

            if (_sequence.Count > 0 && _sequence.PeekLast().Sample.Time > sample.Time)
            {
                throw new InvalidOperationException("out-of-order state update is prohibited");
            }

            TCandidate kestimate = null;
            foreach (TCandidate candidate in vector)
            {
                _counters[candidate] = 0;
                if (candidate.Predecessor != null)
                {
                    if (!_counters.ContainsKey(candidate.Predecessor) || !_sequence.PeekLast().Candidates.Contains(candidate.Predecessor))
                    {
                        throw new InvalidOperationException("Inconsistent update vector.");
                    }
                    _counters[candidate.Predecessor] += 1; // _counters[candidate.Predecessor] + 1;
                }
                if (kestimate == null || candidate.Seqprob > kestimate.Seqprob)
                {
                    kestimate = candidate;
                }
            }

            if (_sequence.Count > 0)
            {
                var last = _sequence.PeekLast();
                var deletes = new List<TCandidate>();

                foreach (TCandidate candidate in last.Candidates)
                {
                    if (_counters[candidate] == 0)
                    {
                        deletes.Add(candidate);
                    }
                }

                var size = _sequence.PeekLast().Candidates.Count;

                foreach (TCandidate candidate in deletes)
                {
                    if (deletes.Count != size || candidate != last.Estimated)
                    {
                        this.Remove(candidate, _sequence.Count - 1);
                    }
                }
            }

            _sequence.AddToBack(new SequenceEntry(vector, sample, kestimate));

            while ((_t != TimeSpan.MaxValue && (sample.Time - _sequence.PeekFirst().Sample.Time) > _t)
                    || (_k >= 0 && _sequence.Count > _k + 1))
            {
                var deletes = _sequence.PeekFirst().Candidates;
                _sequence.RemoveFromFront();

                foreach (TCandidate candidate in deletes)
                {
                    _counters.Remove(candidate);
                }

                foreach (TCandidate candidate in _sequence.PeekFirst().Candidates)
                {
                    candidate.Predecessor = null;
                }
            }

            bool assert = (_k < 0 || _sequence.Count <= _k + 1);
            if (!assert)
            {
                throw new InvalidOperationException();
            }
        }

        protected void Remove(in TCandidate candidate, int index)
        {
            if (_sequence[index].Estimated == candidate)
            {
                return;
            }

            var vector = _sequence[index].Candidates;
            _counters.Remove(candidate);
            vector.Remove(candidate);

            var predecessor = candidate.Predecessor;
            if (predecessor != null)
            {
                _counters[predecessor] = _counters[predecessor] - 1;
                if (_counters[predecessor] == 0)
                {
                    this.Remove(predecessor, index - 1);
                }
            }
        }

        public ICollection<TCandidate> Vector()
        {
            if (_sequence.Count == 0)
            {
                return new HashSet<TCandidate>();
            }
            else
            {
                return _sequence[_sequence.Count - 1].Candidates;
            }
        }

        public TCandidate Estimate()
        {
            if (_sequence.Count == 0)
            {
                return null;
            }

            TCandidate estimate = null;
            foreach (TCandidate candidate in _sequence.PeekLast().Candidates)
            {
                if (estimate == null || candidate.Filtprob > estimate.Filtprob)
                {
                    estimate = candidate;
                }
            }
            return estimate;
        }


        /// <summary>
        /// Gets the most likely sequence of state candidates <i>s<sub>0</sub>, s<sub>1</sub>, ...,
        /// s<sub>t</sub></i>.
        /// </summary>
        /// <returns>List of the most likely sequence of state candidates.</returns>
        public IEnumerable<TCandidate> Sequence()
        {
            var ksequence = new List<TCandidate>(_sequence.Count);
            if (_sequence.Count > 0)
            {
                var kestimate = _sequence.PeekLast().Estimated;

                for (int i = _sequence.Count - 1; i >= 0; --i)
                {
                    if (kestimate != null)
                    {
                        ksequence.Add(kestimate);
                        kestimate = kestimate.Predecessor;
                    }
                    else
                    {
                        ksequence.Add(_sequence[i].Estimated);
                        kestimate = _sequence[i].Estimated.Predecessor;
                    }
                }
            }
            ksequence.Reverse();
            return ksequence;
        }

    }
}
