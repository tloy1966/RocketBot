﻿#region using directives

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GeoCoordinatePortable;
using PokemonGo.RocketAPI;
using PokemonGo.RocketBot.Logic.Utils;
using POGOProtos.Networking.Responses;

#endregion

namespace PokemonGo.RocketBot.Logic
{
    public delegate void UpdatePositionDelegate(double lat, double lng);

    public class Navigation
    {
        private const double SpeedDownTo = 10/3.6;
        private readonly Client _client;

        public Navigation(Client client)
        {
            _client = client;
        }

        public async Task<PlayerUpdateResponse> Move(GeoCoordinate targetLocation,
            double walkingSpeedInKilometersPerHour, double walkingSpeedOffSetInKilometersPerHour,
            Func<Task<bool>> functionExecutedWhileWalking,
            CancellationToken cancellationToken, bool disableHumanLikeWalking)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!disableHumanLikeWalking)
            {
                var sourceLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude);

                var nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, targetLocation);
                var nextWaypointDistance = getspeedInMetersPerSecond(walkingSpeedInKilometersPerHour,
                    walkingSpeedOffSetInKilometersPerHour);
                ;
                var waypoint = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing);

                //Initial walking
                var requestSendDateTime = DateTime.Now;
                var result =
                    await
                        _client.Player.UpdatePlayerLocation(waypoint.Latitude, waypoint.Longitude,
                            waypoint.Altitude);

                UpdatePositionEvent?.Invoke(waypoint.Latitude, waypoint.Longitude);

                do
                {
                    var speedInMetersPerSecond = getspeedInMetersPerSecond(walkingSpeedInKilometersPerHour,
                        walkingSpeedOffSetInKilometersPerHour);
                    cancellationToken.ThrowIfCancellationRequested();

                    var millisecondsUntilGetUpdatePlayerLocationResponse =
                        (DateTime.Now - requestSendDateTime).TotalMilliseconds;

                    sourceLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude);
                    var currentDistanceToTarget = LocationUtils.CalculateDistanceInMeters(sourceLocation, targetLocation);

                    if (currentDistanceToTarget < 40)
                    {
                        if (speedInMetersPerSecond > SpeedDownTo)
                        {
                            //Logger.Write("We are within 40 meters of the target. Speeding down to 10 km/h to not pass the target.", LogLevel.Info);
                            speedInMetersPerSecond = SpeedDownTo;
                        }
                    }

                    nextWaypointDistance = Math.Min(currentDistanceToTarget,
                        millisecondsUntilGetUpdatePlayerLocationResponse/1000*speedInMetersPerSecond);
                    nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, targetLocation);
                    waypoint = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing);

                    requestSendDateTime = DateTime.Now;
                    result =
                        await
                            _client.Player.UpdatePlayerLocation(waypoint.Latitude, waypoint.Longitude,
                                waypoint.Altitude);

                    UpdatePositionEvent?.Invoke(waypoint.Latitude, waypoint.Longitude);


                    if (functionExecutedWhileWalking != null)
                        await functionExecutedWhileWalking(); // look for pokemon
                } while (LocationUtils.CalculateDistanceInMeters(sourceLocation, targetLocation) >= 30);

                return result;
            }

            var curLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude);
            var dist = LocationUtils.CalculateDistanceInMeters(curLocation, targetLocation);
            if (dist >= 100)
            {
                var nextWaypointDistance = dist*70/100;
                var nextWaypointBearing = LocationUtils.DegreeBearing(curLocation, targetLocation);

                var waypoint = LocationUtils.CreateWaypoint(curLocation, nextWaypointDistance, nextWaypointBearing);
                var sentTime = DateTime.Now;

                var result =
                    await
                        _client.Player.UpdatePlayerLocation(waypoint.Latitude, waypoint.Longitude,
                            waypoint.Altitude);
                UpdatePositionEvent?.Invoke(waypoint.Latitude, waypoint.Longitude);

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var millisecondsUntilGetUpdatePlayerLocationResponse =
                        (DateTime.Now - sentTime).TotalMilliseconds;

                    curLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude);
                    var currentDistanceToTarget = LocationUtils.CalculateDistanceInMeters(curLocation, targetLocation);

                    dist = LocationUtils.CalculateDistanceInMeters(curLocation, targetLocation);
                    if (dist >= 100)
                    {
                        nextWaypointDistance = dist*70/100;
                    }
                    else
                    {
                        nextWaypointDistance = dist;
                    }
                    nextWaypointBearing = LocationUtils.DegreeBearing(curLocation, targetLocation);
                    waypoint = LocationUtils.CreateWaypoint(curLocation, nextWaypointDistance, nextWaypointBearing);
                    sentTime = DateTime.Now;
                    result =
                        await
                            _client.Player.UpdatePlayerLocation(waypoint.Latitude, waypoint.Longitude,
                                waypoint.Altitude);

                    UpdatePositionEvent?.Invoke(waypoint.Latitude, waypoint.Longitude);


                    if (functionExecutedWhileWalking != null)
                        await functionExecutedWhileWalking(); // look for pokemon
                } while (LocationUtils.CalculateDistanceInMeters(curLocation, targetLocation) >= 10);
                return result;
            }
            else
            {
                var result =
                    await
                        _client.Player.UpdatePlayerLocation(targetLocation.Latitude, targetLocation.Longitude,
                            LocationUtils.getElevation(targetLocation.Latitude, targetLocation.Longitude));
                UpdatePositionEvent?.Invoke(targetLocation.Latitude, targetLocation.Longitude);
                if (functionExecutedWhileWalking != null)
                    await functionExecutedWhileWalking(); // look for pokemon
                return result;
            }
        }

        public async Task<PlayerUpdateResponse> HumanPathWalking(GpxReader.Trkpt trk,
            double walkingSpeedInKilometersPerHour, Func<Task<bool>> functionExecutedWhileWalking,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            //PlayerUpdateResponse result = null;

            var targetLocation = new GeoCoordinate(Convert.ToDouble(trk.Lat, CultureInfo.InvariantCulture),
                Convert.ToDouble(trk.Lon, CultureInfo.InvariantCulture));

            var speedInMetersPerSecond = walkingSpeedInKilometersPerHour/3.6;

            var sourceLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude);
            LocationUtils.CalculateDistanceInMeters(sourceLocation, targetLocation);
            // Logger.Write($"Distance to target location: {distanceToTarget:0.##} meters. Will take {distanceToTarget/speedInMetersPerSecond:0.##} seconds!", LogLevel.Info);

            var nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, targetLocation);
            var nextWaypointDistance = speedInMetersPerSecond;
            var waypoint = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing,
                Convert.ToDouble(trk.Ele, CultureInfo.InvariantCulture));

            //Initial walking

            var requestSendDateTime = DateTime.Now;
            var result =
                await
                    _client.Player.UpdatePlayerLocation(waypoint.Latitude, waypoint.Longitude, waypoint.Altitude);

            UpdatePositionEvent?.Invoke(waypoint.Latitude, waypoint.Longitude);

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var millisecondsUntilGetUpdatePlayerLocationResponse =
                    (DateTime.Now - requestSendDateTime).TotalMilliseconds;

                sourceLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude);
                var currentDistanceToTarget = LocationUtils.CalculateDistanceInMeters(sourceLocation, targetLocation);

                //if (currentDistanceToTarget < 40)
                //{
                //    if (speedInMetersPerSecond > SpeedDownTo)
                //    {
                //        //Logger.Write("We are within 40 meters of the target. Speeding down to 10 km/h to not pass the target.", LogLevel.Info);
                //        speedInMetersPerSecond = SpeedDownTo;
                //    }
                //}

                nextWaypointDistance = Math.Min(currentDistanceToTarget,
                    millisecondsUntilGetUpdatePlayerLocationResponse/1000*speedInMetersPerSecond);
                nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, targetLocation);
                waypoint = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing);

                requestSendDateTime = DateTime.Now;
                result =
                    await
                        _client.Player.UpdatePlayerLocation(waypoint.Latitude, waypoint.Longitude,
                            waypoint.Altitude);

                UpdatePositionEvent?.Invoke(waypoint.Latitude, waypoint.Longitude);

                if (functionExecutedWhileWalking != null)
                    await functionExecutedWhileWalking(); // look for pokemon & hit stops
            } while (LocationUtils.CalculateDistanceInMeters(sourceLocation, targetLocation) >= 30);

            return result;
        }

        public double getspeedInMetersPerSecond(double SpeedInKilometersPerHour, double SpeedOffSetInKilometersPerHour)
        {
            var random = new Random();
            double offset;
            if (random.Next(0, 2) == 1)
                offset = random.NextDouble()*SpeedOffSetInKilometersPerHour;
            else
                offset = -random.NextDouble()*SpeedOffSetInKilometersPerHour;

            return (SpeedInKilometersPerHour + offset)/3.6;
        }

        public event UpdatePositionDelegate UpdatePositionEvent;
    }
}