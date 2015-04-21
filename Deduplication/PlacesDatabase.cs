using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deduplication
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

    public class PlaceComparer : IEqualityComparer<Place>
    {
        public bool Equals(Place place1, Place place2)
        {
            return (place1.id == place2.id && place1.title.Equals(place2.title));
        }

        public int GetHashCode(Place customer)
        {
            return customer.id;
        }
    }
    
    public class PlacesDatabase
    {
        public enum Tiling {Coordinates, City};

        Tiling tiling;

        public const int UndefinedTileId = Int32.MinValue;

        public HashSet<Place> Places { get; private set; }
        Dictionary<String, List<Place>> placesByName = new Dictionary<String, List<Place>>();
        Dictionary<String, List<Place>> placesByCity = new Dictionary<String, List<Place>>();

        public double MinLatitude { get; private set; }
        public double MaxLatitude { get; private set; }
        public double MinLongitude { get; private set; }
        public double MaxLongitude { get; private set; }

        public double TileSize { get; private set; }

        public List<Place>[,] placesByArea;

        public PlacesDatabase()
        {
            Places = new HashSet<Place>(new PlaceComparer());
            MinLatitude = Double.MaxValue;
            MinLongitude = Double.MaxValue;
            MaxLatitude = Double.MinValue;
            MaxLongitude = Double.MinValue;
        }

        public void Load(String JSONFile)
        {
            using (StreamReader r = new StreamReader(JSONFile))
            {
                string serializedJSON = r.ReadToEnd();
                List<Place> fileData = JsonConvert.DeserializeObject<List<Place>>(serializedJSON);
                foreach (Place place in fileData)
                {
                    MinLatitude = Math.Min(MinLatitude, place.location.lat);
                    MinLongitude = Math.Min(MinLongitude, place.location.lon);
                    MaxLatitude = Math.Max(MaxLatitude, place.location.lat);
                    MaxLongitude = Math.Max(MaxLongitude, place.location.lon);

                    if (place.title == null)
                    {
                        // Set the text between the <a> tags of urlhtml as the title of place if it is missing
                        HtmlDocument doc = new HtmlDocument();
                        doc.LoadHtml(place.urlhtml);
                        place.title = doc.DocumentNode.SelectNodes("//a[@href]")[0].InnerText;
                    }
                    Places.Add(place);

                    string placeName = place.title;
                    if (!placesByName.ContainsKey(placeName))
                        placesByName.Add(placeName, new List<Place>());
                    placesByName[placeName].Add(place);
                }
            }
        }

        // Add a new place named 'placeTitle' if it was not found in the places database
        public List<Place> AddPlace(string placeTitle)
        {
            if (!placesByName.ContainsKey(placeTitle))
            {
                Random random = new Random();
                Place newPlace = new Place { id = random.Next(), title = placeTitle };
                Places.Add(newPlace);
                placesByName[placeTitle] = new List<Place>();
                placesByName[placeTitle].Add(newPlace);
            }
            return placesByName[placeTitle];
        }

        public List<Place> GetByName(string placeName)
        {
            List<Place> places;
            if (placesByName.TryGetValue(placeName, out places))
                return places;
            return null;
        }

        public int GetTileId(Place place)
        {
            if (tiling == Tiling.City)
                return Convert.ToInt32(place.location.city_id);
            else if (tiling == Tiling.Coordinates)
            {
                // Cantor pairing function (http://en.wikipedia.org/wiki/Pairing_function#Cantor_pairing_function)
                int i = (int)Math.Floor((place.location.lat - MinLatitude) / TileSize);
                int j = (int)Math.Floor((place.location.lon - MinLongitude) / TileSize);
                return (int)((double)((i + j) * (i + j + 1)) / 2 + j);
            }
            return 0;
        }

        public List<Place> GetPlacesInSameTileAs(Place place)
        {
            if (tiling == Tiling.City)
                return placesByCity[place.location.city_id];
            else if (tiling == Tiling.Coordinates)
            {
                int i = (int)Math.Floor((place.location.lat - MinLatitude) / TileSize);
                int j = (int)Math.Floor((place.location.lon - MinLongitude) / TileSize);
                return placesByArea[i, j];
            }
            return null;
        }

        public void GenerateTilesByCity()
        {
            tiling = Tiling.City;
            foreach(Place place in Places)
            {
                var cityId = place.location.city_id;
                if (!placesByCity.ContainsKey(cityId))
                    placesByCity.Add(cityId, new List<Place>());
                placesByCity[cityId].Add(place);
            }
        }

        public void GenerateTiles(double tileSize)
        {
            tiling = Tiling.Coordinates;
            TileSize = tileSize;
            double latRange = MaxLatitude - MinLatitude;
            double lonRange = MaxLongitude - MinLongitude;
            placesByArea = new List<Place>[(int)(Math.Ceiling(latRange / tileSize)), (int)(Math.Ceiling(lonRange / tileSize))];
            for (int i = 0; i < placesByArea.GetLength(0); ++i )
            {
                for (int j = 0; j < placesByArea.GetLength(1); ++j)
                    placesByArea[i, j] = new List<Place>();
            }

            foreach (Place place in Places)
            {
                int i = (int)Math.Floor((place.location.lat - MinLatitude) / tileSize);
                int j = (int)Math.Floor((place.location.lon - MinLongitude) / tileSize);
                placesByArea[i, j].Add(place);
            }
        }
    }
}
