using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Text.RegularExpressions;


/* 
 * Navigation Application
 * Navigates from Point A to Point B using OSM data
 * 
 * Peter Gestewitz
 * 
 * TODO: Filter out intersections created by just joining paths in output
 * TODO: Deploy cost-calculation
 * TODO: Use road directions
 * 
 */


namespace MapApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public class Program
    {
        public static void Main()
        {
            (new Program()).RunProgram();
        }

        public void RunProgram()
        {
            ParseOSM();
            if (!GetCoords(out Intersection start, out Intersection end))
            {
                return;
            }
            Navigate(start, end);
        }

        private bool GetCoords(out Intersection start, out Intersection end)
        {
            Regex latLonPattern = new Regex(@"([-.0-9]+)");
            Console.Write("\nPlease enter start coordinates: ");
            var startCoords = GetMatches(latLonPattern.Match(Console.ReadLine()));

            Console.Write("\nPlease enter end coordinates: ");
            var endCoords = GetMatches(latLonPattern.Match(Console.ReadLine()));

            if (startCoords.Length != 2 || endCoords.Length != 2)
            {
                Console.WriteLine("\nInvalid input. Sorry!");
                start = end = null;
                return false;
            }

            end = AddMarkerNear(startCoords[0], startCoords[1]);
            start = AddMarkerNear(endCoords[0], endCoords[1]);

            return true;
        }

        private void Navigate(Intersection start, Intersection end)
        {

            Console.WriteLine("\nGetting directions...");
            var stopwatch = Stopwatch.StartNew();
            Stack<Intersection> intersections = new Stack<Intersection>();
            var res = GetDirections(start, end, ref intersections, 20);
            stopwatch.Stop();

            Console.WriteLine($"Got directions in {stopwatch.ElapsedMilliseconds / 1000.0} s");
            Console.WriteLine($"{(res > 0 ? "Found directions!" : "No path found")}");
            coordinates = new StreamWriter(File.Create(@"WaypointCoords.csv"));
            foreach (var i in intersections)
            {
                Console.WriteLine($"{i.Location.Latitude}, {i.Location.Longitude} {i.Location.AssociatedWay.RoadName}");
                coordinates.WriteLine($"{i.Location.Latitude},{i.Location.Longitude}");
            }
            coordinates.Close();
            Console.ReadKey();
        }

        private string[] GetMatches(Match matches)
        {
            List<string> list = new List<string>();
            Match match = matches;
            while (match.Success)
            {
                list.Add(match.Value);
                match = match.NextMatch();
            }
            return list.ToArray();
        }

        Dictionary<long, Node> Nodes;
        Dictionary<long, Way> Ways;
        Dictionary<long, Intersection> Intersections;

        public void ParseOSM()
        {
            Console.Write("Please enter path to OSM file: ");
            string path = Console.ReadLine();
            Console.WriteLine();
            var xml = LoadXML(path);
            ProcessNodes(xml);
            ProcessWays(xml);
            xml = null;
            GC.Collect();
            ProcessIntersections();
        }

        StreamWriter coordinates;
         public double GetDirections(Intersection start, Intersection end, ref Stack<Intersection> subroute, int maxDepth, bool rough = false)
        {
            if (maxDepth <= 0 || start?.Next == null)
            {
                return -1;
            }
            else if (start == end)
            {
                subroute.Push(end);
                return 0; // Found solution!!!
            }
            else if (start.AlreadyExplored)
            {
                return -1;
            }
            else if (start.Next.Where(n => n.End != start).Count() == 1)
            {
                var path = start.Next.Where(n => n.End != start).FirstOrDefault();
                double cost = GetDirections(path.End, end, ref subroute, maxDepth, rough);
                if (cost < 0)
                {
                    return -1;
                }
                else
                {
                    return path.Cost + cost;
                }
                
            }
            else
            {
                subroute.Push(start);
                start.AlreadyExplored = true;
                var paths = start.Next.Where(n => n.End != start).OrderBy(p => Node.DistanceBetween(p.End.Location, end.Location));

                double bestValue = double.MaxValue;
                Stack<Intersection> bestSubroute = null;
                foreach (var path in paths)
                {
                    Stack<Intersection> newSubroute = new Stack<Intersection>();
                    newSubroute.Push(path.End);
                    double cost = GetDirections(path.End, end, ref newSubroute, maxDepth - 1, rough);
                    if (cost < 0)
                    {
                        // solution not found
                        continue;
                    }
                    if (path.Cost + cost < bestValue)
                    {
                        bestValue = path.Cost + cost;
                        bestSubroute = newSubroute;
                    }
                }
                start.AlreadyExplored = false;
                if (bestValue == double.MaxValue)
                {
                    return -1;
                }
                // copy best route onto this stack
                while(bestSubroute.Count > 0)
                {
                    subroute.Push(bestSubroute.Pop());
                }
                return bestValue;
            }
        }

        public XElement LoadXML(string path)
        {
            Console.WriteLine("Opening XML...");
            var stopwatch = Stopwatch.StartNew();
            var xml = XElement.Load(path);
            stopwatch.Stop();
            Console.WriteLine($"Completed load in {stopwatch.ElapsedMilliseconds / 1000.0} s");
            return xml;
        }

        public void ProcessNodes(XElement file)
        {
            Nodes = new Dictionary<long, Node>();
            Console.WriteLine("Starting to process nodes...");
            var stopwatch = Stopwatch.StartNew();
            IEnumerable<XElement> nodes = file.Elements("node");
            foreach (var e in nodes)
            {
                Nodes.Add(long.Parse(e.Attribute("id").Value), new Node(e.Attribute("lat").Value, e.Attribute("lon").Value));
            }
            stopwatch.Stop();
            Console.WriteLine($"Finished reading nodes in {stopwatch.ElapsedMilliseconds / 1000.0} s");
        }

        public void ProcessWays(XElement file)
        {
            Regex speed = new Regex(@"[0-9]*");
            Ways = new Dictionary<long, Way>();
            Console.WriteLine("Processing ways...");
            var stopwatch = Stopwatch.StartNew();
            IEnumerable<XElement> ways = file.Elements("way");
            foreach (var e in ways)
            {
                var highway = e.XPathSelectElement("tag[@k='highway']");
                var name = e.XPathSelectElement("tag[@k='name']");
                var maxSpeed = e.XPathSelectElement("tag[@k='maxspeed']");

                if (highway != null && name != null)
                {
                    double max = 30;
                    if (maxSpeed != null)
                    {
                        max = double.Parse(speed.Match(maxSpeed.Attribute("v").Value).Value);
                    }
                    Ways.Add(long.Parse(e.Attribute("id").Value), new Way(e.Elements("nd").Select(x => long.Parse(x.Attribute("ref").Value)), name.Attribute("v").Value, max));
                }

            }
            stopwatch.Stop();
            Console.WriteLine($"Done in {stopwatch.ElapsedMilliseconds / 1000.0} s");
        }

        public void ProcessIntersections()
        {
            Console.WriteLine("Starting to process intersections...");
            var stopwatch = Stopwatch.StartNew();
            Intersections = new Dictionary<long, Intersection>();

            // maps the id of a node to the way it is used in
            Dictionary<long, Way> NodesUsedInWays = new Dictionary<long, Way>();


            // get all overlapping nodes in all ways.
            // make intersections of all duplicate points with way vectors for each.
            foreach (var way in Ways.Values)
            {
                Way w = (Way)way;
                long first = w.Nodes.First();
                long last = w.Nodes.Last();

                var start = GetOrCreateIntersection(first);
                var end = GetOrCreateIntersection(last);

                AddPathBetweenIntersections(w, start, end);

                foreach (long node in w.Nodes)
                {
                    
                    if (NodesUsedInWays.ContainsKey(node))
                    {
                        // duplicate! create an intersection.
                        Nodes[node].AssociatedWay = w;
                        Intersection i = GetOrCreateIntersection(node);
                        SplicePathAlongWay(w, start, i, end);
                    }
                    else
                    {
                        // add node to hashmap
                        NodesUsedInWays.Add(node, w);
                    }
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"Done in {stopwatch.ElapsedMilliseconds / 1000.0} s");
            GC.Collect();
        }

        private Intersection AddMarkerNear(string lat, string lon)
        {
            Node pos = new Node(lat, lon);
            long closest = -1;
            double closestDist = double.MaxValue;
            double d;
            foreach (var n in Nodes)
            {
                if (n.Value.AssociatedWay == null) continue;
                d = Node.DistanceBetween(n.Value, pos);
                if (d < closestDist)
                {
                    closest = n.Key;
                    closestDist = d;
                }
            }

            Way way = Nodes[closest].AssociatedWay;
            SplicePathAlongWay(way, GetOrCreateIntersection(way.GetStart()), GetOrCreateIntersection(closest), GetOrCreateIntersection(way.GetEnd()));

            return GetOrCreateIntersection(closest);
        }

        private Intersection GetOrCreateIntersection(long id)
        {
            if (Intersections.ContainsKey(id))
            {
                return Intersections[id];
            }
            else
            {
                Intersection i = new Intersection();
                i.Location = Nodes[id];
                i.NodeLocation = id;
                Intersections.Add(id, i);
                return i;
            }
        }

        private void SplicePathAlongWay(Way way, Intersection start, Intersection midpoint, Intersection end)
        {
            if (start.NodeLocation == midpoint.NodeLocation || end.NodeLocation == midpoint.NodeLocation)
            {
                return;
            }

            start.RemoveDestination(end);
            end.RemoveDestination(start);
            AddPathBetweenIntersections(way, start, midpoint);
            AddPathBetweenIntersections(way, midpoint, end);
            AddPathBetweenIntersections(way, midpoint, start);
            AddPathBetweenIntersections(way, end, midpoint);
        }

        /// <summary>
        /// Adds path to intersections that follow a way
        /// </summary>
        /// <param name="way"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        private void AddPathBetweenIntersections(Way way, Intersection start, Intersection end)
        {
            start.AddExitingPath(PathFromAToB(way, start.NodeLocation, end.NodeLocation));
            end.AddExitingPath(PathFromAToB(way, end.NodeLocation, start.NodeLocation));
        }

        private Path PathFromAToB(Way way, long start, long end)
        {
            Path path = new Path();

            // calculate distance
            Node point = Nodes[start];
            Node point2;
            double distance = 0;
            var items = way.EnumerateFromTo(start, end).Skip(1);
            foreach (long id in items)
            {
                point2 = Nodes[id];
                distance += Node.DistanceBetween(point, point2);
                point = point2;
            }

            if (distance == 0)
            {
                return null;
            }

            // compute cost
            path.Cost = (distance / way.Speed);

            path.End = GetOrCreateIntersection(end);

            return path;
        }

    }

    /// <summary>
    /// represents a node on a map
    /// </summary>
    [DebuggerDisplay("{Latitude},{Longitude}")]
    public class Node
    {
        public float Latitude;
        public float Longitude;
        public Node Next;
        public Node Previous;
        public bool isIntersection;
        public Way AssociatedWay;

        public Node(string lat, string lon)
        {
            float.TryParse(lat, out Latitude);
            float.TryParse(lon, out Longitude);
            Next = null;
            Previous = null;
        }

        /// <summary>
        /// Distance between nodes in miles.
        /// 
        /// Algorithm taken from outside source:
        /// https://www.movable-type.co.uk/scripts/latlong.html
        /// </summary>
        /// <param name="nodeA"></param>
        /// <param name="nodeB"></param>
        /// <returns></returns>
        public static double DistanceBetween(Node nodeA, Node nodeB)
        {
            double R = 6371e3; // metres
            double φ1 = nodeA.Latitude * Math.PI / 180; // φ, λ in radians
            double φ2 = nodeB.Latitude * Math.PI / 180;
            double Δφ = (nodeB.Latitude - nodeA.Latitude) * Math.PI / 180;
            double Δλ = (nodeB.Longitude - nodeA.Longitude) * Math.PI / 180;

            double a = Math.Sin(Δφ / 2) * Math.Sin(Δφ / 2) +
                      Math.Cos(φ1) * Math.Cos(φ2) *
                      Math.Sin(Δλ / 2) * Math.Sin(Δλ / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return (R * c) * 0.000621371; // in miles
        }

    }

    /// <summary>
    /// Represents an OSM way
    /// </summary>
    public class Way
    {
        public string RoadName;
        public double Speed;
        public List<long> Nodes = new List<long>();
        public bool GoesForward = true;
        public bool GoesBackward = true;
        public Way(IEnumerable<long> nodes, string roadName, double speed)
        {
            Nodes = nodes.ToList();
            RoadName = roadName;
            Speed = speed;
        }

        public long GetStart()
        {
            return Nodes.First();
        }

        public long GetEnd()
        {
            return Nodes.Last();
        }

        /// <summary>
        /// Enumerates nodes from start to end of way
        /// </summary>
        /// <param name="start"></param>
        /// <returns></returns>
        public IEnumerable<long> EnumerateNodesForward(long start)
        {
            if (!GoesForward) yield break;
            for (int idx = Nodes.IndexOf(start); idx < Nodes.Count; idx++)
            {
                yield return Nodes[idx];
            }
        }

        /// <summary>
        /// Enumerates nodes from start to beginning of way
        /// </summary>
        /// <param name="start"></param>
        /// <returns></returns>
        public IEnumerable<long> EnumerateNodesBackwards(long start)
        {
            if (!GoesBackward) yield break;
            for (int idx = Nodes.IndexOf(start); idx >= 0; idx--)
            {
                yield return Nodes[idx];
            }
        }

        public IEnumerable<long> EnumerateFromTo(long start, long end)
        {
            var startIndex = Nodes.IndexOf(start);
            var endIndex = Nodes.IndexOf(end);
            int d = endIndex >= startIndex ? 1 : -1;

            if (d > 0 && !GoesForward) yield break;
            if (d < 0 && !GoesBackward) yield break;

            for (int idx = startIndex; idx != endIndex + d; idx += d)
            {
                yield return Nodes[idx];
            }
        }

    }

    /// <summary>
    /// Intersections have the same ID as the nodes they occur at
    /// </summary>
    [DebuggerDisplay("{Location.AssociatedWay.RoadName}")]
    public class Intersection : EqualityComparer<Intersection>
    {
        public List<Path> Next = new List<Path>();
        public Node Location;
        public long NodeLocation;
        public bool AlreadyExplored = false;
        public void AddExitingPath(Path path)
        {
            if (path == null)
            {
                return;
            }
            if (Next.Find(p => p.End.NodeLocation == path.End.NodeLocation) == null)
            {
                Next.Add(path);
            }

        }

        public void RemoveDestination(Intersection i)
        {
            Next.Remove(Next.Find(p => p.End == i));
        }

        public override bool Equals([AllowNull] Intersection x, [AllowNull] Intersection y)
        {
            return long.Equals(x.NodeLocation, y.NodeLocation);
        }

        public override int GetHashCode([DisallowNull] Intersection obj)
        {
            return NodeLocation.GetHashCode();
        }
    }

    /// <summary>
    /// An edge that must be followed to go to another intersection
    /// </summary>
    [DebuggerDisplay("{End.Location.AssociatedWay.RoadName}")]
    public class Path : EqualityComparer<Path>
    {
        public Intersection End;
        public double Cost;

        public override bool Equals([AllowNull] Path x, [AllowNull] Path y)
        {
            return x.Equals(y);
        }

        public override int GetHashCode([DisallowNull] Path obj)
        {
            return HashCode.Combine(End.GetHashCode(), Cost.GetHashCode());
        }
    }
}
