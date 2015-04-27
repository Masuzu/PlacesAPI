# PlacesAPI
Places database deduplication
This is a Visual Studio 2013 C# project. Open PlacesAPI.sln and launch the compilation to generate the executables.
You will find the data retrieval code in DataRetrieval and the actual computation of the name model distributions in Deduplication.
The class PlacesDatabase is used to load places data from JSON files retrieved by DataRetrieval from Wikimapia.
The class Deduplication does the background and core distribution calculations depending on the model chosen (IDF, name model, spatial context). The results are read from the public attribute IsCoreWordProbability.