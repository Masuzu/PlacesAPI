using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deduplication
{
    class Program
    {
        static void Main(string[] args)
        {
            Deduplication deduplication = new Deduplication();
            var database = deduplication.Database;
            string[] filePaths = Directory.GetFiles("../../../Data/");
            foreach (string filePath in filePaths)
            {

                if(filePath.Contains("Paris"))
                 database.Load(filePath);
            }
            //database.GenerateTiles(0.05);
            database.GenerateTilesByCity();

            //database.AddPlace("Starbucks Coffee");
            //database.AddPlace("Peets Coffee");
            //database.AddPlace("Starbucks");

            deduplication.Setup();

            // Run the expectation maximization algorithm
            deduplication.ExpectationMaximization(100, 1E-3, Deduplication.Model.Name);
            //var cp = deduplication.IsCoreWordProbability[database.GetByName("Metro-Station Mouton Duvernet (Linie 4)")[0]];
            Test test = new Test();
            test.LoadGroundTruthFromFile("GroundTruth.txt");
            System.IO.StreamWriter file = new System.IO.StreamWriter("precision SpatialContext.csv");
            for (int i = 1; i < 15; ++i)
            {
                double precision = test.GetPrecision(deduplication.IsCoreWordProbability, i * 10);
                file.WriteLine(i * 10 + "," + precision);
            }
            file.Close();
        }
    }
}
