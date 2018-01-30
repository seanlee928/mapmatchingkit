﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Castle.Core.Logging;

namespace Sandwych.MapMatchingKit.Markov
{

    /**
     * Hidden Markov Model (HMM) filter for online and offline inference of states in a stochastic
     * process.
     *
     * @param <C> Candidate inherits from {@link StateCandidate}.
     * @param <T> Transition inherits from {@link StateTransition}.
     * @param <S> Sample inherits from {@link Sample}.
     */
    public abstract class AbstractFilter<TCandidate, TTransition, TSample> :
        IFilter<TCandidate, TTransition, TSample>
        where TCandidate : IStateCandidate<TCandidate, TTransition, TSample>
    {
        private readonly static ILogger logger = NullLogger.Instance;

        /**
         * Gets state vector, which is a set of {@link StateCandidate} objects and with its emission
         * probability.
         *
         * @param predecessors Predecessor state candidate <i>s<sub>t-1</sub></i>.
         * @param sample Measurement sample.
         * @return Set of tuples consisting of a {@link StateCandidate} and its emission probability.
         */
        protected abstract (TCandidate, double)[] Candidates(ISet<TCandidate> predecessors, in TSample sample);

        /**
         * Gets transition and its transition probability for a pair of {@link StateCandidate}s, which
         * is a candidate <i>s<sub>t</sub></i> and its predecessor <i>s<sub>t</sub></i>
         *
         * @param predecessor Tuple of predecessor state candidate <i>s<sub>t-1</sub></i> and its
         *        respective measurement sample.
         * @param candidate Tuple of state candidate <i>s<sub>t</sub></i> and its respective measurement
         *        sample.
         * @return Tuple consisting of the transition from <i>s<sub>t-1</sub></i> to
         *         <i>s<sub>t</sub></i> and its transition probability, or null if there is no
         *         transition.
         */
        protected abstract (TTransition, double) Transition(in (TSample, TCandidate) predecessor, in (TSample, TCandidate) candidate);

        /**
         * Gets transitions and its transition probabilities for each pair of state candidates
         * <i>s<sub>t</sub></i> and <i>s<sub>t-1</sub></i>.
         * <p>
         * <b>Note:</b> This method may be overridden for better performance, otherwise it defaults to
         * the method {@link Filter#transition} for each single pair of state candidate and its possible
         * predecessor.
         *
         * @param predecessors Tuple of a set of predecessor state candidate <i>s<sub>t-1</sub></i> and
         *        its respective measurement sample.
         * @param candidates Tuple of a set of state candidate <i>s<sub>t</sub></i> and its respective
         *        measurement sample.
         * @return Maps each predecessor state candidate <i>s<sub>t-1</sub> &#8712; S<sub>t-1</sub></i>
         *         to a map of state candidates <i>s<sub>t</sub> &#8712; S<sub>t</sub></i> containing
         *         all transitions from <i>s<sub>t-1</sub></i> to <i>s<sub>t</sub></i> and its
         *         transition probability, or null if there no transition.
         */
        protected virtual IDictionary<TCandidate, IDictionary<TCandidate, (TTransition, double)>> Transitions(
            in (TSample, ISet<TCandidate>) predecessors, in (TSample, ISet<TCandidate>) candidates)
        {
            TSample sample = candidates.Item1;
            TSample previous = predecessors.Item1;

            IDictionary<TCandidate, IDictionary<TCandidate, (TTransition, double)>> map =
                new Dictionary<TCandidate, IDictionary<TCandidate, (TTransition, double)>>();

            foreach (TCandidate predecessor in predecessors.Item2)
            {
                map.Add(predecessor, new Dictionary<TCandidate, (TTransition, double)>());

                foreach (TCandidate candidate in candidates.Item2)
                {
                    map[predecessor].Add(candidate, this.Transition((previous, predecessor), (sample, candidate)));
                }
            }

            return map;
        }

        /**
         * Executes Hidden Markov Model (HMM) filter iteration that determines for a given measurement
         * sample <i>z<sub>t</sub></i>, which is a {@link Sample} object, and of a predecessor state
         * vector <i>S<sub>t-1</sub></i>, which is a set of {@link StateCandidate} objects, a state
         * vector <i>S<sub>t</sub></i> with filter and sequence probabilities set.
         * <p>
         * <b>Note:</b> The set of state candidates <i>S<sub>t-1</sub></i> is allowed to be empty. This
         * is either the initial case or an HMM break occured, which is no state candidates representing
         * the measurement sample could be found.
         *
         * @param predecessors State vector <i>S<sub>t-1</sub></i>, which may be empty.
         * @param sample Measurement sample <i>z<sub>t</sub></i>.
         * @param previous Previous measurement sample <i>z<sub>t-1</sub></i>.
         *
         * @return State vector <i>S<sub>t</sub></i>, which may be empty if an HMM break occured.
         */
        public ISet<TCandidate> Execute(ISet<TCandidate> predecessors, in TSample previous, in TSample sample)
        {
            //assert(predecessors != null);
            //assert(sample != null);

            var result = new HashSet<TCandidate>();
            var candidates = this.Candidates(predecessors, sample);
            //logger.trace("{} state candidates", candidates.size());

            double normsum = 0;

            if (predecessors.Count() > 0)
            {
                var states = new HashSet<TCandidate>();
                foreach (var candidate in candidates)
                {
                    states.Add(candidate.Item1);
                }

                var transitions = this.Transitions((previous, predecessors), (sample, states));

                foreach (var candidate in candidates)
                {
                    var candidate_ = candidate.Item1;
                    candidate_.Seqprob = Double.NegativeInfinity;

                    foreach (var predecessor in predecessors)
                    {
                        var transition = transitions[predecessor][candidate_];

                        //if (transition == null || transition.Item2 == 0)
                        if (transition.Item2 == 0)
                        {
                            continue;
                        }

                        candidate_.Filtprob = candidate_.Filtprob + (transition.Item2 * predecessor.Filtprob);

                        var seqprob = predecessor.Seqprob + Math.Log10(transition.Item2) + Math.Log10(candidate.Item2);

                        /*
                        if (logger.isTraceEnabled())
                        {
                            try
                            {
                                logger.trace("state transition {} -> {} ({}, {}, {}) {}",
                                        predecessor.id(), candidate_.id(), predecessor.seqprob(),
                                        Math.log10(transition.two()), Math.log10(candidate.two()),
                                        transition.one().toJSON().toString());
                            }
                            catch (JSONException e)
                            {
                                logger.trace("state transition (not JSON parsable transition: {})",
                                        e.getMessage());
                            }
                        }
                        */

                        if (seqprob > candidate_.Seqprob)
                        {
                            candidate_.Predecessor = predecessor;
                            candidate_.Transition = transition.Item1;
                            candidate_.Seqprob = seqprob;
                        }
                    }

                    /*
                    if (candidate_.predecessor() != null)
                    {
                        logger.trace("state candidate {} -> {} ({}, {})", candidate_.predecessor().id(),
                                candidate_.id(), candidate_.filtprob(), candidate_.seqprob());
                    }
                    else
                    {
                        logger.trace("state candidate - -> {} ({}, {})", candidate_.id(),
                                candidate_.filtprob(), candidate_.seqprob());
                    }
                    */


                    if (candidate_.Filtprob == 0)
                    {
                        continue;
                    }

                    candidate_.Filtprob = candidate_.Filtprob * candidate.Item2;
                    result.Add(candidate_);

                    normsum += candidate_.Filtprob;
                }
            }

            /*
            if (!candidates.isEmpty() && result.isEmpty() && !predecessors.isEmpty())
            {
                logger.info("HMM break - no state transitions");
            }
            */

            if (result.Count == 0 || predecessors.Count() == 0)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate.Item2 == 0)
                    {
                        continue;
                    }
                    TCandidate candidate_ = candidate.Item1;
                    normsum += candidate.Item2;
                    candidate_.Filtprob = candidate.Item2;
                    candidate_.Seqprob = Math.Log10(candidate.Item2);
                    result.Add(candidate_);

                    /*
                    if (logger.isTraceEnabled())
                    {
                        try
                        {
                            logger.trace("state candidate {} ({}) {}", candidate_.id(), candidate.two(),
                                    candidate_.toJSON().toString());
                        }
                        catch (JSONException e)
                        {
                            logger.trace("state candidate (not JSON parsable candidate: {})",
                                    e.getMessage());
                        }
                    }
                    */
                }
            }

            /*
            if (result.isEmpty())
            {
                logger.info("HMM break - no state emissions");
            }
            */

            foreach (TCandidate candidate in result)
            {
                candidate.Filtprob = candidate.Filtprob / normsum;
            }

            /*
            logger.trace("{} state candidates for state update", result.size());
            */
            return result;
        }
    }
}