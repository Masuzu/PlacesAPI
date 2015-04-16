
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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

    class Program
    {
       
        private HashSet<Place> data = new HashSet<Place>(new PlaceComparer());

        private String FormatRequest(double lonMin, double latMin, double lonMax, double latMax, uint page = 1, int pageCount = 100)
        {
            String APIRequest = "http://api.wikimapia.org/?function=place.getbyarea&key=2459245A-BC667625-51063F19-44719F55-DFD013ED-D091DECB-D81D04C5-4C94E3C3";
            APIRequest += "&coordsby=latlon&lon_min=";
            APIRequest += lonMin;
            APIRequest += "&lat_min=";
            APIRequest += latMin;
            APIRequest += "&lon_max=";
            APIRequest += lonMax;
            APIRequest += "&lat_max=";
            APIRequest += latMax;
            APIRequest += "&format=json&pack=&language=en&data_blocks=main%2Cgeometry%2Clocation%2C&page=";
            APIRequest += page;
            APIRequest += "&count=";
            APIRequest += pageCount;
            APIRequest += "&category=&categories_or=&categories_and=";
            return APIRequest;
        }

        public async Task<bool> CheckForErrorMessage(RootObject rootObject, HttpResponseMessage response)
        {
            if (rootObject.places == null)
            {
                String responseContent = await response.Content.ReadAsStringAsync();
                var debugObject = JsonConvert.DeserializeObject<DebugRootObject>(responseContent);
                Console.WriteLine("Error page: " + debugObject.debug.message);
                return true;
            }
            return false;
        }

        public async Task<bool> LoadFromWikimapia(double lonMin, double latMin, double lonMax, double latMax, uint maxNumPages = uint.MaxValue)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
 
            HttpResponseMessage response = await client.GetAsync(FormatRequest(lonMin, latMin, lonMax, latMax, 1));
            if (response.IsSuccessStatusCode)
            {
                RootObject rootObject = await response.Content.ReadAsAsync<RootObject>();
                if (await CheckForErrorMessage(rootObject, response))
                    return false;
                foreach (Place place in rootObject.places)
                    data.Add(place);
                Console.WriteLine("Number of places found: " + rootObject.found);
               
                // Up to 100 requests per 5 minutes...
                await Task.Delay(4000);

                // Load the rest of the pages
                double numPages = Math.Ceiling(Convert.ToDouble(rootObject.found) / rootObject.count);
                var numPagesToLoad = Math.Min(numPages, maxNumPages);

                // Can't load more than 10000 objects per area
                if (numPagesToLoad * rootObject.count >= 10000)
                {
                    // Recursively load data from smaller areas
                    bool result = true;
                    result &= await LoadFromWikimapia(lonMin, latMin, lonMin + (lonMax - lonMin) / 2, latMin + (latMax - latMin) / 2);
                    result &= await LoadFromWikimapia(lonMin + (lonMax - lonMin) / 2, latMin, lonMax, latMin + (latMax - latMin) / 2);
                    result &= await LoadFromWikimapia(lonMin + (lonMax - lonMin) / 2, latMin + (latMax - latMin) / 2, lonMax, latMax);
                    result &= await LoadFromWikimapia(lonMin, latMin + (latMax - latMin) / 2, lonMin + (lonMax - lonMin) / 2, latMax);
                    return result;
                }
                else
                {
                    for (uint page = 2; page <= numPagesToLoad; ++page)
                    {
                        response = await client.GetAsync(FormatRequest(lonMin, latMin, lonMax, latMax, page, rootObject.count));
                        rootObject = await response.Content.ReadAsAsync<RootObject>();
                        Console.WriteLine("Loading page: " + page + "/" + numPagesToLoad);
                        if (await CheckForErrorMessage(rootObject, response))
                            return false;
                        foreach (Place place in rootObject.places)
                            data.Add(place);
                        // Up to 100 requests per 5 minutes...
                        await Task.Delay(4000);
                    }
                }
            }

            return true;
        }

        public async Task<bool> LoadPageRange(double lonMin, double latMin, double lonMax, double latMax, uint startPage, uint endPage)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = await client.GetAsync(FormatRequest(lonMin, latMin, lonMax, latMax, startPage));
            if (response.IsSuccessStatusCode)
            {
                RootObject rootObject = await response.Content.ReadAsAsync<RootObject>();
                if (await CheckForErrorMessage(rootObject, response))
                    return false;
                foreach (Place place in rootObject.places)
                    data.Add(place);
                Console.WriteLine("Number of places found: " + rootObject.found);

                // Up to 100 requests per 5 minutes...
                await Task.Delay(4000);

                // Load the rest of the pages 
                for (uint page = startPage + 1; page <= endPage; ++page)
                {
                    response = await client.GetAsync(FormatRequest(lonMin, latMin, lonMax, latMax, page, rootObject.count));
                    rootObject = await response.Content.ReadAsAsync<RootObject>();
                    Console.WriteLine("Loading page: " + page + "/" + endPage);
                    if (await CheckForErrorMessage(rootObject, response))
                        return false;
                    foreach (Place place in rootObject.places)
                        data.Add(place);
                    // Up to 100 requests per 5 minutes...
                    await Task.Delay(4000);
                }
            }

            return true;
        }

        public async Task LoadFromWikimapiaBySubdivision(double lonMin, double latMin, double lonMax, double latMax, double subdivisionSize)
        {
            for (int i = 0; i < Math.Ceiling((latMax - latMin) / subdivisionSize); ++i)
            {
                for (int j = 0; j < Math.Ceiling((lonMax - lonMin) / subdivisionSize); ++j)
                {
                    double newLonMin = lonMin+j*subdivisionSize;
                    double newLatMin = latMin+i*subdivisionSize;
                    double newLonMax = Math.Min(newLonMin + subdivisionSize, lonMax);
                    double newLatMax = Math.Min(newLatMin + subdivisionSize, latMax);
                    Console.WriteLine("Loading data from area (latitude, longitude)=(" + newLatMin + ", " + newLonMin + ") ; (" + newLatMax + ", " + newLonMax + ")");
                    bool successfulLoad = await LoadFromWikimapia(newLonMin, newLatMin, newLonMin + subdivisionSize, newLatMin + subdivisionSize);
                    if (!successfulLoad) // Repeat the same query
                        --j;
                }
            }
        }

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

        static void Main(string[] args)
        {
            var NSDistance = Distance(48.676114, 2.045517, 49.041539, 2.045517);
            var EWDistance = Distance(48.676114, 2.045517, 48.676114, 2.736969);
            Program program = new Program();
            //program.LoadFromWikimapia(2.045517, 48.676114, 2.736969, 49.041539).Wait();

            program.LoadFromWikimapiaBySubdivision(2.045517, 48.676114, 2.736969, 49.041539, 0.05).Wait();
            Console.WriteLine("Retrieved " + program.data.Count + " entries");
            StreamWriter sw = new StreamWriter("data.json");
            JsonWriter writer = new JsonTextWriter(sw);
            JsonSerializer serializer = new JsonSerializer();
            serializer.Serialize(writer, program.data);
        }
    }
}
