using System.IO.Compression;

namespace EcoFlow.Mqtt.Api.Protobuf.Extraction;

public static class ZipReader
{
    public static IEnumerable<(string FilePath, Stream FileStream)> EnumerateFilesRecursively(string zipFilePath, Predicate<string>? filterPredicate = null)
    {
        using var rootFileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);

        foreach (var result in EnumerateZipStream(rootFileStream, Path.GetFileName(zipFilePath), filterPredicate))
            yield return result;
    }

    private static IEnumerable<(string FilePath, Stream FileStream)> EnumerateZipStream(Stream inputStream, string parentPath, Predicate<string>? filterPredicate)
    {
        using var zipArchive = new ZipArchive(inputStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var zipArchiveEntry in zipArchive.Entries)
        {
            if (string.IsNullOrEmpty(zipArchiveEntry.Name))
                continue;

            var fullEntryPath = Path.Combine(parentPath, zipArchiveEntry.FullName);

            var uncompressedStream = new MemoryStream();

            using (var entryStream = zipArchiveEntry.Open())
                entryStream.CopyTo(uncompressedStream);

            uncompressedStream.Position = 0;

            if (IsZipHeader(uncompressedStream))
            {
                foreach (var nestedResult in EnumerateZipStream(uncompressedStream, fullEntryPath, filterPredicate))
                    yield return nestedResult;
            }
            else
            {
                if (filterPredicate is null || filterPredicate(fullEntryPath))
                {
                    uncompressedStream.Position = 0;
                    yield return (fullEntryPath, uncompressedStream);
                }
            }
        }
    }

    private static bool IsZipHeader(Stream stream)
    {
        if (stream.Length < 4)
            return false;

        var buffer = (stackalloc byte[4]);
        stream.ReadExactly(buffer);
        stream.Position = 0;

        return buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04;
    }
}
