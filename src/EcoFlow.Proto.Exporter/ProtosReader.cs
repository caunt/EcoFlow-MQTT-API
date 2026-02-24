using AlphaOmega.Debug;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace EcoFlow.Proto.Exporter;

public static class ProtosReader
{
    private static readonly string[] ReservedPrefixes = ["has", "get", "set", "clear", "add", "remove"];

    public record Item(FileDescriptorProto FileDescriptorProto, string TypeDescriptor)
    {
        public bool RemoveFirstSegment { get; set; } = true;
        public string ProtoName => GetProtoName(RemoveFirstSegment);

        public string GetProtoName(bool removeFirstSegment)
        {
            var typeDescriptor = TypeDescriptor;

            if (typeDescriptor.EndsWith(';'))
                typeDescriptor = typeDescriptor[..^1];

            var indexes = typeDescriptor.IndexesOf('.');

            if (removeFirstSegment && indexes.Count() > 2)
                typeDescriptor = typeDescriptor[(indexes.First() + 1)..];

            return typeDescriptor;
        }
    }

    public static async Task<FileDescriptorSet> GetProtoSetAsync(string apkFileName, CancellationToken cancellationToken = default)
    {
        return await GetProtoSetAsync(apkFileName, update => update, cancellationToken);
    }

    public static async Task<FileDescriptorSet> GetProtoSetAsync(string apkFileName, Func<Item, Item?> update, CancellationToken cancellationToken = default)
    {
        var protos = new List<Item>();

        await foreach (var item in EnumerateParallelAsync(apkFileName, cancellationToken).Select(update).WhereNotNull())
            protos.Add(item);

        Repair(protos);

        var set = new FileDescriptorSet();

        foreach (var fileDescriptorProto in Sort(protos.Select(item => item.FileDescriptorProto)))
            set.File.Add(fileDescriptorProto);

        return set;
    }

    public static async IAsyncEnumerable<Item> EnumerateParallelAsync(string apkFileName, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var outputChannel = Channel.CreateBounded<Item>(new BoundedChannelOptions(100)
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

                    foreach (var item in Enumerate(dexFile))
                        await outputChannel.Writer.WriteAsync(item, token);
                });

