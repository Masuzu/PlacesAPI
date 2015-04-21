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
        #region Attributes
        public enum Model { Name, SpatialContext, IDF };

        const double BackgroundDistributonSmoothingFactor = 0.5;
        const double CoreDistributonImportance = 0.5;

        public PlacesDatabase Database {get; private set;} 
        HashSet<String> vocabulary = new HashSet<String>();
        Dictionary<String, Double> coreWordDistribution = new Dictionary<String, Double>();
        Dictionary<String, Double> backgroundWordDistribution = new Dictionary<String, Double>();
        Dictionary<Int32, Dictionary<String, Double>> geolocalizedBackgroundWordDistribution = new Dictionary<Int32, Dictionary<String, Double>>();

        // Key: word in vocabulary, Value: places in which it occurs
        Dictionary<String, List<Place>> invertedIndex = new Dictionary<String, List<Place>>();

        // Key: place identified by its unique id, Value: list of words in the place title and the probability for each of them to be a core word
        Dictionary<Place, Dictionary<String, Double>> isCoreWordProbability = new Dictionary<Place, Dictionary<String, Double>>(new PlaceComparer());

        int numWords = 0;
        #endregion

        public Deduplication()
        {
            Database = new PlacesDatabase();
        }

        public void Setup()
        {
            // Build the vocabulary set
            foreach (Place place in Database.Places)
                AddOrUpdatePlaceName(place);

            double uniformWordProbability = 1.0 / vocabulary.Count;
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
        }

        #region Expectation maximization
        // If multiple places named 'placeName' are found, select the one for which the core word distribution should be retrived with 'index'
        public Dictionary<String, Double> ComputeIsCoreProbabilityNameModel(string placeName, int index = 0)
        {
            List<Place> places = Database.AddPlace(placeName);
            Place place = places[index];
            if (AddOrUpdatePlaceName(place))
                ExpectationMaximization(1, 1E-3, Model.Name);
            return isCoreWordProbability[place];
        }

        void ComputePosteriorIsCoreProbabilityNameModel()
        {
            Parallel.ForEach(isCoreWordProbability, entry =>
            {
                var probabilities = entry.Value;
                List<String> keys = new List<String>(probabilities.Keys);
                foreach (string word in keys)
                {
                    probabilities[word] = coreWordDistribution[word] * CoreDistributonImportance /
                        (coreWordDistribution[word] * CoreDistributonImportance + backgroundWordDistribution[word] * (1 - CoreDistributonImportance));
                }
                NormalizeProbabilities(keys, probabilities);
            });
        }

        void ComputePosteriorIsCoreProbabilitySpatialContextModel()
        {
            Parallel.ForEach(isCoreWordProbability, entry =>
            {
                var probabilities = entry.Value;
                List<String> keys = new List<String>(probabilities.Keys);
                foreach(string word in keys)
                {
                    int tileId = Database.GetTileId(entry.Key);
                    probabilities[word] = coreWordDistribution[word] * CoreDistributonImportance /
                        (coreWordDistribution[word] * CoreDistributonImportance + geolocalizedBackgroundWordDistribution[tileId][word] * (1 - CoreDistributonImportance));
                }
                NormalizeProbabilities(keys, probabilities);
            });
        }

        // @param keys list of keys of 'probabilities'
        void NormalizeProbabilities(List<string> keys, Dictionary<String, Double> probabilities)
        {
            double probabilitySum = 0;
            foreach (String word in keys)
                probabilitySum += probabilities[word];
            foreach (String word in keys)
                probabilities[word] /= probabilitySum;
        }

        // Returns the maximum absolute difference of the change(s) induced
        // @param keys list of keys of 'probabilities'
        double ComputeIsCoreProbabilityFromBDistribution(List<string> keys, Dictionary<String, Double> probabilities, Dictionary<String, Double> backgroundDistribution)
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

        double ComputeIsCoreProbabilitySpatialContextModel(Dictionary<String, Double> probabilities, Place place)
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
                int tileId = Database.GetTileId(place);
                var backgroundDistribution = geolocalizedBackgroundWordDistribution[tileId];
                maxAbsoluteDifference = Math.Max(
                    ComputeIsCoreProbabilityFromBDistribution(keys, probabilities, backgroundDistribution),
                    maxAbsoluteDifference);
                NormalizeProbabilities(keys, probabilities);
            }
            return maxAbsoluteDifference;
        }

        double ComputeIsCoreProbabilityNameModel(Dictionary<String, Double> probabilities)
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
        double ExpectationStep(Model model = Model.Name)
        {
            double maxAbsoluteDifference = 0;
            Object maxDiffLock = new Object();
            if(model == Model.Name)
            {
                Parallel.ForEach(isCoreWordProbability, entry =>
                {
                    double absoluteDiff = ComputeIsCoreProbabilityNameModel(entry.Value);
                    lock (maxDiffLock)
                    {
                        maxAbsoluteDifference = Math.Max(maxAbsoluteDifference, absoluteDiff);
                    }
                });
            }
            else if(model == Model.SpatialContext)
            {
                Parallel.ForEach(isCoreWordProbability, entry =>
                {
                    double absoluteDiff = ComputeIsCoreProbabilitySpatialContextModel(entry.Value, entry.Key);
                    lock (maxDiffLock)
                    {
                        maxAbsoluteDifference = Math.Max(maxAbsoluteDifference, absoluteDiff);
                    }
                });               
            }
            return maxAbsoluteDifference;
        }

        void UpdateCoreWordProbabilities()
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

        void UpdateBackgroundDistributionNameModel()
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

        void UpdateBDistributionSpatialContextModel()
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
                        if (Database.GetTileId(place) == tileId)
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
                        if (Database.GetTileId(place) == tileId)
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

        void MaximimizationStep(Model model = Model.Name)
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

        double ComputeIDF(string word)
        {
            return Math.Log(Convert.ToDouble(isCoreWordProbability.Count)/invertedIndex[word].Count);
        }

        public void ComputePosteriorIsCoreProbabilityByIDF()
        {
            Parallel.ForEach(isCoreWordProbability, entry =>
            {
                var probabilities = entry.Value;
                List<String> keys = new List<String>(probabilities.Keys);
                foreach(string word in keys)
                    probabilities[word] = ComputeIDF(word);
                NormalizeProbabilities(keys, probabilities);
            });
        }

        public void ExpectationMaximization(int maxNumSteps, double threshold, Model model)
        {
            if (model == Model.IDF)
                ComputePosteriorIsCoreProbabilityByIDF();
            else
            {
                for (int step = 0; step < maxNumSteps; ++step)
                {
                    double variation = ExpectationStep(model);
                    if (variation < threshold)
                        break;
                    MaximimizationStep(model);
                }
                if (model == Model.Name)
                    ComputePosteriorIsCoreProbabilityNameModel();
                else if (model == Model.SpatialContext)
                    ComputePosteriorIsCoreProbabilitySpatialContextModel();
            }
        }
        #endregion

        #region Database initialization
        string FilterWord(string s)
        {
            // Transforms s to lowercase and removes garbage characters
            s = s.ToLower();
            s = s.Trim(new Char[] { '(', '\"', ')', ',', '{', '}', '/' });
            return s;
        }

        // Returns true if the place does not exist in the database
        bool AddOrUpdatePlaceName(Place place)
        {
            int tileId = PlacesDatabase.UndefinedTileId;
            if (place.location != null)
                tileId = Database.GetTileId(place);
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
    }
}