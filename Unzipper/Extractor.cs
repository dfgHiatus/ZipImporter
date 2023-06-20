using BaseX;
using System;
using System.IO;
using System.IO.Compression;
// using TarExtractor;

namespace Unzipper;

public class Extractor
{
    public static void Unpack(string input, string outputDir)
    {
        var extension = Path.GetExtension(input).ToLower();

        try
        {
            if (Unzipper.SupportedZippedFiles.Contains(extension))
            {
                ZipFile.ExtractToDirectory(input, outputDir);
            }
            //else if (Unzipper.SupportedTarFiles.Contains(extension)) 
            //{ 
            //    Tar.ExtractTar(input, outputDir);
            //}
            //else if (Unzipper.SupportedTarGZFiles.Contains(extension))
            //{
            //    Tar.ExtractTarGz(input, outputDir);
            //}
        }
        catch (Exception e)
        {
            UniLog.Error(e.Message);
        }
    }
}
