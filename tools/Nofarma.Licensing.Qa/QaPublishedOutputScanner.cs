using System.Security.Cryptography;
using System.Text;

namespace Nofarma.Licensing.Qa;

public static class QaPublishedOutputScanner
{
    private const int BufferBytes = 1024 * 1024;
    private const int MaximumPublicKeyFileBytes = 8 * 1024;
    private const long MaximumTextBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> ForbiddenExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".key",
        ".nofarma-license",
        ".nofarma-request",
        ".p8",
        ".pem"
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
    private static readonly byte[][] ForbiddenAsciiPatterns =
    [
        Encoding.ASCII.GetBytes("PRIVATE KEY"),
        Encoding.ASCII.GetBytes("\"client_secret\""),
        Encoding.ASCII.GetBytes("\"private_key\""),
        Encoding.ASCII.GetBytes("qa-signing-key.bin"),
        Encoding.ASCII.GetBytes("Nofarma.Licensing.Qa")
    ];

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

                oppositeBase64 = Encoding.ASCII.GetBytes(
                    Convert.ToBase64String(oppositePublicKey));
                oppositeFileName = Path.GetFileName(oppositePublicKeyPath);
            }

            List<FileInfo> files = EnumerateFilesWithoutReparsePoints(root);
            foreach (FileInfo file in files)
            {
                if (IsForbiddenName(file.Name, oppositeFileName))
                {
                    return Failure(
                        "The publish contains a forbidden private or licensing file.");
                }
            }

            var secretScanner = new BytePatternAutomaton(
                ForbiddenAsciiPatterns,
                ignoreAsciiCase: true);
            BytePatternAutomaton? oppositeScanner = oppositePublicKey.Length == 0
                ? null
                : new BytePatternAutomaton(
                    [oppositePublicKey, oppositeBase64],
                    ignoreAsciiCase: false);
            long remainingTextBytes = MaximumTextBytes;
            string? normalizedOppositeBase64 = oppositeBase64.Length == 0
                ? null
                : Encoding.ASCII.GetString(oppositeBase64);
            foreach (FileInfo file in files)
            {
                if (ContainsPattern(file.FullName, secretScanner, oppositeScanner))
                {
                    return Failure(
                        "The publish contains a forbidden binary secret pattern.");
                }

                if (normalizedOppositeBase64 is not null
                    && TextExtensions.Contains(file.Extension)
                    && file.Length <= remainingTextBytes)
                {
                    remainingTextBytes -= file.Length;
                    string normalizedText = RemoveWhitespace(
                        File.ReadAllText(file.FullName));
                    if (normalizedText.Contains(
                            normalizedOppositeBase64,
                            StringComparison.Ordinal))
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

    private static List<FileInfo> EnumerateFilesWithoutReparsePoints(
        string root)
    {
        var files = new List<FileInfo>();
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
                    files.Add(file);
                }
            }
        }

        return files;
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
        BytePatternAutomaton secretScanner,
        BytePatternAutomaton? oppositeScanner)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferBytes,
            FileOptions.SequentialScan);
        byte[] buffer = new byte[BufferBytes];
        int secretState = 0;
        int oppositeState = 0;
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
            state = _states[state].Next[Normalize(value)];
            return _states[state].IsMatch;
        }

        private List<State> Build(IReadOnlyList<byte[]> patterns)
        {
            var states = new List<State> { new() };
            foreach (byte[] pattern in patterns)
            {
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

                states[state].IsMatch = true;
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
                    states[child].IsMatch |= states[failure].IsMatch;
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

            public bool IsMatch { get; set; }
        }
    }
}
