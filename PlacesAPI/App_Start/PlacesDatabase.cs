using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;

namespace PlacesAPI
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

    public static class PlacesDatabase
    {
        static List<Place> data = new List<Place>();

        private static String FormatRequest(double lonMin, double latMin, double lonMax, double latMax, uint page = 1, int pageCount = 100)
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

        public static async Task<bool> CheckForErrorMessage(RootObject rootObject, HttpResponseMessage response)
        {
            if (rootObject.places == null)
            {
                String responseContent = await response.Content.ReadAsStringAsync();
                var debugObject = JsonConvert.DeserializeObject<DebugRootObject>(responseContent);
                Debug.WriteLine("Error page: " + debugObject.debug.message);
                return true;
            }
            return false;
        }

        public static async Task LoadFromWikimapia(double lonMin, double latMin, double lonMax, double latMax, uint maxNumPages = uint.MaxValue)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = await client.GetAsync(FormatRequest(lonMin, latMin, lonMax, latMax, 1));
            if (response.IsSuccessStatusCode)
            {
                RootObject rootObject = await response.Content.ReadAsAsync<RootObject>();
                if (await CheckForErrorMessage(rootObject, response))
                    return;
                foreach (Place place in rootObject.places)
                    data.Add(place);
                Debug.WriteLine("Number of places found: " + rootObject.found);

                // Up to 100 requests per 5 minutes...
                await Task.Delay(4000);

                // Load the rest of the pages
                double numPages = Math.Ceiling(Convert.ToDouble(rootObject.found) / rootObject.count);
                var numPagesToLoad = Math.Min(numPages, maxNumPages);
                for (uint page = 2; page <= numPagesToLoad; ++page)
                {
                    response = await client.GetAsync(FormatRequest(lonMin, latMin, lonMax, latMax, page, rootObject.count));
                    rootObject = await response.Content.ReadAsAsync<RootObject>();
                    Debug.WriteLine("Loading page: " + page + "/" + numPagesToLoad);
                    if (await CheckForErrorMessage(rootObject, response))
                        return;
                    foreach (Place place in rootObject.places)
                        data.Add(place);
                    // Up to 100 requests per 5 minutes...
                    await Task.Delay(4000);
                }
            }

        }

        public static async Task LoadPageRange(double lonMin, double latMin, double lonMax, double latMax, uint startPage, uint endPage)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = await client.GetAsync(FormatRequest(lonMin, latMin, lonMax, latMax, startPage));
            if (response.IsSuccessStatusCode)
            {
                RootObject rootObject = await response.Content.ReadAsAsync<RootObject>();
                if (await CheckForErrorMessage(rootObject, response))
                    return;
                foreach (Place place in rootObject.places)
                    data.Add(place);
                Debug.WriteLine("Number of places found: " + rootObject.found);

                // Up to 100 requests per 5 minutes...
                await Task.Delay(4000);

                // Load the rest of the pages 
                for (uint page = startPage + 1; page <= endPage; ++page)
                {
                    response = await client.GetAsync(FormatRequest(lonMin, latMin, lonMax, latMax, page, rootObject.count));
                    rootObject = await response.Content.ReadAsAsync<RootObject>();
                    Debug.WriteLine("Loading page: " + page + "/" + endPage);
                    if (await CheckForErrorMessage(rootObject, response))
                        return;
                    foreach (Place place in rootObject.places)
                        data.Add(place);
                    // Up to 100 requests per 5 minutes...
                    await Task.Delay(4000);
                }
            }

        }

        public static void Load(String JSONFile)
        {
            using (StreamReader r = new StreamReader(JSONFile))
            {
                string serializedJSON = r.ReadToEnd();
                data = JsonConvert.DeserializeObject<List<Place>>(serializedJSON);
            }
        }
    }
}