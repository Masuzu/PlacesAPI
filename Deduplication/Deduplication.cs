using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deduplication
{
    class Deduplication
    {
        #region Static variables
        enum Model { Name, SpatialContext };

        const double BackgroundDistributonSmoothingFactor = 0.1;

        static PlacesDatabase database = new PlacesDatabase();
        static HashSet<String> vocabulary = new HashSet<String>();
        static Dictionary<String, Double> coreWordDistribution = new Dictionary<String, Double>();
        static Dictionary<String, Double> backgroundWordDistribution = new Dictionary<String, Double>();
        static Dictionary<Int32, Dictionary<String, Double>> geolocalizedBackgroundWordDistribution = new Dictionary<Int32, Dictionary<String, Double>>();

        // Key: word in vocabulary, Value: places in which it occurs
        static Dictionary<String, List<Place>> invertedIndex = new Dictionary<String, List<Place>>();

        // Key: place identified by its unique id, Value: list of words in the place title and the probability for each of them to be a core word
        static Dictionary<Place, Dictionary<String, Double>> isCoreWordProbability = new Dictionary<Place, Dictionary<String, Double>>(new PlaceComparer());

        static int numWords = 0;
        #endregion

        #region Expectation maximization
        // If multiple places named 'placeName' are found, select the one for which the core word distribution should be retrived with 'index'
        static Dictionary<String, Double> ComputeIsCoreProbabilityNameModel(string placeName, int index = 0)
        {
            List<Place> places = database.AddPlace(placeName);
            Place place = places[index];
            if (AddOrUpdatePlaceName(place))
                ExpectationMaximization(1, 1E-3, Model.Name);
            return isCoreWordProbability[place];
        }

        // @param keys list of keys of 'probabilities'
        static void NormalizeProbabilities(List<string> keys, Dictionary<String, Double> probabilities)
        {
            double probabilitySum = 0;
            foreach (String word in keys)
                probabilitySum += probabilities[word];
            foreach (String word in keys)
                probabilities[word] /= probabilitySum;
        }

        // Returns the maximum absolute difference of the change(s) induced
         // @param keys list of keys of 'probabilities'
        static double ComputeIsCoreProbabilityFromBDistribution(List<string> keys, Dictionary<String, Double> probabilities, Dictionary<String, Double> backgroundDistribution)
        {
            double maxAbsoluteDifference = 0;
            double denominator = 0;
            bool nullBackgroundProbability = false;
            foreach (String word in keys)
            {
                double backgroundProbability = backgroundDistribution[word];
                if (backgroundProbability == 0)
                {
                    nullBackgroundProbability = true;
                    break;
                }
                denominator += coreWordDistribution[word] / backgroundProbability;
            }
            if (nullBackgroundProbability)
            {
                denominator = 0;
                foreach (String word in keys)
                {
                    double product = coreWordDistribution[word];
                    foreach (String otherWord in keys)
                    {
                        if (otherWord == word)
                            continue;
                        product *= backgroundDistribution[otherWord];
                    }
                    denominator += product;
                }
            }
            // If denominator is still null, stop updating the probabilities for this place name
            if (denominator == 0)
                return maxAbsoluteDifference;
            foreach (String word in keys)
            {
                double numerator = coreWordDistribution[word];
                if (nullBackgroundProbability)
                {
                    double product = coreWordDistribution[word];
                    foreach (String otherWord in keys)
                    {
                        if (otherWord == word)
                            continue;
                        numerator *= backgroundDistribution[otherWord];
                    }
                }
                else
                    numerator /= backgroundDistribution[word];
                double newProbability = numerator / denominator;
                // Fine tuning to avoid getting values constantly decreasing without being null when they should
                if (newProbability < 1E-10)
                    newProbability = 0;
                maxAbsoluteDifference = Math.Max(Math.Abs(newProbability - probabilities[word]), maxAbsoluteDifference);
                probabilities[word] = newProbability;
            }
            return maxAbsoluteDifference;
        }

        static double ComputeIsCoreProbabilitySpatialContextModel(Dictionary<String, Double> probabilities, Place place)
        {
            double maxAbsoluteDifference = 0;
            List<string> keys = new List<string>(probabilities.Keys);
            if (keys.Count == 1)
            {
                String coreWord = keys[0];
                maxAbsoluteDifference = Math.Max(Math.Abs(1 - probabilities[coreWord]), maxAbsoluteDifference);
                probabilities[coreWord] = 1;
            }
            else
            {
                int tileId = database.GetTileId(place);
                var backgroundDistribution = geolocalizedBackgroundWordDistribution[tileId];
                maxAbsoluteDifference = Math.Max(
                    ComputeIsCoreProbabilityFromBDistribution(keys, probabilities, backgroundDistribution),
                    maxAbsoluteDifference);
                NormalizeProbabilities(keys, probabilities);
            }
            return maxAbsoluteDifference;
        }

        static double ComputeIsCoreProbabilityNameModel(Dictionary<String, Double> probabilities)
        {
            double maxAbsoluteDifference = 0;
            List<string> keys = new List<string>(probabilities.Keys);
            if (keys.Count == 1)
            {
                String coreWord = keys[0];
                maxAbsoluteDifference = Math.Max(Math.Abs(1 - probabilities[coreWord]), maxAbsoluteDifference);
                probabilities[coreWord] = 1;
            }
            else
            {
                maxAbsoluteDifference = Math.Max(
                   ComputeIsCoreProbabilityFromBDistribution(keys, probabilities, backgroundWordDistribution),
                   maxAbsoluteDifference);
                NormalizeProbabilities(keys, probabilities);
            }
            return maxAbsoluteDifference;
        }

        // Updates backgroundWordDistribution from the core and background word distributions
        // Returns the maximum absolute difference of the changes induced
        static double ExpectationStep(Model model = Model.Name)
        {
            double maxAbsoluteDifference = 0;
            if(model == Model.Name)
            {
                Parallel.ForEach(isCoreWordProbability, entry =>
                {
                    maxAbsoluteDifference = Math.Max(maxAbsoluteDifference, ComputeIsCoreProbabilityNameModel(entry.Value));
                });
            }
            else if(model == Model.SpatialContext)
            {
                Parallel.ForEach(isCoreWordProbability, entry =>
                {
                    maxAbsoluteDifference = Math.Max(maxAbsoluteDifference, ComputeIsCoreProbabilitySpatialContextModel(entry.Value, entry.Key));
                });               
            }
            return maxAbsoluteDifference;
        }

        static void UpdateCoreWordProbabilities()
        {
            int numPlaceNames = isCoreWordProbability.Count;
            List<String> keys = new List<String>(coreWordDistribution.Keys);
            Parallel.ForEach(keys, word =>
            {
                double numerator = 0;
                // Sum up the probabilities of 'is word in the core of placeName' for each 'placeName' containing 'word' 
                foreach (Place place in invertedIndex[word])
                {
                    var probabilities = isCoreWordProbability[place];
                    numerator += probabilities[word];
                }
                coreWordDistribution[word] = numerator / numPlaceNames;
            });
        }

        static void UpdateBackgroundDistributionNameModel()
        {
            List<String> keys = new List<String>(backgroundWordDistribution.Keys);
            int numPlaceNames = isCoreWordProbability.Count;
            double denominator = numWords - numPlaceNames;
            Parallel.ForEach(keys, word =>
            {
                double numerator = 0;
                // Sum up the probabilities of 'is word in the core of placeName' for each 'placeName' containing 'word' 
                foreach (Place place in invertedIndex[word])
                {
                    var probabilities = isCoreWordProbability[place];
                    numerator += (1 - probabilities[word]);
                }
                backgroundWordDistribution[word] = numerator / denominator;
            });
        }

        static void UpdateBDistributionSpatialContextModel()
        {
            Parallel.ForEach(geolocalizedBackgroundWordDistribution, tileDistribution =>
            {
                int tileId = tileDistribution.Key;
                var wordDistribution = tileDistribution.Value;
                List<String> words = new List<String>(wordDistribution.Keys);
                double denominator = 0;
                foreach (string word in words)
                {
                    foreach (Place place in invertedIndex[word])
                    {
                        if (database.GetTileId(place) == tileId)
                        {
                            var probabilities = isCoreWordProbability[place];
                            denominator += (1 - probabilities[word]);
                        }
                    }
                }

                foreach (string word in words)
                {
                    double numerator = 0;
                    // Iterate over the place names located in tileId and which contain 'word'
                    foreach (Place place in invertedIndex[word])
                    {
                        if (database.GetTileId(place) == tileId)
                        {
                            var probabilities = isCoreWordProbability[place];
                            numerator += (1 - probabilities[word]);
                        }
                    }
                    wordDistribution[word] = numerator / denominator;
                    // Smooth by interpolating the tile distributions with the global background
                    wordDistribution[word] = BackgroundDistributonSmoothingFactor * wordDistribution[word]
                       + (1 - BackgroundDistributonSmoothingFactor) * backgroundWordDistribution[word];
                }
            });
        }

        static void MaximimizationStep(Model model = Model.Name)
        {  
            if (model == Model.Name)
            {             
                UpdateCoreWordProbabilities();
                UpdateBackgroundDistributionNameModel();
            }
            else if (model == Model.SpatialContext)
            {
                UpdateCoreWordProbabilities();
                UpdateBackgroundDistributionNameModel();
                UpdateBDistributionSpatialContextModel();
            }
        }

        static void ExpectationMaximization(int maxNumSteps, double threshold, Model model)
        {
            for (int step = 0; step < maxNumSteps; ++step)
            {
                double variation = ExpectationStep(model);
                if (variation < threshold)
                    break;
                MaximimizationStep(model);
            }
        }
        #endregion

        #region Database initialization
        static string FilterWord(string s)
        {
            // Transforms s to lowercase and removes garbage characters
            s = s.ToLower();
            s = s.Trim(new Char[] { '(', '\"', ')', ',', '{', '}', '/' });
            return s;
        }

        // Returns true if placeName contains new words
        static bool AddOrUpdatePlaceName(Place place)
        {
            int tileId = PlacesDatabase.UndefinedTileId;
            if (place.location != null)
                tileId = database.GetTileId(place);
            if (!isCoreWordProbability.ContainsKey(place))
            {
                isCoreWordProbability.Add(place, new Dictionary<String, Double>());
                Dictionary<String, Double> probabilityList = isCoreWordProbability[place];
                string[] words = place.title.Split(' ');
                foreach (string word in words)
                {
                    string processedWord = FilterWord(word);
                    if (processedWord.Length != 0)
                    {
                        if (vocabulary.Add(processedWord))
                        {
                            invertedIndex.Add(processedWord, new List<Place>());
                            coreWordDistribution.Add(processedWord, 1);
                            backgroundWordDistribution.Add(processedWord, 1);
                        }
                        if (tileId != PlacesDatabase.UndefinedTileId)
                        {
                            if(!geolocalizedBackgroundWordDistribution.ContainsKey(tileId))
                                geolocalizedBackgroundWordDistribution.Add(tileId, new Dictionary<string,double>());
                            geolocalizedBackgroundWordDistribution[tileId][processedWord] = 0;
                        }
                        invertedIndex[processedWord].Add(place);
                        probabilityList[processedWord] = 0;
                        ++numWords;
                    }
                }
                return true;
            }
            return false;
        }
        #endregion

        static void Main(string[] args)
        {
            string[] filePaths = Directory.GetFiles("../../../Data/");
            foreach (string filePath in filePaths)
                database.Load(filePath);
            database.GenerateTiles(0.05);

            //database.AddPlace("Starbucks Coffee");
            //database.AddPlace("Peets Coffee");
            //database.AddPlace("Starbucks");

            // Build the vocabulary set
            foreach (Place place in database.Places)
                AddOrUpdatePlaceName(place);

            double uniformWordProbability = 1.0/vocabulary.Count;
            foreach (string word in vocabulary)
            {
                coreWordDistribution[word] = uniformWordProbability;
                backgroundWordDistribution[word] = uniformWordProbability;
            }
            Parallel.ForEach(geolocalizedBackgroundWordDistribution, entry =>
            {
                var probabilities = entry.Value;
                int numWordsInTile = probabilities.Count;
                double uniformProbability = 1.0 / numWordsInTile;
                List<String> keys = new List<String>(probabilities.Keys);
                foreach (String word in keys)
                    probabilities[word] = uniformProbability;
            });
    
            // Run the expectation maximization algorithm
            ExpectationMaximization(100, 1E-3, Model.SpatialContext);
            var test = ComputeIsCoreProbabilityNameModel("Golden Triangle");
        }
    }
}
