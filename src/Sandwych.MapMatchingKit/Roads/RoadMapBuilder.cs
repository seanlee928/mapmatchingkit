﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sandwych.MapMatchingKit.Roads
{
    public class RoadMapBuilder
    {
        private IDictionary<long, RoadInfo> _roads = new Dictionary<long, RoadInfo>();

        public RoadMapBuilder AddRoad(RoadInfo road)
        {
            if (_roads.ContainsKey(road.Id))
            {
                throw new ArgumentOutOfRangeException(nameof(road));
            }
            _roads.Add(road.Id, road);
            return this;
        }

        public RoadMapBuilder AddRoads(IEnumerable<RoadInfo> roads)
        {
            foreach (var r in roads)
            {
                this.AddRoad(r);
            }
            return this;
        }

        public RoadMap Build()
        {
            return new RoadMap(this.GetAllRoads());
        }

        private IEnumerable<Road> GetAllRoads()
        {
            foreach (var r in _roads.Values)
            {
                if (r.OneWay)
                {
                    yield return new Road(r, Heading.Forward);
                }
                else
                {
                    yield return new Road(r, Heading.Forward);
                    yield return new Road(r, Heading.Backward);
                }
            }
        }

        private static RoadMap ConstructEdges(RoadMap graph)
        {
            var map = new Dictionary<long, IList<Road>>();

            foreach (var edge in graph.Edges.Values)
            {
                if (!map.ContainsKey(edge.Source))
                {
                    map[edge.Source] = new List<Road>() { edge };
                }
                else
                {
                    map[edge.Source].Add(edge);
                }
            }

            IList<Road> successors = null;
            foreach (var edges in map.Values)
            {
                for (int i = 1; i < edges.Count; ++i)
                {
                    var prevEdge = edges[i - 1];
                    prevEdge.Neighbor = edges[i];

                    prevEdge.Successor = map.TryGetValue(prevEdge.Target, out successors) ? successors.First() : default;
                }

                var lastEdge = edges.Last();
                lastEdge.Neighbor = edges.First();
                lastEdge.Successor = map.TryGetValue(lastEdge.Target, out successors) ? successors.First() : default;
            }
            return graph;
        }


    }
}