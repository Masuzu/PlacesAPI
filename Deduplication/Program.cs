using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deduplication
{
    class Program
    {
        static void Main(string[] args)
        {
            PlacesDatabase database = new PlacesDatabase();
            database.Load("../../../Data/data0.json");
        }
    }
}
