using EcoFlow.Mqtt.Api.Protobuf.Extraction;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using System.CommandLine;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var inputFilePathOption = new Option<FileInfo>("--input", "-i")
{
    Description = "The path to the input .xapk file",
    Required = true
};

var outputFilePathOption = new Option<FileInfo>("--output", "-o")
{
    Description = "The path to the output File Descriptor Set .pb file",
    DefaultValueFactory = result => new FileInfo("file_descriptor_set.pb")
};

var rootCommand = new RootCommand("Extracts proto descriptor set from XAPK file")
{
    inputFilePathOption,
    outputFilePathOption
};

rootCommand.SetAction(async (result, cancellationToken) =>
{
    var inputFileInfo = result.GetRequiredValue(inputFilePathOption);
    var outputFileInfo = result.GetRequiredValue(outputFilePathOption);

    var fileDescriptorSet = new FileDescriptorSet();

    await foreach (var fileDescriptorProto in ProtosReader.Enumerate(inputFileInfo.FullName, cancellationToken))
    {
        Console.WriteLine($"Extracted proto: {fileDescriptorProto.Name}");
        fileDescriptorSet.File.Add(fileDescriptorProto);
    }

    await File.WriteAllBytesAsync(outputFileInfo.FullName, fileDescriptorSet.ToByteArray(), cancellationToken);
});

return await rootCommand.Parse(args).InvokeAsync();