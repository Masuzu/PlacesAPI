using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deduplication
{
    class CoreWordProbability
    {
        public String Word { get; set; }
        public double Probability { get; set; }

        public CoreWordProbability(String word)
        {
            Word = word;
            Probability = 0.0;
        }
    }

    class Program
    {
        static HashSet<String> vocabulary = new HashSet<String>();
        static Dictionary<String, Double> coreWordDistribution = new Dictionary<String, Double>();
        static Dictionary<String, Double> backgroundWordDistribution = new Dictionary<String, Double>();

        // Key: place title, Value: list of words in the place title and the probability for each of them to be a core word
        static Dictionary<String, List<CoreWordProbability>> isCoreWordProbability = new Dictionary<String, List<CoreWordProbability>>();

        static string FilterWord(string s)
        {
            // Transforms s to lowercase and removes garbage characters
            s = s.ToLower();
            s = s.Trim(new Char[] { '(', '\"', ')', ',', '{', '}' });
            return s;
        }

        static void Main(string[] args)
        {
            PlacesDatabase database = new PlacesDatabase();
            string[] filePaths = Directory.GetFiles("../../../Data/");
            foreach(string filePath in filePaths)
                database.Load(filePath);
            foreach (Place place in database.Places)
            {
                string placeName = place.title;
                if (!isCoreWordProbability.ContainsKey(placeName))
                    isCoreWordProbability.Add(placeName, new List<CoreWordProbability>());
                List<CoreWordProbability> probabilityList = isCoreWordProbability[placeName];
                string[] words = place.title.Split(' ');
                foreach (string word in words)
                {
                    string processedWord = FilterWord(word);
                    if (processedWord.Length != 0)
                    {
                        vocabulary.Add(processedWord);
                        probabilityList.Add(new CoreWordProbability(processedWord));
                    }
                }
            }
            double uniformWordProbability = 1.0/vocabulary.Count;
            foreach (string word in vocabulary)
            {
                coreWordDistribution.Add(word, uniformWordProbability);
                backgroundWordDistribution.Add(word, uniformWordProbability);
            }
        }
    }
}
