using Elements.Core;
using System;
using System.IO;
using System.IO.Compression;

namespace Unzipper;

public class Extractor
{
    public static void Unpack(string input, string outputDir)
    {
        var extension = Path.GetExtension(input).ToLower();

        try
        {
            if (extension == Unzipper.ZIP_FILE_EXTENSION)
            {
                ZipFile.ExtractToDirectory(input, outputDir);
            }
        }
        catch (Exception e)
        {
            UniLog.Error(e.Message);
        }
    }
}
