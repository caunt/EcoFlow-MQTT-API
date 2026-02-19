using AlphaOmega.Debug;
using Google.Protobuf.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace EcoFlow.Proto.Exporter;

public static class ProtosReader
{
    public static async Task<FileDescriptorSet> GetProtoSetAsync(string apkFileName, CancellationToken cancellationToken = default)
    {
        var set = new FileDescriptorSet();

        await foreach (var fileDescriptorProto in Enumerate(apkFileName, cancellationToken))
            set.File.Add(fileDescriptorProto);

        return set;
    }

    public static async IAsyncEnumerable<FileDescriptorProto> Enumerate(string apkFileName, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var outputChannel = Channel.CreateBounded<FileDescriptorProto>(new BoundedChannelOptions(100)
        {
            SingleWriter = false,
            SingleReader = true
        });

        var writingTask = Task.Run(async () =>
        {
            try
            {
                var dexFiles = ZipReader.EnumerateFilesRecursively(apkFileName, fileName => fileName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase));
                var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

                await Parallel.ForEachAsync(dexFiles, parallelOptions, async (value, token) =>
                {
                    var (filePath, fileStream) = value;
                    using var dexFile = new DexFile(new StreamLoader(fileStream));

                    foreach (var fileDescriptorProto in Enumerate(dexFile))
                        await outputChannel.Writer.WriteAsync(fileDescriptorProto, token);
                });

                outputChannel.Writer.Complete();
            }
            catch (Exception exception)
            {
                outputChannel.Writer.Complete(exception);
            }
        }, cancellationToken);

        await foreach (var fileDescriptorProto in outputChannel.Reader.ReadAllAsync(cancellationToken))
            yield return fileDescriptorProto;

        await writingTask;
    }

    public static IEnumerable<FileDescriptorProto> Enumerate(DexFile dex)
    {
        foreach (var typeIdRow in dex.TYPE_ID_ITEM)
        {
            var classDefRow = dex.CLASS_DEF_ITEM.FirstOrDefault(classDefRow => classDefRow.class_idx.TypeDescriptor == typeIdRow.TypeDescriptor);

            if (classDefRow == null)
                continue;

            if (classDefRow.class_data_off is not { } classDataRow)
                continue;

            if (!classDataRow.static_fields.Any(encodedFieldRow => dex.FIELD_ID_ITEM[encodedFieldRow.field_idx_diff] is { name_idx.data: "descriptor", type_idx.TypeDescriptor: "com.google.protobuf.Descriptors$FileDescriptor;" }))
                continue;

            foreach (var encodedMethodRow in classDataRow.direct_methods)
            {
                var methodIdRow = dex.METHOD_ID_ITEM[encodedMethodRow.method_idx_diff];

                if (methodIdRow.name_idx.data is not "<clinit>")
                    continue;

                var instructions = encodedMethodRow.code_off.insns;
                var registers = new string[byte.MaxValue];

                for (int index = 0; index < instructions.Length; index++)
                {
                    var instruction = instructions[index];
                    var opcode = (byte)instruction;

                    switch (opcode)
                    {
                        case 0x1A: // const-string
                            var register = (byte)(instruction >> 8);

                            var stringIndex = instructions[++index];
                            var stringValue = dex.STRING_DATA_ITEM[stringIndex];

                            registers[register] = stringValue.data;
                            break;
                        case 0x24: // filled-new-array
                            var format = (byte)(instruction >> 8);
                            var registerCount = (format & 0xF0) >> 4;
                            var fifthRegisterIndex = format & 0x0F;

                            var typeIndex = instructions[++index];
                            var argumentRegistersEncoded = instructions[++index];

                            var firstRegisterIndex = argumentRegistersEncoded & 0x0F;
                            var secondRegisterIndex = (argumentRegistersEncoded >> 4) & 0x0F;
                            var thirdRegisterIndex = (argumentRegistersEncoded >> 8) & 0x0F;
                            var fourthRegisterIndex = (argumentRegistersEncoded >> 12) & 0x0F;

                            var registerIndexes = new int[]
                            {
                                 firstRegisterIndex,
                                 secondRegisterIndex,
                                 thirdRegisterIndex,
                                 fourthRegisterIndex,
                                 fifthRegisterIndex
                            };

                            var stringBuilder = new StringBuilder();

                            for (int i = 0; i < registerCount; i++)
                            {
                                var currentRegisterIndex = registerIndexes[i];

                                if (currentRegisterIndex < registers.Length)
                                {
                                    var registerContent = registers[currentRegisterIndex];

                                    if (registerContent is null)
                                        continue;

                                    stringBuilder.Append(registerContent);
                                }
                            }

                            yield return ParseStringBuilder(stringBuilder, typeIdRow.TypeDescriptor);

                            index = instructions.Length;
                            break;

                        case 0x25: // filled-new-array/range
                            var rangeRegisterCount = (byte)(instruction >> 8);

                            var rangeTypeIndex = instructions[++index];
                            var startRegisterIndex = instructions[++index];

                            var rangeStringBuilder = new StringBuilder();

                            for (int i = 0; i < rangeRegisterCount; i++)
                            {
                                var currentRegisterIndex = startRegisterIndex + i;

                                if (currentRegisterIndex < registers.Length)
                                {
                                    var registerContent = registers[currentRegisterIndex];

                                    if (registerContent is null)
                                        continue;

                                    rangeStringBuilder.Append(registerContent);
                                }
                            }

                            yield return ParseStringBuilder(rangeStringBuilder, typeIdRow.TypeDescriptor);

                            index = instructions.Length;
                            break;
                    }
                }
            }
        }

        static FileDescriptorProto ParseStringBuilder(StringBuilder stringBuilder, string? typeDescriptor = null)
        {
            var latin1bytes = Encoding.Latin1.GetBytes(stringBuilder.ToString());
            var fileDescriptorProto = FileDescriptorProto.Parser.ParseFrom(latin1bytes);

            if (!fileDescriptorProto.HasPackage && typeDescriptor is not null)
            {
                if (typeDescriptor.EndsWith(';'))
                    typeDescriptor = typeDescriptor[..^1];

                var lastDotIndex = typeDescriptor.LastIndexOf('.');
                typeDescriptor = lastDotIndex >= 0 ? typeDescriptor[..lastDotIndex] : typeDescriptor;

                fileDescriptorProto.Package = typeDescriptor;
            }

            return fileDescriptorProto;
        }
    }
}
