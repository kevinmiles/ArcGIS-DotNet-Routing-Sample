﻿using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.Tasks.NetworkAnalyst;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if NETFX_CORE
using Windows.UI.Xaml;
#else
using System.Windows.Threading;
#endif

namespace RoutingSample
{
	/// <summary>
	/// Location simulator that takes a route result and simulates driving down the route
	/// </summary>
	public class RouteLocationSimulator : ILocationProvider
	{
		private DispatcherTimer timer;
		private RouteResult m_route;
		private int directionIndex;
		private double totalDistance;
		private double lineLength;
		private Polyline drivePath;

		/// <summary>
		/// Initializes a new instance of the <see cref="RouteLocationSimulator"/> class.
		/// </summary>
		/// <param name="route">The route to use for simulation. The spatial reference of the route
		/// must be in geographic coordinates (WGS84).</param>
		/// <param name="startPoint">An optional starting point.</param>
		public RouteLocationSimulator(RouteResult route, MapPoint startPoint = null)
		{
			if (route == null)
				throw new ArgumentNullException("route");
			m_route = route;
			timer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
			if (startPoint == null)
			{
				startPoint = new MapPoint(route.Directions.First().MergedGeometry.Paths[0][0], SpatialReferences.Wgs84);
			}
			timer.Tick += timer_Tick;
			Speed = 50; // double.NaN;
			directionIndex = 0;
			lineLength = 0;// GeometryEngine.GeodesicLength(route.Directions.First().MergedGeometry);
			drivePath = route.Directions.First().MergedGeometry;
		}

		/// <summary>
		/// Gets or sets the speed to drive down the route in meters per second.
		/// </summary>
		public double Speed { get; set; }
		
		private void timer_Tick(object sender, object e)
		{
			var time = timer.Interval;

			double deltaDistance = time.TotalSeconds * Speed;
			double course;
			double[] p = PointAlongLine(totalDistance + deltaDistance, out course);
			if (p == null)
			{
				totalDistance = 0;
				return;
			}
			totalDistance += deltaDistance;
			double lon = p[0];
			double lat = p[1];
			while (lat < -90) lat += 180;
			while (lat > 90) lat -= 180;
			while (lon < -180) lon += 360;
			while (lon > 180) lon -= 360;
			while (course < 0) course += 360;
			while (course > 360) course -= 360;

			if (LocationChanged != null)
			{
				 LocationChanged(this, new LocationInfo()
				{
					Course = course,
					Speed = Speed,
					Location = new MapPoint(lon, lat, SpatialReferences.Wgs84),
					HorizontalAccuracy = 0.001
				});
			}
		}

		/// <summary>
		/// Raised when the location provider has a new location.
		/// </summary>
		public event EventHandler<LocationInfo> LocationChanged;

		/// <summary>
		/// Starts the location provider.
		/// </summary>
		/// <returns>
		/// Task
		/// </returns>
		public Task Start()
		{
			timer.Start();
			return Task.FromResult(true);
		}

		/// <summary>
		/// Stops the location provider.
		/// </summary>
		/// <returns>
		/// Task
		/// </returns>
		public Task Stop()
		{
			timer.Stop();
			return Task.FromResult(true);
		}

		#region Some funky geodesic trigonometry here...

		/// <summary>
		/// Gets a point a certain distance down a polyline
		/// </summary>
		/// <param name="dist">Distance in meters along the line</param>
		/// <param name="course"></param>
		/// <returns></returns>
		private double[] PointAlongLine(double dist, out double course)
		{
			double accDist = 0;
			course = double.NaN;
			if (dist > lineLength) //reached end - move to next direction, or start over
			{
				directionIndex++;
				Direction currDir;
				if (directionIndex >= m_route.Directions.Count)
					directionIndex = 0;
				currDir = m_route.Directions[directionIndex];
				lineLength = GeometryEngine.GeodesicLength(currDir.MergedGeometry);
				totalDistance = 0;
				drivePath = currDir.MergedGeometry;
				course = 0; dist = 0;
				var start = drivePath.Paths[0][0];
			}
			//else
			{
				for (int j = 0; j < drivePath.Paths.Count; j++)
			{

					var part = drivePath.Paths[j];
				for (int i = 0; i < part.Count - 1; i++)
				{
					var p1 = part[i];
					var p2 = part[i + 1];
					if (p1.X == p2.X && p2.Y == p2.Y)
						continue;
						Polyline linesegment = new Polyline() { SpatialReference = SpatialReferences.Wgs84 };
						linesegment.Paths.AddPart(new Coordinate[] { new Coordinate(p1.X, p1.Y), new Coordinate(p2.X, p2.Y) });
					double distToWaypoint = GeometryEngine.GeodesicLength(linesegment);
					if (dist < accDist + distToWaypoint)
					{
						var distAlongSegment = dist - accDist;
						double fraction = distAlongSegment / distToWaypoint;
						course = GetTrueBearingGeodesic(p1.X, p1.Y, p2.X, p2.Y);
						return GetPointFromHeadingGeodesic(new double[] { p1.X, p1.Y }, distAlongSegment, course);
					}
					accDist += distToWaypoint;
				}
			}
			}
			return null;
		}

		/// <summary>
		/// Calculates the heading of a line segment
		/// </summary>
		/// <param name="lon1"></param>
		/// <param name="lat1"></param>
		/// <param name="lon2"></param>
		/// <param name="lat2"></param>
		/// <returns></returns>
		private static double GetTrueBearingGeodesic(double lon1, double lat1, double lon2, double lat2)
		{
			lat1 = lat1 / 180 * Math.PI;
			lon1 = lon1 / 180 * Math.PI;
			lat2 = lat2 / 180 * Math.PI;
			lon2 = lon2 / 180 * Math.PI;
			double tc1 = Math.Atan2(Math.Sin(lon1 - lon2) * Math.Cos(lat2),
				Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(lon1 - lon2)) % (2 * Math.PI);
			return 360 - (tc1 / Math.PI * 180);
		}

		/// <summary>
		/// Given a starting point, distance and heading, calculates a new point
		/// </summary>
		/// <param name="start"></param>
		/// <param name="distance"></param>
		/// <param name="heading"></param>
		/// <returns></returns>
		private static double[] GetPointFromHeadingGeodesic(double[] start, double distance, double heading)
		{
			double brng = heading / 180 * Math.PI;
			double lon1 = start[0] / 180 * Math.PI;
			double lat1 = start[1] / 180 * Math.PI;
			double dR = distance / 6378137; //Angular distance in radians
			double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(dR) + Math.Cos(lat1) * Math.Sin(dR) * Math.Cos(brng));
			double lon2 = lon1 + Math.Atan2(Math.Sin(brng) * Math.Sin(dR) * Math.Cos(lat1), Math.Cos(dR) - Math.Sin(lat1) * Math.Sin(lat2));
			double lon = lon2 / Math.PI * 180;
			double lat = lat2 / Math.PI * 180;
			while (lon < -180) lon += 360;
			while (lat < -90) lat += 180;
			while (lon > 180) lon -= 360;
			while (lat > 90) lat -= 180;
			return new double[] { lon, lat };
		}
		#endregion
	}
}
