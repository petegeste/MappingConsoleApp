# Mapping Console App
An application that uses a brute-force approach to navigate between waypoints in Open Street Map data.

This project was written in an afternoon for CS416 at UNH. I was suddenly inspired to create a mapping application, so here it is!

Steps performed by program:

1. OSM data is parsed into several hashtables:
   - One containing map nodes and their lat/lon coordinates,
   - the other containing ways.
1. Checking for collisions when adding nodes permits the program to identify Intersections
1. Map is simplified to a large collection of Intersections (waypoints). Each Intersection has a collection of Paths which lead to the next Intersection.
1. Each path is given a certain "cost" based on the speed and length of the road segment it reprsents.
1. Start and end coordinates are translated to Intersections on the nearest way.
1. Navigation is performed by a recursive algorithm that starts at the Start Intersection and follows all Paths until it finds the End intersection.
1. On finding the end, waypoints are pushed onto a stack and each recursive method call returns its subpath and accumulated cost.  Each recursive call must choose the subpath that with the lowest cost ad return.
1. Results in the returned stack are displayed in the console, and waypoints are outputted to the WaypointCoords.csv file

# Issues to resolve

- Waypoints may be delivered in the wrong order (oops)
- Adding Intersections along Ways has an issue that makes it impossible to enter a road from either the side it was drawn or the end side -- I don't know which.
- Recursively finding all paths is laughably slow
- Need to add a way to store the most cost-effective subpath from each intersection. Currently a subpath may be explored multiple times.
