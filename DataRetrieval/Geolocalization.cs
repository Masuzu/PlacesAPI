using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataRetrieval
{
    public static class Geolocalization
    {
        public const double ParisLatitude = 48.853943;
        public const double ParisLongitude = 2.343521;
        public static double ConvertToRadians(double angle)
        {
            return (Math.PI / 180) * angle;
        }

        // Returns the distance in meters between the points (lat1, lon1) and (lat2, long2)
        public static double Distance(double lat1, double lon1, double lat2, double lon2)
        {
            // Haversince formula
            var R = 6371000; // Eath radius in metres
            lat1 = ConvertToRadians(lat1);
            lat2 = ConvertToRadians(lat2);
            var deltaLat = lat2 - lat1;
            var deltaLon = ConvertToRadians(lon2 - lon1);

            var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                    Math.Cos(lat1) * Math.Cos(lat2) *
                    Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }
    }
}
