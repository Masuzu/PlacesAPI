
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

        static void Main(string[] args)
        {
            //var NSDistance = Distance(48.676114, 2.045517, 49.041539, 2.045517);
            //var EWDistance = Distance(48.676114, 2.045517, 48.676114, 2.736969);
            //var NSDistance = Geolocalization.Distance(Geolocalization.ParisLatitude - 0.04, Geolocalization.ParisLongitude - 0.055, Geolocalization.ParisLatitude + 0.04, Geolocalization.ParisLongitude - 0.055);
            //var EWDistance = Geolocalization.Distance(Geolocalization.ParisLatitude - 0.04, Geolocalization.ParisLongitude - 0.055, Geolocalization.ParisLatitude - 0.04, Geolocalization.ParisLongitude + 0.055);
            Program program = new Program();
            //program.LoadFromWikimapia(2.045517, 48.676114, 2.736969, 49.041539).Wait();

            //program.LoadFromWikimapiaBySubdivision(2.045517, 48.676114, 2.736969, 49.041539, 0.05).Wait();
            //program.LoadFromWikimapia(Geolocalization.ParisLongitude - 0.055, Geolocalization.ParisLatitude - 0.04, Geolocalization.ParisLongitude + 0.055, Geolocalization.ParisLatitude + 0.04).Wait();
            // Lyon
            //program.LoadFromWikimapia(4.792442, 45.679695, 4.968910, 45.787053).Wait();
            // London
            program.LoadFromWikimapia(-0.167198, 51.465465, -0.062828, 51.531721).Wait();
            Console.WriteLine("Retrieved " + program.data.Count + " entries");
           
            List<Place> dataList = program.data.ToList();
            JsonSerializer serializer = new JsonSerializer();
            for (int page = 0; page < Math.Ceiling(Convert.ToDouble(dataList.Count) / 100); ++page)
            {
                var dataSubset = dataList.GetRange(page * 100, Math.Min(100, dataList.Count - page * 100));
                StreamWriter sw = new StreamWriter("London" + page + ".json");
                JsonWriter writer = new JsonTextWriter(sw);
                serializer.Serialize(writer, dataSubset);
                sw.Flush();
            }
           
        }
    }
}
