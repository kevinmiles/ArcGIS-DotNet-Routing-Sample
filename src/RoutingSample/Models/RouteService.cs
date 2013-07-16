﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Tasks.NetworkAnalyst;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Tasks.Geocoding;
using Esri.ArcGISRuntime.Layers;
using Esri.ArcGISRuntime.Http;

namespace RoutingSample.Models
{
	/// <summary>
	/// Helper service that handles geocoding and routing
	/// </summary>
	public class RouteService
	{
		private const string locatorService = "http://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer";
		private const string routeService = "http://tasks.arcgisonline.com/ArcGIS/rest/services/NetworkAnalysis/ESRI_Route_NA/NAServer/Route";
		private const string longRouteService = "http://tasks.arcgisonline.com/ArcGIS/rest/services/NetworkAnalysis/ESRI_Route_NA/NAServer/Long_Route"; //faster for routes > 200km

		public RouteService() { }

		public async Task<RouteResult> GetRoute(string address, MapPoint from, CancellationToken cancellationToken)
		{
			var to = await Geocode(address, cancellationToken).ConfigureAwait(false);
			if (to == null)
				throw new ArgumentException("Address not found");
			return await GetRoute(from, to, cancellationToken);
		}

		public async Task<MapPoint> Geocode(string address, CancellationToken cancellationToken)
		{
			Locator locator = new Locator(new Uri(locatorService))
				 { HttpMessageHandler = messageHandler };
			var result = await locator.FindAsync(new LocatorFindParameter(address),
				cancellationToken).ConfigureAwait(false);
			if (result.Locations != null && result.Locations.Count > 0)
				return result.Locations.First().Feature.Geometry as MapPoint;
			return null;
		}

		public Task<RouteResult> GetRoute(MapPoint from, MapPoint to, CancellationToken cancellationToken)
		{
			return GetRoute(new MapPoint[] { from, to }, cancellationToken);
		}

		public Task<RouteResult> GetRoute(IEnumerable<MapPoint> stops, CancellationToken cancellationToken)
		{
			if (stops == null)
				throw new ArgumentNullException("stops");

			List<Graphic> stopList = new List<Graphic>();
			foreach (var stop in stops)
			{
				stopList.Add(new Graphic(stop));
			}
			if (stopList.Count < 2)
				throw new ArgumentException("Not enough stops");

			//determine which route service to use. Long distance routes should use the long-route service
			Polyline line = new Polyline() { SpatialReference = SpatialReferences.Wgs84 };
			line.Paths.AddPart(stops.Select(m => m.Coordinate));
			var length = GeometryEngine.GeodesicLength(line);
			string svc = routeService;
			if (length > 200000)
				svc = longRouteService;

			//Calculate route
			RouteTask task = new RouteTask(new Uri(svc)) { HttpMessageHandler = messageHandler };
			return task.SolveAsync(new RouteParameter(new FeatureStops(stopList))
			{
				OutputLines = OutputLine.TrueShapeWithMeasure,
				OutSpatialReference = SpatialReferences.Wgs84,
				ReturnStops = true,
				DirectionsLengthUnits = Esri.ArcGISRuntime.ArcGISServices.MapUnit.Meters ,
				UseTimeWindows = false,
				RestrictionAttributeNames = new List<string>(new string[] { "OneWay "})
			}, cancellationToken);
		}


//The following is solely used for mocking the route and location services in unit tests
		private HttpMessageHandler messageHandler = new ArcGISHttpClientHandler();

		public RouteService(HttpMessageHandler messageHandler) //Used for testing with mock service
		{
			if (messageHandler == null)
				throw new ArgumentNullException();
			this.messageHandler = messageHandler;
		}
	}
}
