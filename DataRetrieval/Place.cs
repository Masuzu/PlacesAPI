using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataRetrieval
{
    public class Tag
    {
        public int id { get; set; }
        public string title { get; set; }
    }

    public class Polygon
    {
        public double x { get; set; }
        public double y { get; set; }
    }

    public class Gadm
    {
        public string id { get; set; }
        public string country { get; set; }
        public string level { get; set; }
        public string is_last_level { get; set; }
        public string name { get; set; }
        public object iso { get; set; }
        public object type { get; set; }
        public string translation { get; set; }
    }

    public class Location
    {
        public double lon { get; set; }
        public double lat { get; set; }
        public double north { get; set; }
        public double south { get; set; }
        public double east { get; set; }
        public double west { get; set; }
        public string country { get; set; }
        public string state { get; set; }
        public string place { get; set; }
        public int country_adm_id { get; set; }
        public List<Gadm> gadm { get; set; }
        public string city_id { get; set; }
        public object city { get; set; }
        public int zoom { get; set; }
    }

    public class Place
    {
        public int id { get; set; }
        public int language_id { get; set; }
        public string language_iso { get; set; }
        public string urlhtml { get; set; }
        public string title { get; set; }
        public List<Tag> tags { get; set; }
        public string wikipedia { get; set; }
        public bool is_building { get; set; }
        public bool is_region { get; set; }
        public bool is_deleted { get; set; }
        public string parent_id { get; set; }
        public List<Polygon> polygon { get; set; }
        public Location location { get; set; }
        public string description { get; set; }
    }

    public class RootObject
    {
        public string language { get; set; }
        // Total number of results
        public string found { get; set; }
        public List<Place> places { get; set; }
        public int page { get; set; }
        // Number of results returned per page
        public int count { get; set; }
    }

    // Captures error messsages
    public class DebugMessage
    {
        public int code { get; set; }
        public string message { get; set; }
    }

    public class DebugRootObject
    {
        public DebugMessage debug { get; set; }
    }

    public class PlaceComparer : IEqualityComparer<Place>
    {
        public bool Equals(Place place1, Place place2)
        {
            return place1.id == place2.id;
        }

        public int GetHashCode(Place customer)
        {
            return customer.id;
        }
    }
}
