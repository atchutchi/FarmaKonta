using System.Security.Cryptography;
using System.Text;

namespace Nofarma.Licensing.Qa;

public static class QaPublishedOutputScanner
{
    private const int BufferBytes = 64 * 1024;
    private const int PrivateHeaderWindowCharacters = 96;
    private const int PuttyPrivateSectionWindowCharacters = 16 * 1024;
    private const int MaximumPublicKeyFileBytes = 8 * 1024;
    private const long MaximumTextBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> ForbiddenExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".key",
        ".nofarma-license",
        ".nofarma-request",
        ".p8",
        ".p12",
        ".pfx",
        ".pem",
        ".ppk",
        ".snk"
    };
    private static readonly HashSet<string> TextExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".b64",
        ".config",
        ".json",
        ".md",
        ".txt",
        ".xml"
    };
    private static readonly string[] ForbiddenAllEncodingPatterns =
    [
        "\"client_secret\"",
        "\"private_key\"",
        "qa-signing-key.bin",
        "Nofarma.Licensing.Qa"
    ];
    private static readonly Encoding[] SupportedTextEncodings =
    [
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true),
        new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: false,
            throwOnInvalidBytes: true),
        new UnicodeEncoding(
            bigEndian: true,
            byteOrderMark: false,
            throwOnInvalidBytes: true),
        new UTF32Encoding(
            bigEndian: false,
            byteOrderMark: false,
            throwOnInvalidCharacters: true),
        new UTF32Encoding(
            bigEndian: true,
            byteOrderMark: false,
            throwOnInvalidCharacters: true)
    ];
    private static readonly BoundedPairSetDefinition PrivateHeaderDefinition =
        CreatePairDefinitions(
            ["-----BEGIN", "---- BEGIN"],
            "PRIVATE KEY",
            PrivateHeaderWindowCharacters,
            sameLine: true);
    private static readonly BoundedPairSetDefinition PuttyDefinition =
        CreatePairDefinitions(
            [string.Concat("PuTTY-User-", "Key-File-")],
            string.Concat("Private-", "Lines:"),
            PuttyPrivateSectionWindowCharacters,
            sameLine: false);
    private static readonly byte[][] ForbiddenAllFilePatterns =
    [
        Encoding.ASCII.GetBytes("PRIVATE KEY"),
        .. EncodeTextPatterns(ForbiddenAllEncodingPatterns)
    ];
    private static readonly byte[][] ForbiddenTextFilePatterns =
        EncodeTextPatterns(["PRIVATE KEY"]);

    public static LicenseChannelKeyValidation Validate(
        string rootPath,
        string? oppositePublicKeyPath = null)
    {
        try
        {
            string root = Path.GetFullPath(rootPath);
            if (!Directory.Exists(root))
            {
                return Failure("The published output directory is unavailable.");
            }

            byte[] oppositePublicKey = [];
            byte[] oppositeBase64 = [];
            byte[][] oppositeEncodedBase64 = [];
            string? oppositeFileName = null;
            if (!string.IsNullOrWhiteSpace(oppositePublicKeyPath))
            {
                if (!TryReadOppositePublicKey(
                        oppositePublicKeyPath,
                        out oppositePublicKey))
                {
                    return Failure(
                        "The opposite channel public key is unavailable or invalid.");
                }

                string oppositeBase64Text =
                    Convert.ToBase64String(oppositePublicKey);
                oppositeBase64 = Encoding.ASCII.GetBytes(oppositeBase64Text);
                oppositeEncodedBase64 = EncodeTextPatterns(
                    [oppositeBase64Text]);
                oppositeFileName = Path.GetFileName(oppositePublicKeyPath);
            }

            foreach (FileInfo file in EnumerateFilesWithoutReparsePoints(root))
            {
                if (IsForbiddenName(file.Name, oppositeFileName))
                {
                    return Failure(
                        "The publish contains a forbidden private or licensing file.");
                }
            }

            var secretScanner = new BytePatternAutomaton(
                ForbiddenAllFilePatterns,
                ignoreAsciiCase: true);
            var textSecretScanner = new BytePatternAutomaton(
                ForbiddenTextFilePatterns,
                ignoreAsciiCase: true);
            BytePatternAutomaton? oppositeScanner = oppositePublicKey.Length == 0
                ? null
                : new BytePatternAutomaton(
                    [oppositePublicKey, .. oppositeEncodedBase64],
                    ignoreAsciiCase: false);
            long remainingTextBytes = MaximumTextBytes;
            string? normalizedOppositeBase64 = oppositeBase64.Length == 0
                ? null
                : Encoding.ASCII.GetString(oppositeBase64);
            byte[] scanBuffer = new byte[BufferBytes];
            foreach (FileInfo file in EnumerateFilesWithoutReparsePoints(root))
            {
                if (IsForbiddenName(file.Name, oppositeFileName))
                {
                    return Failure(
                        "The publish contains a forbidden private or licensing file.");
                }

                if (ContainsPattern(
                        file.FullName,
                        scanBuffer,
                        secretScanner,
                        TextExtensions.Contains(file.Extension)
                            ? textSecretScanner
                            : null,
                        oppositeScanner))
                {
                    return Failure(
                        string.Concat(
                            "The publish contains a forbidden binary secret pattern in ",
                            Path.GetRelativePath(root, file.FullName),
                            "."));
                }

                if (normalizedOppositeBase64 is not null
                    && TextExtensions.Contains(file.Extension)
                    && file.Length <= remainingTextBytes)
                {
                    remainingTextBytes -= file.Length;
                    if (ContainsNormalizedText(
                            file.FullName,
                            file.Length,
                            normalizedOppositeBase64))
                    {
                        return Failure(
                            "The publish contains opposite channel public-key material.");
                    }
                }
            }

            return new LicenseChannelKeyValidation(
                true,
                null,
                "The published output contains no forbidden licensing material.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or FormatException
                or CryptographicException
                or PlatformNotSupportedException)
        {
            return Failure("The published output could not be scanned safely.");
        }
    }

    public static bool ContainsSemanticPrivateKeyMaterial(
        ReadOnlySpan<byte> contents)
    {
        var detector = new SemanticPrivateMaterialDetector();
        foreach (byte value in contents)
        {
            if (detector.Advance(value))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<FileInfo> EnumerateFilesWithoutReparsePoints(
        string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            DirectoryInfo directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "The published output contains a reparse point.");
            }

            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        "The published output contains a reparse point.");
                }

                if (entry is DirectoryInfo child)
                {
                    pending.Push(child);
                }
                else if (entry is FileInfo file)
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsForbiddenName(
        string fileName,
        string? oppositeFileName) =>
        fileName.Equals("qa-signing-key.bin", StringComparison.OrdinalIgnoreCase)
        || fileName.Contains(
            "Nofarma.Licensing.Qa",
            StringComparison.OrdinalIgnoreCase)
        || ForbiddenExtensions.Contains(Path.GetExtension(fileName))
        || (oppositeFileName is not null
            && fileName.Equals(
                oppositeFileName,
                StringComparison.OrdinalIgnoreCase));

    private static bool ContainsPattern(
        string path,
        byte[] buffer,
        BytePatternAutomaton secretScanner,
        BytePatternAutomaton? textSecretScanner,
        BytePatternAutomaton? oppositeScanner)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferBytes,
            FileOptions.SequentialScan);
        int secretState = 0;
        int textSecretState = 0;
        int oppositeState = 0;
        var semanticPrivateDetector = new SemanticPrivateMaterialDetector();
        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return false;
            }

            for (int index = 0; index < read; index++)
            {
                if (secretScanner.Advance(ref secretState, buffer[index])
                    || semanticPrivateDetector.Advance(buffer[index])
                    || (textSecretScanner is not null
                        && textSecretScanner.Advance(
                            ref textSecretState,
                            buffer[index]))
                    || (oppositeScanner is not null
                        && oppositeScanner.Advance(
                            ref oppositeState,
                            buffer[index])))
                {
                    return true;
                }
            }
        }
    }

    private static bool ContainsNormalizedText(
        string path,
        long expectedLength,
        string expected)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferBytes,
            FileOptions.SequentialScan);
        if (stream.Length != expectedLength
            || stream.Length > MaximumTextBytes
            || stream.Length > int.MaxValue)
        {
            throw new IOException(
                "The published text changed while it was being scanned.");
        }

        byte[] contents = new byte[checked((int)stream.Length)];
        try
        {
            stream.ReadExactly(contents);
            foreach (Encoding encoding in SupportedTextEncodings)
            {
                try
                {
                    string normalized = RemoveWhitespace(
                        encoding.GetString(contents));
                    if (normalized.Contains(expected, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (DecoderFallbackException)
                {
                }
            }

            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contents);
        }
    }

    private static byte[][] EncodeTextPatterns(
        string[] patterns)
    {
        var encoded = new List<byte[]>(
            patterns.Length * SupportedTextEncodings.Length);
        foreach (string pattern in patterns)
        {
            foreach (Encoding encoding in SupportedTextEncodings)
            {
                encoded.Add(encoding.GetBytes(pattern));
            }
        }

        return encoded.ToArray();
    }

    private static BoundedPairSetDefinition CreatePairDefinitions(
        string[] starts,
        string target,
        int maximumCharacters,
        bool sameLine)
    {
        var definitions = new List<BoundedPairDefinition>(
            SupportedTextEncodings.Length);
        var encodedStarts = new List<byte[]>(
            SupportedTextEncodings.Length * starts.Length);
        var startDefinitionIndexes = new List<int>(encodedStarts.Capacity);
        foreach (Encoding encoding in SupportedTextEncodings)
        {
            int definitionIndex = definitions.Count;
            int bytesPerAsciiCharacter = encoding.GetByteCount("A");
            definitions.Add(new BoundedPairDefinition(
                new BytePatternAutomaton(
                    [encoding.GetBytes(target)],
                    ignoreAsciiCase: true),
                sameLine
                    ? new BytePatternAutomaton(
                        [encoding.GetBytes("\r"), encoding.GetBytes("\n")],
                        ignoreAsciiCase: false)
                    : null,
                checked(maximumCharacters * bytesPerAsciiCharacter)));
            foreach (string start in starts)
            {
                encodedStarts.Add(encoding.GetBytes(start));
                startDefinitionIndexes.Add(definitionIndex);
            }
        }

        return new BoundedPairSetDefinition(
            new BytePatternAutomaton(
                encodedStarts,
                ignoreAsciiCase: true),
            startDefinitionIndexes.ToArray(),
            definitions.ToArray());
    }

    private static bool TryReadOppositePublicKey(
        string path,
        out byte[] subjectPublicKey)
    {
        subjectPublicKey = [];
        string fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length < 1 || stream.Length > MaximumPublicKeyFileBytes)
        {
            return false;
        }

        byte[] encoded = new byte[checked((int)stream.Length)];
        stream.ReadExactly(encoded);
        try
        {
            subjectPublicKey = Convert.FromBase64String(
                Encoding.ASCII.GetString(encoded));
            return subjectPublicKey.Length > 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private static string RemoveWhitespace(string value)
    {
        var normalized = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                normalized.Append(character);
            }
        }

        return normalized.ToString();
    }

    private static LicenseChannelKeyValidation Failure(string message) =>
        new(false, "NFLC010", message);

    private sealed class SemanticPrivateMaterialDetector
    {
        private readonly BoundedPairSetScanner _privateHeaderScanner =
            new(PrivateHeaderDefinition);
        private readonly BoundedPairSetScanner _puttyScanner =
            new(PuttyDefinition);

        public bool Advance(byte value) =>
            _privateHeaderScanner.Advance(value)
            || _puttyScanner.Advance(value);
    }

    private sealed class BoundedPairSetScanner
    {
        private readonly BoundedPairSetDefinition _definition;
        private readonly BoundedPairScanner[] _followers;
        private int _activeFollowers;
        private int _startState;

        public BoundedPairSetScanner(BoundedPairSetDefinition definition)
        {
            _definition = definition;
            _followers = definition.Definitions
                .Select(pair => new BoundedPairScanner(pair))
                .ToArray();
        }

        public bool Advance(byte value)
        {
            if (_activeFollowers > 0)
            {
                foreach (BoundedPairScanner follower in _followers)
                {
                    if (!follower.IsActive)
                    {
                        continue;
                    }

                    if (follower.Advance(value))
                    {
                        return true;
                    }

                    if (!follower.IsActive)
                    {
                        _activeFollowers--;
                    }
                }
            }

            int match = _definition.Start.AdvanceMatch(
                ref _startState,
                value);
            if (match >= 0)
            {
                BoundedPairScanner follower =
                    _followers[_definition.StartDefinitionIndexes[match]];
                if (!follower.IsActive)
                {
                    _activeFollowers++;
                }

                follower.Activate();
            }

            return false;
        }
    }

    private sealed class BoundedPairScanner(
        BoundedPairDefinition definition)
    {
        private int _targetState;
        private int _lineBreakState;
        private int _remainingBytes;

        public bool IsActive => _remainingBytes > 0;

        public void Activate()
        {
            _targetState = 0;
            _lineBreakState = 0;
            _remainingBytes = definition.MaximumBytes;
        }

        public bool Advance(byte value)
        {
            if (_remainingBytes == 0)
            {
                return false;
            }

            if (definition.LineBreak is not null
                && definition.LineBreak.Advance(ref _lineBreakState, value))
            {
                _remainingBytes = 0;
                return false;
            }

            if (definition.Target.Advance(ref _targetState, value))
            {
                return true;
            }

            _remainingBytes--;
            return false;
        }
    }

    private sealed record BoundedPairDefinition(
        BytePatternAutomaton Target,
        BytePatternAutomaton? LineBreak,
        int MaximumBytes);

    private sealed record BoundedPairSetDefinition(
        BytePatternAutomaton Start,
        int[] StartDefinitionIndexes,
        BoundedPairDefinition[] Definitions);

    private sealed class BytePatternAutomaton
    {
        private readonly List<State> _states;
        private readonly bool _ignoreAsciiCase;

        public BytePatternAutomaton(
            IReadOnlyList<byte[]> patterns,
            bool ignoreAsciiCase)
        {
            if (patterns.Count == 0 || patterns.Any(pattern => pattern.Length == 0))
            {
                throw new ArgumentException(
                    "At least one non-empty byte pattern is required.",
                    nameof(patterns));
            }

            _ignoreAsciiCase = ignoreAsciiCase;
            _states = Build(patterns);
        }

        public bool Advance(ref int state, byte value)
        {
            return AdvanceMatch(ref state, value) >= 0;
        }

        public int AdvanceMatch(ref int state, byte value)
        {
            state = _states[state].Next[Normalize(value)];
            return _states[state].MatchId;
        }

        private List<State> Build(IReadOnlyList<byte[]> patterns)
        {
            var states = new List<State> { new() };
            for (int patternIndex = 0;
                 patternIndex < patterns.Count;
                 patternIndex++)
            {
                byte[] pattern = patterns[patternIndex];
                int state = 0;
                foreach (byte value in pattern)
                {
                    int normalized = Normalize(value);
                    int next = states[state].Next[normalized];
                    if (next < 0)
                    {
                        next = states.Count;
                        states[state].Next[normalized] = next;
                        states.Add(new State());
                    }

                    state = next;
                }

                if (states[state].MatchId < 0)
                {
                    states[state].MatchId = patternIndex;
                }
            }

            var pending = new Queue<int>();
            for (int value = 0; value < 256; value++)
            {
                int child = states[0].Next[value];
                if (child < 0)
                {
                    states[0].Next[value] = 0;
                }
                else
                {
                    pending.Enqueue(child);
                }
            }

            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                for (int value = 0; value < 256; value++)
                {
                    int child = states[current].Next[value];
                    if (child < 0)
                    {
                        states[current].Next[value] =
                            states[states[current].Failure].Next[value];
                        continue;
                    }

                    int failure = states[states[current].Failure].Next[value];
                    states[child].Failure = failure;
                    if (states[child].MatchId < 0)
                    {
                        states[child].MatchId = states[failure].MatchId;
                    }
                    pending.Enqueue(child);
                }
            }

            return states;
        }

        private int Normalize(byte value) =>
            _ignoreAsciiCase && value is >= (byte)'a' and <= (byte)'z'
                ? value - 32
                : value;

        private sealed class State
        {
            public State()
            {
                Array.Fill(Next, -1);
            }

            public int[] Next { get; } = new int[256];

            public int Failure { get; set; }

            public int MatchId { get; set; } = -1;
        }
    }
}
