using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deduplication
{
    class Test
    {
        public Dictionary<Place, List<String>> CoreWords { get; private set; }

        // Write to an output file with the following format: place id|place name|list of words in the place name|
        static public void WriteToFile(Deduplication deduplication, string outputFileName = "GroundTruth.txt", int maxOutputSize = 100)
        {
            int count = 0;
            System.IO.StreamWriter file = new System.IO.StreamWriter(outputFileName);
            foreach (var entry in deduplication.IsCoreWordProbability)
            {
                Place place = entry.Key;
                var probabilities = entry.Value;
                string line = Convert.ToString(place.id);
                line += "|";
                line += place.title;
                line += "|";
                int wordCount = 0;
                foreach (var word in probabilities.Keys)
                {
                    line += word;
                    ++wordCount;
                    if (wordCount < probabilities.Count)
                        line += " ";
                }
                line += "|";
                file.WriteLine(line);
                ++count;
                if (count >= maxOutputSize)
                    break;
            }
            file.Close();
        }

        // Load an output file with the following format: place id|place name|list of words in the place name|core word(s) (space separated)
        public void LoadGroundTruthFromFile(string inputFile)
        {
            string[] lines = System.IO.File.ReadAllLines(inputFile);
            foreach (string line in lines)
            {
                var tokens = line.Split('|');
                var placeId = Convert.ToInt32(tokens[0]);
                var placeName = tokens[1];
                var coreWords = tokens[3].Split(' ');
                CoreWords.Add(new Place { id = placeId, title = placeName }, coreWords.ToList());
            }
        }

        public double GetPrecision(Dictionary<Place, Dictionary<String, Double>> isCoreWordProbability, int maxTestCount = Int32.MaxValue)
        {
            double numMatches = 0;
            int testCount = 0;
            foreach(var entry in CoreWords)
            {   
                var referenceCoreWords = entry.Value;
                List<KeyValuePair<String, Double>> sortedProbabilities = isCoreWordProbability[entry.Key].ToList();

                sortedProbabilities.Sort(
                    delegate(KeyValuePair<String, Double> firstPair,
                    KeyValuePair<String, Double> nextPair)
                    {
                        return -firstPair.Value.CompareTo(nextPair.Value);
                    }
                );
                int referenceNumCoreWords = referenceCoreWords.Count;
                // Determine if the top referenceNumCoreWords potential core words are actual core words
                for (int i = 0; i < referenceNumCoreWords; ++i)
                {
                    if (referenceCoreWords.Contains(sortedProbabilities[i].Key) && sortedProbabilities[i].Value >= Math.Max(1.0 / Convert.ToDouble(2 + i) - 1E-3, 0))
                    {
                        numMatches += 1.0;
                        break;
                    }
                    else
                    {
                        bool mismatch = true;
                    }
                }
                ++testCount;
                if (testCount >= maxTestCount)
                    break;
            }
            return numMatches / testCount;
        }

        public Test()
        {
            CoreWords = new Dictionary<Place, List<string>>();
        }
    }
}