                outputChannel.Writer.Complete();
            }
            catch (Exception exception)
            {
                outputChannel.Writer.Complete(exception);
            }
        }, cancellationToken);

        await foreach (var item in outputChannel.Reader.ReadAllAsync(cancellationToken))
            yield return item;

        await writingTask;
    }

    public static IEnumerable<Item> Enumerate(DexFile dex)
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

                            yield return new Item(BuildProto(stringBuilder), typeIdRow.TypeDescriptor);

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

                            yield return new Item(BuildProto(rangeStringBuilder), typeIdRow.TypeDescriptor);

                            index = instructions.Length;
                            break;
                    }
                }
            }
        }
    }

    private static FileDescriptorProto BuildProto(StringBuilder stringBuilder)
    {
        var latin1bytes = Encoding.Latin1.GetBytes(stringBuilder.ToString());
        var fileDescriptorProto = FileDescriptorProto.Parser.ParseFrom(latin1bytes);

        return fileDescriptorProto;
    }

    private static void Repair(IEnumerable<Item> items)
    {
        Apply(items, RepairName);
        Apply(items, RepairPackage);
        Apply(items, RepairDependencies);
        Apply(items, RepairTypeNames);
        Apply(items, RepairFieldNames);

        return;

        void RepairName(Item item)
        {
            var fileDescriptorProto = item.FileDescriptorProto;

            // If name is nested, it is likely correct and we can skip it
            if (fileDescriptorProto.Name.Contains('/'))
                return;

            // Name
            var directory = item.ProtoName.Replace('.', '/');

            fileDescriptorProto.Name = $"{directory}/{fileDescriptorProto.Name}";
        }

        void RepairPackage(Item item)
        {
            var fileDescriptorProto = item.FileDescriptorProto;

            // If package is nested, it is likely correct and we can skip it
            if (fileDescriptorProto.Package.Contains('.'))
                return;

            var package = fileDescriptorProto.Name;

            // Remove file name
            var extensionIndex = package.LastIndexOf('.');

            if (extensionIndex < 0)
                throw new Exception($"Unexpected proto name format: '{package}'");

            package = package[..extensionIndex];

            // Remove last directory
            var lastDirectoryIndex = package.LastIndexOf('/');

            if (lastDirectoryIndex > 0)
                package = package[..lastDirectoryIndex];

            // Done!
            item.FileDescriptorProto.Package = package.Replace('/', '.');
        }

        void RepairDependencies(Item item)
        {
            var fileDescriptorProto = item.FileDescriptorProto;

            for (var i = 0; i < fileDescriptorProto.Dependency.Count; i++)
            {
                var dependency = fileDescriptorProto.Dependency[i];
                var isValid = items.Any(otherItem => otherItem.FileDescriptorProto.Name == dependency);

                if (isValid)
                    continue;

                var candidate = items
                    .Where(otherItem => otherItem.FileDescriptorProto.Name.EndsWith(dependency))
                    .Select(otherItem => new { Item = otherItem, CommonPrefixLength = CountCommonPrefixLength(fileDescriptorProto.Name, otherItem.FileDescriptorProto.Name) })
                    .MaxBy(result => result.CommonPrefixLength)
                    ?? throw new InvalidOperationException("No candidate items.");

                if (candidate.CommonPrefixLength is 0)
                    throw new InvalidOperationException($"No candidate has a common prefix with dependency: {dependency}");

                fileDescriptorProto.Dependency[i] = candidate.Item.FileDescriptorProto.Name;
            }

            return;

            static int CountCommonPrefixLength(string left, string right)
            {
                if (left.Length == 0 || right.Length == 0)
                    return 0;

                var limit = Math.Min(left.Length, right.Length);
                var commonPrefixLength = 0;

                for (var index = 0; index < limit; index++)
                {
                    if (left[index] != right[index])
                        break;

                    commonPrefixLength++;
                }

                return commonPrefixLength;
            }
        }

        void RepairTypeNames(Item item)
        {
            var fileDescriptorProto = item.FileDescriptorProto;

            RepairFields(fileDescriptorProto.MessageType);

            return;

            void RepairFields(RepeatedField<DescriptorProto> messageDescriptors)
            {
                foreach (var messageDescriptor in messageDescriptors)
                {
                    foreach (var field in messageDescriptor.Field)
                    {
                        if (!field.HasTypeName)
                            continue;

                        var cleanTypeName = field.TypeName;

                        if (cleanTypeName.StartsWith('.'))
                            cleanTypeName = cleanTypeName[1..];

                        var typeParts = cleanTypeName.Split('.');
                        var matchingType = default(string);

                        for (var currentIndex = 0; currentIndex < typeParts.Length; currentIndex++)
                        {
                            var expectedSuffix = $".{string.Join(".", typeParts[currentIndex..])}";

                            matchingType = GetAllAvailableTypes(fileDescriptorProto).FirstOrDefault(availableType => availableType.EndsWith(expectedSuffix));

                            if (matchingType is not null)
                                break;
                        }

                        if (matchingType is not null)
                            field.TypeName = matchingType;
                    }

                    RepairFields(messageDescriptor.NestedType);
                }
            }

            IEnumerable<string> GetAllAvailableTypes(FileDescriptorProto currentFileDescriptor)
            {
                var localPrefix = $".{currentFileDescriptor.Package}";

                foreach (var messageType in CollectAllTypes(currentFileDescriptor.MessageType, localPrefix))
                    yield return messageType;

                foreach (var enumType in CollectTopLevelEnums(currentFileDescriptor.EnumType, localPrefix))
                    yield return enumType;

                foreach (var dependencyName in currentFileDescriptor.Dependency)
                {
                    var dependencyItem = items.FirstOrDefault(targetItem => targetItem.FileDescriptorProto.Name == dependencyName);

                    if (dependencyItem is not null)
                    {
                        var dependencyPrefix = $".{dependencyItem.FileDescriptorProto.Package}";

                        foreach (var dependencyMessageType in CollectAllTypes(dependencyItem.FileDescriptorProto.MessageType, dependencyPrefix))
                            yield return dependencyMessageType;

                        foreach (var dependencyEnumType in CollectTopLevelEnums(dependencyItem.FileDescriptorProto.EnumType, dependencyPrefix))
                            yield return dependencyEnumType;
                    }
                }
            }

            IEnumerable<string> CollectAllTypes(IEnumerable<DescriptorProto> descriptors, string currentPrefix)
            {
                foreach (var descriptor in descriptors)
                {
                    var fullTypeName = $"{currentPrefix}.{descriptor.Name}";

                    yield return fullTypeName;

                    foreach (var nestedType in CollectAllTypes(descriptor.NestedType, fullTypeName))
                        yield return nestedType;

                    foreach (var enumType in descriptor.EnumType)
                        yield return $"{fullTypeName}.{enumType.Name}";
                }
            }

            IEnumerable<string> CollectTopLevelEnums(IEnumerable<EnumDescriptorProto> enumDescriptors, string currentPrefix)
            {
                foreach (var enumDescriptor in enumDescriptors)
                    yield return $"{currentPrefix}.{enumDescriptor.Name}";
            }
        }

        void RepairFieldNames(Item item)
        {
            foreach (var messageType in item.FileDescriptorProto.MessageType)
                RepairFieldNames(messageType);

            void RepairFieldNames(DescriptorProto descriptor)
            {
                foreach (var field in descriptor.Field)
                {
                    var hasCollision = descriptor.Field.Any(otherField => ReservedPrefixes.Any(prefix => string.Equals(otherField.Name, prefix + "_" + field.Name, StringComparison.OrdinalIgnoreCase)));

                    if (!hasCollision)
                        continue;

                    field.Name = "value_of_" + field.Name;
                }

                foreach (var nestedType in descriptor.NestedType)
                    RepairFieldNames(nestedType);
            }
        }

        static void Apply<T>(IEnumerable<T> items, Action<T> action) => items.ToList().ForEach(action);
    }

    private static IEnumerable<FileDescriptorProto> Sort(IEnumerable<FileDescriptorProto> filesList)
    {

        var filesByNameGrouped = new Dictionary<string, List<FileDescriptorProto>>(StringComparer.Ordinal);
        var originalIndexByNameGrouped = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (fileIndex, currentFile) in filesList.Index())
        {
            if (string.IsNullOrEmpty(currentFile.Name))
            {
                throw new InvalidOperationException("FileDescriptorProto.Name must be set for dependency sorting.");
            }

            if (!filesByNameGrouped.TryGetValue(currentFile.Name, out var groupList))
            {
                groupList = [];
                filesByNameGrouped.Add(currentFile.Name, groupList);
                originalIndexByNameGrouped.Add(currentFile.Name, fileIndex);
            }

            groupList.Add(currentFile);
        }

        var dependentsByDependencyName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inDegreeByNameGroup = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var nameGroup in filesByNameGrouped)
        {
            inDegreeByNameGroup.Add(nameGroup.Key, 0);
        }

        foreach (var nameGroup in filesByNameGrouped)
        {
            var currentGroupName = nameGroup.Key;
            var currentGroupFiles = nameGroup.Value;

            var uniqueDependencies = currentGroupFiles
                .SelectMany(file => file.Dependency)
                .Distinct(StringComparer.Ordinal);

            foreach (var dependencyName in uniqueDependencies)
            {
                if (string.Equals(dependencyName, currentGroupName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!filesByNameGrouped.ContainsKey(dependencyName))
                {
                    throw new InvalidOperationException($"Missing dependency '{dependencyName}' referenced by a file named '{currentGroupName}'.");
                }

                if (!dependentsByDependencyName.TryGetValue(dependencyName, out var dependents))
                {
                    dependents = [];
                    dependentsByDependencyName.Add(dependencyName, dependents);
                }

                dependents.Add(currentGroupName);
                inDegreeByNameGroup[currentGroupName]++;
            }
        }

        var readyQueue = new PriorityQueue<string, int>();

        foreach (var inDegreePair in inDegreeByNameGroup)
        {
            if (inDegreePair.Value == 0)
            {
                readyQueue.Enqueue(inDegreePair.Key, originalIndexByNameGrouped[inDegreePair.Key]);
            }
        }

        var processedGroupCount = 0;

        while (readyQueue.Count > 0)
        {
            var currentGroupName = readyQueue.Dequeue();
            processedGroupCount++;

            foreach (var fileToYield in filesByNameGrouped[currentGroupName])
            {
                Sort(fileToYield.MessageType);
                yield return fileToYield;
            }

            if (!dependentsByDependencyName.TryGetValue(currentGroupName, out var dependents))
            {
                continue;
            }

            foreach (var dependentName in dependents)
            {
                inDegreeByNameGroup[dependentName]--;

                if (inDegreeByNameGroup[dependentName] == 0)
                {
                    readyQueue.Enqueue(dependentName, originalIndexByNameGrouped[dependentName]);
                }
            }
        }

        if (processedGroupCount != filesByNameGrouped.Count)
        {
            var remainingNames = inDegreeByNameGroup
                .Where(pair => pair.Value > 0)
                .Select(pair => pair.Key)
                .OrderBy(name => originalIndexByNameGrouped[name])
                .ToList();

            throw new InvalidOperationException($"Dependency cycle detected among: {string.Join(", ", remainingNames)}");
        }

        yield break;

        static void Sort(IList<DescriptorProto> targetMessages)
        {
            var messagesByName = new Dictionary<string, DescriptorProto>(StringComparer.Ordinal);
            var originalIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var messageIndex = 0; messageIndex < targetMessages.Count; messageIndex++)
            {
                var currentMessage = targetMessages[messageIndex];

                if (string.IsNullOrEmpty(currentMessage.Name))
                {
                    throw new InvalidOperationException("DescriptorProto.Name must be set for dependency sorting.");
                }

                if (!messagesByName.ContainsKey(currentMessage.Name))
                {
                    messagesByName.Add(currentMessage.Name, currentMessage);
                    originalIndexByName.Add(currentMessage.Name, messageIndex);
                }
            }

            var dependentsByDependencyName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var inDegreeByName = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var namePair in messagesByName)
            {
                inDegreeByName.Add(namePair.Key, 0);
            }

            foreach (var namePair in messagesByName)
            {
                var currentMessageName = namePair.Key;
                var currentMessage = namePair.Value;

                var uniqueDependencies = new HashSet<string>(StringComparer.Ordinal);

                foreach (var currentField in currentMessage.Field)
                {
                    if (currentField.TypeName is not null)
                    {
                        foreach (var knownMessageName in messagesByName.Keys)
                        {
                            if (string.Equals(currentField.TypeName, knownMessageName, StringComparison.Ordinal) ||
                                currentField.TypeName.EndsWith("." + knownMessageName, StringComparison.Ordinal))
                            {
                                uniqueDependencies.Add(knownMessageName);
                            }
                        }
                    }
                }

                foreach (var dependencyName in uniqueDependencies)
                {
                    if (string.Equals(dependencyName, currentMessageName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!dependentsByDependencyName.TryGetValue(dependencyName, out var dependents))
                    {
                        dependents = new List<string>();
                        dependentsByDependencyName.Add(dependencyName, dependents);
                    }

                    dependents.Add(currentMessageName);
                    inDegreeByName[currentMessageName]++;
                }
            }

            var readyQueue = new PriorityQueue<string, int>();

            foreach (var inDegreePair in inDegreeByName)
            {
                if (inDegreePair.Value == 0)
                {
                    readyQueue.Enqueue(inDegreePair.Key, originalIndexByName[inDegreePair.Key]);
                }
            }

            var sortedMessages = new List<DescriptorProto>(targetMessages.Count);

            while (sortedMessages.Count < messagesByName.Count)
            {
                if (readyQueue.Count == 0)
                {
                    var cycleBreakerName = inDegreeByName
                        .Where(pair => pair.Value > 0)
                        .OrderBy(pair => originalIndexByName[pair.Key])
                        .Select(pair => pair.Key)
                        .FirstOrDefault();

                    if (cycleBreakerName is not null)
                    {
                        inDegreeByName[cycleBreakerName] = 0;
                        readyQueue.Enqueue(cycleBreakerName, originalIndexByName[cycleBreakerName]);
                    }
                    else
                    {
                        break;
                    }
                }

                var currentMessageName = readyQueue.Dequeue();

                sortedMessages.Add(messagesByName[currentMessageName]);
                inDegreeByName[currentMessageName] = 0;

                if (!dependentsByDependencyName.TryGetValue(currentMessageName, out var dependents))
                {
                    continue;
                }

                foreach (var dependentName in dependents)
                {
                    if (inDegreeByName[dependentName] > 0)
                    {
                        inDegreeByName[dependentName]--;

                        if (inDegreeByName[dependentName] == 0)
                        {
                            readyQueue.Enqueue(dependentName, originalIndexByName[dependentName]);
                        }
                    }
                }
            }

            for (var writeIndex = 0; writeIndex < sortedMessages.Count; writeIndex++)
            {
                targetMessages[writeIndex] = sortedMessages[writeIndex];
            }
        }
    }
}
